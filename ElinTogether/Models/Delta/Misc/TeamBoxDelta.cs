using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to one player on its map (host option "Teams keep their own bank and delivery box",
///     <see cref="TeamBoxes" />): copies of the two boxes of the player's owner, with the uids of the real ones, so that
///     what the player puts in and takes out is told to the host as it is for any card. Sent when the player comes to
///     the map and when its owner changes. Empty: its owner has no boxes of its own any more (the box was unticked)
/// </summary>
[MessagePackObject]
public class TeamBoxDelta : ElinDelta
{
    [Key(0)]
    public LZ4Bytes? Bank { get; init; }

    [Key(1)]
    public LZ4Bytes? Delivery { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        // (a zone hosted by a client: the visitor hears it from the host of the world)
        if (net is ElinNetHost || FromPeer) {
            return;
        }

        TeamBoxes.Receive(Bank, Delivery);
    }
}
