using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace ZombieSharp.Models;

public class PlayerData
{
    public static Dictionary<CCSPlayerController, ZombiePlayer>? ZombiePlayerData { get; set; } = [];
    public static Dictionary<CCSPlayerController, PlayerClasses>? PlayerClassesData { get; set; } = [];
    public static Dictionary<CCSPlayerController, PurchaseCount>? PlayerPurchaseCount { get; set; } = [];
    public static Dictionary<CCSPlayerController, SpawnData>? PlayerSpawnData { get; set; } = [];
    public static Dictionary<CCSPlayerController, CParticleSystem?>? PlayerBurnData { get; set; } = [];
    public static Dictionary<CCSPlayerController, CounterStrikeSharp.API.Modules.Timers.Timer?>? PlayerRegenData { get; set; } = [];
    public static Dictionary<CCSPlayerController, PlayerMarketData>? PlayerMarketData { get; set; } = [];
}

public class PlayerMarketData
{
    public WeaponAttribute? SavedPrimary { get; set; }
    public WeaponAttribute? SavedSecondary { get; set; }
    public WeaponAttribute? SavedZeus { get; set; }
    public WeaponAttribute? SavedKevlar { get; set; }
    public List<WeaponAttribute> SavedGrenades { get; set; } = new();
    public bool AutoRebuy { get; set; } = false;
}