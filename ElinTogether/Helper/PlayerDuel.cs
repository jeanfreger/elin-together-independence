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

    public const int Invited = 0;
    public const int Countdown = 1;
    public const int Fighting = 2;
    public const int Won = 3;
    public const int Cancelled = 4;

    // what a duel waits for between the yes and the fight (Countdown): the arena (step 6), then the countdown itself
    private const int Travelling = 1;
    private const int Counting = 2;

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
        if (Enabled) {
            Send(new() {
                Kind = Challenge_,
                PartnerUid = partnerUid,
            });
        }
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
                _box = Dialog.YesNo("emp_duel_invite".Loc(a),
                    () => Answer(challenge, Accept_),
                    () => Answer(challenge, Decline_));
                break;
            case Invited:
                Msg.Say("emp_duel_asked".Loc(b));
                break;
            case Countdown when !state.Ready:
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
                } else {
                    Msg.Say("emp_duel_lose".Loc(other));
                }

                break;
            case Cancelled:
                CloseBox();
                Music(false);
                Msg.Say(state.Reason.lang());
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
            Start(from, intent.PartnerUid, host);
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
            session.Stage = Arena(session, host) ? Travelling : Counting;
            session.Deadline = Time.realtimeSinceStartup + (session.Stage == Travelling ? ArenaWaitSeconds : CountdownSeconds);
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
        if (_sessions.Count == 0 && _back is null && _doomed is null) {
            return;
        }

        if (NetSession.Instance.Connection is not ElinNetHost host) {
            _sessions.Clear();
            return;
        }

        UpdateArenaBack();
        DestroyArena(host);
        foreach (var session in _sessions.ToArray()) {
            if (session.Phase == Countdown && session.Stage == Travelling) {
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

    private static void Start(Chara from, int partnerUid, ElinNetHost host)
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
        Tell(session, host, 0, reason);
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
        internal Zone? ArenaZone;
        internal Chara B = null!;
        internal float Deadline;
        internal int Id;
        internal int Phase;
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
