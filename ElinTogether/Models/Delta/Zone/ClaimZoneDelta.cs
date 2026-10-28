using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A player of the host's map reads a deed and says yes: the land is claimed by the game that keeps the world,
///     which is the only one that can make a base everyone has (the branch, the hearth stone, the residents to
///     come). Until 2026-10-13 the deed was refused to a guest, "the one inequality that stays". <br />
///     Read again from this game's state: the land must be free to claim here, and the sender must hold a deed
/// </summary>
[MessagePackObject]
public class ClaimZoneDelta : ElinDelta
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    /// <summary>
    ///     The host's answer when it does not claim
    /// </summary>
    [Key(1)]
    public bool Refused { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost { IsZoneSession: false } host) {
            if (Refused) {
                RemoteBasePaidPatch.Refuse(true);
            }

            return;
        }

        if (NetSession.Instance.Rules.HostManagesBase || !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
            host.IsAwayPeer(OriginPeer) || _zone.uid != ZoneUid || _zone.mainFaction == pc.faction || !_zone.isClaimable ||
            _zone.instance is not null || Teams.LandCapReached(sender) || sender.things.List(t => t.trait is TraitDeed, true).FirstOrDefault() is not { } deed) {
            EmpLog.Information("Land {ZoneUid} not claimed for peer {Peer}", ZoneUid, OriginPeer);
            host.SendDeltaTo(OriginPeer, new ClaimZoneDelta { ZoneUid = ZoneUid, Refused = true });
            return;
        }

        EmpLog.Information("Land {ZoneUid} claimed for player {Uid}", ZoneUid, sender.uid);
        using var _ = Simulate();
        deed.ModNum(-1);
        Teams.Claimer = sender;
        try {
            _zone.ClaimZone(false, sender.pos.GetNearestPoint(false, true, false) ?? sender.pos);
        } finally {
            Teams.Claimer = null;
        }
    }
}
