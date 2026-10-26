using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CharaPickThingDelta : ElinDelta
{
    public enum PickType : byte
    {
        Pick,
        PickOrDrop,
        TrySmoothPick,
    }

    [Key(0)]
    public required RemoteCard Owner { get; init; }

    [Key(1)]
    public required RemoteCard Thing { get; init; }

    [Key(2)]
    public required Position? Pos { get; init; }

    [Key(3)]
    public required PickType Type { get; init; }

    // as the game that picked it did (Chara.HoldCard picks without stacking)
    [Key(4)]
    public bool TryStack { get; set; } = true;

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return NoCharacter(sender) ?? NotOwned(sender, Owner, "picker");
    }

    protected override void OnApply(ElinNetBase net)
    {
        // we do not apply to ourselves
        if (Owner.Find() is not Chara chara) {
            return;
        }

        // (a delta naming no thing, as a modified game may send, is dropped)
        if (Thing is null) {
            return;
        }

        if (Thing.Find() is not Thing { isDestroyed: false } thing) {
            TaskCache.CancelClientAct(net, this, Thing);
            return;
        }

        // realign
        if (net.IsHost && thing.GetRootCard() is Chara holder && holder != chara && holder.IsPlayer) {
            EmpLog.Warning("Refusing {DeltaType} from peer {PeerIndex}, uid {Uid} is held by player {HolderUid}",
                nameof(CharaPickThingDelta), OriginPeer, Thing.Uid, holder.uid);
            CardAddThingDelta.Rebind(net, Thing, thing);
            return;
        }

        // a player of another team on this land: the thing stays where it is, and everyone is told so (Teams)
        if (net.IsHost && !Teams.CanTake(chara, thing)) {
            EmpLog.Warning("Refusing {DeltaType} from peer {PeerIndex}, uid {Uid} belongs to another team",
                nameof(CharaPickThingDelta), OriginPeer, Thing.Uid);
            if (thing.parent is Card) {
                CardAddThingDelta.Rebind(net, Thing, thing);
            } else if (thing.parent == _zone) {
                net.Delta.AddRemote(new ZoneAddCardDelta {
                    Card = Thing,
                    Pos = thing.pos,
                    ZoneUid = _zone.uid,
                });
            }

            return;
        }

        if (CharaProgressCompleteDelta.IsReplaying) {
            if (net.IsClient && chara.IsRemotePlayer) {
                _zone.AddCard(thing);
                return;
            }
        } else if (chara.IsPC) {
            return;
        }

        // relay to clients
        if (net.IsHost && Type != PickType.Pick) {
            net.Delta.AddRemote(this);
        }

        switch (Type) {
            case PickType.Pick:
                // a card of the codex: collected or kept is the choice of the player who picked it (its own
                // "collect cards" setting), and its game told the codex itself (CodexDelta). With this
                // game's setting the card went to this codex, or was taken away from a player who keeps them
                var collect = game.config.autoCollectCard;
                game.config.autoCollectCard = false;
                try {
                    chara.Pick(thing, tryStack: TryStack);
                } finally {
                    game.config.autoCollectCard = collect;
                }

                // force add
                if (net.IsHost && !thing.isDestroyed && thing.parent == _zone) {
                    EmpLog.Warning("Mirror pick of {Uid} failed to store, forcing into chara {OwnerUid}",
                        thing.uid, chara.uid);
                    // clients must add thingy
                    using (Simulate()) {
                        chara.AddThing(thing);
                    }
                }

                if (!thing.isDestroyed) {
                    EmpLog.Debug("Chara {OwnerUid} picked {Uid}, now in parent {ParentUid}",
                        chara.uid, thing.uid, (thing.parent as Card)?.uid ?? -1);
                }

                break;
            case PickType.PickOrDrop:
                chara.PickOrDrop(Pos, thing);
                break;
            case PickType.TrySmoothPick:
                _map.TrySmoothPick(Pos, thing, chara);
                break;
        }

        if (thing.isDestroyed || thing.parent != _zone) {
            return;
        }

        if (net.IsClient && !CharaProgressCompleteDelta.IsReplaying) {
            EmpLog.Warning("Relay {PickType} of {Uid} failed to store in {OwnerUid}, forcing local",
                Type, thing.uid, chara.uid);
            chara.AddThing(thing);
            return;
        }

        net.Delta.AddRemote(new ZoneAddCardDelta {
            Card = Thing,
            Pos = thing.pos,
            ZoneUid = _zone.uid,
        });
    }
}