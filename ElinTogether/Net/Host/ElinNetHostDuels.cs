using System.Collections.Generic;
using System.Linq;

namespace ElinTogether.Net;

/// <summary>
///     What the duels keep in the host's save until they are settled (council 7): the adventurers guests fight
///     (see AdventurerBout)
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Adventurer uid -> where it stood (zone uid, x, z), the player character fighting it and how that went,
    ///     see AdventurerBout. In the save: a bout going on when the host quits puts the adventurer back next time
    /// </summary>
    [ElinGameIOProperty("adventurer_bouts")]
    internal static Dictionary<int, int[]> AdventurerBouts
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     The connected player playing this character, on this map or away: its peer id, -1 for none
    /// </summary>
    /// <summary>
    ///     Player chara uid -> the gold it put down for the duel it fights (PlayerDuel, step 7). In the save: a duel cut
    ///     by a crash or the host quitting gives the stakes back next time
    /// </summary>
    [ElinGameIOProperty("duel_stakes")]
    internal static Dictionary<int, long> DuelStakes
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Gold for a player that is not on this map: handed over as its shipping money is, when it stands here again
    ///     (or at once when it runs its own map)
    /// </summary>
    internal void OweMoney(int charaUid, long gold)
    {
        GetShippingAccount(charaUid)[AccountOwedMoney] += gold;
        PayShipping(charaUid);
    }

    internal int PeerOfChara(int charaUid)
    {
        return Socket.Peers.FirstOrDefault(p => IsOwnChara(p, charaUid))?.Id ?? -1;
    }
}
