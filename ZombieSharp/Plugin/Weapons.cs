using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZombieSharp.Models;
using MenuManager;

namespace ZombieSharp.Plugin;

public class Weapons
{
    private readonly ZombieSharp _core;
    private readonly ILogger<Weapons> _logger;
    private readonly IMenuApi? _menuApi;
    private readonly PluginCapability<IMenuApi?> _menuCapability = new("menu:nfcore");
    public static Dictionary<string, WeaponAttribute>? WeaponsConfig = null;
    private MarketData _marketData = new();
    private readonly string _marketDataPath;
    private bool weaponCommandInitialized = false;
    private Dictionary<CCSPlayerController, bool> _firstPurchaseUsed = new();

    public Weapons(ZombieSharp core, ILogger<Weapons> logger)
    {
        _core = core;
        _logger = logger;
        _menuApi = _menuCapability.Get();
        _marketDataPath = Path.Combine(ZombieSharp.ConfigPath, "zmarket_data.jsonc");
        if (_menuApi == null)
        {
            _logger.LogError("[Weapons] MenuManager API not found. Center menus unavailable. Ensure MenuManager.dll is loaded.");
        }
        else
        {
            _logger.LogInformation("[Weapons] MenuManager API successfully loaded. Version: {0}", typeof(IMenuApi).Assembly.GetName().Version);
        }
    }

    public void InitializeMarketData()
    {
        try
        {
            _logger.LogInformation("[InitializeMarketData] Attempting to load zmarket_data.jsonc from {0}", _marketDataPath);
            if (File.Exists(_marketDataPath))
            {
                var json = File.ReadAllText(_marketDataPath);
                _marketData = JsonConvert.DeserializeObject<MarketData>(json) ?? new MarketData();
                _logger.LogInformation("[InitializeMarketData] Loaded zmarket_data.jsonc with {0} player entries.", _marketData.Players.Count);
            }
            else
            {
                _marketData = new MarketData();
                SaveMarketData();
                _logger.LogInformation("[InitializeMarketData] Created new zmarket_data.jsonc.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[InitializeMarketData] Failed to load zmarket_data.jsonc: {0}", ex.Message);
        }
    }

    private void SaveMarketData()
    {
        try
        {
            _logger.LogInformation("[SaveMarketData] Saving zmarket_data.jsonc to {0}", _marketDataPath);
            var json = JsonConvert.SerializeObject(_marketData, Formatting.Indented);
            File.WriteAllText(_marketDataPath, json);
            _logger.LogInformation("[SaveMarketData] Saved zmarket_data.jsonc with {0} player entries.", _marketData.Players.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError("[SaveMarketData] Failed to save zmarket_data.jsonc: {0}", ex.Message);
        }
    }

    public void OnClientPutInServer(CCSPlayerController client)
    {
        _logger.LogInformation("[OnClientPutInServer] Processing client {0} (SteamID: {1})", client.PlayerName, client.SteamID);
        if (PlayerData.PlayerMarketData == null)
        {
            _logger.LogError("[OnClientPutInServer] PlayerMarketData is null!");
            return;
        }

        if (!PlayerData.PlayerMarketData.ContainsKey(client))
        {
            var steamId = client.SteamID;
            PlayerMarketData marketData = _marketData.Players.ContainsKey(steamId) 
                ? _marketData.Players[steamId] 
                : new PlayerMarketData();
            PlayerData.PlayerMarketData[client] = marketData;
            _logger.LogInformation("[OnClientPutInServer] Loaded market data for player {0} (SteamID: {1}).", client.PlayerName, steamId);
        }
    }

    public void OnRoundStart()
    {
        _logger.LogInformation("[OnRoundStart] Processing AutoRebuy for players");
        if (PlayerData.PlayerMarketData == null)
        {
            _logger.LogError("[OnRoundStart] PlayerMarketData is null!");
            return;
        }

        _firstPurchaseUsed.Clear();
        _logger.LogInformation("[OnRoundStart] Reset first purchase flags for all players");

        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !Infect.IsClientInfect(p)))
        {
            if (PlayerData.PlayerMarketData[player].AutoRebuy)
            {
                _logger.LogInformation("[OnRoundStart] AutoRebuy triggered for {0}", player.PlayerName);
                BuySavedSetup(player, isFree: true);
            }
        }
    }

    public void OnPlayerSpawn(CCSPlayerController client)
    {
        _logger.LogInformation("[OnPlayerSpawn] Processing spawn for {0}", client.PlayerName);
        if (PlayerData.PlayerMarketData == null || !PlayerData.PlayerMarketData.ContainsKey(client))
        {
            _logger.LogError("[OnPlayerSpawn] PlayerMarketData is null or missing for {0}!", client.PlayerName);
            return;
        }

        if (PlayerData.PlayerMarketData[client].AutoRebuy && !Infect.IsClientInfect(client))
        {
            _logger.LogInformation("[OnPlayerSpawn] Scheduling AutoRebuy for {0}", client.PlayerName);
            _core.AddTimer(0.5f, () => BuySavedSetup(client, isFree: true));
        }
    }

    public void WeaponOnLoad()
    {
        _logger.LogInformation("[WeaponOnLoad] Registering commands: zs_restrict, zs_unrestrict, css_zmarket");
        _core.AddCommand("zs_restrict", "Restrict Weapon Command", WeaponRestrictCommand);
        _core.AddCommand("zs_unrestrict", "Unrestrict Weapon Command", WeaponUnrestrictCommand);
        _core.AddCommand("css_zmarket", "Open ZMarket Menu", ZMarketCommand);
        _logger.LogInformation("[WeaponOnLoad] Commands registered successfully");
    }

    public void WeaponsOnMapStart()
    {
        _logger.LogInformation("[WeaponsOnMapStart] Initializing WeaponsConfig");
        WeaponsConfig = null;
        WeaponsConfig = new Dictionary<string, WeaponAttribute>();

        var configPath = Path.Combine(ZombieSharp.ConfigPath, "weapons.jsonc");
        _logger.LogInformation("[WeaponsOnMapStart] Loading weapons.jsonc from {0}", configPath);

        if (!File.Exists(configPath))
        {
            _logger.LogCritical("[WeaponsOnMapStart] Couldn't find a weapons.jsonc file!");
            return;
        }

        try
        {
            WeaponsConfig = JsonConvert.DeserializeObject<Dictionary<string, WeaponAttribute>>(File.ReadAllText(configPath));
            _logger.LogInformation("[WeaponsOnMapStart] Loaded weapons.jsonc with {0} weapons.", WeaponsConfig?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError("[WeaponsOnMapStart] Failed to load weapons.jsonc: {0}", ex.Message);
        }

        IntialWeaponPurchaseCommand();
    }

    public void IntialWeaponPurchaseCommand()
    {
        _logger.LogInformation("[IntialWeaponPurchaseCommand] Initializing purchase commands");
        if (!(GameSettings.Settings?.WeaponPurchaseEnable ?? false))
        {
            _logger.LogInformation("[IntialWeaponPurchaseCommand] Purchasing is disabled");
            return;
        }

        if (weaponCommandInitialized)
        {
            _logger.LogInformation("[IntialWeaponPurchaseCommand] Purchase commands already initialized");
            return;
        }

        if (WeaponsConfig == null)
        {
            _logger.LogError("[IntialWeaponPurchaseCommand] Weapon Configs is null!");
            return;
        }

        foreach (var weapon in WeaponsConfig.Values)
        {
            if (weapon.PurchaseCommand == null || weapon.PurchaseCommand.Count <= 0)
                continue;

            foreach (var command in weapon.PurchaseCommand)
            {
                if (string.IsNullOrEmpty(command))
                    continue;

                _logger.LogInformation("[IntialWeaponPurchaseCommand] Registering command {0} for {1}", command, weapon.WeaponName);
                _core.AddCommand(command, $"Weapon {weapon.WeaponName} Purchase Command", WeaponPurchaseCommand);
            }
        }

        weaponCommandInitialized = true;
        _logger.LogInformation("[IntialWeaponPurchaseCommand] Purchase commands initialized");
    }

    [CommandHelper(0, "", CommandUsage.CLIENT_ONLY)]
    public void ZMarketCommand(CCSPlayerController? client, CommandInfo? info)
    {
        _logger.LogInformation("[ZMarketCommand] ZMarket command invoked by client {0} (SteamID: {1})", client?.PlayerName ?? "unknown", client?.SteamID ?? 0);
        if (client == null || !client.IsValid || client.IsBot)
        {
            _logger.LogWarning("[ZMarketCommand] Invalid client or bot: {0}", client?.PlayerName ?? "null");
            return;
        }

        if (!GameSettings.Settings?.WeaponPurchaseEnable ?? false)
        {
            _logger.LogInformation("[ZMarketCommand] WeaponPurchaseEnable is false for {0}", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.PurchaseDisabled"]}");
            return;
        }

        if (_menuApi == null)
        {
            _logger.LogError("[ZMarketCommand] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        _logger.LogInformation("[ZMarketCommand] Opening ZMarket menu for player {0} (SteamID: {1})", client.PlayerName, client.SteamID);
        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.MainMenu"]}");
            _logger.LogInformation("[ZMarketCommand] Menu created for {0}", client.PlayerName);
            menu.AddMenuOption(_core.Localizer["ZMarket.SaveWeapons"], (p, option) => SaveCurrentWeapons(p));
            menu.AddMenuOption(_core.Localizer["ZMarket.ViewEditSetup"], (p, option) => ViewEditSavedSetup(p));
            menu.AddMenuOption(_core.Localizer["ZMarket.BuySavedSetup"], (p, option) => BuySavedSetup(p, isFree: false));
            menu.AddMenuOption($"{_core.Localizer["ZMarket.AutoRebuy"]} {(PlayerData.PlayerMarketData?.ContainsKey(client) == true && PlayerData.PlayerMarketData[client].AutoRebuy ? "On" : "Off")}", 
                (p, option) => ToggleAutoRebuy(p));
            menu.AddMenuOption(_core.Localizer["ZMarket.BuyWeapons"], (p, option) => BuyWeaponsMenu(p));
            menu.ExitButton = true;
            _logger.LogInformation("[ZMarketCommand] Menu options added for {0}, opening menu", client.PlayerName);
            menu.Open(client);
            _logger.LogInformation("[ZMarketCommand] Menu opened successfully for {0}", client.PlayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError("[ZMarketCommand] Failed to open ZMarket menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    private void SaveCurrentWeapons(CCSPlayerController client)
    {
        _logger.LogInformation("[SaveCurrentWeapons] Saving weapons for {0}", client.PlayerName);
        if (PlayerData.PlayerMarketData == null || !PlayerData.PlayerMarketData.ContainsKey(client))
        {
            _logger.LogError("[SaveCurrentWeapons] PlayerMarketData is null or missing for {0}!", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.Error"]}");
            return;
        }

        var weapons = client.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons == null)
        {
            _logger.LogError("[SaveCurrentWeapons] Weapon service is null for {0}!", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.Error"]}");
            return;
        }

        var marketData = PlayerData.PlayerMarketData[client];
        marketData.SavedPrimary = null;
        marketData.SavedSecondary = null;
        marketData.SavedZeus = null;
        marketData.SavedKevlar = null;
        marketData.SavedGrenades.Clear();

        foreach (var weapon in weapons)
        {
            if (weapon.Value == null || !weapon.Value.IsValid)
                continue;

            var entity = weapon.Value.DesignerName;
            var attr = GetWeaponAttributeByEntityName(entity);
            if (attr == null)
                continue;

            _logger.LogInformation("[SaveCurrentWeapons] Saving weapon {0} (slot {1}) for {2}", attr.WeaponName, attr.WeaponSlot, client.PlayerName);
            switch (attr.WeaponSlot)
            {
                case 0: marketData.SavedPrimary = attr; break;
                case 1: marketData.SavedSecondary = attr; break;
                case 2: marketData.SavedZeus = attr; break;
                case 3: marketData.SavedGrenades.Add(attr); break;
                case 4: marketData.SavedKevlar = attr; break;
            }
        }

        var steamId = client.SteamID;
        _marketData.Players[steamId] = marketData;
        SaveMarketData();

        client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.WeaponsSaved"]}");
        _menuApi?.CloseMenu(client);
        ZMarketCommand(client, null);
    }

    private void ViewEditSavedSetup(CCSPlayerController client)
    {
        _logger.LogInformation("[ViewEditSavedSetup] Opening View/Edit Setup for {0}", client.PlayerName);
        if (_menuApi == null)
        {
            _logger.LogError("[ViewEditSavedSetup] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        if (PlayerData.PlayerMarketData == null || !PlayerData.PlayerMarketData.ContainsKey(client))
        {
            _logger.LogError("[ViewEditSavedSetup] PlayerMarketData is null or missing for {0}!", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.Error"]}");
            return;
        }

        var marketData = PlayerData.PlayerMarketData[client];
        var title = $" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.ViewEditSetup"]}\n" +
                    $"{_core.Localizer["ZMarket.Primary"]}: {marketData.SavedPrimary?.WeaponName ?? "None"}\n" +
                    $"{_core.Localizer["ZMarket.Secondary"]}: {marketData.SavedSecondary?.WeaponName ?? "None"}\n" +
                    $"{_core.Localizer["ZMarket.Grenades"]}: {(marketData.SavedGrenades.Any() ? string.Join(", ", marketData.SavedGrenades.Select(g => g.WeaponName)) : "None")}\n" +
                    $"{_core.Localizer["ZMarket.Kevlar"]}: {(marketData.SavedKevlar != null ? "Yes" : "No")}\n" +
                    $"{_core.Localizer["ZMarket.Zeus"]}: {(marketData.SavedZeus != null ? "Yes" : "No")}\n" +
                    $"Knife: Yes";

        try
        {
            var menu = _menuApi.GetMenu(title);
            menu.AddMenuOption(_core.Localizer["ZMarket.OverwriteSetup"], (p, option) => SaveCurrentWeapons(p));
            menu.AddMenuOption(_core.Localizer["ZMarket.ChangeSetup"], (p, option) => BuyWeaponsMenu(p, true));
            menu.AddMenuOption(_core.Localizer["ZMarket.Back"], (p, option) => ZMarketCommand(p, null));
            menu.ExitButton = true;
            _logger.LogInformation("[ViewEditSavedSetup] Opening menu for {0}", client.PlayerName);
            menu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[ViewEditSavedSetup] Failed to open menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    private void BuySavedSetup(CCSPlayerController client, bool isFree)
    {
        _logger.LogInformation("[BuySavedSetup] Buying saved setup for {0} (isFree: {1})", client.PlayerName, isFree);
        if (PlayerData.PlayerMarketData == null || !PlayerData.PlayerMarketData.ContainsKey(client))
        {
            _logger.LogError("[BuySavedSetup] PlayerMarketData is null or missing for {0}!", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.Error"]}");
            return;
        }

        var marketData = PlayerData.PlayerMarketData[client];
        var weaponsToBuy = new List<WeaponAttribute>();
        if (marketData.SavedPrimary != null) weaponsToBuy.Add(marketData.SavedPrimary);
        if (marketData.SavedSecondary != null) weaponsToBuy.Add(marketData.SavedSecondary);
        if (marketData.SavedZeus != null) weaponsToBuy.Add(marketData.SavedZeus);
        if (marketData.SavedKevlar != null) weaponsToBuy.Add(marketData.SavedKevlar);
        weaponsToBuy.AddRange(marketData.SavedGrenades);

        if (!weaponsToBuy.Any())
        {
            _logger.LogInformation("[BuySavedSetup] No saved setup for {0}", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.NoSavedSetup"]}");
            return;
        }

        bool deductMoney = !isFree || (_firstPurchaseUsed.ContainsKey(client) && _firstPurchaseUsed[client]);
        foreach (var attr in weaponsToBuy)
        {
            _logger.LogInformation("[BuySavedSetup] Purchasing {0} for {1} (deductMoney: {2})", attr.WeaponName, client.PlayerName, deductMoney);
            PurchaseWeapon(client, attr, deductMoney);
        }

        if (isFree && !_firstPurchaseUsed.ContainsKey(client))
        {
            _firstPurchaseUsed[client] = true;
            _logger.LogInformation("[BuySavedSetup] Marked first free purchase used for {0}", client.PlayerName);
        }

        _menuApi?.CloseMenu(client);
        ZMarketCommand(client, null);
    }

    private void ToggleAutoRebuy(CCSPlayerController client)
    {
        _logger.LogInformation("[ToggleAutoRebuy] Toggling AutoRebuy for {0}", client.PlayerName);
        if (PlayerData.PlayerMarketData == null || !PlayerData.PlayerMarketData.ContainsKey(client))
        {
            _logger.LogError("[ToggleAutoRebuy] PlayerMarketData is null or missing for {0}!", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.Error"]}");
            return;
        }

        var marketData = PlayerData.PlayerMarketData[client];
        marketData.AutoRebuy = !marketData.AutoRebuy;
        var steamId = client.SteamID;
        _marketData.Players[steamId] = marketData;
        SaveMarketData();

        client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.AutoRebuy"]} {(marketData.AutoRebuy ? "enabled" : "disabled")}");
        _menuApi?.CloseMenu(client);
        ZMarketCommand(client, null);
    }

    private void BuyWeaponsMenu(CCSPlayerController client, bool isChangeSetup = false)
    {
        _logger.LogInformation("[BuyWeaponsMenu] Opening Buy Weapons menu for {0}", client.PlayerName);
        if (_menuApi == null)
        {
            _logger.LogError("[BuyWeaponsMenu] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.BuyWeapons"]}");
            menu.AddMenuOption(_core.Localizer["ZMarket.Pistols"], (p, option) => ShowWeaponCategory(p, "Pistols", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.Shotguns"], (p, option) => ShowWeaponCategory(p, "Shotguns", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.SMGs"], (p, option) => ShowWeaponCategory(p, "SMGs", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.Rifles"], (p, option) => ShowWeaponCategory(p, "Rifles", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.Snipers"], (p, option) => ShowWeaponCategory(p, "Snipers", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.MachineGuns"], (p, option) => ShowWeaponCategory(p, "MachineGuns", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.Grenades"], (p, option) => ShowWeaponCategory(p, "Grenades", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.Equipment"], (p, option) => ShowWeaponCategory(p, "Equipment", isChangeSetup));
            menu.AddMenuOption(_core.Localizer["ZMarket.Back"], (p, option) => ZMarketCommand(p, null));
            menu.ExitButton = true;
            _logger.LogInformation("[BuyWeaponsMenu] Opening menu for {0}", client.PlayerName);
            menu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[BuyWeaponsMenu] Failed to open menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    private void ShowWeaponCategory(CCSPlayerController client, string category, bool isChangeSetup)
    {
        _logger.LogInformation("[ShowWeaponCategory] Opening {0} category for {1}", category, client.PlayerName);
        if (_menuApi == null || WeaponsConfig == null)
        {
            _logger.LogError("[ShowWeaponCategory] MenuManager API or WeaponsConfig unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        var weapons = WeaponsConfig.Values.Where(w => !w.Restrict && GetWeaponCategory(w) == category).ToList();
        if (!weapons.Any())
        {
            _logger.LogInformation("[ShowWeaponCategory] No weapons in category {0} for {1}", category, client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.NoWeaponsInCategory", category]}");
            BuyWeaponsMenu(client, isChangeSetup);
            return;
        }

        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket." + category]}");
            foreach (var weapon in weapons)
            {
                var optionText = $"{weapon.WeaponName} (${weapon.Price})";
                menu.AddMenuOption(optionText, (p, option) =>
                {
                    if (isChangeSetup)
                    {
                        SaveWeaponToSetup(p, weapon);
                        ViewEditSavedSetup(p);
                    }
                    else
                    {
                        PurchaseWeapon(p, weapon, deductMoney: true);
                        BuyWeaponsMenu(p, isChangeSetup);
                    }
                });
            }
            menu.AddMenuOption(_core.Localizer["ZMarket.Back"], (p, option) => BuyWeaponsMenu(p, isChangeSetup));
            menu.ExitButton = true;
            _logger.LogInformation("[ShowWeaponCategory] Opening {0} menu for {1}", category, client.PlayerName);
            menu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[ShowWeaponCategory] Failed to open {0} menu for player {1}: {2}", category, client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    private string GetWeaponCategory(WeaponAttribute weapon)
    {
        if (weapon.WeaponSlot == 1) return "Pistols";
        if (weapon.WeaponSlot == 3) return "Grenades";
        if (weapon.WeaponSlot == 2 || weapon.WeaponSlot == 4) return "Equipment";
        if (new[] { "weapon_nova", "weapon_xm1014", "weapon_sawedoff", "weapon_mag7" }.Contains(weapon.WeaponEntity))
            return "Shotguns";
        if (new[] { "weapon_mac10", "weapon_mp9", "weapon_mp5sd", "weapon_mp7", "weapon_ump45", "weapon_bizon", "weapon_p90" }.Contains(weapon.WeaponEntity))
            return "SMGs";
        if (new[] { "weapon_ssg08", "weapon_awp", "weapon_scar20", "weapon_g3sg1" }.Contains(weapon.WeaponEntity))
            return "Snipers";
        if (new[] { "weapon_m249", "weapon_negev" }.Contains(weapon.WeaponEntity))
            return "MachineGuns";
        return "Rifles";
    }

    private void SaveWeaponToSetup(CCSPlayerController client, WeaponAttribute weapon)
    {
        _logger.LogInformation("[SaveWeaponToSetup] Saving {0} to setup for {1}", weapon.WeaponName, client.PlayerName);
        if (PlayerData.PlayerMarketData == null || !PlayerData.PlayerMarketData.ContainsKey(client))
        {
            _logger.LogError("[SaveWeaponToSetup] PlayerMarketData is null or missing for {0}!", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.Error"]}");
            return;
        }

        var marketData = PlayerData.PlayerMarketData[client];
        switch (weapon.WeaponSlot)
        {
            case 0: marketData.SavedPrimary = weapon; break;
            case 1: marketData.SavedSecondary = weapon; break;
            case 2: marketData.SavedZeus = weapon; break;
            case 4: marketData.SavedKevlar = weapon; break;
            case 3:
                if (!marketData.SavedGrenades.Contains(weapon))
                    marketData.SavedGrenades.Add(weapon);
                break;
        }

        var steamId = client.SteamID;
        _marketData.Players[steamId] = marketData;
        SaveMarketData();

#pragma warning disable CS8604
        client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["ZMarket.SetupUpdated", weapon.WeaponName]}");
#pragma warning restore CS8604
    }

    [RequiresPermissions("@css/slay")]
    public void WeaponRestrictCommand(CCSPlayerController? client, CommandInfo info)
    {
        _logger.LogInformation("[WeaponRestrictCommand] Command invoked by {0}", client?.PlayerName ?? "console");
        if (WeaponsConfig == null)
        {
            _logger.LogError("[WeaponRestrictCommand] WeaponsConfig is null!");
            return;
        }

        if (info.ArgCount > 1)
        {
            var weaponname = info.GetArg(1);
            if (string.IsNullOrEmpty(weaponname))
            {
                _logger.LogInformation("[WeaponRestrictCommand] Empty weapon name provided");
                info.ReplyToCommand($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotFound"]}");
                return;
            }

            var weapon = GetWeaponAttributeByName(weaponname);
            if (weapon == null)
            {
                _logger.LogInformation("[WeaponRestrictCommand] Weapon {0} not found", weaponname);
                info.ReplyToCommand($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotFound", weaponname]}");
                return;
            }

            _logger.LogInformation("[WeaponRestrictCommand] Restricting {0}", weapon.WeaponName);
            Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Restrict.Weapon", weapon.WeaponName!]}");
            weapon.Restrict = true;
            return;
        }

        if (client == null)
        {
            _logger.LogWarning("[WeaponRestrictCommand] Client is null, command likely run from console");
            return;
        }

        if (_menuApi == null)
        {
            _logger.LogError("[WeaponRestrictCommand] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        _logger.LogInformation("[WeaponRestrictCommand] Opening restrict menu for player {0}", client.PlayerName);
        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["Restrict.MainMenu"]}");
            foreach (var weapon in WeaponsConfig)
            {
                menu.AddMenuOption(weapon.Value.WeaponName!, (p, option) =>
                {
                    weapon.Value.Restrict = true;
                    Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Restrict.Weapon", weapon.Value.WeaponName!]}");
                    _menuApi.CloseMenu(p);
                }, weapon.Value.Restrict);
            }
            menu.ExitButton = true;
            menu.Open(client);
            _logger.LogInformation("[WeaponRestrictCommand] Restrict menu opened for {0}", client.PlayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError("[WeaponRestrictCommand] Failed to open restrict menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    [RequiresPermissions("@css/slay")]
    public void WeaponUnrestrictCommand(CCSPlayerController? client, CommandInfo info)
    {
        _logger.LogInformation("[WeaponUnrestrictCommand] Command invoked by {0}", client?.PlayerName ?? "console");
        if (WeaponsConfig == null)
        {
            _logger.LogError("[WeaponUnrestrictCommand] WeaponsConfig is null!");
            return;
        }

        if (info.ArgCount > 1)
        {
            var weaponname = info.GetArg(1);
            if (string.IsNullOrEmpty(weaponname))
            {
                _logger.LogInformation("[WeaponUnrestrictCommand] Empty weapon name provided");
                info.ReplyToCommand($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotFound"]}");
                return;
            }

            var weapon = GetWeaponAttributeByName(weaponname);
            if (weapon == null)
            {
                _logger.LogInformation("[WeaponUnrestrictCommand] Weapon {0} not found", weaponname);
                info.ReplyToCommand($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotFound", weaponname]}");
                return;
            }

            _logger.LogInformation("[WeaponUnrestrictCommand] Unrestricting {0}", weapon.WeaponName);
            Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Unrestrict.Weapon", weapon.WeaponName!]}");
            weapon.Restrict = false;
            return;
        }

        if (client == null)
        {
            _logger.LogWarning("[WeaponUnrestrictCommand] Client is null, command likely run from console");
            return;
        }

        if (_menuApi == null)
        {
            _logger.LogError("[WeaponUnrestrictCommand] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        _logger.LogInformation("[WeaponUnrestrictCommand] Opening unrestrict menu for player {0}", client.PlayerName);
        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["Unrestrict.MainMenu"]}");
            foreach (var weapon in WeaponsConfig)
            {
                menu.AddMenuOption(weapon.Value.WeaponName!, (p, option) =>
                {
                    weapon.Value.Restrict = false;
                    Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Unrestrict.Weapon", weapon.Value.WeaponName!]}");
                    _menuApi.CloseMenu(p);
                }, !weapon.Value.Restrict);
            }
            menu.ExitButton = true;
            menu.Open(client);
            _logger.LogInformation("[WeaponUnrestrictCommand] Unrestrict menu opened for {0}", client.PlayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError("[WeaponUnrestrictCommand] Failed to open unrestrict menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    [CommandHelper(0, "", CommandUsage.CLIENT_ONLY)]
    public void WeaponPurchaseCommand(CCSPlayerController? client, CommandInfo info)
    {
        _logger.LogInformation("[WeaponPurchaseCommand] Command {0} invoked by {1}", info.GetArg(0), client?.PlayerName ?? "unknown");
        var command = info.GetArg(0);
        var weaponAttribute = WeaponsConfig?.Where(weapon => weapon.Value.PurchaseCommand!.Contains(command)).FirstOrDefault().Value;

        if (weaponAttribute != null && client != null)
        {
            _logger.LogInformation("[WeaponPurchaseCommand] Purchasing {0} for {1}", weaponAttribute.WeaponName, client.PlayerName);
            PurchaseWeapon(client, weaponAttribute, deductMoney: true);
        }
        else
        {
            _logger.LogWarning("[WeaponPurchaseCommand] No weapon found for command {0} or invalid client", command);
        }
    }

    public void PurchaseWeapon(CCSPlayerController client, WeaponAttribute attribute, bool deductMoney)
    {
        _logger.LogInformation("[PurchaseWeapon] Attempting to purchase {0} for {1} (deductMoney: {2})", attribute.WeaponName, client.PlayerName, deductMoney);
        if (!(GameSettings.Settings?.WeaponPurchaseEnable ?? false))
        {
            _logger.LogInformation("[PurchaseWeapon] WeaponPurchaseEnable is false for {0}", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.PurchaseDisabled"]}");
            return;
        }

        if (attribute == null || client == null)
        {
            _logger.LogError("[PurchaseWeapon] Attribute or client is null for {0}", client?.PlayerName ?? "unknown");
            return;
        }

        if (!Utils.IsPlayerAlive(client))
        {
            _logger.LogInformation("[PurchaseWeapon] Player {0} is not alive", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MustBeAlive"]}");
            return;
        }

        if (Infect.IsClientInfect(client))
        {
            _logger.LogInformation("[PurchaseWeapon] Player {0} is infected", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MustBeHuman"]}");
            return;
        }

        var buyzone = GameSettings.Settings?.WeaponBuyZoneOnly ?? false;
        if (buyzone && !Utils.IsClientInBuyZone(client))
        {
            _logger.LogInformation("[PurchaseWeapon] Player {0} is not in buy zone", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.BuyZoneOnly"]}");
            return;
        }

        if (IsRestricted(attribute.WeaponEntity!))
        {
            _logger.LogInformation("[PurchaseWeapon] Weapon {0} is restricted for {1}", attribute.WeaponName, client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.IsRestricted", attribute.WeaponName!]}");
            return;
        }

        if (deductMoney && client.InGameMoneyServices?.Account < attribute.Price)
        {
            _logger.LogInformation("[PurchaseWeapon] Player {0} has insufficient funds for {1}", client.PlayerName, attribute.WeaponName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotEnoughCash"]}");
            return;
        }

        if (attribute.MaxPurchase != 0)
        {
            var count = 0;

            if (PlayerData.PlayerPurchaseCount == null)
            {
                _logger.LogError("[PurchaseWeapon] Player Purchase count is null!");
                return;
            }

            if (!PlayerData.PlayerPurchaseCount.ContainsKey(client))
            {
                _logger.LogInformation("[PurchaseWeapon] Player {0} is not in purchase count data, creating new entry", client.PlayerName);
                PlayerData.PlayerPurchaseCount.Add(client, new());
            }

            if (PlayerData.PlayerPurchaseCount[client].WeaponCount == null)
            {
                _logger.LogError("[PurchaseWeapon] Player {0} Purchase data is null", client.PlayerName);
                return;
            }

            if (PlayerData.PlayerPurchaseCount[client].WeaponCount!.ContainsKey(attribute.WeaponEntity!))
                count = PlayerData.PlayerPurchaseCount[client].WeaponCount![attribute.WeaponEntity!];

            if (count >= attribute.MaxPurchase)
            {
                _logger.LogInformation("[PurchaseWeapon] Player {0} reached max purchase limit for {1}", client.PlayerName, attribute.WeaponName);
                client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.ReachMaxPurchase", attribute.MaxPurchase]}");
                return;
            }
        }

        var weapons = client.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons == null)
        {
            _logger.LogError("[PurchaseWeapon] Weapon service is null for {0}", client.PlayerName);
            return;
        }

        foreach (var weapon in weapons)
        {
            var slot = (int)weapon.Value!.GetVData<CCSWeaponBaseVData>()!.GearSlot;
            if (slot > 2 && slot != attribute.WeaponSlot)
                continue;

            if (slot == attribute.WeaponSlot)
            {
                _logger.LogInformation("[PurchaseWeapon] Dropping existing weapon in slot {0} for {1}", slot, client.PlayerName);
                Utils.DropWeaponByDesignName(client, weapon.Value.DesignerName);
                break;
            }
        }

        Server.NextWorldUpdate(() =>
        {
            _logger.LogInformation("[PurchaseWeapon] Executing purchase for {0}: {1}", client.PlayerName, attribute.WeaponName);
            if (attribute.WeaponEntity == "item_kevlar")
            {
                client.PlayerPawn.Value!.ArmorValue = 100;
                Utilities.SetStateChanged(client.PlayerPawn.Value, "CCSPlayerPawn", "m_ArmorValue");
            }
            else
            {
                client.GiveNamedItem(attribute.WeaponEntity!);
            }

            if (PlayerData.PlayerPurchaseCount![client].WeaponCount!.ContainsKey(attribute.WeaponEntity!))
                PlayerData.PlayerPurchaseCount![client].WeaponCount![attribute.WeaponEntity!]++;
            else
                PlayerData.PlayerPurchaseCount?[client].WeaponCount?.Add(attribute.WeaponEntity!, 1);

            var purchaseCount = PlayerData.PlayerPurchaseCount![client].WeaponCount![attribute.WeaponEntity!];
            var message = $" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.PurchaseSuccess", attribute.WeaponName!]}";
            if (attribute.MaxPurchase > 0)
                message += $" {_core.Localizer["Weapon.PurchaseCount", attribute.MaxPurchase - purchaseCount, attribute.MaxPurchase]}";

            client.PrintToChat($"{message}");
            if (deductMoney)
            {
                client.InGameMoneyServices!.Account -= attribute.Price;
                Utilities.SetStateChanged(client, "CCSPlayerController", "m_pInGameMoneyServices");
            }
            _logger.LogInformation("[PurchaseWeapon] Purchase completed for {0}: {1} (Money deducted: {2})", client.PlayerName, attribute.WeaponName, deductMoney);
        });
    }

    public static bool IsRestricted(string weaponentity)
    {
        if (WeaponsConfig == null)
            return false;

        var weapon = GetWeaponAttributeByEntityName(weaponentity);
        if (weapon == null)
            return false;

        return weapon.Restrict;
    }

    public static WeaponAttribute? GetWeaponAttributeByEntityName(string? weaponentity)
    {
        if (WeaponsConfig == null)
            return null;

        return WeaponsConfig.Where(data => data.Value.WeaponEntity == weaponentity).FirstOrDefault().Value;
    }

    public static WeaponAttribute? GetWeaponAttributeByName(string? weapon)
    {
        if (WeaponsConfig == null)
            return null;

        return WeaponsConfig.Where(data => string.Equals(data.Value.WeaponName, weapon, StringComparison.OrdinalIgnoreCase)).FirstOrDefault().Value;
    }
}