using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to a player who just came in (<see cref="Teams" />): a new player, or one whose team has no
///     base, starts at the host's spawn zone; a player whose link dropped while it held a map alone goes back to it.
///     Its game travels there the way a player travels alone
/// </summary>
[MessagePackObject]
public class TeamSpawnDelta : ElinDelta
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetClient { IsZoneSession: false } || NetSession.Instance.IsAway ||
            game.spatials.Find(ZoneUid) is not Zone zone || zone == _zone) {
            return;
        }

        EmpLog.Information("Going to {ZoneFullName}, as the host says", zone.ZoneFullName);
        pc.MoveZone(zone, ZoneTransition.EnterState.Auto);
    }
}
