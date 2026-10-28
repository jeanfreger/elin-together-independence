using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Two rules, the host's choice (NetSessionRules.UseOwnSleep) <br />
///     Off: a player who goes to bed waits until every player of the map did, then the host's night is everyone's <br />
///     On: a player who goes to bed sleeps at once, a night of its own: its body rests, the date of the world
///     stays. When enough of the living, active players connected sleep at the same time (NightPasses: all, half,
///     one; idle ones not counted, see PlayerIdle), wherever they are, the night of the host's game becomes the
///     world's: the date moves once and the host wakes everyone
/// </summary>
[HarmonyPatch]
internal class SleepSynchronizationContext : SynchronizationContext
{
    private static readonly HashSet<int> _ready = [];
    private static readonly HashSet<int> _lastReady = [];
    private static bool _sleepStarted;
    private static bool _cancelSent;
    private static bool _nightTick;
    private static readonly List<Chara> _shielded = [];
    private static readonly List<Chara> _noDreams = [];
    private static (Thing? Bed, Thing? Pillow, ItemPosition? PosBed, ItemPosition? PosPillow)? _laidDown;
    private static Thing? _bed;

    // own sleep: the night screen of this game is a night of its player alone, it leaves the date where it is
    private static bool _ownNight;
    private static bool _ownEnd;

    // own sleep, a guest: it asked to sleep, for that many hours, and plays its night once the sleep came back
    private static bool _requested;
    private static float _requestedAt;
    private static int _asked;

    // a request the host ignored must not turn a sleep from elsewhere (a spell) into a night screen later
    private const float RequestLife = 5f;

    // a guest whose night screen reached its end waits that long for the host to end the night of the world
    private const float WakeWait = 60f;
    private static float _waitSince;

    // own sleep, the host: the players away from its map (by peer, with their name), those of them who said
    // they sleep, those who said they are dead, and the hours of the night each sleeper asked for
    private static readonly Dictionary<int, string> _away = [];
    private static readonly HashSet<int> _awayAsleep = [];
    private static readonly HashSet<int> _awayDead = [];
    private static readonly Dictionary<int, int> _nightHours = [];

    // own sleep, the host: the bed and the pillow (uids) each guest asleep on this map asked for
    private static readonly Dictionary<int, (int Bed, int Pillow)> _guestBeds = [];

    // own sleep, the host: next time the count of sleepers is told again, for players who came in meanwhile
    private static float _nextCount;

    // the party of the game while only the sleeper's own must be in it, see NarrowParty
    private static List<Chara>? _party;
    private static Chara[] _own = [];

    private static readonly AccessTools.FieldRef<LayerSleep, int> _minRef =
        AccessTools.FieldRefAccess<LayerSleep, int>("min");
    private static readonly AccessTools.FieldRef<LayerSleep, int> _maxMinRef =
        AccessTools.FieldRefAccess<LayerSleep, int>("maxMin");
    private static readonly AccessTools.FieldRef<LayerSleep, int> _hoursRef =
        AccessTools.FieldRefAccess<LayerSleep, int>("hours");

    internal static bool AllPlayersReady { get; private set; } = true;

    /// <summary>
    ///     Players asleep right now, as the host counts them (a guest hears it from SleepReadyDelta)
    /// </summary>
    internal static int Sleepers { get; set; }

    /// <summary>
    ///     The night screen of this game is a night of its player alone: the other players are not held by it
    /// </summary>
    internal static bool IsOwnNight => _ownNight;

    internal static void Update()
    {
        var session = NetSession.Instance;
        var own = session.Rules.UseOwnSleep;

        if (pc?.conSleep is null) {
            _cancelSent = false;
        }

        if (ui.GetLayer<LayerSleep>() is null) {
            _ownNight = false;
            _waitSince = 0f;
        }

        if (pc is null or { isDead: true }) {
            _requested = false;
        }

        if (_requested && UnityEngine.Time.unscaledTime - _requestedAt > RequestLife) {
            _requested = false;
        }

        // own sleep, a guest: the sleep it asked for came back from the game that keeps the map
        if (_requested && session.Connection is ElinNetClient && pc?.conSleep is not null) {
            _requested = false;
            if (ui.GetLayer<LayerSleep>() is null) {
                _ownNight = true;
                ui.AddLayer<LayerSleep>().Sleep(_asked, null);
            }
        }

        if (session.Connection is not ElinNetHost host || pc is null) {
            _ready.Clear();
            _lastReady.Clear();
            _away.Clear();
            _awayAsleep.Clear();
            _awayDead.Clear();
            _nightHours.Clear();
            _guestBeds.Clear();
            _sleepStarted = false;
            AllPlayersReady = true;
            if (session.CurrentPlayers.Count <= 1) {
                Sleepers = 0;
            }

            return;
        }

        _ready.Clear();
        _away.Clear();
        var alive = 0;
        foreach (var netPlayer in session.CurrentPlayers) {
            var chara = netPlayer.CharaUid == pc.uid ? pc : netPlayer.FindChara();
            // 死是凉爽的夏夜，可供人无忧的安眠
            if (chara is null or { isDead: true }) {
                continue;
            }

            // away from the keyboard (council 2026-10-26): neither holds the night nor counts toward it; one who
            // went to bed counts, its sleep was its own act
            if (chara.conSleep is null && PlayerIdle.IsIdle(netPlayer.Index)) {
                continue;
            }

            alive++;
            if (chara.conSleep is not null) {
                _ready.Add(netPlayer.Index);
            }
        }

        if (own) {
            // everyone connected counts, wherever they are: those away say by themselves when they sleep
            foreach (var (id, name) in host.AwayPeers) {
                _away[id] = name;

                // a dead player, as on this map, is not waited for
                if (_awayDead.Contains(id) || (!_awayAsleep.Contains(id) && PlayerIdle.IsIdle(id))) {
                    continue;
                }

                alive++;
                if (_awayAsleep.Contains(id)) {
                    _ready.Add(id);
                }
            }

            _awayAsleep.RemoveWhere(id => !_away.ContainsKey(id));
            _awayDead.RemoveWhere(id => !_away.ContainsKey(id));
        }

        // nobody to wait for when alone (the game's own sleep, as AllowPartySleep). Own sleep: the host's share
        // of sleepers ("Night passes when"); otherwise everyone waits for everyone, as before
        AllPlayersReady = (own ? session.Rules.NightPasses : NightRule.Everyone) switch {
            NightRule.One => _ready.Count >= System.Math.Min(1, alive),
            NightRule.Half => _ready.Count * 2 >= alive,
            _ => _ready.Count >= alive,
        };
        Sleepers = _ready.Count;


        // a player who comes in while others sleep must know it, or it counts nobody (the count is also told
        // at each change, see Announce)
        if (own && alive >= 2 && Sleepers > 0 && UnityEngine.Time.unscaledTime >= _nextCount) {
            _nextCount = UnityEngine.Time.unscaledTime + 5f;
            host.Delta.AddRemote(new SleepReadyDelta {
                PlayerIndex = -1,
                Ready = true,
                ReadyCount = Sleepers,
                TotalCount = alive,
                Quiet = true,
            });
        }

        if (!own && (_sleepStarted || alive < 2)) {
            if (_sleepStarted && _ready.Count == 0) {
                _sleepStarted = false;
            }

            _lastReady.Clear();
            _lastReady.UnionWith(_ready);
            return;
        }

        if (_sleepStarted && (_ready.Count == 0 || ui.GetLayer<LayerSleep>() is null)) {
            _sleepStarted = false;
        }

        var last = -1;
        foreach (var index in _ready) {
            if (!_lastReady.Contains(index)) {
                last = index;
                if (alive >= 2) {
                    Announce(host, index, true, alive);
                }
            }
        }

        foreach (var index in _lastReady) {
            if (!_ready.Contains(index) && alive >= 2) {
                Announce(host, index, false, alive);
            }
        }

        _lastReady.Clear();
        _lastReady.UnionWith(_ready);

        // the last one closed its eyes while the host sleeps a night of its own: that night becomes the
        // world's, for the hours the last one asked for. A host that goes to bed last: see GateSleepTick
        // (counted by who is connected: the last awake one may also have just turned idle)
        if (own && _ownNight && session.CurrentPlayers.Count + _away.Count >= 2 && AllPlayersReady && session.Transport is ElinNetHost &&
            ui.GetLayer<LayerSleep>() is { } layer && _minRef(layer) <= _maxMinRef(layer)) {
            // (living world: at night, up to the morning)
            var hours = LivingWorld.NightHours(_nightHours.GetValueOrDefault(last, _hoursRef(layer)));
            _ownNight = false;
            _minRef(layer) = 0;
            _hoursRef(layer) = hours;
            _maxMinRef(layer) = hours * 60;
            OnSleepStart(hours);
        }
    }

    private static void Announce(ElinNetHost host, int playerIndex, bool ready, int total)
    {
        EmpLog.Debug("Sleep vote from {PeerIndex}, ready {SleepReady}, {SleepReadyCount} of {PlayerCount}",
            playerIndex, ready, _ready.Count, total);

        var delta = new SleepReadyDelta {
            PlayerIndex = playerIndex,
            Ready = ready,
            ReadyCount = _ready.Count,
            TotalCount = total,
            Name = _away.GetValueOrDefault(playerIndex),
        };
        delta.Play();
        host.Delta.AddRemote(delta);
    }

    /// <summary>
    ///     ConSleep.Tick: the hours of a night, from how tired the sleeper is
    /// </summary>
    private static int NightHours(Chara chara)
    {
        var phase = chara.sleepiness.GetPhase();
        var hours = phase >= 3 ? 12 : phase >= 2 ? 10 : phase >= 1 ? 7 : 5;
        hours += chara.stamina.GetPhase() switch {
            2 => 2,
            1 => 3,
            0 => 4,
            _ => 0,
        };
        return hours + rnd(3) - rnd(2);
    }

    /// <summary>
    ///     Own sleep: a player away from the host map (alone, holding it for visitors, or visiting) tells the
    ///     host by itself when it sleeps and when it wakes, the host does not see its character
    /// </summary>
    private static void ReportAway(bool asleep, int hours = 0)
    {
        if (NetSession.Instance is { IsAway: true, Transport: ElinNetClient main, Rules.UseOwnSleep: true }) {
            main.SendWhileAway(new SleepStateDelta {
                Asleep = asleep,
                Hours = hours,
                Dead = pc is { isDead: true },
            });
        }
    }

    /// <summary>
    ///     A player away that dies or comes back to life says so: the host skips the dead for the night of the
    ///     world, as it does on its own map. Dying ends its sleep through ConSleep.OnRemoved (Chara.Die cures
    ///     it, Condition.Kill), reported there too
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Die))]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Revive))]
    internal static void ReportAwayLife(Chara __instance)
    {
        if (__instance.IsPC) {
            ReportAway(false);
        }
    }

    internal static void OnAwaySleep(int peer, bool asleep, int hours, bool dead)
    {
        EmpLog.Debug("Away player {PeerIndex} asleep {Asleep} dead {Dead}", peer, asleep, dead);

        if (dead) {
            _awayDead.Add(peer);
        } else {
            _awayDead.Remove(peer);
        }

        if (asleep && !dead) {
            _awayAsleep.Add(peer);
            _nightHours[peer] = System.Math.Clamp(hours, 1, 24);
        } else {
            _awayAsleep.Remove(peer);
        }
    }

    /// <summary>
    ///     Own sleep, the game that keeps the map: a guest fell asleep. What ConSleep.Tick does for the local
    ///     player as its night starts is done for it: its own animals come beside it, its own companions sleep,
    ///     no more bleeding, poison or miasma
    /// </summary>
    internal static void OnGuestAsleep(int peer, Chara guest, int hours, int bed, int pillow)
    {
        _nightHours[peer] = System.Math.Clamp(hours, 1, 24);
        _guestBeds[peer] = (bed, pillow);

        BringBeside(guest);
        foreach (var chara in CompanionHelper.CompanionsOf(guest)) {
            if (chara.IsInActiveMap && !chara.HasCondition<ConSleep>()) {
                chara.AddCondition<ConSleep>(5 + rnd(10), force: true);
            }
        }

        guest.RemoveCondition<ConBleed>();
        guest.RemoveCondition<ConPoison>();
        guest.RemoveCondition<ConMiasma>();
    }

    /// <summary>
    ///     Own sleep, the game that keeps the map: the power of the rest a guest woke with. The guest says it; what
    ///     the host takes is at most what its bed and pillow give, as Chara.OnSleep(Thing, int) counts it (bed
    ///     power or 20, half the pillow's, five per point of element 750 of the bed), with the bed and pillow
    ///     the guest slept in (SleepRequestDelta): a card of its own bag or of this map, nothing else
    /// </summary>
    internal static int RestPower(int peer, Chara guest, int claimed)
    {
        _guestBeds.Remove(peer, out var uids);
        var bed = Real<TraitBed>(uids.Bed, guest);
        var pillow = Real<TraitPillow>(uids.Pillow, guest);

        var power = bed?.Power ?? 20;
        if (pillow is not null) {
            power += pillow.Power / 2;
        }

        if (bed is not null) {
            power += bed.Evalue(750) * 5;
        }

        return System.Math.Clamp(System.Math.Min(claimed, power), 0, 1000);
    }

    private static Thing? Real<T>(int uid, Chara guest) where T : Trait
    {
        return uid != 0 && CardCache.Find(uid) is Thing { isDestroyed: false, trait: T } thing &&
               (thing.ExistsOnMap || thing.GetRootCard() == guest)
            ? thing
            : null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.CanSleep))]
    internal static void AllowPartySleep(Chara __instance, ref bool __result)
    {
        var session = NetSession.Instance;
        var own = session.Rules.UseOwnSleep;

        // alone in a session (it opens by itself at load): the game's own rule, as in a solo game
        if (__result || !__instance.IsPC ||
            (own ? session.Transport is null : session.Connection is null || session.CurrentPlayers.Count <= 1)) {
            return;
        }

        if (_zone.events.GetEvent<ZoneEventQuest>() is not null) {
            return;
        }

        // a rest dozes off one turn in ten once the player can sleep: only when it is tired for real, or every
        // rest of a session ended in a sleep within seconds
        if (AIPassTimePatch.IsResting) {
            return;
        }

        // own sleep: to bed alone only when tired, as in a solo game; always to join a player who sleeps
        if (own && Sleepers == 0) {
            return;
        }

        // are you tired? yes you are
        __result = true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Sleep))]
    internal static bool OnPcSleep(Chara __instance, Thing? bed, Thing? pillow, bool pickup,
        ItemPosition? posBed, ItemPosition? posPillow)
    {
        if (!__instance.IsPC) {
            return true;
        }

        if (NetSession.Instance.Connection is not ElinNetClient client) {
            // the game's own sleep
            if (NetSession.Instance.IsAway) {
                ReportAway(true, NightHours(__instance));
            }

            return true;
        }

        // forced
        if (ElinDelta.IsApplying) {
            return false;
        }

        if (__instance.conSleep is not null) {
            return false;
        }

        _laidDown = pickup ? (bed, pillow, posBed, posPillow) : null;
        _bed = bed;

        var own = NetSession.Instance.Rules.UseOwnSleep;
        _requested = own;
        _requestedAt = UnityEngine.Time.unscaledTime;
        _asked = own ? NightHours(__instance) : 0;

        EmpLog.Debug("Requesting party sleep");

        client.Delta.AddRemote(new SleepRequestDelta {
            Hours = _asked,
            Bed = bed?.uid ?? 0,
            Pillow = pillow?.uid ?? 0,
        });
        ReportAway(true, _asked);
        if (!own) {
            WidgetPopText.Say("emp_ui_sleep_request".Loc());
        }

        return false;
    }

    /// <summary>
    ///     Sleeping from the hotbar, the game lays the bed and pillow of the bag on the floor and notes on the
    ///     sleep condition to take them back at wake-up. A client's sleep is only a request: its condition comes
    ///     from the host, with nothing to take back, and both stayed on the floor <br />
    ///     What was laid down is kept from the request and put back on the condition as it ends, the game does
    ///     the rest
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.OnRemoved))]
    internal static void OnPcWake(ConSleep __instance, out ElinDelta.PatchScope __state)
    {
        __state = default;

        if (__instance.owner is not { IsPC: true }) {
            // what woke a guest here, before its own night ended there, is only known from this
            if (__instance.owner is { IsRemotePlayer: true } && NetSession.Instance.Connection is ElinNetHost &&
                !ElinDelta.IsApplying) {
                EmpLog.Debug("Guest sleep ended here by {SleepEnd}",
                    string.Join(" < ", new System.Diagnostics.StackTrace(2, false).GetFrames()?.Take(6)
                        .Select(f => $"{f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name}") ?? []));
            }

            return;
        }

        // the bed of a sleep that ended any other way than by CharaSleepDelta is not the next one's
        _bed = null;

        if (NetSession.Instance is { Rules.UseOwnSleep: true } session) {
            ReportAway(false);

            // a night of its own ends for the sleeper's own (a pillow of Jure takes sanity from "the party")
            if (session.Connection is ElinNetHost && !_sleepStarted) {
                NarrowParty(__instance.owner);
            }
        }

        if (_laidDown is not { } laid) {
            return;
        }

        _laidDown = null;

        // the game's own sleep (no session, or this player simulates the map): it knows what to take back
        if (__instance.pickup) {
            return;
        }

        (__instance.pcBed, __instance.pcPillow, __instance.posBed, __instance.posPillow) = laid;
        __instance.pickup = true;

        // a sleep given up ends by a delta from the host, yet taking the bed back is this player's own act
        __state = ElinDelta.PatchScope.Simulate();
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.OnRemoved))]
    internal static void OnPcWakeEnd(ElinDelta.PatchScope __state)
    {
        RestoreParty();
        __state.Exit();
    }

    /// <summary>
    ///     The bed this player asked to sleep in, null for none: its own sleep power at wake-up comes from it,
    ///     see <see cref="CharaSleepDelta" />
    /// </summary>
    internal static Thing? TakeBed()
    {
        var bed = _bed;
        _bed = null;
        return bed is { isDestroyed: false } ? bed : null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RecipeManager), nameof(RecipeManager.OnSleep))]
    internal static bool OnDreamRecipe(bool ehe)
    {
        return !CharaSleepDelta.DeferRecipe(ehe);
    }

    private static bool InSleepWaitWindow(Chara chara)
    {
        if (chara.conSleep is not { pcSleep: <= 1 } || ui.GetLayer<LayerSleep>() is not null) {
            return false;
        }

        // own sleep: the host waits for nobody
        return NetSession.Instance.Connection is not ElinNetHost ||
               (!AllPlayersReady && !NetSession.Instance.Rules.UseOwnSleep);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.ConsumeTurn), MethodType.Getter)]
    internal static void FreezeWaitTurns(ConSleep __instance, ref bool __result)
    {
        if (!__result || NetSession.Instance.Connection is null) {
            return;
        }

        if (__instance.owner is { IsPC: true } owner && InSleepWaitWindow(owner)) {
            __result = false;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.SetAIImmediate))]
    internal static bool CancelSleepOnAction(Chara __instance, AIAct g)
    {
        if (NetSession.Instance.Connection is not { } connection || !__instance.IsPC || g.IsNoGoal) {
            return true;
        }

        if (ElinDelta.IsApplying || !InSleepWaitWindow(__instance)) {
            return true;
        }

        switch (connection) {
            case ElinNetHost:
                __instance.conSleep?.Kill();
                break;
            case ElinNetClient client when !_cancelSent:
                // client wait for delta
                _cancelSent = true;
                _requested = false;
                EmpLog.Debug("Requesting sleep cancel");
                client.Delta.AddRemote(new SleepCancelDelta());
                break;
        }

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.Tick))]
    internal static bool GateSleepTick(ConSleep __instance)
    {
        var session = NetSession.Instance;
        if (__instance.owner is not { } owner) {
            return true;
        }

        var own = session.Rules.UseOwnSleep;
        var last = owner.IsPC && __instance.pcSleep == 1;

        // own sleep: asleep for as long as the night screen runs, as in a solo game, which is paused there
        // (a session is not: the sleep ended one turn into the night, and the sleeper was no longer counted)
        if (own && owner.IsPC && __instance.slept && session.Transport is not null &&
            ui.GetLayer<LayerSleep>() is { } layer && _minRef(layer) <= _maxMinRef(layer)) {
            return false;
        }

        if (session.Connection is not { } connection) {
            // own sleep, alone on a map of its own: the game's sleep, without the date of the world
            if (own && last && session.Transport is ElinNetClient) {
                _ownNight = true;
            }

            return true;
        }

        if (connection.IsClient) {
            return !owner.IsPlayer;
        }

        if (owner.IsRemotePlayer) {
            return false;
        }

        if (!last) {
            return true;
        }

        if (!own && !AllPlayersReady) {
            return false;
        }

        // own sleep: the night of the world when everyone sleeps and this game keeps the world, else a night
        // of this player alone
        _ownNight = own && (!AllPlayersReady || session.Transport is not ElinNetHost);
        ShieldOtherPlayers(owner);
        if (_ownNight) {
            NarrowParty(owner);
        }

        return true;
    }

    /// <summary>
    ///     The tick that ends the countdown of the host makes the animals of the map sleep beside the bed: the game
    ///     picks them from every chara of the map, taking the companions of the other players and the other players
    ///     themselves from where they stand <br />
    ///     Those skip the game's loop (restrained ones do), and what the game does for the sleeper is done for each
    ///     other player on its own companions, see <see cref="EndSleepTick" />
    /// </summary>
    private static void ShieldOtherPlayers(Chara sleeper)
    {
        _nightTick = true;
        foreach (var chara in _map.charas) {
            if (!chara.isRestrained && CompanionHelper.OwnerOf(chara) is { } who && who != sleeper) {
                chara.isRestrained = true;
                _shielded.Add(chara);
            }
        }
    }

    /// <summary>
    ///     What a night does to "the party" of the sleeper (falling asleep with it, ConSleep.Tick; resting and
    ///     getting hungry with it, LayerSleep.Advance; ConSleep.OnRemoved), the game does to every player and
    ///     all their companions: they are one party here. For a night of its own, the party is the sleeper and
    ///     its own companions for as long as the game's code runs, see <see cref="RestoreParty" />
    /// </summary>
    private static void NarrowParty(params Chara[] sleepers)
    {
        if (_party is not null || pc?.party is not { } party) {
            return;
        }

        _own = party.members.Where(c => c is not null && sleepers.Contains(CompanionHelper.OwnerOf(c))).ToArray();
        _party = party.members;
        party._members = _own.ToList();
    }

    private static void RestoreParty()
    {
        if (_party is not { } all) {
            return;
        }

        _party = null;
        if (pc?.party is not { } party) {
            return;
        }

        // whoever joined or left meanwhile did
        var now = party._members;
        all.RemoveAll(c => _own.Contains(c) && !now.Contains(c));
        all.AddRange(now.Where(c => !all.Contains(c)));
        party._members = all;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.Tick))]
    internal static void EndSleepTick(ConSleep __instance)
    {
        if (!_nightTick) {
            return;
        }

        _nightTick = false;
        RestoreParty();
        foreach (var chara in _shielded) {
            chara.isRestrained = false;
        }

        _shielded.Clear();

        // own sleep: each guest got it as it fell asleep, see OnGuestAsleep
        if (__instance.slept && NetSession.Instance is { Connection: ElinNetHost host, Rules.UseOwnSleep: false }) {
            foreach (var guest in host.ActiveRemoteCharas.Values) {
                BringBeside(guest);

                // the game only clears these for the local player: the guests get the same, their game is told
                guest.RemoveCondition<ConBleed>();
                guest.RemoveCondition<ConPoison>();
                guest.RemoveCondition<ConMiasma>();
            }
        }
    }

    /// <summary>
    ///     ConSleep.Tick for a player asleep: the animals of its own come beside it, one in five, or all those
    ///     told to by a dialogue. The game only does it for the local player, the host here, so the guests get the
    ///     same
    /// </summary>
    private static void BringBeside(Chara guest)
    {
        if (guest.conSleep is null || !guest.IsInActiveMap || guest.pos.IsInSpot<TraitPillowStrange>()) {
            return;
        }

        foreach (var chara in CompanionHelper.CompanionsOf(guest)) {
            if (!chara.IsInActiveMap || chara.host != null || chara.noMove || chara.conSuspend != null ||
                chara.isRestrained) {
                continue;
            }

            if (!chara.GetBool(123) &&
                !(System.Array.IndexOf(chara.race.tag, "sleepBeside") >= 0 && rnd(5) == 0)) {
                continue;
            }

            chara.MoveImmediate(guest.pos);
            chara.SetDir(chara.IsPCC ? guest.dir : 0);
            chara.Say("sleep_beside", chara, guest);
            if (!chara.HasCondition<ConSleep>()) {
                chara.AddCondition<ConSleep>(20 + rnd(25), force: true);
            }
        }
    }

    /// <summary>
    ///     A dream monster (succubus, dream worm) is picked among the charas of the map, the other players' ones
    ///     included: one of them would be pulled onto the bed of the sleeper <br />
    ///     "Told to leave dreamers alone" is how the game lets a chara out of it
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.SuccubusVisit))]
    internal static void ShieldPlayersFromDreams()
    {
        if (NetSession.Instance.Connection is not ElinNetHost host) {
            return;
        }

        foreach (var chara in host.ActiveRemoteCharas.Values) {
            if (chara.GetInt(119) == 0) {
                chara.SetInt(119, 1);
                _noDreams.Add(chara);
            }
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.SuccubusVisit))]
    internal static void EndDreamVisit()
    {
        foreach (var chara in _noDreams) {
            chara.SetInt(119, 0);
        }

        _noDreams.Clear();
    }

    /// <summary>
    ///     The night of the world starts: the night screen of the host opens (the game's own call), or its own
    ///     night becomes the world's (<see cref="Update" />)
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LayerSleep), nameof(LayerSleep.Sleep))]
    internal static void OnSleepStart(int _hours)
    {
        if (NetSession.Instance.Connection is not ElinNetHost host || _ownNight) {
            return;
        }

        _sleepStarted = true;

        EmpLog.Debug("Party sleep started {SleepHours}", _hours);

        // Personal Pace: a night slept together, the world catches up with whoever travelled ahead of it
        host.ReleaseLeads();

        if (NetSession.Instance.Rules.UseOwnSleep && _ready.Count > 1) {
            WidgetPopText.Say("emp_ui_sleep_all".Loc());
        }

        host.Delta.AddRemote(new SleepStartDelta {
            Hours = _hours,
        });
    }

    /// <summary>
    ///     A guest hears that the night of the world starts (SleepStartDelta). True when there is no night
    ///     screen to open for it
    /// </summary>
    /// <param name="hours">hours of that night</param>
    /// <param name="away">heard on the host link while away from the host map</param>
    internal static bool JoinNight(int hours, bool away)
    {
        var layer = ui.GetLayer<LayerSleep>();
        if (!NetSession.Instance.Rules.UseOwnSleep) {
            return layer is not null || away;
        }

        // a player awake is not put to bed by the others
        if (pc.conSleep is null) {
            return true;
        }

        WidgetPopText.Say("emp_ui_sleep_all".Loc());
        _requested = false;
        if (layer is null) {
            return away;
        }

        if (_maxMinRef(layer) == int.MaxValue) {
            return true;
        }

        // its own night becomes the world's: on the host map the host wakes everyone (CharaSleepDelta);
        // elsewhere the night still ends by itself, and still leaves the date to the host
        _minRef(layer) = 0;
        _hoursRef(layer) = hours;
        _maxMinRef(layer) = hours * 60;
        _ownNight = away;
        return true;
    }

    /// <summary>
    ///     The night of the world is over and this player sleeps away from the host map: its night ends now
    /// </summary>
    internal static void EndAwayNight()
    {
        if (_ownNight && ui.GetLayer<LayerSleep>() is { } layer && _maxMinRef(layer) != int.MaxValue) {
            _minRef(layer) = _maxMinRef(layer) + 1;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.OnSleep), typeof(int), typeof(int), typeof(bool))]
    internal static void OnHostSleep(Chara __instance, int power, int days)
    {
        if (!__instance.IsPC) {
            return;
        }

        CharaSleepDelta.Rested(power);

        // a night of its own: nobody else slept it
        if (NetSession.Instance.Connection is not ElinNetHost host || _ownNight) {
            return;
        }

        EmpLog.Debug("Zzz party sleep {SleepPower}", power);

        host.Delta.AddRemote(new CharaSleepDelta {
            Power = power,
            Days = days,
        });

        foreach (var chara in host.ActiveRemoteCharas.Values) {
            if (chara.conSleep is { } sleep) {
                sleep.Kill();
            }
        }

        // one night for one sleep: a player away, still in its night screen, is not asleep for the next one
        _awayAsleep.Clear();
    }

    /// <summary>
    ///     Waking up away from a base, the game walks the player through each of its bases and back, to catch
    ///     them up on the hours slept: zone changes one after the other, in one frame. For the host of a session
    ///     these are real moves to everything else here. The players asleep next to it were handed the map, the
    ///     way back waited for that map to be recalled while the game went on without waiting, and the host woke
    ///     up looking at a map its character was not on, unable to do anything <br />
    ///     Not done while others are connected: a base catches up when someone enters it <br />
    ///     Nor for a guest, alone on a map it holds or not: the bases belong to the host's world, and each
    ///     move of the walk is a request for a lease to the host
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.SimulateFaction))]
    internal static bool OnSimulateFaction()
    {
        var session = NetSession.Instance;
        if (session.Transport is ElinNetClient) {
            return false;
        }

        // (a player elsewhere is still a player: counting those on our map let the game load every base under the host)
        return session.Connection is not ElinNetHost host || !host.IsConnected;
    }

    /// <summary>
    ///     The night screen adds ten minutes to the date at each step, then ends the night for "the party" <br />
    ///     A guest's screen never moves the date: it waits for the host to wake it. A night of its own
    ///     (<see cref="_ownNight" />) does not either, in any game, and ends by itself: for a guest by its own
    ///     wake-up (CharaSleepDelta.WakeOwn), else by the game's end of a night, for the sleeper's own only
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerSleep), nameof(LayerSleep.Advance))]
    internal static bool OnAdvance(LayerSleep __instance)
    {
        var session = NetSession.Instance;
        var guest = session.Connection as ElinNetClient;
        if (guest is null && !_ownNight) {
            // the end of the night of the world: the game rests and feeds "the party", not a guest who woke
            // before and was rested already (CharaSleepDelta, Own)
            if (session is { Connection: ElinNetHost world, Rules.UseOwnSleep: true } &&
                _minRef(__instance) > _maxMinRef(__instance) &&
                world.ActiveRemoteCharas.Values.Any(c => c.conSleep is null)) {
                NarrowParty(world.ActiveRemoteCharas.Values.Where(c => c.conSleep is not null).Append(pc).ToArray());
            }

            return true;
        }

        // woken before the end of its own night (a blow, a sleep given up): no night screen for a player awake
        if (guest is not null && _ownNight && pc.conSleep is null) {
            CloseSleepLayer(__instance);
            return false;
        }

        if (_minRef(__instance) <= _maxMinRef(__instance)) {
            _minRef(__instance) += 10;
            return false;
        }

        if (guest is null) {
            if (session.Connection is ElinNetHost) {
                NarrowParty(pc);
            }

            _ownEnd = true;
            return true;
        }

        if (_ownNight) {
            try {
                CharaSleepDelta.WakeOwn(guest);
            } finally {
                CloseSleepLayer(__instance);
                SayAlone();
            }
        } else {
            // the host ends the night of the world: counted in seconds, not in steps of this screen (a host
            // slower than this game, five players on one machine, was given up on after four seconds)
            if (_waitSince <= 0f) {
                _waitSince = UnityEngine.Time.unscaledTime;
            }

            if (UnityEngine.Time.unscaledTime - _waitSince > WakeWait) {
                EmpLog.Warning("Sleep layer timed out waiting for host wake");
                CloseSleepLayer(__instance);
            } else {
                _minRef(__instance) += 10;
            }
        }

        return false;
    }

    /// <summary>
    ///     Own sleep: a solo game is paused while its night screen shows, nothing touches the sleeper. Here the
    ///     world goes on for the others, and the monsters with it: a player was beaten, even killed, during the
    ///     few seconds of a night of its own. A player takes no damage while its own night plays (this game's
    ///     player during its night screen, a guest asleep on this map)
    /// </summary>
    internal static bool Sheltered(Card target)
    {
        if (target is not Chara { conSleep: not null } chara || !NetSession.Instance.Rules.UseOwnSleep ||
            NetSession.Instance.Transport is null) {
            return false;
        }

        if (chara.IsPC) {
            return _ownNight && ui.GetLayer<LayerSleep>() is not null;
        }

        return NetSession.Instance.Connection is ElinNetHost host &&
               host.ActiveRemoteCharas.Any(p => p.Value == chara && _guestBeds.ContainsKey(p.Key));
    }

    /// <summary>
    ///     The clock of the world runs in the game that keeps it. A solo game is paused while its night screen
    ///     shows; a session is not, and a night of the host's own cost the world the forty minutes its screen
    ///     lasts, when a guest's costs nothing. The minutes of the clock are not counted meanwhile
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceMin))]
    internal static bool OnClockMinute(int a)
    {
        return a != 1 || !_ownNight || NetSession.Instance.Connection is not ElinNetHost ||
               ui.GetLayer<LayerSleep>() is null;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(LayerSleep), nameof(LayerSleep.Advance))]
    internal static void OnAdvanceEnd()
    {
        RestoreParty();
        if (!_ownEnd) {
            return;
        }

        _ownEnd = false;
        _ownNight = false;
        SayAlone();
    }

    /// <summary>
    ///     To the one who slept a night of its own: who was awake, as far as this game knows
    /// </summary>
    private static void SayAlone()
    {
        var names = NetSession.Instance.CurrentPlayers
            .Where(p => p.CharaUid != pc.uid && _map.charas.Find(c => c.uid == p.CharaUid)?.conSleep is null)
            .Select(p => p.User.Name)
            .Concat(_away.Where(a => !_awayAsleep.Contains(a.Key) && !_awayDead.Contains(a.Key)).Select(a => a.Value))
            .ToList();
        WidgetPopText.Say("emp_ui_sleep_alone".Loc(names.Count == 0 ? "…" : string.Join(", ", names)));
    }

    internal static void CloseSleepLayerIfOpen()
    {
        if (ui.GetLayer<LayerSleep>() is { } layer) {
            CloseSleepLayer(layer);
        }
    }

    private static void CloseSleepLayer(LayerSleep layer)
    {
        _ownNight = false;
        _waitSince = 0f;
        if (_maxMinRef(layer) == int.MaxValue) {
            return;
        }

        _maxMinRef(layer) = int.MaxValue;
        layer.CancelInvoke();
        Msg.Say("slept", _hoursRef(layer).ToString());
        ui.ShowCover();
        TweenUtil.Delay(layer.hideDelay, () => ui.HideCover(layer.coverHide, layer.Close));
    }
}
