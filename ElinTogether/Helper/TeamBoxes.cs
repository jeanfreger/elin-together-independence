using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper.Extensions;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-28 (dev/COUNCIL_2026-10-28_team_boxes.md), host option "Teams keep their own bank and delivery
///     box". The game has one bank and one delivery box for the whole world; with the box on, each OWNER
///     (<see cref="StoryOwner" />: a team, or a player without one alone) has its own two, REAL ones, kept in the host's
///     save (<see cref="ElinNetHost.TeamBoxBook" />) and parked outside the game's slots. Off by default, always on for
///     the official public server. <br />
///     The boxes the world already had stay in the game's slots, as the shared "world" boxes anyone can still take from;
///     what is put in from now on goes to the owner's own. The host picks the box from the sender's owner, never from a
///     value a client sends, and a take is only from the sender's own box or the world's. <br />
///     Wherever the game's own code reads a slot (a bill paid by the bank, the month's salary, the delivery box
///     shipped) <see cref="As" /> puts that owner's boxes in the slots for the call; a window opened by the host's own
///     player is opened on its own boxes (<see cref="OnCreateContainer" />), a guest on the host's map is sent copies
///     of its own, with the real uids (<see cref="TeamBoxDelta" />): what the host and the guest do to them reaches
///     the other as it does for any card
/// </summary>
[HarmonyPatch]
internal static class TeamBoxes
{
    /// <summary>
    ///     The ledger's layout. A save written by a newer layout is left as it is (the world's boxes only)
    /// </summary>
    internal const int Schema = 1;

    private const int Bank = 0;
    private const int Delivery = 1;

    // (the guest's copies only: its team's bank, beside its personal one)
    private const int TeamBank = 2;

    // the game that keeps the boxes: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    private static bool TooNew => KeepsBook && ElinNetHost.TeamBoxState[0] > Schema;

    /// <summary>
    ///     The box as this game knows it: the host's own setting, a guest's from the host's rules
    /// </summary>
    internal static bool On => !TooNew && (KeepsBook ? Wanted : NetSession.Instance.Rules is { TeamBoxes: true });

    /// <summary>
    ///     The host's box: needs Teams, read as on while this game hosts the official public server
    /// </summary>
    internal static bool Wanted => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBoxes.Value || StoryOwner.OfficialHosting;

    // the game the ledger was read against, and whether the box was on the last time we looked
    private static Game? _game;
    private static bool? _wasWanted;

    // owner -> [bank, delivery box]: the real boxes, parked (no parent), in the game that keeps the book
    private static readonly Dictionary<int, Thing?[]> _live = [];

    // a guest on the host's map: copies of its personal bank, its owner's delivery box and its team's bank (none when
    // it has no team), with the host's uids
    private static Thing?[]? _replica;
    private static Game? _replicaGame;

    // peer -> the owner whose boxes it was sent
    private static readonly Dictionary<int, int> _told = [];
    private static float _nextPoll;
    private static bool _shipping;
    private static bool _opening;

    // ---- the boxes

    private static int SlotOf(int kind)
    {
        return kind == ShippingHelper.BoxBank ? Bank : Delivery;
    }

    /// <summary>
    ///     The owner's real box (made when it has none yet): 0 the bank, 1 the delivery box
    /// </summary>
    internal static Thing Box(int owner, int slot)
    {
        Sync();
        if (!_live.TryGetValue(owner, out var boxes)) {
            _live[owner] = boxes = new Thing?[2];
        }

        if (boxes[slot] is not { isDestroyed: false } box) {
            box = boxes[slot] = Register(ThingGen.Create(slot == Bank ? "container_deposit" : "container_delivery"));
            EmpLog.Information("{Owner} has a new {Box}", StoryOwner.Name(owner), slot == Bank ? "bank" : "delivery box");
        }

        return box;
    }

    // known to the card cache (deltas about it resolve), and kept alive: nothing holds a parked box but us
    private static Thing Register(Thing box)
    {
        CardCache.Add(box);
        foreach (var thing in box.things.Flatten()) {
            CardCache.Add(thing);
        }

        CardCache.KeepAlive(box);
        return box;
    }

    /// <summary>
    ///     Every box of the owners, for the card cache and the bill counter
    /// </summary>
    internal static IEnumerable<Thing> All()
    {
        return _live.Values.SelectMany(b => b).Concat(_replica ?? []).OfType<Thing>().Where(b => !b.isDestroyed);
    }

    /// <summary>
    ///     ShippingHelper.BoxBank or BoxDelivery when the card is a box of an owner, 0 for any other card
    /// </summary>
    internal static int KindOf(Card? card)
    {
        if (card is null || _live.Count == 0 && _replica is null) {
            return 0;
        }

        if (OwnerOf(card, out var slot) is not null) {
            return slot == Bank ? ShippingHelper.BoxBank : ShippingHelper.BoxDelivery;
        }

        return ReferenceEquals(_replica?[Bank], card) || ReferenceEquals(_replica?[TeamBank], card) ? ShippingHelper.BoxBank :
            ReferenceEquals(_replica?[Delivery], card) ? ShippingHelper.BoxDelivery : 0;
    }

    /// <summary>
    ///     The owner of the box this card is, none for any other card
    /// </summary>
    internal static int? OwnerOf(Card? card, out int slot)
    {
        slot = -1;
        if (card is null) {
            return null;
        }

        foreach (var (owner, boxes) in _live) {
            for (var i = 0; i < boxes.Length; i++) {
                if (ReferenceEquals(boxes[i], card)) {
                    slot = i;
                    return owner;
                }
            }
        }

        return null;
    }

    private static bool IsWorldBox(Card card, out int slot)
    {
        var cards = EClass.game?.cards;
        slot = card == cards?.container_deposit ? Bank : card == cards?.container_deliver ? Delivery : -1;
        return slot >= 0;
    }

    /// <summary>
    ///     The personal bank of that player when the box is on, the world's bank otherwise. Its key is the one of the
    ///     player alone (<see cref="StoryOwner.Of(int,int)" /> of no team): a lone player's bank of the first version of
    ///     the box is already this one
    /// </summary>
    internal static Thing? BankOf(Chara? who)
    {
        return On && KeepsBook && who is not null ? Personal(who.uid) : EClass.game?.cards?.container_deposit;
    }

    /// <summary>
    ///     The personal bank of a player (made when it has none yet)
    /// </summary>
    internal static Thing Personal(int charaUid)
    {
        return Box(StoryOwner.Of(Teams.Wanderers, charaUid), Bank);
    }

    /// <summary>
    ///     The team a player is in (0: none), whose bank it also has
    /// </summary>
    private static int TeamOf(int charaUid)
    {
        return Teams.TeamOf(charaUid) is var team && team != Teams.Wanderers && team > 0 ? team : 0;
    }

    /// <summary>
    ///     Is this owner's box one of that player's: its personal bank, or its team's
    /// </summary>
    private static bool Owns(int charaUid, int owner)
    {
        return owner == StoryOwner.Of(Teams.Wanderers, charaUid) || owner > 0 && owner == TeamOf(charaUid);
    }

    /// <summary>
    ///     Where a player's own thing goes in place of a box of the world: the personal bank, the owner's delivery box
    /// </summary>
    internal static Thing Own(int charaUid, int slot)
    {
        return slot == Bank ? Personal(charaUid) : Box(StoryOwner.Of(charaUid), Delivery);
    }

    /// <summary>
    ///     The boxes a player may see and take from in that kind of box: the personal bank and the team's, or the
    ///     owner's delivery box
    /// </summary>
    internal static IEnumerable<Thing> Mine(int charaUid, int slot)
    {
        yield return Own(charaUid, slot);
        if (slot == Bank && TeamOf(charaUid) is > 0 and var team) {
            yield return Box(team, Bank);
        }
    }

    /// <summary>
    ///     The bank of that team's lands: the team's own, the world's for the lands of no team
    /// </summary>
    internal static Thing? BankOfTeam(int team)
    {
        return On && KeepsBook && team > 0 ? Box(team, Bank) : EClass.game?.cards?.container_deposit;
    }

    /// <summary>
    ///     The game's own code reads the slots: for the call, that owner's boxes are in them
    /// </summary>
    internal static void As(int owner, Action action)
    {
        if (!On || !KeepsBook || EClass.game?.cards is not { } cards) {
            action();
            return;
        }

        var (bank, delivery) = (cards.container_deposit, cards.container_deliver);
        (cards.container_deposit, cards.container_deliver) = (Box(owner, Bank), Box(owner, Delivery));
        try {
            action();
        } finally {
            (cards.container_deposit, cards.container_deliver) = (bank, delivery);
        }
    }

    /// <summary>
    ///     <see cref="As" /> for a team's lands: the Wanderers' (team 0) keep the world's boxes
    /// </summary>
    internal static void AsTeam(int team, Action action)
    {
        if (team > 0) {
            As(team, action);
        } else {
            action();
        }
    }

    // ---- who may reach what

    /// <summary>
    ///     On the host, for a thing a guest puts in <paramref name="parent" />: where it goes. The world's box: the
    ///     sender's own (chosen here, from the sender); a box of another owner: nowhere (null)
    /// </summary>
    internal static Card? Target(ElinNetHost host, int peer, Card parent)
    {
        if (!On || host.IsZoneSession || !host.ActiveRemoteCharas.TryGetValue(peer, out var sender)) {
            return parent;
        }

        if (OwnerOf(parent.GetRootCard(), out _) is { } owner) {
            return Owns(sender.uid, owner) ? parent : null;
        }

        return IsWorldBox(parent, out var slot) ? Own(sender.uid, slot) : parent;
    }

    /// <summary>
    ///     On the host: may this player take that thing? Not out of the box of another owner
    /// </summary>
    internal static bool CanTake(Chara taker, Thing thing)
    {
        return !On || OwnerOf(thing.GetRootCard(), out _) is not { } owner || Owns(taker.uid, owner);
    }

    /// <summary>
    ///     A change to what an owner's box holds (a thing added, removed, a count changed) goes to that owner's players
    ///     only: the others have no such box to apply it to (they logged "cannot be resolved") and would read the gold in
    ///     it. True when it was sent here
    /// </summary>
    internal static bool Divert(ElinDeltaManager manager, ElinDelta delta)
    {
        var card = delta switch {
            CardAddThingDelta add => add.Parent.Find(),
            CardRemoveThingDelta remove => remove.Thing.Find()?.parent as Card ?? remove.Thing.Find(),
            CardModNumDelta num => num.Card.Find(),
            CardTryStackToDelta stack => stack.To.Find(),
            _ => null,
        };
        if (card is null || !On || NetSession.Instance.Transport is not ElinNetHost { IsZoneSession: false } host ||
            !ReferenceEquals(host.Delta, manager) || OwnerOf(card.GetRootCard(), out _) is not { } owner) {
            return false;
        }

        // (the buffer refreshes a delta just before it goes: a thing new to the players carries its data, or they drop it
        // as "cannot be resolved", here nothing passes through the buffer)
        if (!delta.Refresh()) {
            return true;
        }

        foreach (var (peer, uid) in host.PeerCharas()) {
            if (Owns(uid, owner)) {
                host.SendDeltaTo(peer, delta);
            }
        }

        return true;
    }

    // ---- every frame, in the game that keeps the book

    // a save just read: the parked boxes are the ledger's (before any box is asked for)
    private static void Sync()
    {
        if (!KeepsBook || ReferenceEquals(EClass.game, _game)) {
            return;
        }

        _game = EClass.game;
        _wasWanted = null;
        Load();
        if (TooNew) {
            EmpLog.Warning("The teams' boxes were written by a newer version of the mod ({Schema}): left as they are",
                ElinNetHost.TeamBoxState[0]);
        }
    }

    /// <summary>
    ///     The box unticked (seen on, now off, in this game): everything the owners' boxes hold goes back in the world's.
    ///     On: the guests on the host's map are sent their own boxes
    /// </summary>
    internal static void Update()
    {
        if (!KeepsBook || !EClass.core.IsGameStarted || EClass.game?.cards is null) {
            return;
        }

        Sync();
        if (TooNew) {
            return;
        }

        var unticked = _wasWanted == true && !Wanted;
        _wasWanted = Wanted;
        if (!Wanted) {
            if (unticked && _live.Count > 0) {
                MergeBack();
            }

            return;
        }

        if (ElinNetHost.TeamBoxState[1] == 0) {
            ElinNetHost.TeamBoxState = [Schema, 1];
            EmpLog.Information("Teams keep their own bank and delivery box: what the world's boxes hold stays shared");
        }

        if (NetSession.Instance.Transport is ElinNetHost { IsZoneSession: false } host && Time.unscaledTime >= _nextPoll) {
            _nextPoll = Time.unscaledTime + 1f;
            Tell(host);
        }
    }

    /// <summary>
    ///     Every player on the host's map has the copies of its owner's boxes (once, then when its owner changes: the
    ///     changes to a box reach it as the changes to any card do)
    /// </summary>
    private static void Tell(ElinNetHost host)
    {
        foreach (var peer in _told.Keys.Where(p => !host.ActiveRemoteCharas.ContainsKey(p)).ToArray()) {
            _told.Remove(peer);
        }

        foreach (var (peer, chara) in host.ActiveRemoteCharas) {
            if (host.IsAwayPeer(peer)) {
                _told.Remove(peer);
                continue;
            }

            var owner = StoryOwner.Of(chara.uid);
            if (_told.TryGetValue(peer, out var was) && was == owner) {
                continue;
            }

            _told[peer] = owner;
            host.SendDeltaTo(peer, new TeamBoxDelta {
                Bank = LZ4Bytes.Create(Personal(chara.uid)),
                Delivery = LZ4Bytes.Create(Box(owner, Delivery)),
                TeamBank = TeamOf(chara.uid) is > 0 and var team ? LZ4Bytes.Create(Box(team, Bank)) : null,
            });
        }
    }

    // ---- the ledger

    private static string Pack(Thing box)
    {
        return Convert.ToBase64String(LZ4Bytes.Create(box).Bytes);
    }

    private static Thing? Unpack(string text)
    {
        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        try {
            return Register(new LZ4Bytes { Bytes = Convert.FromBase64String(text) }.Decompress<Thing>());
        } catch (Exception ex) {
            EmpLog.Warning(ex, "A team's box could not be read, it starts again empty");
            return null;
        }
    }

    private static void Load()
    {
        _live.Clear();
        _told.Clear();
        ForgetReplica();
        foreach (var (owner, texts) in ElinNetHost.TeamBoxBook) {
            _live[owner] = [Unpack(texts.ElementAtOrDefault(Bank) ?? ""), Unpack(texts.ElementAtOrDefault(Delivery) ?? "")];
        }
    }

    // the game writes its save: the ledger holds the boxes as they are now (the game's slots keep the world's)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Game), nameof(Game.Save))]
    private static void BeforeSave()
    {
        if (!KeepsBook || TooNew || !ReferenceEquals(EClass.game, _game)) {
            return;
        }

        var book = ElinNetHost.TeamBoxBook;
        book.Clear();
        foreach (var (owner, boxes) in _live) {
            book[owner] = boxes.Select(b => b is { isDestroyed: false } ? Pack(b) : "").ToArray();
        }
    }

    // ---- unticked

    private static void MergeBack()
    {
        var cards = EClass.game.cards;
        int gold = 0, items = 0;
        foreach (var boxes in _live.Values.ToArray()) {
            for (var slot = 0; slot < boxes.Length; slot++) {
                if (boxes[slot] is not { isDestroyed: false } box) {
                    continue;
                }

                var world = slot == Bank ? cards.container_deposit : cards.container_deliver;
                gold += slot == Bank ? box.GetCurrency() : 0;
                foreach (var thing in box.things.ToArray()) {
                    world.AddThing(thing);
                    items++;
                }

                box.Destroy();
            }
        }

        _live.Clear();
        ElinNetHost.TeamBoxBook.Clear();
        ElinNetHost.TeamBoxState = [Schema, 0];
        _told.Clear();
        EmpLog.Information("Teams share the bank and the delivery box again: {Gold} gold and {Items} things went back in the world's",
            gold, items);
        if (NetSession.Instance.Transport is ElinNetHost { IsZoneSession: false } host) {
            foreach (var peer in host.ActiveRemoteCharas.Keys) {
                host.SendDeltaTo(peer, new TeamBoxDelta());
            }
        }
    }

    // ---- a guest on the host's map

    internal static Thing? Replica(int slot)
    {
        return ReferenceEquals(_replicaGame, EClass.game) ? _replica?[slot] : null;
    }

    /// <summary>
    ///     The host says what this player has in its boxes: its personal bank, its owner's delivery box and its team's
    ///     bank, if any (none: it has no boxes of its own any more)
    /// </summary>
    internal static void Receive(LZ4Bytes? bank, LZ4Bytes? delivery, LZ4Bytes? teamBank)
    {
        if (KeepsBook) {
            return;
        }

        ForgetReplica();
        if (bank is null || delivery is null) {
            return;
        }

        _replica = [Register(bank.Decompress<Thing>()), Register(delivery.Decompress<Thing>()),
            teamBank is null ? null : Register(teamBank.Decompress<Thing>())];
        _replicaGame = EClass.game;
        EmpLog.Information("The host sent this player's bank ({Gold} gold), team bank ({Team} gold) and delivery box ({Count} things)",
            _replica[Bank]!.GetCurrency(), _replica[TeamBank]?.GetCurrency() ?? -1, _replica[Delivery]!.things.Count);
    }

    private static void ForgetReplica()
    {
        foreach (var box in _replica ?? []) {
            if (box is null) {
                continue;
            }

            foreach (var thing in box.things.Flatten()) {
                CardCache.Remove(thing.uid);
            }

            CardCache.Remove(box.uid);
        }

        _replica = null;
        _replicaGame = null;
    }

    // ---- windows

    /// <summary>
    ///     The host's own player, or a guest on the host's map, opens the bank or a delivery box: it opens its own
    ///     (the guests' windows show the copies, the host's the real ones). The team's bank, and what the world's box
    ///     still holds, are shown in windows of their own beside it, so that nothing is out of reach
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerInventory), nameof(LayerInventory.CreateContainer), typeof(Card), typeof(Card))]
    private static void OnCreateContainer(ref Card owner, ref Card container)
    {
        if (_opening || !On || EClass.game?.cards is not { } cards || container is null || !IsWorldBox(container, out var slot)) {
            return;
        }

        var (own, team) = MyWindows(slot);
        if (own is null) {
            return;
        }

        var world = container;
        if (owner == container) {
            owner = own;
        }

        container = own;
        _opening = true;
        try {
            // (the team's bank and what the world's box still holds: windows of their own beside it)
            if (team is not null) {
                LayerInventory.CreateContainer(team, team);
            }

            if (world.things.Count > 0) {
                LayerInventory.CreateContainer(world, world);
            }
        } finally {
            _opening = false;
        }
    }

    // this game's own player: its boxes of that kind (the host's real ones, a guest's copies), the team's bank second
    private static (Thing? Own, Thing? Team) MyWindows(int slot)
    {
        return NetSession.Instance.Transport switch {
            ElinNetHost { IsZoneSession: false } => (Own(EClass.pc.uid, slot),
                slot == Bank && TeamOf(EClass.pc.uid) is > 0 and var team ? Box(team, Bank) : null),
            ElinNetClient { IsZoneSession: false } when NetSession.Instance.Connection is not null =>
                (Replica(slot), slot == Bank ? Replica(TeamBank) : null),
            _ => (null, null),
        };
    }

    /// <summary>
    ///     The character sheet's "bank" is the world's box in the game: with the box on it adds this player's personal
    ///     bank and its team's (the host's real ones, a guest's copies), the world's being the shared one beside them
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(WindowChara), nameof(WindowChara.RefreshProfile))]
    private static void OnSheet(WindowChara __instance)
    {
        if (!On || !__instance.chara.IsPC || EClass.game?.cards is not { } cards) {
            return;
        }

        var (own, team) = MyWindows(Bank);
        if (own is not null) {
            __instance.textDeposit.text = Lang._currency(
                cards.container_deposit.GetCurrency() + own.GetCurrency() + (team?.GetCurrency() ?? 0), showUnit: true);
        }
    }

    // ---- the game's own calls

    // the delivery boxes go out in the morning: each owner's, paid by its own bank
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.ShipPackages))]
    private static void OnShipPackages(GameDate __instance)
    {
        if (_shipping || !On || !KeepsBook) {
            return;
        }

        _shipping = true;
        try {
            foreach (var owner in _live.Keys.ToArray()) {
                if (Box(owner, Delivery).things.Count > 0) {
                    As(owner, __instance.ShipPackages);
                }
            }
        } finally {
            _shipping = false;
        }
    }

    // ---- bench

    /// <summary>
    ///     What the bench reads: the world's boxes, then each owner's gold in the bank and things in the delivery box
    /// </summary>
    internal static string Describe()
    {
        var cards = EClass.game.cards;
        var lines = _live.OrderBy(o => o.Key).Select(o =>
            $"{StoryOwner.Name(o.Key)} bank {o.Value[Bank]?.GetCurrency() ?? 0} things {o.Value[Bank]?.things.Count ?? 0} delivery {o.Value[Delivery]?.things.Count ?? 0}");
        return $"schema {ElinNetHost.TeamBoxState[0]} seen {ElinNetHost.TeamBoxState[1]}\nworld bank {cards.container_deposit.GetCurrency()} things {cards.container_deposit.things.Count} delivery {cards.container_deliver.things.Count}\n" +
               string.Join("\n", lines);
    }
}
