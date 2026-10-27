using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CardTryStackToDelta : ElinDelta
{
    [Key(0)]
    public required RemoteCard Card { get; init; }

    [Key(1)]
    public required RemoteCard To { get; init; }

    [Key(2)]
    public required RemoteCard? Parent { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return NoCharacter(sender) ?? NotReachable(sender, Card) ?? NotReachable(sender, To);
    }

    internal override void OnHostRefused(ElinNetHost host)
    {
        SendBack(host, Card);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (Card.Find() is not { isDestroyed: false } card) {
            return;
        }

        // picked up by another player a moment before: it stays in that player's bag
        if (net is ElinNetHost host && OriginPeer != 0 &&
            card.GetRootCard() is Chara { IsPlayer: true } holder &&
            holder != host.ActiveRemoteCharas.GetValueOrDefault(OriginPeer)) {
            EmpLog.Warning("Refusing {DeltaType} from peer {PeerIndex}, uid {Uid} is held by player {HolderUid}",
                nameof(CardTryStackToDelta), OriginPeer, Card.Uid, holder.uid);
            if (card is Thing held) {
                CardAddThingDelta.Rebind(net, Card, held);
            }

            return;
        }

        if (To.Find() is not Thing { isDestroyed: false } to) {
            Parent?.Find()?.AddCard(card);
            return;
        }

        if (Parent?.Find() is { } parent && parent != to.parent) {
            parent.AddCard(card);
            return;
        }

        if (Parent is null && to.parent is not Zone) {
            _zone.AddCard(card);
            return;
        }

        card.TryStackTo(to);
    }
}