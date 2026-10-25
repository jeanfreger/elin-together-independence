using System.Collections.Generic;
using System.Linq;
using ElinTogether.Components;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     A trade between two players standing next to each other: each puts items and gold on its side of a window,
///     both confirm, and the exchange happens at once. <br />
///     Nothing is set aside while they talk it over: an offer is only a list of numbers. When both confirmed, the
///     game simulating the map (the host, or the player holding that map) checks everything again and moves the
///     real items, so nothing can be copied or lost on the way. Walking away, changing map or dropping out cancels
/// </summary>
public static class PlayerTrade
{
    public const int Invite_ = 0;
    public const int Accept_ = 1;
    public const int Decline_ = 2;
    public const int SetOffer_ = 3;
    public const int Confirm_ = 4;
    public const int Cancel_ = 5;

    public const int Invited = 0;
    public const int Open = 1;
    public const int Done = 2;
    public const int Cancelled = 3;

    private const int Reach = 3;

    /// <summary>
    ///     After any change of either offer, nobody can confirm for this long (real time)
    /// </summary>
    internal const float LockSeconds = 3f;
    private const float CheckInterval = 0.25f;

    private static readonly List<Session> _sessions = [];
    private static readonly List<TradeItem> _myItems = [];
    private static int _myGold;
    private static int _nextId = 1;
    private static object? _connection;
    private static Game? _game;
    private static float _nextCheck;

    /// <summary>
    ///     What the authority last said about the trade the local player is in
    /// </summary>
    public static TradeStateDelta? View { get; private set; }

    /// <summary>
    ///     Until when (this game's real time) the table is locked after a change: "confirm" waits
    /// </summary>
    public static float LockedUntil { get; private set; }

    public static bool IsLocked => Time.realtimeSinceStartup < LockedUntil;

    internal static bool Enabled => NetSession.Instance.Rules.AllowPlayerTrade;

    // ---------------------------------------------------------------- the local player

    public static void Invite(int partnerUid)
    {
        if (!Enabled || View is { Phase: Invited or Open }) {
            return;
        }

        _myItems.Clear();
        _myGold = 0;
        Send(new TradeIntentDelta {
            Kind = Invite_,
            PartnerUid = partnerUid,
        });
    }

    public static void Accept()
    {
        Answer(Accept_);
    }

    public static void Decline()
    {
        Answer(Decline_);
    }

    public static void Cancel()
    {
        Answer(Cancel_);
    }

    /// <summary>
    ///     Puts <paramref name="num" /> of that item on the table, 0 takes it back
    /// </summary>
    public static void Offer(int thingUid, int num)
    {
        _myItems.RemoveAll(i => i.Uid == thingUid);
        if (num > 0) {
            _myItems.Add(new() {
                Uid = thingUid,
                Num = num,
            });
        }

        SendOffer();
    }

    public static IReadOnlyList<TradeItem> MyItems => _myItems;

    public static int MyGold => _myGold;

    public static void SetGold(int gold)
    {
        _myGold = Mathf.Max(0, gold);
        SendOffer();
    }

    public static void Confirm()
    {
        if (View is not { Phase: Open } view) {
            return;
        }

        Send(new TradeIntentDelta {
            Kind = Confirm_,
            TradeId = view.TradeId,
            Revision = view.Revision,
        });
    }

    /// <summary>
    ///     For the test bridge: where the trade stands, in one line
    /// </summary>
    public static string Describe()
    {
        if (View is not { } view) {
            return "none";
        }

        var phase = view.Phase switch {
            Invited => "Invited",
            Open => "Open",
            Done => "Done",
            _ => "Cancelled",
        };
        return $"{phase} rev {view.Revision} A[{string.Join(",", view.ItemsA.Select(i => $"{i.Uid}x{i.Num}"))}]+{view.GoldA} " +
               $"ready={view.ReadyA} B[{string.Join(",", view.ItemsB.Select(i => $"{i.Uid}x{i.Num}"))}]+{view.GoldB} " +
               $"ready={view.ReadyB} {view.Reason}";
    }

    private static void AnswerInvitation(int tradeId, int kind)
    {
        if (View is { Phase: Invited } view && view.TradeId == tradeId) {
            Answer(kind);
        }
    }

    private static void Answer(int kind)
    {
        if (View is not { Phase: Invited or Open } view) {
            return;
        }

        Send(new TradeIntentDelta {
            Kind = kind,
            TradeId = view.TradeId,
        });
    }

    private static void SendOffer()
    {
        if (View is not { Phase: Open } view) {
            return;
        }

        Send(new TradeIntentDelta {
            Kind = SetOffer_,
            TradeId = view.TradeId,
            Items = _myItems.ToList(),
            Gold = _myGold,
        });
    }

    private static void Send(TradeIntentDelta intent)
    {
        switch (NetSession.Instance.Connection) {
            case ElinNetHost host:
                // this game simulates the map: the request is its own
                Handle(intent, EClass.pc, host);
                break;
            case ElinNetClient client:
                client.Delta.AddRemote(intent);
                break;
        }
    }

    /// <summary>
    ///     The authority tells where the trade stands
    /// </summary>
    internal static void Show(TradeStateDelta state)
    {
        var mine = EClass.pc.uid;
        if (state.UidA != mine && state.UidB != mine) {
            return;
        }

        var before = View;
        View = state;
        LockedUntil = Time.realtimeSinceStartup + state.LockMs / 1000f;

        switch (state.Phase) {
            case Invited when state.UidB == mine && (before is not { Phase: Invited } || before.TradeId != state.TradeId):
                _myItems.Clear();
                _myGold = 0;

                // the answer is for this invitation, whatever trade is going on by the time it is clicked
                var invitation = state.TradeId;
                Dialog.YesNo("emp_trade_invite".Loc(NameOf(state.UidA)),
                    () => AnswerInvitation(invitation, Accept_),
                    () => AnswerInvitation(invitation, Decline_));
                break;
            case Open:
                TradeWindow.Refresh();
                break;
            case Done:
                TradeWindow.Dismiss();
                EmpPop.Information("emp_trade_done".lang());
                break;
            case Cancelled:
                TradeWindow.Dismiss();
                EmpPop.Information((string.IsNullOrEmpty(state.Reason) ? "emp_trade_cancelled" : state.Reason).lang());
                break;
        }
    }

    internal static string NameOf(int charaUid)
    {
        return EClass._map.charas.Find(c => c.uid == charaUid)?.Name ?? "?";
    }

    // ---------------------------------------------------------------- the game simulating the map

    internal static void Handle(TradeIntentDelta intent, Chara from, ElinNetHost host)
    {
        if (!Enabled) {
            return;
        }

        if (intent.Kind == Invite_) {
            Start(from, intent.PartnerUid, host);
            return;
        }

        var session = _sessions.Find(s => s.Id == intent.TradeId && (s.A == from || s.B == from));
        if (session is null) {
            return;
        }

        switch (intent.Kind) {
            case Accept_ when session.B == from && !session.IsOpen:
                session.IsOpen = true;
                Tell(session, host, Open);
                break;
            case Decline_ or Cancel_:
                End(session, host, Cancelled, "");
                break;
            case SetOffer_ when session.IsOpen:
                if (session.A == from) {
                    session.ItemsA = intent.Items ?? [];
                    session.GoldA = Mathf.Max(0, intent.Gold);
                } else {
                    session.ItemsB = intent.Items ?? [];
                    session.GoldB = Mathf.Max(0, intent.Gold);
                }

                // what was agreed on is not what is on the table anymore
                session.Revision++;
                session.ReadyA = session.ReadyB = false;
                session.LockUntil = Time.realtimeSinceStartup + LockSeconds;
                Tell(session, host, Open);
                break;
            case Confirm_ when session.IsOpen && intent.Revision == session.Revision && Time.realtimeSinceStartup >= session.LockUntil:
                if (session.A == from) {
                    session.ReadyA = true;
                } else {
                    session.ReadyB = true;
                }

                if (session is { ReadyA: true, ReadyB: true }) {
                    Commit(session, host);
                } else {
                    Tell(session, host, Open);
                }

                break;
        }
    }

    /// <summary>
    ///     A trade needs both players there: gone, dead or too far cancels it
    /// </summary>
    internal static void Update()
    {
        if (_sessions.Count == 0 || Time.unscaledTime < _nextCheck) {
            return;
        }

        _nextCheck = Time.unscaledTime + CheckInterval;

        if (NetSession.Instance.Connection is not ElinNetHost host) {
            _sessions.Clear();
            return;
        }

        foreach (var session in _sessions.ToArray()) {
            if (!Present(session.A) || !Present(session.B)) {
                End(session, host, Cancelled, "");
            } else if (session.A.pos.Distance(session.B.pos) > Reach) {
                End(session, host, Cancelled, "emp_trade_far");
            }
        }
    }

    /// <summary>
    ///     Every frame, connected or not: a trade is between two players on one map, simulated by one game.
    ///     Another connection (travelling alone, the holder of the map changed, back at the title screen) or
    ///     another world (any map change of a client) and whatever trade was going on is over
    /// </summary>
    internal static void WatchSession()
    {
        var connection = NetSession.Instance.Connection;
        var game = EClass.core.IsGameStarted ? EClass.game : null;
        if (ReferenceEquals(connection, _connection) && ReferenceEquals(game, _game)) {
            return;
        }

        _connection = connection;
        _game = game;
        PlayerDuel.Clear();
        ElinTogether.Patches.TeamPvp.Clear();

        if (View is not null || _sessions.Count > 0) {
            Clear();
        }
    }

    internal static void Clear()
    {
        TradeWindow.ForgetClosed();
        _sessions.Clear();
        _myItems.Clear();
        _myGold = 0;
        View = null;
        TradeWindow.Dismiss();
    }

    /// <summary>
    ///     Invited to a trade or in one, as the authority knows it
    /// </summary>
    internal static bool Busy(Chara chara)
    {
        return _sessions.Exists(s => s.A == chara || s.B == chara);
    }

    private static bool Present(Chara chara)
    {
        return chara is { isDead: false, isDestroyed: false } && EClass._map.charas.Contains(chara);
    }

    private static void Start(Chara from, int partnerUid, ElinNetHost host)
    {
        var partner = EClass._map.charas.Find(c => c.uid == partnerUid);
        if (partner is null || partner == from || !partner.IsPlayer || !Present(from) || !Present(partner)) {
            return;
        }

        if (_sessions.Exists(s => s.A == from || s.B == from || s.A == partner || s.B == partner)) {
            TellOne(host, from, new() {
                Phase = Cancelled,
                UidA = from.uid,
                UidB = partner.uid,
                Reason = "emp_trade_busy",
            });
            return;
        }

        if (from.pos.Distance(partner.pos) > Reach) {
            TellOne(host, from, new() {
                Phase = Cancelled,
                UidA = from.uid,
                UidB = partner.uid,
                Reason = "emp_trade_far",
            });
            return;
        }

        var session = new Session {
            Id = _nextId++,
            A = from,
            B = partner,
        };
        _sessions.Add(session);
        Tell(session, host, Invited);
    }

    private static void Commit(Session session, ElinNetHost host)
    {
        var fromA = Resolve(session.A, session.ItemsA, session.GoldA, out var reasonA);
        var fromB = Resolve(session.B, session.ItemsB, session.GoldB, out var reasonB);
        if (fromA is null || fromB is null) {
            // something on the table is not there anymore or cannot be traded: no half trade, and they are told why
            End(session, host, Cancelled, fromA is null ? reasonA : reasonB);
            return;
        }

        // a bag that cannot take what it is about to receive: refused before anything moves. The simple safe count:
        // free cells now >= items received, without counting the cells freed by what that player gives
        if (!HasRoom(session.A, fromB.Count, session.GoldB) || !HasRoom(session.B, fromA.Count, session.GoldA)) {
            End(session, host, Cancelled, "emp_trade_full");
            return;
        }

        // the real items change hands here; the host's own patches tell everyone, nothing is created
        using (ElinDelta.Simulate()) {
            Move(session.A, session.B, fromA, session.GoldA);
            Move(session.B, session.A, fromB, session.GoldB);
        }

        EmpLog.Information("Players {UidA} and {UidB} traded {CountA}+{GoldA} for {CountB}+{GoldB}",
            session.A.uid, session.B.uid, fromA.Count, session.GoldA, fromB.Count, session.GoldB);
        End(session, host, Done, "");
    }

    /// <summary>
    ///     The items of an offer as they are right now in their owner's bag, null if any of it does not hold
    /// </summary>
    private static List<(Thing Thing, int Num)>? Resolve(Chara owner, List<TradeItem> items, int gold, out string reason)
    {
        reason = "emp_trade_invalid";
        if (gold > owner.GetCurrency()) {
            return null;
        }

        var resolved = new List<(Thing, int)>();
        foreach (var item in items) {
            var thing = owner.things.Find(item.Uid);
            if (thing is null || thing.isDestroyed || thing.GetRootCard() != owner || item.Num < 1 || thing.Num < item.Num ||
                resolved.Exists(r => r.Item1 == thing)) {
                return null;
            }

            if (Refuse(thing) is { } why) {
                reason = why;
                return null;
            }

            resolved.Add((thing, item.Num));
        }

        return resolved;
    }

    /// <summary>
    ///     Why this item cannot be put on the table (the text key shown to both players), null if it can. What the
    ///     single player game refuses to hand to an ally (<c>InvOwner.AllowHold</c>) for an item not worn, plus the
    ///     items bound to a character. Used by the trade window to list what can be offered, and again when both confirm
    /// </summary>
    internal static string? Refuse(Thing thing)
    {
        if (thing.isEquipped) {
            return "emp_trade_equipped";
        }

        if (!thing.trait.CanBeDropped) {
            return "emp_trade_nodrop";
        }

        if (thing.isNPCProperty) {
            return "emp_trade_npcprop";
        }

        if (thing.isGifted) {
            return "emp_trade_gifted";
        }

        if (thing.c_uidAttune != 0) {
            return "emp_trade_bound";
        }

        if (thing.c_isImportant || thing.trait is TraitAbility || thing.id is "money" || (thing.IsContainer && thing.things.Count > 0)) {
            return "emp_trade_invalid";
        }

        return null;
    }

    /// <summary>
    ///     Whether the top level of the bag has <paramref name="incoming" /> free cells. <c>Move</c> adds each item
    ///     there unstacked, so each one takes a cell. Counted from the items as <c>ThingContainer.IsOverflowing</c> does
    ///     (not worn, not on the hotbar) against <c>GridSize</c>, and not through <c>ThingContainer.IsFull</c> or the
    ///     <c>grid</c> list: only the game of that character's owner builds a grid, here a remote player's has none and
    ///     <c>IsFull</c> would then count its worn items too. Gold takes a cell too when the receiver has none yet
    ///     (<c>AddCurrency</c> creates the coins at the top level)
    /// </summary>
    private static bool HasRoom(Chara chara, int incoming, int gold)
    {
        if (gold > 0 && chara.GetCurrency() == 0) {
            incoming++;
        }

        return chara.things.GridSize - chara.things.Count(t => t.invY != 1 && !t.isEquipped) >= incoming;
    }

    private static void Move(Chara from, Chara to, List<(Thing Thing, int Num)> items, int gold)
    {
        foreach (var (thing, num) in items) {
            // not stacked: the receiver's game would stack it into a pile inside one of its bags, where this
            // game does not look, and lose it
            to.AddThing(thing.Num == num ? thing : thing.Split(num), false);
        }

        if (gold > 0) {
            from.ModCurrency(-gold);
            to.ModCurrency(gold);
        }
    }

    private static void End(Session session, ElinNetHost host, int phase, string reason)
    {
        _sessions.Remove(session);
        Tell(session, host, phase, reason);
    }

    private static void Tell(Session session, ElinNetHost host, int phase, string reason = "")
    {
        var state = new TradeStateDelta {
            TradeId = session.Id,
            Phase = phase,
            UidA = session.A.uid,
            UidB = session.B.uid,
            ItemsA = session.ItemsA.ToList(),
            ItemsB = session.ItemsB.ToList(),
            GoldA = session.GoldA,
            GoldB = session.GoldB,
            ReadyA = session.ReadyA,
            ReadyB = session.ReadyB,
            Revision = session.Revision,
            Reason = reason,
            LockMs = Mathf.Max(0, (int)((session.LockUntil - Time.realtimeSinceStartup) * 1000)),
        };

        TellOne(host, session.A, state);
        TellOne(host, session.B, state);
    }

    private static void TellOne(ElinNetHost host, Chara chara, TradeStateDelta state)
    {
        if (chara == EClass.pc) {
            Show(state);
            return;
        }

        foreach (var (peerId, remote) in host.ActiveRemoteCharas) {
            if (remote == chara) {
                host.SendDeltaTo(peerId, state);
                return;
            }
        }
    }

    private sealed class Session
    {
        internal Chara A = null!;
        internal Chara B = null!;
        internal int GoldA;
        internal int GoldB;
        internal int Id;
        internal bool IsOpen;
        internal List<TradeItem> ItemsA = [];
        internal List<TradeItem> ItemsB = [];
        internal bool ReadyA;
        internal bool ReadyB;
        internal int Revision;

        // a change on the table locks "confirm" a moment, for both (an offer swapped at the last second)
        internal float LockUntil;
    }
}
