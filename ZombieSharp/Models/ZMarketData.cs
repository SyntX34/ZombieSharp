using System.Collections.Generic;
using ZombieSharp.Models;

namespace ZombieSharp;

public class MarketData
{
    public Dictionary<ulong, PlayerMarketData> Players { get; set; } = new();
}