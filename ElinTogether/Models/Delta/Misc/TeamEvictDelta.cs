using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to a player standing on a land that closed on it (<see cref="Teams.Allowed" />, host box "Land
///     rules"), once its grace is over: its game leaves for <see cref="Target" /> the way a player travels alone. A zone
///     it holds is handed back first, as for any departure. Never a trap: the player always has a place to go
/// </summary>
[MessagePackObject]
public class TeamEvictDelta : ElinDelta
{
    /// <summary>
    ///     The land it must leave: nothing is done when the player is not there any more
    /// </summary>
    [Key(0)]
    public required int ZoneUid { get; init; }

    [Key(1)]
    public required int Target { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetClient { IsZoneSession: false } || pc.currentZone?.uid != ZoneUid ||
            game.spatials.Find(Target) is not Zone zone) {
            return;
        }

        EmpLog.Information("Leaving {ZoneFullName} for {Target}: the land is closed to us", pc.currentZone.ZoneFullName, zone.ZoneFullName);
        pc.MoveZone(zone, ZoneTransition.EnterState.Auto);
    }
}
