using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Client to host: a dialog gave this, create it for real
/// </summary>
[MessagePackObject]
public class StoryGiftDelta : ElinDelta
{
    [Key(0)]
    public required LZ4Bytes Data { get; init; }

    [Key(1)]
    public required Position Pos { get; init; }

    /// <summary>
    ///     The story gave it (a story quest that completes, the outcome of a dialog): the other players get their
    ///     share, see ElinNetHost.ShareStoryReward
    /// </summary>
    [Key(2)]
    public bool Story { get; init; }

    // a dialog or a wish drops it at the feet of the one talking: a crowded tile pushes it a few cells away
    private const int MaxReach = 15;

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (NoCharacter(sender) is { } none) {
            return none;
        }

        // (a sender on another map: OnApply drops it anyway)
        if (sender!.currentZone != _zone || sender.pos is not { IsValid: true } at) {
            return null;
        }

        var reach = System.Math.Max(System.Math.Abs(at.x - Pos.X), System.Math.Abs(at.z - Pos.Z));
        return reach > MaxReach ? $"a gift {reach} cells away from the sender" : OverBudget();
    }

    // what the dialogs and wishes of one player may give in an hour (security audit 2026-10-27, item 5: any thing, up to
    // 1000 of it or 10,000,000 gold, as often as a modified game liked): far above a real game's quests and wishes
    private const int HourlyGifts = 40;
    private const long HourlyGold = 2_000_000;
    private const long HourlyValue = 2_000_000;

    private static readonly System.Collections.Generic.Dictionary<int, (float Since, int Gifts, long Gold, long Value)> Given = [];

    private string? OverBudget()
    {
        Thing thing;
        try {
            thing = Data.Decompress<Thing>();
        } catch (System.Exception) {
            return "a gift that cannot be read";
        }

        var gold = thing.trait is TraitCurrency && thing.id == "money" ? (long)thing.Num : 0;
        var value = gold > 0 ? 0 : (long)thing.GetValue() * thing.Num;
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (!Given.TryGetValue(OriginPeer, out var given) || now - given.Since > 3600f) {
            given = (now, 0, 0, 0);
        }

        if (given.Gifts + 1 > HourlyGifts || given.Gold + gold > HourlyGold || given.Value + value > HourlyValue) {
            var why = $"gift {thing.id} x{thing.Num} over the hour's budget ({given.Gifts} gifts, {given.Gold} gold, {given.Value} value)";
            if (TrustEnforced) {
                ElinNetHost.AdminLog($"refused {why} from peer {OriginPeer}");
            }

            return why;
        }

        Given[OriginPeer] = (given.Since, given.Gifts + 1, given.Gold + gold, given.Value + value);
        return null;
    }

    protected override void OnApply(ElinNetBase net)
    {
        // travelling alone, a player creates its own things
        if (net is not ElinNetHost host || host.IsAwayPeer(OriginPeer) || !Pos.IsInActiveMapBounds) {
            return;
        }

        var thing = Data.Decompress<Thing>();
        Bound(thing);
        game.cards.AssignUIDRecursive(thing);
        CardCache.Add(thing);
        CardCache.CacheContainer(thing.things);

        using (Simulate()) {
            host.Delta.AddRemote(CardGenDelta.Create(thing));
            _zone.AddCard(thing, new Point(Pos.X, Pos.Z));
        }

        if (Story) {
            ElinNetHost.ShareStoryReward(thing, host.PlayerChara(OriginPeer));
        }

        EmpLog.Debug("Created story gift {ThingId} for peer {PeerIndex}", thing.id, OriginPeer);
    }

    /// <summary>
    ///     The client's gift is kept whole (a bill, a love lunch, a chest's contents are set after it is made),
    ///     only what no dialog or wish can give is brought back within the game's maxima: currency up to
    ///     QuestRandom's 10,000,000, anything else up to the 1000 of ActEffect.Wish (a dialog gives several spellbooks or deeds at once),
    ///     an enchant up to what Thing.GetEnchant rolls at the game's level cap (ElementContainer, 10,000,000),
    ///     one charge on a rod of wishing (TraitRod.OnCreate)
    /// </summary>
    private void Bound(Thing thing)
    {
        var max = thing.trait is TraitCurrency ? 10_000_000 : 1000;
        if (thing.Num > max) {
            Odd(thing, "Num", thing.Num, max);
            thing.SetNum(max);
        }

        if (thing.id == "rod_wish" && thing.c_charges > 1) {
            Odd(thing, "charges", thing.c_charges, 1);
            thing.c_charges = 1;
        }

        foreach (var e in thing.elements.dict.Values) {
            // (an element this game does not know, from a mod only the client has: left as it came)
            if (e.source is not { encFactor: > 0 }) {
                continue;
            }

            // (mtp + rnd(mtp + 3 + 15 + sqrt(lv * encFactor / 100))) / mtp
            var cap = 20 + (int)System.Math.Sqrt(10_000_000L * e.source.encFactor / 100);
            if (System.Math.Abs(e.vBase) > cap) {
                Odd(thing, $"element {e.id}", e.vBase, cap);
                e.vBase = System.Math.Sign(e.vBase) * cap;
            }
        }

        foreach (var held in thing.things) {
            Bound(held);
        }
    }

    private void Odd(Thing thing, string what, int value, int max)
    {
        EmpLog.Warning("Story gift {ThingId} from peer {PeerIndex}: {What} {Value} brought back to {Max}",
            thing.id, OriginPeer, what, value, max);
    }
}
