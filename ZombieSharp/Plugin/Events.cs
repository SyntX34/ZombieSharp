using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZombieSharp.Models;
using static CounterStrikeSharp.API.Core.Listeners;

namespace ZombieSharp.Plugin;

public class Events
{
    private readonly ZombieSharp _core;
    private readonly Infect _infect;
    private readonly Classes _classes;
    private readonly GameSettings _settings;
    private readonly Weapons _weapons;
    private readonly ILogger<ZombieSharp> _logger;
    private readonly Teleport _teleport;
    private readonly Napalm _napalm;
    private readonly Respawn _respawn;
    private readonly ConVars _convar;
    private readonly HitGroup _hitgroup;

    public Events(ZombieSharp core, Infect infect, GameSettings settings, Classes classes, Weapons weapons, Teleport teleport, Respawn respawn, Napalm napalm, ConVars convar, HitGroup hitgroup, ILogger<ZombieSharp> logger)
    {
        _core = core;
        _infect = infect;
        _settings = settings;
        _classes = classes;
        _weapons = weapons;
        _logger = logger;
        _teleport = teleport;
        _napalm = napalm;
        _respawn = respawn;
        _convar = convar;
        _hitgroup = hitgroup;
    }

    public void EventOnLoad()
    {
        _core.RegisterListener<OnClientPutInServer>(OnClientPutInServer);
        _core.RegisterListener<OnClientDisconnect>(OnClientDisconnect);
        _core.RegisterListener<OnMapStart>(OnMapStart);
        _core.RegisterListener<OnServerPrecacheResources>(OnPrecahceResources);

        _core.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        _core.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        _core.RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        _core.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        _core.RegisterEventHandler<EventCsPreRestart>(OnPreRoundStart);
        _core.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        _core.RegisterEventHandler<EventWarmupEnd>(OnWarmupEnd);
        _core.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        _core.RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        _core.RegisterEventHandler<EventCsPreRestart>(OnPreRestart);
    }

    public void EventOnUnload()
    {
        _core.RemoveListener<OnClientPutInServer>(OnClientPutInServer);
        _core.RemoveListener<OnClientDisconnect>(OnClientDisconnect);
        _core.RemoveListener<OnMapStart>(OnMapStart);
        _core.RemoveListener<OnServerPrecacheResources>(OnPrecahceResources);

        _core.DeregisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        _core.DeregisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        _core.DeregisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        _core.DeregisterEventHandler<EventCsPreRestart>(OnPreRoundStart);
        _core.DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        _core.DeregisterEventHandler<EventWarmupEnd>(OnWarmupEnd);
        _core.DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        _core.DeregisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
    }

    public void OnClientPutInServer(int playerslot)
    {
        var client = Utilities.GetPlayerFromSlot(playerslot);

        if (client == null)
            return;

        PlayerData.ZombiePlayerData?.Add(client, new());
        PlayerData.PlayerClassesData?.Add(client, new());
        PlayerData.PlayerPurchaseCount?.Add(client, new());
        PlayerData.PlayerSpawnData?.Add(client, new());
        PlayerData.PlayerBurnData?.Add(client, null);
        PlayerData.PlayerRegenData?.Add(client, null);
        PlayerData.PlayerMarketData?.Add(client, new());

        _classes?.ClassesOnClientPutInServer(client);
        _weapons?.OnClientPutInServer(client);
    }

    public void OnClientDisconnect(int playerslot)
    {
        var client = Utilities.GetPlayerFromSlot(playerslot);

        if (client == null)
            return;

        PlayerData.ZombiePlayerData?.Remove(client);
        PlayerData.PlayerClassesData?.Remove(client);
        PlayerData.PlayerPurchaseCount?.Remove(client);
        PlayerData.PlayerSpawnData?.Remove(client);
        PlayerData.PlayerBurnData?.Remove(client);
        PlayerData.PlayerMarketData?.Remove(client);
        HealthRegen.RegenOnClientDisconnect(client);
    }

    public void OnMapStart(string mapname)
    {
        _settings.GameSettingsOnMapStart();
        _weapons.WeaponsOnMapStart();
        _classes.ClassesOnMapStart();
        _hitgroup.HitGroupOnMapStart();
        _convar.ConVarOnLoad();
        _convar.ConVarExecuteOnMapStart(mapname);

        Server.ExecuteCommand("sv_predictable_damage_tag_ticks 0");
        Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
        Server.ExecuteCommand("mp_give_player_c4 0");
    }

    public void OnPrecahceResources(ResourceManifest manifest)
    {
        if (Classes.ClassesConfig == null)
        {
            _logger.LogCritical("[OnPrecahceResources] The player classes config is null or not loaded yet!");
            return;
        }

        foreach (var classes in Classes.ClassesConfig.Values)
        {
            if (!string.IsNullOrEmpty(classes.Model))
            {
                if (classes.Model != "default")
                    manifest.AddResource(classes.Model!);
            }
        }

        manifest.AddResource("particles\\oylsister\\env_fire_large.vpcf");
        manifest.AddResource("soundevents\\soundevents_zsharp.vsndevts");
    }

    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var client = @event.Userid;
        var attacker = @event.Attacker;
        var weapon = @event.Weapon;
        var dmgHealth = @event.DmgHealth;
        var hitgroups = @event.Hitgroup;

        _infect.InfectOnPlayerHurt(client, attacker);
        Knockback.KnockbackClient(client, attacker, weapon, dmgHealth, hitgroups);
        Utils.UpdatedPlayerCash(attacker, dmgHealth);
        _napalm.NapalmOnHurt(client, attacker, weapon, dmgHealth);
        _classes.ClassesOnPlayerHurt(client);

        return HookResult.Continue;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (Infect.InfectHasStarted())
            RoundEnd.CheckGameStatus();

        var client = @event.Userid;

        if (client == null)
            return HookResult.Continue;

        if (Infect.IsClientInfect(client))
            Utils.EmitSound(client, "zr.amb.zombie_die");

        // Detect suicide (killed by world, e.g., fall damage)
        bool isSuicide = @event.Attacker == null || @event.Attacker?.Index == 0;
        _respawn.RespawnOnPlayerDeath(client, isSuicide);
        HealthRegen.RegenOnPlayerDeath(client);

        return HookResult.Continue;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var client = @event.Userid;

        if (client == null)
            return HookResult.Continue;

        if (client.Team == CsTeam.None || client.Team == CsTeam.Spectator)
            return HookResult.Continue;

        _classes.ClassesOnPlayerSpawn(client);
        _weapons.OnPlayerSpawn(client);

        if (Infect.InfectHasStarted())
        {
            // Check if this is a suicide respawn and SuicideRespawnZM is enabled
            if (Respawn.WasSuicideDeath(client) && (GameSettings.Settings?.SuicideRespawnZM ?? false))
            {
                _infect.InfectClient(client);
                Respawn.ClearSuicideDeath(client);
                _logger.LogInformation("[OnPlayerSpawn] Forced zombie respawn for suicide death of {0} (SteamID: {1})", client.PlayerName, client.SteamID);
            }
            else
            {
                var team = GameSettings.Settings?.RespawTeam ?? 0;
                if (team == 0)
                    _infect.InfectClient(client);
                else if (team == 1)
                    _infect.HumanizeClient(client);
                else
                {
                    if (!(PlayerData.ZombiePlayerData?[client].Zombie ?? false))
                        _infect.HumanizeClient(client);
                    else
                        _infect.InfectClient(client);
                }
            }
        }
        else
        {
            _infect.HumanizeClient(client);
        }

        Utils.RefreshPurchaseCount(client);
        _core.AddTimer(0.2f, () => _teleport.TeleportOnPlayerSpawn(client));

        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var client = @event.Userid;
        var team = @event.Team;
        var isBot = @event.Isbot;

        if (isBot)
            return HookResult.Continue;

        if (!(GameSettings.Settings?.AllowRespawnJoinLate ?? false))
            return HookResult.Continue;

        if (team > 1)
        {
            _core.AddTimer(1.0f, () =>
            {
                if (client == null)
                    return;

                Respawn.RespawnClient(client);
            });
        }

        return HookResult.Continue;
    }

    public HookResult OnPreRoundStart(EventCsPreRestart @event, GameEventInfo info)
    {
        _infect.InfectOnPreRoundStart();
        return HookResult.Continue;
    }

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _infect.InfectKillInfectionTimer();
        Utils.RemoveRoundObjective();
        RoundEnd.RoundEndOnRoundStart();
        _weapons.OnRoundStart();
        Server.PrintToChatAll($" {_core.Localizer["Prefix"]} {_core.Localizer["Infect.GameInfo"]}");
        return HookResult.Continue;
    }

    public HookResult OnWarmupEnd(EventWarmupEnd @event, GameEventInfo info)
    {
        Infect.InfectStarted = false;
        _infect.InfectKillInfectionTimer();
        return HookResult.Continue;
    }

    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        _infect.InfectOnRoundFreezeEnd();
        RoundEnd.RoundEndOnRoundFreezeEnd();
        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        Infect.InfectStarted = false;
        _infect.InfectKillInfectionTimer();
        _infect.InfectOnRoundEnd();
        RoundEnd.RoundEndOnRoundEnd();
        return HookResult.Continue;
    }

    public HookResult OnPreRestart(EventCsPreRestart @event, GameEventInfo info)
    {
        Infect.InfectStarted = false;
        _infect.InfectOnPreRoundStart(false);
        _infect.InfectKillInfectionTimer();
        RoundEnd.RoundEndOnRoundEnd();
        return HookResult.Continue;
    }
}