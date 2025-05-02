using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
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
    private bool weaponCommandInitialized = false;

    public Weapons(ZombieSharp core, ILogger<Weapons> logger)
    {
        _core = core;
        _logger = logger;
        _menuApi = _menuCapability.Get();
        if (_menuApi == null)
        {
            _logger.LogError("[Weapons] MenuManager API not found. Center menus unavailable. Ensure MenuManager.dll is loaded.");
        }
        else
        {
            _logger.LogInformation("[Weapons] MenuManager API successfully loaded. Version: {0}", typeof(IMenuApi).Assembly.GetName().Version);
        }
    }

    public void WeaponOnLoad()
    {
        _core.AddCommand("zs_restrict", "Restrict Weapon Command", WeaponRestrictCommand);
        _core.AddCommand("zs_unrestrict", "Unrestrict Weapon Command", WeaponUnrestrictCommand);
    }

    public void WeaponsOnMapStart()
    {
        WeaponsConfig = null;
        WeaponsConfig = new Dictionary<string, WeaponAttribute>();

        var configPath = Path.Combine(ZombieSharp.ConfigPath, "weapons.jsonc");

        if (!File.Exists(configPath))
        {
            _logger.LogCritical("[WeaponsOnMapStart] Couldn't find a weapons.jsonc file!");
            return;
        }

        _logger.LogInformation("[WeaponsOnMapStart] Load Weapon Config file.");

        WeaponsConfig = JsonConvert.DeserializeObject<Dictionary<string, WeaponAttribute>>(File.ReadAllText(configPath));

        IntialWeaponPurchaseCommand();
    }

    public void IntialWeaponPurchaseCommand()
    {
        if (!(GameSettings.Settings?.WeaponPurchaseEnable ?? false))
        {
            _logger.LogInformation("[IntialWeaponPurchaseCommand] Purchasing is disabled");
            return;
        }

        if (weaponCommandInitialized)
            return;

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

                _core.AddCommand(command, $"Weapon {weapon.WeaponName} Purchase Command", WeaponPurchaseCommand);
            }
        }

        weaponCommandInitialized = true;
    }

    [RequiresPermissions("@css/slay")]
    public void WeaponRestrictCommand(CCSPlayerController? client, CommandInfo info)
    {
        if (WeaponsConfig == null)
        {
            _logger.LogError("[WeaponRestrictCommand] WeaponsConfig is null!");
            return;
        }

        if (info.ArgCount > 1)
        {
            var weaponname = info.GetArg(1);
            var weapon = GetWeaponAttributeByName(weaponname);

            if (weapon == null)
            {
                info.ReplyToCommand($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotFound", weaponname]}");
                return;
            }

            Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Restrict.Weapon", weapon.WeaponName!]}");
            weapon.Restrict = true;
            return;
        }

        if (client == null)
            return;

        if (_menuApi == null)
        {
            _logger.LogError("[WeaponRestrictCommand] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        _logger.LogInformation("[WeaponRestrictCommand] Opening center menu for player {0}", client.PlayerName);
        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["Restrict.MainMenu"]}");
            foreach (var weapon in WeaponsConfig)
            {
                menu.AddMenuOption(weapon.Value.WeaponName!, (player, option) =>
                {
                    weapon.Value.Restrict = true;
                    Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Restrict.Weapon", weapon.Value.WeaponName!]}");
                    _menuApi.CloseMenu(player);
                }, weapon.Value.Restrict);
            }
            menu.ExitButton = true;
            menu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[WeaponRestrictCommand] Failed to open center menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    [RequiresPermissions("@css/slay")]
    public void WeaponUnrestrictCommand(CCSPlayerController? client, CommandInfo info)
    {
        if (WeaponsConfig == null)
        {
            _logger.LogError("[WeaponUnrestrictCommand] WeaponsConfig is null!");
            return;
        }

        if (info.ArgCount > 1)
        {
            var weaponname = info.GetArg(1);
            var weapon = GetWeaponAttributeByName(weaponname);

            if (weapon == null)
            {
                info.ReplyToCommand($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.NotFound"]}");
                return;
            }

            Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Unrestrict.Weapon", weapon.WeaponName!]}");
            weapon.Restrict = false;
            return;
        }

        if (client == null)
            return;

        if (_menuApi == null)
        {
            _logger.LogError("[WeaponUnrestrictCommand] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        _logger.LogInformation("[WeaponUnrestrictCommand] Opening center menu for player {0}", client.PlayerName);
        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["Unrestrict.MainMenu"]}");
            foreach (var weapon in WeaponsConfig)
            {
                menu.AddMenuOption(weapon.Value.WeaponName!, (player, option) =>
                {
                    weapon.Value.Restrict = false;
                    Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Unrestrict.Weapon", weapon.Value.WeaponName!]}");
                    _menuApi.CloseMenu(player);
                }, !weapon.Value.Restrict);
            }
            menu.ExitButton = true;
            menu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[WeaponUnrestrictCommand] Failed to open center menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    [CommandHelper(0, "", CommandUsage.CLIENT_ONLY)]
    public void WeaponPurchaseCommand(CCSPlayerController? client, CommandInfo info)
    {
        var command = info.GetArg(0);
        var weaponAttribute = WeaponsConfig?.Where(weapon => weapon.Value.PurchaseCommand!.Contains(command)).FirstOrDefault().Value;

        if (weaponAttribute != null && client != null)
            PurchaseWeapon(client, weaponAttribute);
    }

    public void PurchaseWeapon(CCSPlayerController client, WeaponAttribute attribute)
    {
        if (!(GameSettings.Settings?.WeaponPurchaseEnable ?? false))
            return;

        if (attribute == null || client == null)
            return;

        if (!Utils.IsPlayerAlive(client))
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MustBeAlive"]}");
            return;
        }

        if (Infect.IsClientInfect(client))
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MustBeHuman"]}");
            return;
        }

        var buyzone = GameSettings.Settings?.WeaponBuyZoneOnly ?? false;
        if (buyzone && !Utils.IsClientInBuyZone(client))
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.BuyZoneOnly"]}");
            return;
        }

        if (IsRestricted(attribute.WeaponEntity!))
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.IsRestricted", attribute.WeaponName!]}");
            return;
        }

        if (client.InGameMoneyServices?.Account < attribute.Price)
        {
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
                _logger.LogError("[PurchaseWeapon] Player {name} is not in purchase count data, so create a new one", client.PlayerName);
                PlayerData.PlayerPurchaseCount.Add(client, new());
            }

            if (PlayerData.PlayerPurchaseCount[client].WeaponCount == null)
            {
                _logger.LogError("[PurchaseWeapon] Player {name} Purchase data is null", client.PlayerName);
                return;
            }

            if (PlayerData.PlayerPurchaseCount[client].WeaponCount!.ContainsKey(attribute.WeaponEntity!))
                count = PlayerData.PlayerPurchaseCount[client].WeaponCount![attribute.WeaponEntity!];

            if (count >= attribute.MaxPurchase)
            {
                client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Weapon.ReachMaxPurchase", attribute.MaxPurchase]}");
                return;
            }
        }

        var weapons = client.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons == null)
        {
            _logger.LogError("[PurchaseWeapon] {0} Weapon service is somehow null", client.PlayerName);
            return;
        }

        foreach (var weapon in weapons)
        {
            var slot = (int)weapon.Value!.GetVData<CCSWeaponBaseVData>()!.GearSlot;
            if (slot > 2)
                continue;

            if (slot == attribute.WeaponSlot)
            {
                Utils.DropWeaponByDesignName(client, weapon.Value.DesignerName);
                break;
            }
        }

        Server.NextWorldUpdate(() =>
        {
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
            client.InGameMoneyServices!.Account -= attribute.Price;
            Utilities.SetStateChanged(client, "CCSPlayerController", "m_pInGameMoneyServices");
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