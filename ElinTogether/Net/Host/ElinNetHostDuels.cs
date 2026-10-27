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
    internal int PeerOfChara(int charaUid)
    {
        return Socket.Peers.FirstOrDefault(p => IsOwnChara(p, charaUid))?.Id ?? -1;
    }
}
