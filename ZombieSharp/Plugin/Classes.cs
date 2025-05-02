using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZombieSharp.Database;
using ZombieSharp.Models;
using MenuManager;

namespace ZombieSharp.Plugin;

public class Classes
{
    private readonly ZombieSharp _core;
    private readonly ILogger<Classes> _logger;
    private readonly DatabaseMain _database;
    private readonly IMenuApi? _menuApi;
    private readonly PluginCapability<IMenuApi?> _menuCapability = new("menu:nfcore");

    public static Dictionary<string, ClassAttribute>? ClassesConfig = [];
    public static ClassAttribute? DefaultHuman = null;
    public static ClassAttribute? DefaultZombie = null;
    public static ClassAttribute? MotherZombie = null;

    public Classes(ZombieSharp core, DatabaseMain database, ILogger<Classes> logger)
    {
        _core = core;
        _logger = logger;
        _database = database;
        _menuApi = _menuCapability.Get();
        if (_menuApi == null)
        {
            _logger.LogError("[Classes] MenuManager API not found. Center menus unavailable. Ensure MenuManager.dll is loaded.");
        }
        else
        {
            _logger.LogInformation("[Classes] MenuManager API successfully loaded. Version: {0}", typeof(IMenuApi).Assembly.GetName().Version);
        }
    }

    public void ClassesOnLoad()
    {
        _core.AddCommand("css_zclass", "Select Class Menu Command", ClassesMainMenuCommand);
    }

    public void ClassesOnMapStart()
    {
        ClassesConfig = null;
        ClassesConfig = new Dictionary<string, ClassAttribute>();

        var configPath = Path.Combine(ZombieSharp.ConfigPath, "playerclasses.jsonc");

        if (!File.Exists(configPath))
        {
            _logger.LogCritical("[ClassesOnMapStart] Couldn't find a playerclasses.jsonc file!");
            return;
        }

        _logger.LogInformation("[ClassesOnMapStart] Load Player Classes file.");

        ClassesConfig = JsonConvert.DeserializeObject<Dictionary<string, ClassAttribute>>(File.ReadAllText(configPath));

        if (GameSettings.Settings != null)
        {
            if (!string.IsNullOrEmpty(GameSettings.Settings.DefaultHumanBuffer))
            {
                var uniqueName = GameSettings.Settings.DefaultHumanBuffer;
                if (!ClassesConfig!.ContainsKey(uniqueName))
                {
                    _logger.LogCritical("[ClassesOnMapStart] Couldn't get classes \"{0}\" from playerclasses.jsonc", uniqueName);
                }
                DefaultHuman = ClassesConfig[uniqueName];
                _logger.LogInformation("[ClassesOnMapStart] Classes {0} is default class for human", DefaultHuman.Name);
            }

            if (!string.IsNullOrEmpty(GameSettings.Settings.DefaultZombieBuffer))
            {
                var uniqueName = GameSettings.Settings.DefaultZombieBuffer;
                if (!ClassesConfig!.ContainsKey(uniqueName))
                {
                    _logger.LogCritical("[ClassesOnMapStart] Couldn't get classes \"{0}\" from playerclasses.jsonc", uniqueName);
                }
                DefaultZombie = ClassesConfig[uniqueName];
                _logger.LogInformation("[ClassesOnMapStart] Classes {0} is default class for zombie", DefaultZombie.Name);
            }

            if (!string.IsNullOrEmpty(GameSettings.Settings.MotherZombieBuffer))
            {
                var uniqueName = GameSettings.Settings.MotherZombieBuffer;
                if (!ClassesConfig!.ContainsKey(uniqueName))
                {
                    _logger.LogCritical("[ClassesOnMapStart] Couldn't get classes \"{0}\" from playerclasses.jsonc", uniqueName);
                }
                MotherZombie = ClassesConfig[uniqueName];
                _logger.LogInformation("[ClassesOnMapStart] Classes {0} is mother zombie classes.", MotherZombie.Name);
            }

            if (DefaultHuman == null)
            {
                _logger.LogCritical("[ClassesOnMapStart] Human class from GameSettings is empty or null!");
            }

            if (DefaultZombie == null)
            {
                _logger.LogCritical("[ClassesOnMapStart] Zombie class from GameSettings is empty or null!");
            }

            if (MotherZombie == null)
            {
                _logger.LogInformation("[ClassesOnMapStart] Mother Zombie class from GameSettings is empty or null, when player infect will use their selected zombie class instead.");
            }
        }
    }

    public void ClassesOnClientPutInServer(CCSPlayerController client)
    {
        if (DefaultHuman == null || DefaultZombie == null)
        {
            _logger.LogError("[ClassesOnClientPutInServer] Default class is null!");
        }

        if (PlayerData.PlayerClassesData == null)
        {
            _logger.LogError("[ClassesOnClientPutInServer] PlayerClassesData is null!");
            return;
        }

        if (GameSettings.Settings?.RandomClassesOnConnect ?? false)
        {
            PlayerData.PlayerClassesData[client].HumanClass = Utils.GetRandomPlayerClasses(1);
            PlayerData.PlayerClassesData[client].ZombieClass = Utils.GetRandomPlayerClasses(0);
        }
        else
        {
            PlayerData.PlayerClassesData[client].HumanClass = DefaultHuman;
            PlayerData.PlayerClassesData[client].ZombieClass = DefaultZombie;

            if (!client.IsBot)
            {
                var steamid = client.AuthorizedSteamID?.SteamId64;
                if (steamid == null || !steamid.HasValue)
                {
                    _logger.LogError("[ClassesOnClientPutInServer] client {0} steam id is null!", client.PlayerName);
                    return;
                }

                Task.Run(async () =>
                {
                    _logger.LogInformation("[ClassesOnClientPutInServer] Getting data of {0}", steamid.Value);
                    var data = await _database.GetPlayerClassData(steamid.Value);

                    if (data == null)
                    {
                        await _database.InsertPlayerClassData(steamid.Value, PlayerData.PlayerClassesData[client]);
                        _logger.LogInformation("[ClassesOnClientPutInServer] Client {0} data is null, initialize a new one.", steamid.Value);
                    }
                    else
                    {
                        if (data.HumanClass?.Enable ?? false)
                            PlayerData.PlayerClassesData[client].HumanClass = data.HumanClass;
                        if (data.ZombieClass?.Enable ?? false)
                            PlayerData.PlayerClassesData[client].ZombieClass = data.ZombieClass;
                    }
                });
            }
        }

        if (PlayerData.PlayerClassesData?[client].HumanClass == null)
            _logger.LogInformation("[ClassesOnClientPutInServer] {0} is not null, but client got null anyway.", DefaultHuman?.Name);

        if (PlayerData.PlayerClassesData?[client].ZombieClass == null)
            _logger.LogInformation("[ClassesOnClientPutInServer] {0} is not null, but client got null anyway.", DefaultZombie?.Name);
    }

    public void ClassesApplyToPlayer(CCSPlayerController client, ClassAttribute? data)
    {
        if (client == null)
        {
            _logger.LogError("[ClassesApplyToPlayer] Player is null!");
            return;
        }

        if (data == null)
        {
            _logger.LogError("[ClassesApplyToPlayer] Class data is null!");
            return;
        }

        var playerPawn = client.PlayerPawn.Value;
        if (playerPawn == null)
        {
            _logger.LogError("[ClassesApplyToPlayer] Player Pawn is null!");
            return;
        }

        Server.NextWorldUpdate(() =>
        {
            if (!string.IsNullOrEmpty(data.Model) && playerPawn.IsValid)
            {
                if (data.Model != "default")
                    playerPawn.SetModel(data.Model);
                else
                {
                    if (data.Team == 0)
                        playerPawn.SetModel("characters/models/tm_phoenix/tm_phoenix.vmdl");
                    else
                        playerPawn.SetModel("characters/models/ctm_sas/ctm_sas.vmdl");
                }
            }

            if (data.Team == 0)
            {
                playerPawn.ArmorValue = 0;
                client.PawnHasHelmet = false;
            }
        });

        _core.AddTimer(0.3f, () =>
        {
            if (!Utils.IsPlayerAlive(client))
                return;

            playerPawn.Health = data.Health;
            Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
            if (data.Team == 0)
            {
                playerPawn.ArmorValue = 0;
                client.PawnHasHelmet = false;
            }

            HealthRegen.RegenOnApplyClass(client, data);
        });

        playerPawn.VelocityModifier = data.Speed / 250f;
        PlayerData.PlayerClassesData![client].ActiveClass = data;
    }

    public void ClassesOnPlayerSpawn(CCSPlayerController? client)
    {
        if (client == null || PlayerData.PlayerClassesData == null || !PlayerData.PlayerClassesData.ContainsKey(client))
            return;

        if (GameSettings.Settings?.RandomClassesOnSpawn ?? false)
        {
            PlayerData.PlayerClassesData[client].HumanClass = Utils.GetRandomPlayerClasses(1);
            PlayerData.PlayerClassesData[client].ZombieClass = Utils.GetRandomPlayerClasses(0);
        }
    }

    public void ClassesOnPlayerHurt(CCSPlayerController? client)
    {
        _core?.AddTimer(0.5f, () => 
        {
            // need to be alive
            if(client == null || client.Handle == IntPtr.Zero)
                return;

            if(!Utils.IsPlayerAlive(client))
                return;

            // prevent error obviously.
            if(!PlayerData.PlayerClassesData!.ContainsKey(client))
            {
                return;
            }

            if(PlayerData.PlayerClassesData?[client].ActiveClass == null)
                return;

            // if speed is equal to 250 then we don't need this.
            if(PlayerData.PlayerClassesData?[client].ActiveClass?.Speed == 250f)
                return;

            // set speed back to velomodify
            client.PlayerPawn.Value!.VelocityModifier = PlayerData.PlayerClassesData![client].ActiveClass!.Speed / 250f;
        });
    }

    [CommandHelper(0, "", CommandUsage.CLIENT_ONLY)]
    public void ClassesMainMenuCommand(CCSPlayerController? client, CommandInfo? info)
    {
        if (client == null)
            return;

        if (!GameSettings.Settings?.AllowChangeClass ?? true)
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.FeatureDisbled"]}");
            return;
        }

        if (!PlayerData.PlayerClassesData?.ContainsKey(client) ?? false)
        {
            _logger.LogError("[ClassesMainMenuCommand] {0} is not in PlayerClassesData!", client.PlayerName);
            return;
        }

        if (_menuApi == null)
        {
            _logger.LogError("[ClassesMainMenuCommand] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        _logger.LogInformation("[ClassesMainMenuCommand] Opening center menu for player {0}", client.PlayerName);
        try
        {
            var menu = _menuApi.GetMenu($" {_core.Localizer["Prefix"]} {_core.Localizer["Classes.MainMenu"]}");
            menu.AddMenuOption(_core.Localizer["Classes.MainMenu.Zombie"], (player, option) => ClassesSelectMenu(player, 0));
            menu.AddMenuOption(_core.Localizer["Classes.MainMenu.Human"], (player, option) => ClassesSelectMenu(player, 1));
            menu.ExitButton = true;
            menu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[ClassesMainMenuCommand] Failed to open center menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }

    public void ClassesSelectMenu(CCSPlayerController client, int team)
    {
        if (ClassesConfig == null)
        {
            _logger.LogError("[ClassesSelectMenu] ClassesConfig is null!");
            return;
        }

        if (_menuApi == null)
        {
            _logger.LogError("[ClassesSelectMenu] MenuManager API unavailable for player {0}.", client.PlayerName);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuUnavailable"]}");
            return;
        }

        string title = team == 0
            ? $" {_core.Localizer["Prefix"]} {_core.Localizer["Classes.Select.Zombie"]}"
            : $" {_core.Localizer["Prefix"]} {_core.Localizer["Classes.Select.Human"]}";

        _logger.LogInformation("[ClassesSelectMenu] Opening center select menu for team {0} for player {1}", team, client.PlayerName);
        try
        {
            var selectmenu = _menuApi.GetMenu(title);
            var menuhandle = (CCSPlayerController player, CounterStrikeSharp.API.Modules.Menu.ChatMenuOption option) =>
            {
                if (option.Text == "Back")
                {
                    ClassesMainMenuCommand(player, null);
                    _menuApi.CloseMenu(player);
                    return;
                }

                if (team == 0)
                    PlayerData.PlayerClassesData![player].ZombieClass = ClassesConfig.FirstOrDefault(x => x.Value.Name == option.Text).Value;
                else
                    PlayerData.PlayerClassesData![player].HumanClass = ClassesConfig.FirstOrDefault(x => x.Value.Name == option.Text).Value;

                player.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Classes.SelectSuccess"]}");
                _menuApi.CloseMenu(player);

                if (GameSettings.Settings?.AllowSavingClass ?? true)
                {
                    var steamid = player.AuthorizedSteamID?.SteamId64;
                    if (steamid == null || !steamid.HasValue)
                    {
                        _logger.LogError("[ClassesSelectMenu] SteamID of {0} is null!", player.PlayerName);
                        return;
                    }

                    Task.Run(async () => await _database.InsertPlayerClassData(steamid.Value, PlayerData.PlayerClassesData![player]));
                }
            };

            foreach (var playerclass in ClassesConfig)
            {
                if (playerclass.Value.Team == team)
                {
                    bool alreadyselected = playerclass.Value == PlayerData.PlayerClassesData?[client].HumanClass ||
                                          playerclass.Value == PlayerData.PlayerClassesData?[client].ZombieClass;
                    bool motherzombie = playerclass.Value.MotherZombie;
                    bool disable = !playerclass.Value.Enable;
                    selectmenu.AddMenuOption(playerclass.Value.Name!, menuhandle, alreadyselected || motherzombie || disable);
                }
            }

            selectmenu.AddMenuOption("Back", menuhandle);
            selectmenu.ExitButton = true;
            selectmenu.Open(client);
        }
        catch (Exception ex)
        {
            _logger.LogError("[ClassesSelectMenu] Failed to open center menu for player {0}: {1}", client.PlayerName, ex.Message);
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MenuError"]}");
        }
    }
}