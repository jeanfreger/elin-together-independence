using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The teams (<see cref="Teams" />), from the host: the whole book, each time it changes and when a player joins.
///     The rule runs in every game, a guest's own included
/// </summary>
[MessagePackObject]
public class TeamsDelta : ElinDelta
{
    /// <summary>
    ///     Team id -> leader uid, open (1) or not (0), then the member chara uids
    /// </summary>
    [Key(0)]
    public required Dictionary<int, int[]> TeamList { get; init; }

    [Key(1)]
    public required Dictionary<int, string> Names { get; init; }

    /// <summary>
    ///     Zone uid -> team id
    /// </summary>
    [Key(2)]
    public required Dictionary<int, int> Zones { get; init; }

    /// <summary>
    ///     Card uid -> owner chara uid
    /// </summary>
    [Key(3)]
    public required Dictionary<int, int> Owners { get; init; }

    /// <summary>
    ///     Chara uid -> the team it asks to join, waiting for the leader
    /// </summary>
    [Key(4)]
    public Dictionary<int, int>? Requests { get; init; }

    /// <summary>
    ///     Team id -> its unpaid tax bills, -1 -> the world's bills of no team (<see cref="TeamBills" />); none while
    ///     the box is off
    /// </summary>
    [Key(5)]
    public Dictionary<int, int>? Bills { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        // the host's own book is the one in its save; a zone hosted by a client learns it as the client it also is
        if (net is ElinNetHost || FromPeer) {
            return;
        }

        Teams.Learn(this);
    }
}
