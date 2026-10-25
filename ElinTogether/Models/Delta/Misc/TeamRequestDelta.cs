using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

public enum TeamAsk : byte
{
    /// <summary>
    ///     A team named after the player, which it leads
    /// </summary>
    Found,

    /// <summary>
    ///     Id is the team
    /// </summary>
    Join,

    Leave,

    /// <summary>
    ///     The leader lets in the player who asked. Id is that player's chara uid
    /// </summary>
    Accept,

    /// <summary>
    ///     Id is the chara uid of the player who asked
    /// </summary>
    Refuse,
}

/// <summary>
///     A player's choice in the team menus (<see cref="Teams" />), asked of the host of the world, which keeps the
///     book: it is checked there against the book as it is now, for the player who sent it
/// </summary>
[MessagePackObject]
public class TeamRequestDelta : ElinDelta
{
    [Key(0)]
    public required TeamAsk Kind { get; init; }

    [Key(1)]
    public int Id { get; init; }

    /// <summary>
    ///     The land it is about, when it is about one
    /// </summary>
    [Key(2)]
    public int ZoneUid { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return NoCharacter(sender);
    }

    protected override void OnApply(ElinNetBase net)
    {
        // (a zone hosted by a client keeps no book)
        if (!FromPeer || net is not ElinNetHost { IsZoneSession: false } host || host.PlayerChara(OriginPeer) is not { } sender) {
            return;
        }

        Teams.Answer(sender, Kind, Id, ZoneUid);
    }
}
