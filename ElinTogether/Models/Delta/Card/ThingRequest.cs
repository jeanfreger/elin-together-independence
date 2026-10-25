using ElinTogether.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class ThingRequest : ElinDelta
{
    private static readonly Dictionary<int, (Action<Thing>, Action?)> _callbackList = [];
    private static readonly Dictionary<int, (WeakReference<Thing> thing, Card? origin, DateTime since)> _dangling = [];
    private static readonly TimeSpan _danglingTimeout = TimeSpan.FromSeconds(10);

    private static int _nextId;

    // client replaying local ... simulation?
    public static bool IsReplayingIntent;

    [Key(0)]
    public required int Id { get; init; }

    [Key(1)]
    public required RemoteCard? Thing { get; set; }

    [Key(2)]
    public required int Num { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        var thing = Thing?.Find() as Thing;
        if (net.IsClient) {
            if (_callbackList.Remove(Id, out var value)) {
                IsReplayingIntent = true;
                try {
                    var (onSuccess, onFail) = value;
                    if (thing is not null) {
                        onSuccess(thing);
                    } else {
                        onFail?.Invoke();
                    }
                } finally {
                    IsReplayingIntent = false;
                }
            } else {
                EmpLog.Warning("ThingRequest {RequestId} response has no pending callback, dropped",
                    Id);
            }

            return;
        }

        if (thing is null || thing.parent is null) {
            EmpLog.Warning("Rejecting ThingRequest {RequestId} from peer {PeerIndex}, uid {Uid} unresolved or parentless",
                Id, OriginPeer, Thing?.Uid ?? -1);
            Respond(net, null);
            return;
        }

        // seen in a shop or a chest a moment ago, since then in the bag of another player
        if (net is ElinNetHost host &&
            thing.GetRootCard() is Chara { IsPlayer: true } holder &&
            holder != host.ActiveRemoteCharas.GetValueOrDefault(OriginPeer)) {
            EmpLog.Warning("Rejecting ThingRequest {RequestId} from peer {PeerIndex}, uid {Uid} is held by player {HolderUid}",
                Id, OriginPeer, thing.uid, holder.uid);
            Respond(net, null);
            return;
        }

        // a chest or the ground of another team's land (Teams)
        if (net is ElinNetHost land && land.ActiveRemoteCharas.TryGetValue(OriginPeer, out var taker) && !Teams.CanTake(taker, thing)) {
            EmpLog.Warning("Rejecting ThingRequest {RequestId} from peer {PeerIndex}, uid {Uid} belongs to another team",
                Id, OriginPeer, thing.uid);
            Respond(net, null);
            return;
        }

        // a limited item of a shop: no more than what is left of it for that player (its own game says no first,
        // when it knows; see LimitedStockPatch)
        var num = Math.Min(Num, thing.Num);
        if (net is ElinNetHost shop && shop.ActiveRemoteCharas.TryGetValue(OriginPeer, out var asker)) {
            num = LimitedStockPatch.Left(thing, asker, num);
        }

        if (num <= 0) {
            Respond(net, null);
            return;
        }

        // a chest of a resident: the guest's game takes the karma but its guards and witnesses are here
        if (net is ElinNetHost thief && thief.ActiveRemoteCharas.TryGetValue(OriginPeer, out var robber)) {
            PlayerKarmaPatch.OnTakeFromResident(thief, robber, thing);
        }

        var origin = thing.parent as Card;
        // the stack may have shrunk since the requester counted it: asking for more than is left would make
        // a copy of what was asked for
        // (before it leaves: a limited item of a shop is laid again whole for the other players)
        var whole = net is ElinNetHost keeper && keeper.ActiveRemoteCharas.TryGetValue(OriginPeer, out var buyer)
            ? LimitedStockPatch.Whole(thing, num, buyer)
            : null;

        var result = thing.Split(num);
        result.parent?.RemoveCard(result);
        if (whole is not null) {
            // as this game's own gesture, so that the stack reaches the players like any change made here
            using var _ = Simulate();
            LimitedStockPatch.Lay(result == thing ? null : thing, whole);
        }

        CardCache.KeepAlive(result);
        RecordDangling(result, origin);

        Thing = result;
        Respond(net, result);
    }

    private void Respond(ElinNetBase net, Thing? thing)
    {
        var response = new ThingRequest {
            Id = Id,
            Thing = thing is null ? null : RemoteCard.Create(thing, withData: true),
            Num = Num,
        };

        if (!net.SendDeltaTo(OriginPeer, response)) {
            EmpLog.Warning("ThingRequest {RequestId} response dropped, peer {PeerIndex} is gone",
                Id, OriginPeer);
        }
    }

    public static ThingRequest Create(Thing thing, int num)
    {
        var req = new ThingRequest {
            Id = _nextId++,
            Thing = thing,
            Num = num,
        };

        return req;
    }

    public ThingRequest Send()
    {
        NetSession.Instance.Connection!.Delta.AddRemote(this);
        return this;
    }

    public void Then(Action<Thing> onSuccess, Action? onFail = null)
    {
        _callbackList[Id] = (onSuccess, onFail);
    }

    internal static void Clear()
    {
        _callbackList.Clear();
        _dangling.Clear();
        _nextId = 0;
    }

    private static void RecordDangling(Thing result, Card? origin)
    {
        _dangling[result.uid] = (new(result), origin, DateTime.Now);
    }

    internal static void InvalidateDangling()
    {
        if (_dangling.Count == 0 || NetSession.Instance.Connection is not ElinNetHost) {
            return;
        }

        foreach (var (uid, entry) in _dangling.ToArray()) {
            if (!entry.thing.TryGetTarget(out var dangling) || dangling.isDestroyed || dangling.parent is not null) {
                _dangling.Remove(uid);
                continue;
            }

            if (DateTime.Now - entry.since < _danglingTimeout) {
                continue;
            }

            EmpLog.Warning("ThingRequest dangling of {Uid} was never dango dongo'd, returning to {ParentUid}",
                uid, entry.origin?.uid ?? -1);

            using (Simulate()) {
                if (entry.origin is { isDestroyed: false } origin) {
                    origin.AddThing(dangling);
                } else {
                    _zone.AddCard(dangling, pc.pos);
                }
            }

            _dangling.Remove(uid);
        }
    }
}