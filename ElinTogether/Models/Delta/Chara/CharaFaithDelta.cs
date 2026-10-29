using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CharaFaithDelta : ElinDelta
{
    [Key(0)]
    public required RemoteCard Owner { get; init; }

    [Key(1)]
    public required string FaithId { get; init; }

    /// <summary>
    ///     The campaign's conversion (Religion.ConvertType.Campaign): no punishment, the days with the god go on
    /// </summary>
    [Key(2)]
    public bool Campaign { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (Owner.Find() is not Chara { IsPC: false } chara) {
            return;
        }

        if (game.religions.Find(FaithId) is not { } religion) {
            EmpLog.Warning("Unknown religion {FaithId} for chara {Uid}, heretic!!",
                FaithId, chara.uid);
            return;
        }

        if (chara.faith == religion) {
            return;
        }

        if (net is ElinNetHost host) {
            if (host.ActiveRemoteCharas.TryGetValue(OriginPeer, pc) != chara) {
                EmpLog.Warning("Rejecting religion change of {Uid} from peer {PeerIndex}",
                    chara.uid, OriginPeer);
                return;
            }

            using var _ = Simulate();
            // leaving a god is punished for "the player" only (Religion.LeaveFaith): the player's own game ran it
            // and its effects were overwritten, so the host does it, once. The same exceptions as the game's:
            // the campaign, no god, and between the two minor gods that are kin. The player's own game reads the lines
            var old = chara.faith;
            var trickery = game.religions.Trickery;
            var moon = game.religions.MoonShadow;
            if (!Campaign && !old.IsEyth && !(old == trickery && religion == moon) && !(old == moon && religion == trickery)) {
                using var quiet = MsgRelayContext.Suppress();
                old.Punish(chara);
            }

            religion.JoinFaith(chara);
            if (!Campaign) {
                chara.c_daysWithGod = 0;
            }

            // what JoinFaith does for "the player" only (Card.PurgeEythArtifact): with the feat of Eyth, joining Eyth
            // turns the other Eyth artifacts of that player's side back to their own god
            if (religion.IsEyth && chara.HasElement(1228) &&
                chara.things.Find(t => t.HasTag(CTAG.godArtifact) && t.c_idDeity == religion.id) is { } eyth) {
                chara.PurgeDuplicateArtifact(eyth);
            }

            net.Delta.AddRemote(this);
            return;
        }

        // client sim
        religion.JoinFaith(chara);
        if (!Campaign) {
            chara.c_daysWithGod = 0;
        }
    }
}