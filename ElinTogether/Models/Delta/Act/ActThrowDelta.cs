using System;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class ActThrowDelta : ElinDelta
{
    [Key(0)]
    public required RemoteCard Owner { get; init; }

    [Key(1)]
    public required Position Point { get; init; }

    [Key(2)]
    public required RemoteCard Target { get; init; }

    [Key(3)]
    public required RemoteCard Thing { get; init; } // split

    [Key(4)]
    public required ThrowMethod Method { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        // at anyone, but by the sender and out of what it holds
        return NoCharacter(sender) ?? NotOwned(sender, Owner, "thrower") ??
               (Thing.Find()?.GetRootCard() is Chara holder && !OwnsOrCompanion(sender!, holder) ? $"the thrown thing is held by {holder.uid}" : null);
    }

    // what it held or took goes back where the host has it
    internal override void OnHostRefused(ElinNetHost host)
    {
        SendBack(host, Thing);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (Owner.Find() is not { } owner) {
            return;
        }

        if (Thing.Find() is not Thing { isDestroyed: false } thing) {
            TaskCache.CancelClientAct(net, this, Thing);
            return;
        }

        // never more than the stack still holds, that would make a copy
        var t = thing.Split(Math.Clamp(Thing.Num, 1, thing.Num));
        // the experience and stamina of a player's throw come from the host's game alone
        Patches.ThrowGrowthPatch.Throw(net, this, owner, () => ActThrow.Throw(owner, Point, Target, t, Method));

        if (net.IsHost) {
            net.Delta.AddRemote(new ActThrowDelta {
                Owner = Owner,
                Point = Point,
                Target = Target,
                Thing = t,
                Method = Method,
            });
        }
    }
}