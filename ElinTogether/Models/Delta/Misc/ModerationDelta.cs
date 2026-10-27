using System.Collections.Generic;
using System.Linq;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Host -> every game: the Steam ids muted in this world (admin tools, council 2026-10-27). Each game that passes
///     chat on (the host, a player holding a map for others) drops theirs, and a muted player's own game says so
/// </summary>
[MessagePackObject]
public class ModerationDelta : ElinDelta
{
    /// <summary>
    ///     What this game knows to be muted, wherever it learnt it
    /// </summary>
    internal static HashSet<ulong> Muted { get; } = [];

    [Key(0)]
    public required ulong[] Mutes { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        // only the host of the world says who is muted
        if (net is ElinNetHost) {
            return;
        }

        Muted.Clear();
        Muted.UnionWith(Mutes);
    }

    internal static bool IsMuted(ulong user)
    {
        return Muted.Contains(user);
    }
}
