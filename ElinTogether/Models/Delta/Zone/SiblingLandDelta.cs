using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A player on the world map reads a deed on a tile that already holds a land (host box "Several lands per tile"):
///     the host makes another plot there, the reader steps in and reads the deed again to claim it. <br />
///     From a guest: a request (X, Y), read again from the host's state like <see cref="BaseRelocateDelta" />. From the
///     host: the plot is <see cref="ZoneUid" /> (the reader steps in), or the request is Refused
/// </summary>
[MessagePackObject]
public class SiblingLandDelta : ElinDelta
{
    [Key(0)]
    public required int X { get; init; }

    [Key(1)]
    public required int Y { get; init; }

    /// <summary>
    ///     The host's answer: the plot
    /// </summary>
    [Key(2)]
    public int ZoneUid { get; init; }

    [Key(3)]
    public bool Refused { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (ZoneUid != 0 || Refused) {
            return "only the host says where a plot is";
        }

        return NoCharacter(sender) ?? SiblingLands.WhyNot(sender!, X, Y);
    }

    internal override void OnHostRefused(ElinNetHost host)
    {
        host.SendDeltaTo(OriginPeer, new SiblingLandDelta { X = X, Y = Y, Refused = true });
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost { IsZoneSession: false } host) {
            string? why = null;
            if (ZoneUid != 0 || Refused || host.PlayerChara(OriginPeer) is not { } sender ||
                (why = SiblingLands.WhyNot(sender, X, Y)) is not null || SiblingLands.Create(X, Y) is not { } zone) {
                EmpLog.Information("No plot at {X},{Y} for peer {Peer}: {Why}", X, Y, OriginPeer, why ?? "an answer, not a request");
                OnHostRefused(host);
                return;
            }

            EmpLog.Information("Plot {ZoneUid} made at {X},{Y} for player {Uid}", zone.uid, X, Y, sender.uid);
            // after the announcement of the new zone to everyone (SpatialGenEvent defers it a frame)
            var (peer, uid) = (OriginPeer, zone.uid);
            CoroutineHelper.Deferred(() => host.SendDeltaTo(peer, new SiblingLandDelta { X = X, Y = Y, ZoneUid = uid }), 3);
            return;
        }

        if (net is not ElinNetClient) {
            return;
        }

        if (Refused) {
            SE.Beep();
            Msg.Say("cannot_use_here");
        } else if (ZoneUid != 0) {
            SiblingLands.EnterWhenKnown(ZoneUid);
        }
    }
}
