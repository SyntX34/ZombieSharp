using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace ZombieSharp.Plugin;

public class Respawn
{
    private readonly ZombieSharp _core;
    private readonly ILogger<ZombieSharp> _logger;
    private static readonly Dictionary<CCSPlayerController, bool> _suicideDeaths = new();
#pragma warning disable CS8618

    private static ZombieSharp _coreStatic;
#pragma warning restore CS8618


    public Respawn(ZombieSharp core, ILogger<ZombieSharp> logger)
    {
        _core = core;
        _logger = logger;
        _coreStatic = core; // Initialize static reference
    }

    public void RespawnOnLoad()
    {
        _core.AddCommand("css_zspawn", "Zspawn command obviously", ZSpawnCommand);
    }

    [CommandHelper(0, "", CommandUsage.CLIENT_ONLY)]
    public void ZSpawnCommand(CCSPlayerController? client, CommandInfo info)
    {
        if (client == null)
            return;

        if (client.Team != CsTeam.Terrorist && client.Team != CsTeam.CounterTerrorist)
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MustBeInTeam"]}");
            return;
        }

        if (Utils.IsPlayerAlive(client))
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Core.MustBeDead"]}");
            return;
        }

        if (!(GameSettings.Settings?.RespawnEnable ?? true))
        {
            client.PrintToChat($" {_core.Localizer["Prefix"]} {_core.Localizer["Respawn.Disabled"]}");
            return;
        }

        RespawnClient(client, isSuicide: false);
    }

    public void RespawnOnPlayerDeath(CCSPlayerController? client, bool isSuicide)
    {
        if (client == null)
        {
            _logger.LogCritical("[RespawnOnPlayerDeath] client {0} is null", client?.PlayerName ?? "Unnamed");
            return;
        }

        // Track if this was a suicide death
        if (isSuicide)
        {
            _suicideDeaths[client] = true;
            _logger.LogInformation("[RespawnOnPlayerDeath] Recorded suicide death for {0} (SteamID: {1})", client.PlayerName, client.SteamID);
        }
        else
        {
            _suicideDeaths.Remove(client);
        }

        // Check if suicide respawn is enabled and this is a suicide
        if (isSuicide && (GameSettings.Settings?.SuicideRespawnZM ?? false))
        {
            _logger.LogInformation("[RespawnOnPlayerDeath] Scheduling zombie respawn for suicide death of {0} (SteamID: {1})", client.PlayerName, client.SteamID);
            _core.AddTimer(GameSettings.Settings?.RespawnDelay ?? 5.0f, () => RespawnClient(client, isSuicide: true));
            return;
        }

        // Normal respawn logic if RespawnEnable is true
        if (!(GameSettings.Settings?.RespawnEnable ?? true))
            return;

        _logger.LogInformation("[RespawnOnPlayerDeath] Scheduling normal respawn for {0} (SteamID: {1})", client.PlayerName, client.SteamID);
        _core.AddTimer(GameSettings.Settings?.RespawnDelay ?? 5.0f, () => RespawnClient(client, isSuicide: false));
    }

    public static void RespawnClient(CCSPlayerController? client, bool isSuicide = false)
    {
        if (client == null || client.Handle == IntPtr.Zero)
        {
            _coreStatic.Logger.LogWarning("[RespawnClient] Client is null or invalid");
            return;
        }

        if (Utils.IsPlayerAlive(client))
        {
            _coreStatic.Logger.LogInformation("[RespawnClient] Client {0} (SteamID: {1}) is already alive, skipping respawn", client.PlayerName, client.SteamID);
            return;
        }

        if (client.Team == CsTeam.Spectator || client.Team == CsTeam.None)
        {
            _coreStatic.Logger.LogInformation("[RespawnClient] Client {0} (SteamID: {1}) is in invalid team ({2}), skipping respawn", client.PlayerName, client.SteamID, client.Team);
            return;
        }

        if (!(GameSettings.Settings?.RespawnEnable ?? true))
        {
            _coreStatic.Logger.LogInformation("[RespawnClient] Respawn is disabled, skipping respawn for {0} (SteamID: {1})", client.PlayerName, client.SteamID);
            return;
        }

        _coreStatic.Logger.LogInformation("[RespawnClient] Respawning {0} (SteamID: {1}, Suicide: {2})", client.PlayerName, client.SteamID, isSuicide);
        client.Respawn();
    }

    public static bool WasSuicideDeath(CCSPlayerController client)
    {
        return _suicideDeaths.ContainsKey(client) && _suicideDeaths[client];
    }

    public static void ClearSuicideDeath(CCSPlayerController client)
    {
        _suicideDeaths.Remove(client);
    }
}