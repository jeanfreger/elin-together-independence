using System.Collections.Generic;

namespace ElinTogether.Net;

internal partial class ElinNetHost
{
    /// <summary>
    ///     Host box "Market" (<see cref="Helper.Market" />): the classified ads of the world, ad number -> the ad as
    ///     text. Empty in a save older than the box
    /// </summary>
    [ElinGameIOProperty("market_ads")]
    internal static Dictionary<int, string> MarketAds
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Key 0: the last ad number given (numbers are never given twice, so a stale removal cannot hit a newer ad)
    /// </summary>
    [ElinGameIOProperty("market_state")]
    internal static Dictionary<int, int> MarketState
    {
        get => field ??= [];
        set;
    }
}
