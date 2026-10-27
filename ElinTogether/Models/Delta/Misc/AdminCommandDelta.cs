using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Admin -> host: an order of a player the host made admin (admin tools, council 2026-10-27): kick, ban, unban,
///     mute, unmute. The host checks it itself, whatever EnforceOwnership says: the sender must be an admin of this
///     world, the target neither the host nor an admin
/// </summary>
[MessagePackObject]
public class AdminCommandDelta : ElinDelta
{
    [Key(0)]
    public required string Verb { get; init; }

    [Key(1)]
    public required ulong Target { get; init; }

    [Key(2)]
    public string? Reason { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost { IsZoneSession: false } host) {
            host.RunAdminOrder(OriginPeer, this);
        }
    }
}
