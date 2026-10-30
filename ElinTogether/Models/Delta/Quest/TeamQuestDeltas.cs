using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Host to one player (host box "Teammates share quests", <see cref="TeamQuests" />): the shared quests of its team
///     that changed, the ones it no longer sees, the ones that are done
/// </summary>
[MessagePackObject]
public class TeamQuestListDelta : ElinDelta
{
    [Key(0)]
    public required List<LZ4Bytes> Quests { get; init; }

    /// <summary>
    ///     The owner's chara uid of each quest, in the same order
    /// </summary>
    [Key(1)]
    public required int[] Owners { get; init; }

    /// <summary>
    ///     Quests this player no longer sees (it left the team, the box went off): out of its journal
    /// </summary>
    [Key(2)]
    public int[]? Removed { get; init; }

    /// <summary>
    ///     Quests a teammate turned in: done here too, this player got its share from the host
    /// </summary>
    [Key(3)]
    public int[]? Closed { get; init; }

    /// <summary>
    ///     The date on the host's clock, deadlines are dates on it
    /// </summary>
    [Key(4)]
    public int Now { get; set; }

    internal IReadOnlyList<(int Owner, Quest Quest)> Read()
    {
        var list = new List<(int, Quest)>();
        for (var i = 0; i < Quests.Count && i < Owners.Length; i++) {
            if (Quests[i].Decompress<Quest>() is { } quest) {
                list.Add((Owners[i], quest));
            }
        }

        return list;
    }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost || FromPeer) {
            return;
        }

        TeamQuests.Receive(Read(), Removed, Closed, Now);
    }
}

/// <summary>
///     A player to the host: what a teammate's shared quest holds now that it advanced it
/// </summary>
[MessagePackObject]
public class TeamQuestUpdateDelta : ElinDelta
{
    [Key(0)]
    public required int Uid { get; init; }

    [Key(1)]
    public required LZ4Bytes Data { get; init; }

    /// <summary>
    ///     The date on the sender's clock
    /// </summary>
    [Key(2)]
    public int Now { get; set; }

    protected override void OnApply(ElinNetBase net)
    {
        // (checked there: the sender must be in the owner's team)
        if (FromPeer && net is ElinNetHost { IsZoneSession: false } host && host.TeamQuestPlayerUid(OriginPeer) is > 0 and var uid) {
            host.UpdateTeamQuest(uid, Uid, Data, Now);
        }
    }
}

/// <summary>
///     A player away from the host's map to the host: it turned in a shared quest there (its own or a teammate's) and
///     had its reward there
/// </summary>
[MessagePackObject]
public class TeamQuestDoneDelta : ElinDelta
{
    [Key(0)]
    public required int Uid { get; init; }

    /// <summary>
    ///     The quest as it was turned in, for a quest the host no longer keeps (its owner told it gone first)
    /// </summary>
    [Key(1)]
    public LZ4Bytes? Data { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (FromPeer && net is ElinNetHost { IsZoneSession: false } host) {
            host.TeamQuestDoneAway(OriginPeer, Uid, Data);
        }
    }
}
