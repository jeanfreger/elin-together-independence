using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Guest -> host of the world, on its map or away: this player turned away from the keyboard, or came back
///     (see <see cref="PlayerIdle" />). Sent only when that changes
/// </summary>
[MessagePackObject]
public class PlayerIdleDelta : ElinDelta
{
    [Key(0)]
    public required bool Idle { get; init; }

    internal override bool RequiresGameStarted => false;

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost { IsZoneSession: false } && FromPeer) {
            PlayerIdle.OnReport(OriginPeer, Idle);
        }
    }
}
