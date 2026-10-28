using System.Collections.Generic;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     A duel between two players, where they stand: one challenges the other from its menu, the other has 15
///     seconds to say yes, a short countdown, then they fight. Nobody dies of it: the blow of one duellist that
///     would kill the other is stopped at 0 hit points (RemotePlayerKillPatch) and ends the duel, both are healed
///     and nothing is lost. What follows a duellist does not die of the other side either. <br />
///     The game simulating the map keeps who duels who. Leaving the map, dropping out or dying of something
///     else ends the duel without a winner. What was drunk, shot or used up during the duel stays spent
/// </summary>
public static class PlayerDuel
{
    public const int Challenge_ = 0;
    public const int Accept_ = 1;
    public const int Decline_ = 2;
    public const int GiveUp_ = 3;
    public const int Paid_ = 4;

    public const int Invited = 0;
    public const int Countdown = 1;
    public const int Fighting = 2;
    public const int Won = 3;
    public const int Cancelled = 4;

    // what a duel waits for between the yes and the fight (Countdown): the stakes (step 7), the arena (step 6), then
    // the countdown itself
    private const int Paying = 0;
    private const int Travelling = 1;
    private const int Counting = 2;

    /// <summary>
    ///     How long the two have to put down their stakes
    /// </summary>
    private const float PaySeconds = 20f;

    // the stakes a challenger picks from: none, two amounts, everything (-1: as much as the poorer of the two has)
    private static readonly int[] Stakes = [0, 100, 1000, -1];

    // a guest's game: the last duel it paid its stake for (the state is said again while it travels)
    private static int _paidDuel;

    /// <summary>
    ///     How long the arena waits for the other duellist to arrive (the guest loads the host's map anew)
    /// </summary>
    private const float ArenaWaitSeconds = 90f;

    /// <summary>
    ///     How long the host stays in the arena after the duel for the guest to leave first: the guest walks out to its
    ///     own tile, the host comes back to its town after it (left first, it would leave the guest the arena)
    /// </summary>
    private const float ArenaLeaveSeconds = 20f;

    // the host after a duel in the arena: back to town once the guest left it, then the arena is no more
    private static ArenaBack? _back;
    private static Zone? _doomed;

    // a guest's game: the arena it goes to, then the way out of it, kept across the copies of the world the trip loads
    private static int _going;
    private static float _goingUntil;
    private static float _nextFollow;
    private static (int Arena, int X, int Z, float At)? _leaving;

    /// <summary>
    ///     How long the challenge stays open, no answer is a no
    /// </summary>
    private const float AnswerSeconds = 15f;

    /// <summary>
    ///     Said in emp_duel_countdown
    /// </summary>
    private const float CountdownSeconds = 3f;

    private static readonly List<Session> _sessions = [];
    private static readonly List<DuelStateDelta> _fights = [];

    // duels that just ended, in every game: the two duellists and until when
    private static readonly List<(int A, int B, float Until)> _over = [];
    private const float OverSeconds = 3f;

    /// <summary>
    ///     A blow between two players whose duel just ended: it was still on its way (dealt in one game, told to
    ///     the other) and landed after both were healed, the loser was left wounded. It does not count
    /// </summary>
    internal static bool JustEnded(Card target, Card? origin)
    {
        if (_over.Count == 0 || origin is null) {
            return false;
        }

        _over.RemoveAll(o => Time.realtimeSinceStartup > o.Until);
        return _over.Exists(o => (o.A == target.uid && o.B == origin.uid) || (o.B == target.uid && o.A == origin.uid));
    }
    private static int _nextId = 1;
    private static int _arenaSaid;
    private static Dialog? _box;

    // a duel's music in this game (the bout music of the game, 102), until the duel is over
    private static bool _music;
    private const int BoutMusic = 102;

    // the same two players cannot start another duel right away (a challenge in a loop), in the authority. Real
    // time: a game that is not in front runs its own clock slower (25 s for 40, bench 2026-10-22)
    private static readonly List<(Chara A, Chara B, float Until)> _rest = [];
    private const float RestSeconds = 30f;

    /// <summary>
    ///     What the authority last said about the duel the local player is in
    /// </summary>
    public static DuelStateDelta? View { get; private set; }

    internal static bool Enabled => NetSession.Instance.Rules.AllowDuels;

    // ---------------------------------------------------------------- the local player

    public static void Challenge(int partnerUid)
    {
        if (!Enabled) {
            return;
        }

        // with bets, the challenger names the stake first (the game's own list window), the other one sees it in
        // the box it answers
        if (!NetSession.Instance.Rules.DuelBets) {
            Challenge(partnerUid, 0);
            return;
        }

        EClass.ui.AddLayer<LayerList>()
            .SetList2(Stakes, StakeText, (stake, _) => Challenge(partnerUid, stake), (_, _) => { })
            .SetSize(360f)
            .SetHeader("emp_duel_stake");
    }

    private static void Challenge(int partnerUid, int stake)
    {
        Send(new() {
            Kind = Challenge_,
            PartnerUid = partnerUid,
            Bet = stake,
        });
    }

    private static string StakeText(int stake)
    {
        return stake switch {
            0 => "emp_duel_stake_none".lang(),
            < 0 => "emp_duel_stake_all".lang(),
            _ => "emp_duel_stake_gold".Loc(stake.ToString("N0")),
        };
    }

    /// <summary>
    ///     For the test bridge: the stake of the duel the local player is in
    /// </summary>
    public static int Stake()
    {
        return View?.Bet ?? 0;
    }

    /// <summary>
    ///     The local player gives up the duel it is in: the other one wins, as at 0 hit points (README "what is left":
    ///     a give-up button)
    /// </summary>
    public static void GiveUp()
    {
        if (View is { Phase: Countdown or Fighting } view) {
            Send(new() {
                Kind = GiveUp_,
                DuelId = view.DuelId,
            });
        }
    }

    /// <summary>
    ///     The local player is in a duel being fought (or about to be)
    /// </summary>
    internal static bool InDuel => View is { Phase: Countdown or Fighting };

    /// <summary>
    ///     For the test bridge: where the duel stands, in one line
    /// </summary>
    public static string Describe()
    {
        if (View is not { } view) {
            return "none";
        }

        var phase = view.Phase switch {
            Invited => "Invited",
            Countdown => "Countdown",
            Fighting => "Fighting",
            Won => "Won",
            _ => "Cancelled",
        };
        return $"{phase} {view.UidA} vs {view.UidB} winner {view.WinnerUid} {view.Reason}";
    }

    private static void Answer(int duelId, int kind)
    {
        _box = null;
        Send(new() {
            Kind = kind,
            DuelId = duelId,
        });
    }

    private static void Send(DuelIntentDelta intent)
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
    ///     The authority tells where a duel stands
    /// </summary>
    internal static void Show(DuelStateDelta state)
    {
        _fights.RemoveAll(f => f.DuelId == state.DuelId);
        if (state.Phase == Fighting) {
            _fights.Add(state);
        }

        var mine = EClass.pc.uid;
        if (state.UidA != mine && state.UidB != mine) {
            return;
        }

        // refused before it existed: the duel we may be in goes on
        if (state.DuelId == 0) {
            Msg.Say(state.Reason.Loc(PlayerTrade.NameOf(state.UidB)));
            return;
        }

        View = state;
        var a = PlayerTrade.NameOf(state.UidA);
        var b = PlayerTrade.NameOf(state.UidB);

        switch (state.Phase) {
            case Invited when state.UidB == mine:
                var challenge = state.DuelId;
                _box = Dialog.YesNo(state.Bet > 0 ? "emp_duel_invite_bet".Loc(a, state.Bet.ToString("N0")) : "emp_duel_invite".Loc(a),
                    () => Answer(challenge, Accept_),
                    () => Answer(challenge, Decline_));
                break;
            case Invited:
                Msg.Say(state.Bet > 0 ? "emp_duel_asked_bet".Loc(b, state.Bet.ToString("N0")) : "emp_duel_asked".Loc(b));
                break;
            case Countdown when !state.Ready:
                // the stake: a guest's game pays it itself (a player's gold is its own game's), then tells the host,
                // which checks the gold really left its purse (CardModCurrencyDelta.TakeSpent)
                if (state.Bet > 0 && _paidDuel != state.DuelId && NetSession.Instance.Connection is ElinNetClient &&
                    EClass.pc.GetCurrency() >= state.Bet) {
                    _paidDuel = state.DuelId;
                    // (out of the delta being applied: this is the player's own act, the host hears of it)
                    using (ElinDelta.Simulate()) {
                        EClass.pc.ModCurrency(-state.Bet);
                    }

                    Msg.Say("emp_duel_paid".Loc(state.Bet.ToString("N0")));
                    Send(new() {
                        Kind = Paid_,
                        DuelId = state.DuelId,
                    });
                }

                // to the arena: the host goes first, a guest follows it there (Always)
                if (state.ArenaUid != 0 && _arenaSaid != state.DuelId) {
                    _arenaSaid = state.DuelId;
                    Msg.Say("emp_duel_arena_go".Loc(a, b));
                }

                if (state.ArenaUid != 0 && EClass._zone?.uid != state.ArenaUid) {
                    _going = state.ArenaUid;
                    _goingUntil = Time.realtimeSinceStartup + ArenaWaitSeconds;
                }

                break;
            case Countdown:
                _going = 0;
                Music(true);
                Msg.Say("emp_duel_countdown".Loc(a, b));
                break;
            case Fighting:
                Msg.Say("emp_duel_fight".lang());
                break;
            case Won:
                _over.Add((state.UidA, state.UidB, Time.realtimeSinceStartup + OverSeconds));
                // mana and stamina are each player's own, its game gives them back
                var pc = EClass.pc;
                pc.hp = pc.MaxHP;
                pc.mana.value = pc.mana.max;
                pc.stamina.value = pc.stamina.max;
                Music(false);
                var other = state.UidA == mine ? b : a;
                if (state.WinnerUid == mine) {
                    SE.Play("questComplete");
                    Msg.Say("emp_duel_win".Loc(other));
                    if (state.Bet > 0) {
                        Msg.Say("emp_duel_pot".Loc((2L * state.Bet).ToString("N0")));
                    }
                } else {
                    Msg.Say("emp_duel_lose".Loc(other));
                }

                break;
            case Cancelled:
                CloseBox();
                Music(false);
                Msg.Say(state.Reason.lang());
                // (after the yes the stakes may have been put down: they go back to each one)
                if (state.Bet > 0 && _paidDuel == state.DuelId) {
                    Msg.Say("emp_duel_refund".lang());
                }

                break;
        }

        // over, in the arena: a guest walks out first, to the tile it left (Always); the host follows (UpdateArenaBack)
        if (state.Phase is Won or Cancelled && state.ArenaUid != 0 && NetSession.Instance.Transport is ElinNetClient) {
            _going = 0;
            if ((state.UidA == mine ? state.ReturnA : state.ReturnB) is { Length: 2 } tile) {
                _leaving = (state.ArenaUid, tile[0], tile[1], Time.realtimeSinceStartup + LeaveDelaySeconds);
            }
        }
    }

    // (the last results of the duel, healing and gold, reach this game before it leaves the host's map)
    private const float LeaveDelaySeconds = 2f;

    /// <summary>
    ///     Every frame, connected or not (a guest on its way to the arena holds a map of its own for a moment): the trip
    ///     to the arena and back of a guest duellist
    /// </summary>
    internal static void Always()
    {
        if (_going == 0 && _leaving is null) {
            return;
        }

        if (NetSession.Instance.Transport is not ElinNetClient client || !EClass.core.IsGameStarted) {
            _going = 0;
            _leaving = null;
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (_going != 0) {
            if (EClass._zone?.uid == _going || now > _goingUntil) {
                _going = 0;
            } else if (NetSession.Instance.IsAway && !client.IsInTransfer && now >= _nextFollow) {
                // the host left for the arena and this game keeps the town meanwhile: it joins the host there,
                // as it joins it in the zone of a quest
                _nextFollow = now + 3f;
                client.FollowHost();
            }
        }

        if (_leaving is not { } leaving || now < leaving.At) {
            return;
        }

        // (still on the way there when the duel was called off: out once it arrives)
        if (EClass._zone?.uid != leaving.Arena || EClass.pc.currentZone?.instance is not { } arena) {
            if (now > leaving.At + ArenaWaitSeconds) {
                _leaving = null;
            }

            return;
        }

        if (client.IsInTransfer || !NetSession.Instance.Rules.AllowIndependentTravel) {
            // (without independent travel the host takes everyone back with it)
            if (!NetSession.Instance.Rules.AllowIndependentTravel) {
                _leaving = null;
            }

            return;
        }

        _leaving = null;
        // out of an instance the game goes back to the tile it notes there (ZoneInstanceBout: Exact): this player's own
        arena.x = leaving.X;
        arena.z = leaving.Z;
        if (EClass.game.spatials.Find(arena.uidZone) is { } town) {
            EClass.pc.MoveZone(town);
        }
    }

    /// <summary>
    ///     Whether this character is in a duel being fought, as a duellist or as what follows one: the other
    ///     side cannot kill it, whatever the host allows outside a duel
    /// </summary>
    internal static bool Protects(Chara target)
    {
        if (_fights.Count == 0) {
            return false;
        }

        var uid = target.uid;
        var owner = CompanionHelper.OwnerOf(target)?.uid ?? 0;
        return _fights.Exists(f => f.UidA == uid || f.UidB == uid || f.UidA == owner || f.UidB == owner);
    }

    /// <summary>
    ///     The other duellist of this game's player, while they fight: an enemy to it, as the opponent of a bout is in
    ///     the game (a left click attacks it, a spell and the bow aim at it). Only between the two duellists
    /// </summary>
    internal static bool IsFoe(Chara chara)
    {
        if (_fights.Count == 0 || EClass.pc is not { } pc) {
            return false;
        }

        return AreDueling(pc.uid, chara.uid);
    }

    /// <summary>
    ///     These two fight a duel, whoever plays them: also two guests whose blows the host plays on its map
    /// </summary>
    internal static bool AreDueling(int one, int other)
    {
        // (asked by every friend check of the game: nothing to look at most of the time)
        if (_fights.Count == 0) {
            return false;
        }

        return _fights.Exists(f => (f.UidA == one && f.UidB == other) || (f.UidB == one && f.UidA == other));
    }

    /// <summary>
    ///     The other side of that character's duel, 0 when none (ElinNetHost.Crowd)
    /// </summary>
    internal static int FoeOf(int uid)
    {
        foreach (var f in _fights) {
            if (f.UidA == uid) {
                return f.UidB;
            }

            if (f.UidB == uid) {
                return f.UidA;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Another connection or another world (see PlayerTrade.WatchSession): whatever duel was going on is over
    /// </summary>
    internal static void Clear()
    {
        _sessions.Clear();
        _fights.Clear();
        _rest.Clear();
        View = null;
        CloseBox();
        _music = false;
        _back = null;
        _doomed = null;
        // (a duel cut by another session or world: its stakes go back at the next look)
        _stakesGame = null;
    }

    /// <summary>
    ///     The game's bout music while this game's player duels, the map's own after
    /// </summary>
    private static void Music(bool on)
    {
        if (on == _music) {
            return;
        }

        _music = on;
        if (on) {
            var ids = new List<int> { BoutMusic };
            EClass.Sound.SwitchPlaylist(ModUtil.CreatePlaylist(ref ids, EClass.Sound.GetPlaylist(EClass._zone.IDPlayList) ?? EClass.Sound.GetPlaylist("Day")));
        } else {
            EClass._zone.RefreshBGM();
        }
    }

    private static void CloseBox()
    {
        if (_box != null) {
            _box.Close();
        }

        _box = null;
    }

    // ---------------------------------------------------------------- the game simulating the map

    internal static void Handle(DuelIntentDelta intent, Chara from, ElinNetHost host)
    {
        if (intent.Kind == Challenge_) {
            Start(from, intent.PartnerUid, intent.Bet, host);
            return;
        }

        // a guest put its stake down in its own game: the gold must have left its purse here too
        if (intent.Kind == Paid_) {
            if (_sessions.Find(s => s.Id == intent.DuelId && s.Phase == Countdown && s.Stage == Paying && (s.A == from || s.B == from)) is { } paying) {
                var owed = paying.Bet - (paying.A == from ? paying.PaidA : paying.PaidB);
                var paid = CardModCurrencyDelta.TakeSpent(intent.OriginPeer, owed, paying.PayFrom);
                Hold(paying, from, paid);
            }

            return;
        }

        // either duellist gives up: the other wins
        if (intent.Kind == GiveUp_) {
            if (_sessions.Find(s => s.Id == intent.DuelId && (s.A == from || s.B == from) && s.Phase is Countdown or Fighting) is { } fought) {
                fought.Winner = fought.A == from ? fought.B : fought.A;
            }

            return;
        }

        // only the challenged player answers, once
        var session = _sessions.Find(s => s.Id == intent.DuelId && s.B == from && s.Phase == Invited);
        if (session is null) {
            return;
        }

        if (intent.Kind == Accept_) {
            session.Phase = Countdown;
            session.Arena = Arena(session, host);
            if (session.Bet > 0) {
                session.Stage = Paying;
                session.PayFrom = Time.realtimeSinceStartup;
                session.Deadline = session.PayFrom + PaySeconds;
                // the host's own stake: its game is this one
                var pc = EClass.pc;
                if (session.A == pc || session.B == pc) {
                    if (pc.GetCurrency() < session.Bet) {
                        End(session, host, "emp_duel_unpaid");
                        return;
                    }

                    pc.ModCurrency(-session.Bet);
                    _paidDuel = session.Id;
                    Msg.Say("emp_duel_paid".Loc(session.Bet.ToString("N0")));
                    Hold(session, pc, session.Bet);
                }
            } else {
                Next(session);
            }

            Tell(session, host);
        } else {
            End(session, host, "emp_duel_declined");
        }
    }

    /// <summary>
    ///     A blow that was stopped at 0 hit points: when it is one duellist's on the other, the duel is lost.
    ///     Settled on the next frame, outside the game's damage code
    /// </summary>
    internal static void Struck(Card target, Card? origin)
    {
        if (_sessions.Count == 0 || target.hp > 0) {
            return;
        }

        // whoever dealt it (the duellist, its companion): the one left at 0 has lost
        var session = _sessions.Find(s => s.Phase == Fighting && (s.A == target || s.B == target));
        if (session is not null) {
            session.Winner ??= session.A == target ? session.B : session.A;
        }
    }

    /// <summary>
    ///     A duel needs both players there: gone or dead of something else ends it without a winner
    /// </summary>
    internal static void Update()
    {
        if (_sessions.Count == 0 && _back is null && _doomed is null && ReferenceEquals(_stakesGame, EClass.game)) {
            return;
        }

        if (NetSession.Instance.Connection is not ElinNetHost host) {
            _sessions.Clear();
            return;
        }

        UpdateArenaBack();
        DestroyArena(host);
        RefundLeftovers(host);
        foreach (var session in _sessions.ToArray()) {
            if (session.Phase == Countdown && session.Stage == Paying && Present(session.A) && Present(session.B)) {
                if (session.PaidA >= session.Bet && session.PaidB >= session.Bet) {
                    Next(session);
                    Tell(session, host);
                } else if (Time.realtimeSinceStartup >= session.Deadline) {
                    End(session, host, "emp_duel_unpaid");
                }
            } else if (session.Phase == Countdown && session.Stage == Travelling) {
                Travel(session, host);
            } else if (!Present(session.A) || !Present(session.B)) {
                End(session, host, "emp_duel_over");
            } else if (session.Winner is { } winner) {
                // the host is right about hit points; the rest is given back by each duellist's own game
                session.A.hp = session.A.MaxHP;
                session.B.hp = session.B.MaxHP;
                session.Phase = Won;
                _sessions.Remove(session);
                _rest.Add((session.A, session.B, Time.realtimeSinceStartup + RestSeconds));
                LeaveArena(session);
                // the winner takes both stakes; the notes go first, a second settling finds nothing to pay
                if (session.Bet > 0) {
                    Give(winner.uid, Release(session.A.uid) + Release(session.B.uid), host);
                }

                Tell(session, host, winner.uid);
                EmpLog.Information("Player {Winner} won a duel against {Loser}",
                    winner.uid, (winner == session.A ? session.B : session.A).uid);
            } else if (session.Phase != Fighting && Time.realtimeSinceStartup >= session.Deadline) {
                if (session.Phase == Invited) {
                    End(session, host, "emp_duel_noanswer");
                } else {
                    session.Phase = Fighting;
                    Tell(session, host);
                }
            }
        }
    }

    private static bool Present(Chara chara)
    {
        // IsPlayer: the character of a player who dropped out is not one anymore
        return chara is { isDead: false, isDestroyed: false, IsPlayer: true } && EClass._map.charas.Contains(chara);
    }

    private static void Start(Chara from, int partnerUid, int stake, ElinNetHost host)
    {
        var partner = EClass._map.charas.Find(c => c.uid == partnerUid);
        if (!Enabled || partner is null || partner == from || !Present(from) || !Present(partner)) {
            return;
        }

        // one thing at a time: a second challenge is refused and the duel going on is left alone (and the way back
        // from the arena is one)
        if (_back is not null || _sessions.Exists(s => s.A == from || s.B == from || s.A == partner || s.B == partner) ||
            PlayerTrade.Busy(from) || PlayerTrade.Busy(partner)) {
            TellOne(host, from, new() {
                Phase = Cancelled,
                UidA = from.uid,
                UidB = partner.uid,
                Reason = "emp_duel_busy",
            });
            return;
        }

        _rest.RemoveAll(r => Time.realtimeSinceStartup > r.Until);
        if (_rest.Exists(r => (r.A == from && r.B == partner) || (r.A == partner && r.B == from))) {
            TellOne(host, from, new() {
                Phase = Cancelled,
                UidA = from.uid,
                UidB = partner.uid,
                Reason = "emp_duel_wait",
            });
            return;
        }

        var session = new Session {
            Id = _nextId++,
            A = from,
            B = partner,
            Deadline = Time.realtimeSinceStartup + AnswerSeconds,
            Bet = StakeOf(stake, from, partner, host),
        };
        _sessions.Add(session);
        Tell(session, host);
    }

    private static void End(Session session, ElinNetHost host, string reason)
    {
        _sessions.Remove(session);
        _rest.Add((session.A, session.B, Time.realtimeSinceStartup + RestSeconds));
        session.Phase = Cancelled;
        LeaveArena(session);
        // no winner: each one gets its own stake back (gone, it waits for it with the host)
        if (session.Bet > 0) {
            Give(session.A.uid, Release(session.A.uid), host);
            Give(session.B.uid, Release(session.B.uid), host);
        }

        Tell(session, host, 0, reason);
    }

    // ---------------------------------------------------------------- the stakes (council 7, step 7)

    /// <summary>
    ///     The stake of a new duel: none without the host's box; at most what the poorer of the two has (as the host
    ///     sees their purses)
    /// </summary>
    private static int StakeOf(int asked, Chara a, Chara b, ElinNetHost host)
    {
        // (only the host keeps stakes: a player running its own map holds a copy of the host's tables)
        if (!NetSession.Instance.Rules.DuelBets || asked == 0 || host.IsZoneSession) {
            return 0;
        }

        var poorer = System.Math.Min(a.GetCurrency(), b.GetCurrency());
        return System.Math.Max(0, asked < 0 ? poorer : System.Math.Min(asked, poorer));
    }

    /// <summary>
    ///     After the yes and the stakes: the arena, or the countdown where they stand
    /// </summary>
    private static void Next(Session session)
    {
        session.Stage = session.Arena ? Travelling : Counting;
        session.Deadline = Time.realtimeSinceStartup + (session.Arena ? ArenaWaitSeconds : CountdownSeconds);
    }

    /// <summary>
    ///     A stake put down: noted in the host's save in the same frame, until the duel is settled
    /// </summary>
    private static void Hold(Session session, Chara chara, long gold)
    {
        if (gold <= 0) {
            return;
        }

        if (session.A == chara) {
            session.PaidA += gold;
        } else {
            session.PaidB += gold;
        }

        var stakes = ElinNetHost.DuelStakes;
        stakes[chara.uid] = (stakes.TryGetValue(chara.uid, out var held) ? held : 0) + gold;
        EmpLog.Information("Duel {DuelId}: player {Uid} put down {Gold} gold", session.Id, chara.uid, gold);
    }

    private static long Release(int uid)
    {
        return ElinNetHost.DuelStakes.Remove(uid, out var gold) ? gold : 0;
    }

    /// <summary>
    ///     Gold to a player: in the host's own game, to a guest on this map as a trade gives it, otherwise kept for it
    ///     with the host until it stands on this map again (as its shipping money)
    /// </summary>
    private static void Give(int uid, long gold, ElinNetHost host)
    {
        if (gold <= 0) {
            return;
        }

        EmpLog.Information("Duel stakes: {Gold} gold to player {Uid}", gold, uid);
        if (EClass.pc.uid == uid) {
            AddGold(EClass.pc, gold);
            return;
        }

        foreach (var (peerId, chara) in host.ActiveRemoteCharas) {
            if (chara.uid == uid && host.IsSettled(peerId) && Present(chara)) {
                using (ElinDelta.Simulate()) {
                    AddGold(chara, gold);
                }

                return;
            }
        }

        host.OweMoney(uid, gold);
    }

    private static void AddGold(Chara chara, long gold)
    {
        while (gold > 0) {
            var part = (int)System.Math.Min(gold, int.MaxValue);
            gold -= part;
            chara.ModCurrency(part);
        }
    }

    // the world whose saved stakes were looked at: stakes left from an earlier session (the host quit during a duel,
    // the game crashed) go back to their owners
    private static Game? _stakesGame;

    private static void RefundLeftovers(ElinNetHost host)
    {
        if (ReferenceEquals(_stakesGame, EClass.game)) {
            return;
        }

        _stakesGame = EClass.game;
        // (a player running its own map has the tables of its copy of the world: they are the host's to settle)
        if (host.IsZoneSession) {
            return;
        }

        foreach (var uid in new List<int>(ElinNetHost.DuelStakes.Keys)) {
            Give(uid, Release(uid), host);
        }
    }

    // ---------------------------------------------------------------- the arena (council 7, step 6)

    /// <summary>
    ///     Whether this duel is fought in an arena: the host's box, and the host is one of the two (the arena is a map
    ///     the host makes and runs, the other one comes along as to the zone of a quest; between two guests one of
    ///     them would have to run it, never tried with three games). Otherwise where they stand
    /// </summary>
    private static bool Arena(Session session, ElinNetHost host)
    {
        if (!NetSession.Instance.Rules.DuelArena || host.IsZoneSession) {
            return false;
        }

        var pc = EClass.pc;
        if (session.A != pc && session.B != pc) {
            foreach (var chara in new[] { session.A, session.B }) {
                TellOne(host, chara, new() {
                    Phase = Cancelled,
                    UidA = chara.uid,
                    UidB = chara.uid,
                    Reason = "emp_duel_arena_host",
                });
            }

            return false;
        }

        // (not from the world map nor from another instance: there is no town to come back to)
        return EClass._zone is { IsRegion: false, IsInstance: false };
    }

    /// <summary>
    ///     The host makes the arena (a field, as the game's bouts, with the game's ZoneInstanceBout: out of it
    ///     everyone goes back to the tile it left) and goes there; the other one follows, then the countdown
    /// </summary>
    private static void Travel(Session session, ElinNetHost host)
    {
        var now = Time.realtimeSinceStartup;
        if (session.ArenaZone is null) {
            session.TownUid = EClass._zone.uid;
            session.ReturnA = [session.A.pos.x, session.A.pos.z];
            session.ReturnB = [session.B.pos.x, session.B.pos.z];
            // (no target: nothing of the game's bout with an adventurer happens there)
            session.ArenaZone = SpatialGen.CreateInstance("field", new ZoneInstanceBout());
            EmpLog.Information("Duel {DuelId}: the host goes to the arena {ZoneUid}", session.Id, session.ArenaZone.uid);
            EClass.pc.MoveZone(session.ArenaZone, ZoneTransition.EnterState.Center);
            session.TellAgain = now + TellArenaSeconds;
            Tell(session, host);
            return;
        }

        if (now >= session.Deadline) {
            End(session, host, "emp_duel_arena_late");
            return;
        }

        // the host is on its way
        if (EClass._zone != session.ArenaZone || EClass._map is null) {
            return;
        }

        // (said again now and then: a game that was loading a map may have missed it)
        if (now >= session.TellAgain) {
            session.TellAgain = now + TellArenaSeconds;
            Tell(session, host);
        }

        // both there: the character of a guest that came is not the one it had in town
        if (Here(session.A.uid, host) is not { } a || Here(session.B.uid, host) is not { } b) {
            return;
        }

        session.A = a;
        session.B = b;
        session.Stage = Counting;
        session.Deadline = now + CountdownSeconds;
        Tell(session, host);
    }

    private const float TellArenaSeconds = 5f;

    /// <summary>
    ///     That player stands on this map and its game holds it
    /// </summary>
    private static Chara? Here(int uid, ElinNetHost host)
    {
        if (EClass.pc.uid == uid) {
            return EClass.pc;
        }

        foreach (var (peerId, chara) in host.ActiveRemoteCharas) {
            if (chara.uid == uid) {
                return host.IsSettled(peerId) && Present(chara) ? chara : null;
            }
        }

        return null;
    }

    /// <summary>
    ///     The duel in the arena is over: the guest walks out first (its game, see Always), the host after it
    /// </summary>
    private static void LeaveArena(Session session)
    {
        if (session.ArenaZone is not { } arena) {
            return;
        }

        var guest = session.A == EClass.pc ? session.B : session.A;
        _back = new() {
            Arena = arena,
            TownUid = session.TownUid,
            GuestUid = guest.uid,
            GiveUp = Time.realtimeSinceStartup + ArenaWaitSeconds,
        };
    }

    private static void UpdateArenaBack()
    {
        if (_back is not { } back) {
            return;
        }

        var now = Time.realtimeSinceStartup;
        // (called off before the host got there: it leaves once it arrives)
        if (EClass._zone != back.Arena || EClass._map is null) {
            if (now > back.GiveUp) {
                _back = null;
            }

            return;
        }

        if (back.Until == 0) {
            back.Until = now + ArenaLeaveSeconds;
        }

        if (now < back.Until && NetSession.Instance.Rules.AllowIndependentTravel &&
            EClass._map.charas.Exists(c => c.uid == back.GuestUid)) {
            return;
        }

        _back = null;
        _doomed = back.Arena;
        // out of an instance the game takes the host back to its own tile (ZoneInstanceBout: Exact)
        if (EClass.game.spatials.Find(back.TownUid) is { } town) {
            EClass.pc.MoveZone(town);
        }
    }

    /// <summary>
    ///     Everyone left the arena: it is gone (the game would only drop it the next time someone goes to the world map)
    /// </summary>
    private static void DestroyArena(ElinNetHost host)
    {
        if (_doomed is not { } arena || EClass._zone == arena || EClass.player.nextZone == arena) {
            return;
        }

        _doomed = null;
        if (arena.destryoed || host.IsLeased(arena.uid) || !arena.CanDestroy()) {
            return;
        }

        EmpLog.Information("Duel arena {ZoneUid} destroyed", arena.uid);
        arena.Destroy();
        arena.parent?.RemoveChild(arena);
    }

    private static void Tell(Session session, ElinNetHost host, int winnerUid = 0, string reason = "")
    {
        var state = new DuelStateDelta {
            DuelId = session.Id,
            Phase = session.Phase,
            UidA = session.A.uid,
            UidB = session.B.uid,
            WinnerUid = winnerUid,
            Reason = reason,
            Ready = session.Phase != Countdown || session.Stage == Counting,
            Bet = session.Bet,
            ArenaUid = session.ArenaZone?.uid ?? 0,
            ReturnA = session.ReturnA,
            ReturnB = session.ReturnB,
        };

        Show(state);
        if (state.Phase == Won) {
            // behind the blow that ended it, which leaves one flush later: sent at once, the loser's game was
            // healed first and then given the hit points of that blow
            host.Delta.DeferRemote(state);
        } else {
            host.SendDeltaToAllExcept(-1, state);
        }
    }

    private static void TellOne(ElinNetHost host, Chara chara, DuelStateDelta state)
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
        internal bool Arena;
        internal Zone? ArenaZone;
        internal Chara B = null!;
        internal float Deadline;
        internal int Id;
        internal int Phase;
        internal int Bet;
        internal long PaidA;
        internal long PaidB;
        internal float PayFrom;
        internal int[]? ReturnA;
        internal int[]? ReturnB;
        internal int Stage;
        internal float TellAgain;
        internal int TownUid;
        internal Chara? Winner;
    }

    private sealed class ArenaBack
    {
        internal Zone Arena = null!;
        internal float GiveUp;
        internal int GuestUid;
        internal int TownUid;
        internal float Until;
    }
}
