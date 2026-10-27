using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Helper.Extensions;
using ElinTogether.Models;
using ElinTogether.Net.Steam;
using UnityEngine;

namespace ElinTogether.Net;

/// <summary>
///     Shipping per player: the morning sale pays every player for the goods it put in the box. <br />
///     Only the money is personal: the shipping total and its bonus tiers are one progression for the world
///     (the bonus of a tier goes to the player whose sale reaches it), like the hearth experience of the base
/// </summary>
internal partial class ElinNetHost
{
    private const int AccountOwedMoney = 0;
    private const int AccountOwedBonus = 1;

    /// <summary>
    ///     Player chara uid -> money and bonus not handed over yet
    ///     (the player was a guest somewhere or offline when its goods were sold)
    /// </summary>
    [ElinGameIOProperty("shipping_owed")]
    private static Dictionary<int, long[]> ShippingAccounts
    {
        get => field ??= [];
        set;
    }

    private static long[] GetShippingAccount(int charaUid)
    {
        if (!ShippingAccounts.TryGetValue(charaUid, out var account) || account.Length < 2) {
            account = ShippingAccounts[charaUid] = new long[2];
        }

        return account;
    }

    /// <summary>
    ///     Zone session host: the shipping box here is a copy, the goods go to the real host
    /// </summary>
    internal bool ForwardShippingDeposit(Thing thing, int shipperUid, int box = 0)
    {
        return IsZoneSession && Session.Transport is ElinNetClient main && main.ForwardShippingDeposit(thing, shipperUid, box);
    }

    /// <summary>
    ///     Answer to a player away: what a box of the world holds now, and what it takes out of it. <br />
    ///     The boxes are the host's, one for everyone: a stack is given once, never more than is left of it
    /// </summary>
    private void SendWorldBox(ISteamNetPeer peer, ShippingDeposit ask)
    {
        if (ShippingHelper.WorldBox(ask.Box) is not { } container) {
            return;
        }

        LZ4Bytes? taken = null;
        TakenPart? held = null;
        if (ask.Ask && ask.TakeNum > 0 &&
            container.things.Flatten().FirstOrDefault(t => t.uid == ask.TakeUid) is { isDestroyed: false } stack) {
            var part = stack.Split(Math.Min(ask.TakeNum, stack.Num));
            var shipper = part.ShipperUid;
            part.SetInt(ShippingHelper.ShipperKey, 0);
            taken = LZ4Bytes.Create(part);
            // above the mark already on the kept character too: this world may come from a host whose clock was
            // behind, and an old mark must not read as the confirmation of this take
            _takeSeq = Math.Max(Math.Max(_takeSeq + 1, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                (KeptChara(peer)?.GetInt(ShippingHelper.TookKey) ?? 0) + 1);
            held = new(taken, ask.Box, shipper, _takeSeq, part.id == "money", part.Num, peer);
            // out of the box now (nobody else can take it); the bytes are the way back, see SettleTaken
            part.Destroy();

            EmpLog.Debug("Player {@Peer} took {CardId} x{CardNum} out of world box {Box}",
                peer, part.id, part.Num, ask.Box);
        }

        var sent = peer.Send(new ShippingPayout {
            Ints = [],
            ItemStrs = [],
            ShipNum = 0,
            ShipMoney = 0,
            BranchLv = 0,
            BranchExp = 0,
            Box = ask.Box,
            BoxThings = LZ4Bytes.Create(container.things.ToList()),
            Taken = taken,
            Asked = ask.Ask ? ask.TakeNum : 0,
            TakenToken = held?.Token ?? 0,
        });

        if (held is not null) {
            if (sent) {
                HoldTaken(held);
            } else {
                PutInWorldBox(held.Bytes, held.Box, held.Shipper);
            }
        }
    }

    /// <summary>
    ///     A thing a player took out of a box of the world, kept as bytes until we know the player has it
    /// </summary>
    private sealed record TakenPart(LZ4Bytes Bytes, int Box, int Shipper, int Token, bool Money, int Num,
        ISteamNetPeer Peer);

    /// <summary>
    ///     The character the host keeps for a player (replaced by each checkpoint and by its return)
    /// </summary>
    private Chara? KeptChara(ISteamNetPeer peer)
    {
        return SavedRemoteCharas.TryGetValue(peer.User, out var uid) ? game.cards.globalCharas.Find(uid) : null;
    }

    private readonly List<TakenPart> _taken = [];
    private int _takeSeq;

    private void HoldTaken(TakenPart held)
    {
        if (_taken.Count == 0) {
            Scheduler.Subscribe(SettleTaken, 2f);
        }

        _taken.Add(held);
    }

    /// <summary>
    ///     The player has what it took when the bag the host keeps for it (replaced by each checkpoint, and by its
    ///     return) carries the number sent with it: the mark and the thing are saved in the same character, so no
    ///     message can be lost between them. Nothing there and the link gone, or the player back on this map: the
    ///     thing goes back into its box. Never on a delay while the player is connected and away: its checkpoint
    ///     may be late, and putting the thing back then would give it twice when the checkpoint comes. <br />
    ///     Left possible: this game ending in the instant between the take and this check loses the thing; a
    ///     player that lost the link but keeps playing its own copy and brings it back later holds it too; a player
    ///     that stays connected and away without ever checkpointing keeps the thing out of its box until it returns
    ///     or drops. A bank line is told only for a confirmed take
    /// </summary>
    private void SettleTaken()
    {
        foreach (var held in _taken.ToArray()) {
            var kept = KeptChara(held.Peer);
            var confirmed = kept?.GetInt(ShippingHelper.TookKey) >= held.Token;
            if (!confirmed && IsAway(held.Peer) && held.Peer.IsConnected && Socket.Peers.Any(p => ReferenceEquals(p, held.Peer))) {
                continue;
            }

            // the entry goes whatever happens below: a failure must not repeat every half second
            _taken.Remove(held);
            try {
                if (confirmed) {
                    if (held.Money && held.Box == ShippingHelper.BoxBank) {
                        BillPayDelta.TellBank(this, kept!.NameSimple, held.Num, false);
                    }
                } else {
                    PutInWorldBox(held.Bytes, held.Box, held.Shipper);
                    EmpLog.Information("Player {@Peer} never confirmed taking a thing out of world box {Box}, put back",
                        held.Peer, held.Box);
                }
            } catch (Exception ex) {
                EmpLog.Warning(ex, "Could not settle a thing taken out of world box {Box} by player {@Peer}",
                    held.Box, held.Peer);
            }
        }

        if (_taken.Count == 0) {
            Scheduler.Unsubscribe(SettleTaken);
        }
    }

    /// <summary>
    ///     Net event: a player away from the host map put an item in the shipping box
    /// </summary>
    private void OnShippingDeposit(ShippingDeposit deposit, ISteamNetPeer peer)
    {
        if (IsZoneSession || !SavedRemoteCharas.TryGetValue(peer.User, out var own)) {
            return;
        }

        if (deposit.Ask) {
            SendWorldBox(peer, deposit);
            return;
        }

        // from afar only (on this map the box is the game's own); and on a public or dedicated server, at a pace and
        // an amount of gold a player reaches (security audit 2026-10-27: any item or sum went into the world's boxes)
        if (!_departed.Contains(peer.Id) || DepositRefused(peer, deposit)) {
            EmpLog.Warning("Refused a deposit from {@Peer} in box {Box}", peer, deposit.Box);
            return;
        }

        // a player ships for itself, the owner of a zone for its guests too
        var forGuest = _guests.Any(kv => kv.Value.HolderId == peer.Id &&
                                         Socket.Peers.FirstOrDefault(p => p.Id == kv.Key) is { } guest &&
                                         SavedRemoteCharas.GetValueOrDefault(guest.User) == deposit.Shipper);
        var shipper = forGuest ? deposit.Shipper : own;

        var depositor = game.cards.globalCharas.Find(own)?.NameSimple;
        var thing = PutInWorldBox(deposit.Thing, deposit.Box, shipper, depositor);

        EmpLog.Debug("Player {@Peer} shipped {CardId} x{CardNum} from afar for chara {Uid}",
            peer, thing.id, thing.Num, shipper);

        // no answer: the player asks for the box once after its deposits, see ElinNetClient.AskWorldBoxSoon
    }

    // peer -> (since, deposits, gold) over the last hour
    private readonly Dictionary<int, (float Since, int Count, long Gold)> _deposits = [];

    private bool DepositRefused(ISteamNetPeer peer, ShippingDeposit deposit)
    {
        if (!ElinDelta.TrustEnforced) {
            return false;
        }

        var now = UnityEngine.Time.realtimeSinceStartup;
        if (!_deposits.TryGetValue(peer.Id, out var seen) || now - seen.Since > 3600f) {
            seen = (now, 0, 0);
        }

        var thing = deposit.Thing.Decompress<Thing>();
        var gold = thing.id == "money" ? (long)thing.Num : 0L;
        if (seen.Count >= 600 || seen.Gold + gold > 2_000_000) {
            AdminLog($"refused a deposit from {(ulong)peer.User}: {seen.Count} deposits, {seen.Gold + gold} gold in an hour");
            return true;
        }

        _deposits[peer.Id] = (seen.Since, seen.Count + 1, seen.Gold + gold);
        return false;
    }

    /// <summary>
    ///     Put a thing (as bytes, from another world) in a box of this world
    /// </summary>
    /// <param name="depositor">name of the player depositing: gold in the bank is told to everyone</param>
    private Thing PutInWorldBox(LZ4Bytes bytes, int box, int shipper, string? depositor = null)
    {
        var thing = bytes.Decompress<Thing>();
        // its uid comes from another world
        foreach (var card in thing.things.Flatten().Prepend(thing)) {
            game.cards.AssignUID(card);
        }

        CardCache.Add(thing);
        Delta.AddRemote(CardGenDelta.Create(thing));

        if (depositor is not null && box == ShippingHelper.BoxBank && thing.id == "money") {
            BillPayDelta.TellBank(this, depositor, thing.Num, true);
        }

        switch (box) {
            case ShippingHelper.BoxDelivery:
                game.cards.container_deliver.AddThing(thing);
                break;
            case ShippingHelper.BoxBank:
                game.cards.container_deposit.AddThing(thing);
                break;
            default:
                ShippingHelper.ShipperOverride = shipper;
                try {
                    game.cards.container_shipping.AddThing(thing);
                } finally {
                    ShippingHelper.ShipperOverride = null;
                }

                break;
        }

        return thing;
    }

    /// <summary>
    ///     Before the host sells the box (GameDate.ShipGoods): sell the goods of the other players for them
    /// </summary>
    internal void ShipPlayersGoods()
    {
        if (IsZoneSession) {
            return;
        }

        PayOwnShipping();

        if (!Session.Rules.UsePlayerShipping || game.cards.container_shipping is not { } box) {
            return;
        }

        var zone = game.spatials.Find(player.uidLastShippedZone);
        if (zone?.branch is null) {
            zone = pc.homeZone;
        }

        if (zone?.branch is null) {
            return;
        }

        var players = SavedRemoteCharas.Values.Concat(PlayerRosters.Values.SelectMany(roster => roster)).ToHashSet();
        // the character this game plays is not another player, whatever the tables say (the host of a world taken
        // over stays in the roster of its account): its goods are left to the game's own sale, paid and reported
        // as in single player
        players.Remove(pc.uid);
        var goods = box.things
            .Where(t => t.trait.CanBeShipped && players.Contains(t.ShipperUid))
            .GroupBy(t => t.ShipperUid)
            .ToList();

        foreach (var group in goods) {
            ShipFor(group.Key, group.ToList(), zone);
        }
    }

    /// <summary>
    ///     What is owed to the character this game plays: sales made while it was a guest of this world, and the
    ///     sales 0.26.532 and before made "for a player" who was the host itself (no peer to pay, kept in the save)
    /// </summary>
    private static void PayOwnShipping()
    {
        if (pc is null || !ShippingAccounts.TryGetValue(pc.uid, out var account) || account.Length < 2) {
            return;
        }

        var money = (int)Math.Clamp(account[AccountOwedMoney], 0, int.MaxValue);
        var bonus = (int)Math.Clamp(account[AccountOwedBonus], 0, int.MaxValue);
        if (money == 0 && bonus == 0) {
            return;
        }

        account[AccountOwedMoney] -= money;
        account[AccountOwedBonus] -= bonus;

        // as GameDate.ShipGoods hands its income over
        if (money > 0) {
            pc.Pick(ThingGen.Create("money").SetNum(money));
        }

        if (bonus > 0) {
            pc.Pick(ThingGen.Create("money2").SetNum(bonus));
        }

        EmpLog.Information("Paid the host what its own shipping was owed: {ShipIncome}, bonus {ShipBonus}", money, bonus);
    }

    private void ShipFor(int shipperUid, List<Thing> goods, Zone zone)
    {
        var account = GetShippingAccount(shipperUid);
        var result = new ShippingResult {
            rawDate = world.date.GetRaw(),
            uidZone = zone.uid,
            total = player.stats.shipMoney,
            hearthLv = zone.branch.lv,
            hearthExp = zone.branch.exp,
        };

        long income = 0;
        var count = 0;
        var exp = 0;
        // the price reads "the player's" god (Card.GetPrice: Kumiromi, Ehekatl): the shipper's, not the host's
        var self = player.chara;
        Dictionary<Thing, int> prices;
        try {
            player.chara = game.cards.globalCharas.Find(shipperUid) ?? self;
            prices = goods.ToDictionary(t => t, t => t.GetPrice(CurrencyType.Money, true, PriceType.Shipping));
        } finally {
            player.chara = self;
        }

        foreach (var thing in goods) {
            // same numbers as GameDate.ShipGoods
            var price = prices[thing];
            var sum = (long)price * thing.Num;
            income += sum;
            count += thing.Num;
            exp += EClass.rndHalf(thing.Num * Mathf.Min(15 + price, 10000) / 100 + 1);
            result.items.Add(new() {
                text = thing.Name,
                income = sum,
            });
        }

        exp = zone.branch.policies.IsActive(2515) ? 0 : exp / 2 + 1;
        result.hearthExpGained = exp;

        // one shipping total for the world, the bonus of a tier goes to whoever reaches it
        var bonusBefore = player.stats.GetShippingBonus(player.stats.shipMoney);
        player.stats.shipNum += count;
        player.stats.shipMoney += income;
        var bonus = Math.Max(0, player.stats.GetShippingBonus(player.stats.shipMoney) - bonusBefore);

        foreach (var thing in goods) {
            thing.Destroy();
        }

        // the base is everyone's
        zone.branch.statistics.ship += income;
        zone.branch.ModExp(exp);

        account[AccountOwedMoney] += income;
        account[AccountOwedBonus] += bonus;

        EmpLog.Information("Shipped {ShipItemCount} goods of player chara {Uid} for {ShipIncome}, bonus {ShipBonus}",
            count, shipperUid, income, bonus);

        PayShipping(shipperUid, result);
    }

    /// <summary>
    ///     Hand over what a player earned: at once on this map or to a player simulating its own zone,
    ///     later (when it stands on this map again) to a guest or an offline player
    /// </summary>
    private void PayShipping(int shipperUid, ShippingResult? result = null)
    {
        if (IsZoneSession) {
            return;
        }

        var account = GetShippingAccount(shipperUid);
        var money = account[AccountOwedMoney];
        var bonus = account[AccountOwedBonus];
        if (money <= 0 && bonus <= 0 && result is null) {
            return;
        }

        var peer = Socket.Peers.FirstOrDefault(p => SavedRemoteCharas.GetValueOrDefault(p.User) == shipperUid);
        if (peer is null) {
            return;
        }

        var onMap = ActiveRemoteCharas.GetValueOrDefault(peer.Id) is { } chara && chara.uid == shipperUid;
        var simulatesItself = _departed.Contains(peer.Id) && !_guests.ContainsKey(peer.Id) &&
                              !_pendingGuests.ContainsKey(peer.Id) &&
                              _leases.TryGetValue(peer.Id, out var zones) && zones.Count > 0;
        if (!onMap && !simulatesItself) {
            // its character is in someone else's hands (or on its way), wait for it here
            return;
        }

        // the client adds the money itself: a player's currency is its own to change (CardModCurrencyDelta
        // brings it back here on this map, its uploads when it travels)
        account[AccountOwedMoney] = 0;
        account[AccountOwedBonus] = 0;

        result ??= new() {
            rawDate = world.date.GetRaw(),
            uidZone = pc.homeZone?.uid ?? 0,
            total = player.stats.shipMoney,
        };

        var zone = game.spatials.Find(result.uidZone);
        peer.Send(new ShippingPayout {
            Ints = [..result.ints],
            ItemStrs = [..result.items.Select(item => item._strs)],
            ShipNum = player.stats.shipNum,
            ShipMoney = player.stats.shipMoney,
            BranchLv = zone?.branch?.lv ?? 0,
            BranchExp = zone?.branch?.exp ?? 0,
            Money = money,
            Bonus = bonus,
        });

        EmpLog.Debug("Paid shipping of player {@Peer}: {ShipIncome}, bonus {ShipBonus}, on map {OnMap}",
            peer, money, bonus, onMap);
    }
}
