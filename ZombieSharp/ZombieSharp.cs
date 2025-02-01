﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using ZombieSharp.Api;
using ZombieSharp.Database;
using ZombieSharp.Models;
using ZombieSharp.Plugin;
using ZombieSharpAPI;

namespace ZombieSharp;

public partial class ZombieSharp : BasePlugin
{
    public override string ModuleName => "ZombieSharp";
    public override string ModuleVersion => "2.2.0";
    public override string ModuleAuthor => "Oylsister";
    public override string ModuleDescription => "Infection/survival style gameplay for CS2 in C#";

    private Events? _event;
    private Infect? _infect;
    private Utils? _utils;
    private Hook? _hook;
    private Classes? _classes;
    private GameSettings? _settings;
    private Weapons? _weapons;
    private Knockback? _knockback;
    private Teleport? _teleport;
    private Respawn? _respawn;
    private DatabaseMain? _database;
    private Napalm? _napalm;
    private ConVars? _convar;
    private HitGroup? _hitgroups;
    private RoundEnd? _roundend;
    private HealthRegen? _healthregen;
    private readonly ILogger<ZombieSharp> _logger;

    // API stuff
    ZombieSharpInterface? api { get; set; }
    public static PluginCapability<IZombieSharpAPI> APICapability = new("zombiesharp:core");

    public ZombieSharp(ILogger<ZombieSharp> logger)
    {
        _logger = logger;
    }

    public override void Load(bool hotReload)
    {
        PlayerData.ZombiePlayerData = [];
        PlayerData.PlayerClassesData = [];
        PlayerData.PlayerPurchaseCount = [];
        PlayerData.PlayerBurnData = [];
        PlayerData.PlayerRegenData = [];

        api = new ZombieSharpInterface();

        Capabilities.RegisterPluginCapability(APICapability, () => api);

        _database = new DatabaseMain(this, _logger);
        _classes = new Classes(this, _database, _logger);
        _infect = new Infect(this, _logger, _classes, api);
        _utils = new Utils(this, _logger);
        _settings = new GameSettings(_logger);
        _weapons = new Weapons(this, _logger);
        _respawn = new Respawn(this, _logger);
        _hook = new Hook(this, _weapons, _respawn, _logger);
        _teleport = new Teleport(this, _logger);
        _napalm = new(this, _logger);
        _convar = new ConVars(this, _weapons, _logger);
        _hitgroups = new HitGroup(_logger);
        _roundend = new RoundEnd(this, _logger);
        _event = new Events(this, _infect, _settings, _classes, _weapons, _teleport, _respawn, _napalm, _convar, _hitgroups, _logger);
        _knockback = new Knockback(_logger);
        _healthregen = new HealthRegen(this, _logger);

        if(hotReload)
        {
            _logger.LogWarning("[Load] The plugin is hotReloaded! This might cause instability to your server.");
            _settings.GameSettingsOnMapStart();
            _classes.ClassesOnMapStart();
            _weapons.WeaponsOnMapStart();
            _hitgroups.HitGroupOnMapStart();
            _convar.ConVarOnLoad();
            _convar.ConVarExecuteOnMapStart(Server.MapName);
        }

        Server.ExecuteCommand("sv_predictable_damage_tag_ticks 0");
        Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
        Server.ExecuteCommand("mp_give_player_c4 0");

        // initial
        _infect.InfectOnLoad();
        _weapons.WeaponOnLoad();
        _event.EventOnLoad();
        _hook.HookOnLoad();
        _teleport.TeleportOnLoad();
        _respawn.RespawnOnLoad();
        _classes.ClassesOnLoad();
        _napalm.NapalmOnLoad();

        _database.DatabaseOnLoad().Wait();
    }

    public override void Unload(bool hotReload)
    {
        PlayerData.ZombiePlayerData = null;
        PlayerData.PlayerClassesData = null;
        PlayerData.PlayerPurchaseCount = null;
        PlayerData.PlayerRegenData = null;
        PlayerData.PlayerBurnData = null;

        _event?.EventOnUnload();
        _hook?.HookOnUnload();
        _database?.DatabaseOnUnload();
    }

    public static string ConfigPath = Path.Combine(Application.RootDirectory, "configs/zombiesharp/");
}
