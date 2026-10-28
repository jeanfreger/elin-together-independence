using System.Collections.Generic;
using System.Linq;
using ElinTogether.Models;
using ElinTogether.Net;
using ElinTogether.Patches;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     Random quests belong to the player who takes them, with the fame and karma they earn. Each game only
///     holds its own in the quest log, next to the story quests everyone shares. <br />
///     A client's world is the host's: after every load of it (joining, travelling alone, coming back) its own
///     quests and standing are put back in place of the host's
/// </summary>
internal static class PersonalQuests
{
    private static readonly HashSet<int> _taken = [];
    private static readonly HashSet<int> _turnedIn = [];
    private static List<Quest> _mine = [];
    private static Game? _source;
    private static bool _hasStanding;
    private static object? _transport;
    private static bool _dropInstances;
    private static int _fame;
    private static int _karma;
    private static int _now;

    internal static bool Enabled => NetSession.Instance.Rules.UsePersonalQuests;

    /// <summary>
    ///     Quests with their own zone, for every player: the zone belongs to the taker, who holds it like any map
    ///     it travels to
    /// </summary>
    internal static bool InstancesEnabled => Enabled && NetSession.Instance.Rules.AllowIndependentTravel;

    private const float StateWait = 10f;

    private static readonly HashSet<int> _gone = [];
    private static (int QuestUid, int Giver, bool Failed)? _outcome;
    private static bool _stateFresh;
    private static float _settleDeadline;

    /// <summary>
    ///     A client leaves the zone of its quest. The game settles the quest when the player walks into the place
    ///     it came from, but that place may be the host's or another player's by now, and then our world is
    ///     replaced on the way: how it went is noted here, and settled once we stand somewhere
    /// </summary>
    internal static void LeaveInstance(Zone zone)
    {
        if (!InstancesEnabled || zone.instance is not ZoneInstanceRandomQuest { uidQuest: not 0 } instance) {
            return;
        }

        if (EClass.game.quests.Get(instance.uidQuest) is not { } quest) {
            return;
        }

        // what leaving decides (a harvest is weighed now), once: the game would run it again when moving
        zone.events.OnLeaveZone();
        zone.events.list.RemoveAll(e => e is ZoneEventQuest);

        _outcome = (quest.uid, instance.uidClient, instance.status != ZoneInstance.Status.Success);
        _stateFresh = false;
        _settleDeadline = 0f;

        // the game's own settling, queued when moving, finds nothing to do
        instance.uidQuest = 0;
    }

    /// <summary>
    ///     The host came along and ran the zone of our quest (see ElinNetHost.AskAlongToQuestZone): it tells how
    ///     the quest went as everyone leaves, and it is settled here like one we ran alone, once we stand in town
    /// </summary>
    internal static void OnHostSettled(int questUid, int giver, bool failed)
    {
        if (!InstancesEnabled || EClass.game?.quests?.list.Exists(q => q.uid == questUid && IsPersonal(q)) != true) {
            return;
        }

        _outcome = (questUid, giver, failed);

        // what the host keeps of this quest lands again when we settle in town: not before
        _stateFresh = false;
        _settleDeadline = 0f;
    }

    private static void SettleOutcome()
    {
        if (_outcome is not { } outcome || EClass._zone.IsInstance ||
            NetSession.Instance.Transport is not ElinNetClient { IsInTransfer: false }) {
            return;
        }

        // on the host's map, what the host kept for this player (its quests, its fame) lands when it settles
        // there, sometimes a moment after the game started: settling before would be undone by it
        if (!NetSession.Instance.IsAway && !_stateFresh) {
            if (_settleDeadline == 0f) {
                _settleDeadline = Time.unscaledTime + StateWait;
            }

            if (Time.unscaledTime < _settleDeadline) {
                return;
            }
        }

        _outcome = null;

        // by its number: the quest in the log may be another copy by now
        var quest = EClass.game.quests.list.Find(q => q.uid == outcome.QuestUid && IsPersonal(q));
        if (quest is null) {
            return;
        }

        _gone.Add(quest.uid);

        if (outcome.Failed) {
            quest.Fail();
        } else {
            quest.Complete();
        }

        if (EClass.pc.IsAliveInCurrentZone && EClass._map.FindChara(outcome.Giver) is { } giver) {
            giver.ShowDialog("_chara", outcome.Failed ? "quest_fail" : "quest_success");
        }
    }

    internal static bool IsPersonal(Quest quest)
    {
        return Enabled && quest.IsRandomQuest;
    }

    internal static void Tick()
    {
        var session = NetSession.Instance;
        if (session.Transport is null || !Enabled) {
            _mine = [];
            _taken.Clear();
            _turnedIn.Clear();
            _source = null;
            _hasStanding = false;
            _dropInstances = false;
            _outcome = null;
            return;
        }

        if (!EClass.core.IsGameStarted || EClass.game?.quests is not { } quests) {
            return;
        }

        if (session.Transport is ElinNetHost host) {
            host.SweepTakenOffers();
            return;
        }

        if (!ReferenceEquals(EClass.game, _source)) {
            OnWorldLoaded();
            return;
        }

        SettleOutcome();

        // offers of this map someone else already holds
        foreach (var chara in EClass._map.charas) {
            if (chara.quest is { } offer && _taken.Contains(offer.uid) && !quests.list.Contains(offer)) {
                chara.quest = null;
            }
        }

        // nobody else holds these: they expire on this player's clock
        foreach (var expired in quests.list.Where(q => IsPersonal(q) && q.IsExpired).ToArray()) {
            Msg.Say("questExpired", expired.GetTitle());
            expired.Fail();
        }

        _mine = quests.list.Where(IsPersonal).ToList();
        _now = EClass.world.date.GetRaw();

        // not before the host said what this player's standing is: until then it shows the host's own
        var player = EClass.player;
        if (!_hasStanding || (player.fame == _fame && player.karma == _karma)) {
            return;
        }

        _fame = player.fame;
        _karma = player.karma;
        TellHost(new PlayerStandingDelta {
            Fame = _fame,
            Karma = _karma,
        });
    }

    /// <summary>
    ///     A client's world was just replaced (joining, travelling alone, coming back, joining someone's map):
    ///     its own quests and standing go back in place of the host's, with the time they had left
    /// </summary>
    internal static void OnWorldLoaded()
    {
        if (!Enabled || NetSession.Instance.Transport is not ElinNetClient || EClass.game?.quests is null) {
            return;
        }

        if (ReferenceEquals(EClass.game, _source)) {
            return;
        }

        _source = EClass.game;
        // what is held is from another connection when the host has not spoken on this one yet (Tick, which resets it
        // at the title, no longer runs once the connection is removed): its quests and date would be taken for this
        // world's, a rejoining player's quests kept the time they had left whatever the host rule said (bench 2026-10-28)
        if (!ReferenceEquals(NetSession.Instance.Transport, _transport)) {
            _mine = [];
            _now = 0;
            _hasStanding = false;
        }

        Rebase(_now);
        Restore();
    }

    /// <summary>
    ///     From the host, when this player settles on its map: what the host kept for it
    /// </summary>
    /// <param name="now">the date on the host's clock</param>
    internal static void Receive(List<Quest> mine, int[] taken, int fame, int karma, int now)
    {
        // settled here a moment ago, the host does not know yet
        mine.RemoveAll(q => _gone.Contains(q.uid));

        _stateFresh = true;
        _mine = mine;
        _now = now;
        _fame = fame;
        _karma = karma;
        _hasStanding = true;

        // the first word of the host on this connection
        if (!ReferenceEquals(NetSession.Instance.Transport, _transport)) {
            _transport = NetSession.Instance.Transport;
            _dropInstances = true;
            _gone.Clear();
        }

        _taken.Clear();
        _taken.UnionWith(taken);

        // put in place by the next tick, once the world it is for has started
        _source = null;
    }

    internal static void MarkTaken(int questUid)
    {
        _taken.Add(questUid);
    }

    /// <summary>
    ///     A quest taken and turned in with one click: the host's answer to the first half arrives after
    /// </summary>
    internal static void MarkTurnedIn(int questUid)
    {
        _turnedIn.Add(questUid);
    }

    internal static bool WasTurnedIn(int questUid)
    {
        return _turnedIn.Contains(questUid);
    }

    private static ElinNetBase? _toldHolder;
    private static int _toldKarma;
    private static float _tellHolderAt;

    /// <summary>
    ///     On a map another player holds, its guards and merchants look at this player's karma: the holder is told
    ///     on arrival, when it changes, and again now and then (the first one may come before it knows this
    ///     character). The host of the world visiting does the same
    /// </summary>
    internal static void TellHolder()
    {
        if (!Enabled || NetSession.Instance.ZoneSession is not ElinNetClient holder || EClass.player is not { } player) {
            _toldHolder = null;
            return;
        }

        // joining someone's map replaces the world: not before this player's own standing is back in it
        if (NetSession.Instance.Transport is ElinNetClient && (!_hasStanding || !ReferenceEquals(EClass.game, _source))) {
            return;
        }

        if (_toldHolder == holder && _toldKarma == player.karma && UnityEngine.Time.unscaledTime < _tellHolderAt) {
            return;
        }

        _toldHolder = holder;
        _toldKarma = player.karma;
        _tellHolderAt = UnityEngine.Time.unscaledTime + 10f;
        holder.Delta.AddRemote(new PlayerStandingDelta {
            Fame = player.fame,
            Karma = player.karma,
        });
    }

    /// <summary>
    ///     To the host of the world, wherever this player is: on its map, travelling alone, a guest somewhere
    /// </summary>
    internal static void TellHost(ElinDelta delta)
    {
        if (NetSession.Instance.Transport is not ElinNetClient main) {
            return;
        }

        if (NetSession.Instance.IsAway) {
            main.SendWhileAway(delta);
        } else {
            main.Delta.AddRemote(delta);
        }
    }

    /// <summary>
    ///     The player hosting a map took a quest there: its offer is gone for the others on that map
    /// </summary>
    internal static void OnStarted(Quest quest)
    {
        if (NetSession.Instance.Connection is ElinNetHost host) {
            host.Delta.AddRemote(new QuestTakenDelta {
                Giver = quest.person.chara,
                Uid = quest.uid,
            });
        }
    }

    /// <summary>
    ///     A deadline is a date: the time left is kept, on the clock of the world just loaded
    /// </summary>
    private static void Rebase(int before)
    {
        var shift = before > 0 ? EClass.world.date.GetRaw() - before : 0;
        _now = EClass.world.date.GetRaw();
        if (shift == 0) {
            return;
        }

        foreach (var quest in _mine.Where(q => q.deadline > 0)) {
            quest.deadline += shift;
        }
    }

    /// <summary>
    ///     Time another player made pass (see WorldDateAdvanceEvent.CatchUp) is not taken from this player's
    ///     quests: their deadlines move with the date. The host hears of it like any change of a quest
    /// </summary>
    internal static void Postpone(int minutes)
    {
        if (!Enabled || EClass.game?.quests is not { } quests) {
            return;
        }

        foreach (var quest in quests.list.Where(q => IsPersonal(q) && q.deadline > 0)) {
            quest.deadline += minutes;
        }
    }

    private static void Restore()
    {
        var quests = EClass.game.quests;

        // the random quests of this world are the host's own
        quests.list.RemoveAll(q => q.IsRandomQuest && !_mine.Contains(q));

        foreach (var quest in _mine) {
            if (!quests.list.Contains(quest)) {
                quests.list.Insert(0, quest);
            }

            // who it is for is looked up again, in this world. An inhabitant of a map (no global character) is
            // looked for on the active map, and a world just received has none yet: the game would throw.
            // It is found the next time this runs, standing on a map (see Receive, Tick)
            quest.person._tempChara = null;
            quest.person.refChara = new();
            var giver = EClass.game.activeZone?.map is null
                ? EClass.game.cards.globalCharas.TryGetValue(quest.person.uidChara)
                : quest.chara;
            if (giver is not null && giver.quest?.uid != quest.uid) {
                giver.quest = quest;
            }

            SharedQuests.Remember(quest);
        }

        if (_hasStanding) {
            EClass.player.fame = _fame;
            EClass.player.karma = _karma;
        }

        if (!_dropInstances) {
            return;
        }

        // connected anew: the zone of a quest is gone with the connection of the player who held it (dropped
        // out or crashed inside), and so is the quest, at no cost
        _dropInstances = false;
        foreach (var quest in _mine.Where(q => q.UseInstanceZone).ToArray()) {
            EmpLog.Information("Quest {QuestUid} {QuestId} lost its zone with the last connection, dropping it", quest.uid, quest.id);
            QuestFailEvent.FailQuietly(quest);
            _mine.Remove(quest);
            _gone.Add(quest.uid);
            TellHost(new PersonalQuestDelta {
                Uid = quest.uid,
                Data = null,
            });
        }
    }
}

/// <summary>
///     While the host runs a quest step for a player on its map (taking a quest, turning it in), that player
///     stands in as the local one: the game's own code gives it the item to deliver, drops the reward at its
///     feet, and the fame and karma it earns go to it
/// </summary>
internal static class PlayerStandIn
{
    private static int _fame;
    private static int _karma;

    internal static bool IsActive { get; private set; }

    internal static ScopeExit For(ElinNetHost host, int peerId, Chara actor)
    {
        var self = EClass.player.chara;

        IsActive = true;
        _fame = _karma = 0;
        EClass.player.chara = actor;
        // what is made meanwhile has the level of this player's fame (H5-04)
        var fame = PlayerFame.As(actor);

        return new() {
            OnExit = () => {
                fame?.Dispose();
                EClass.player.chara = self;
                IsActive = false;

                if (_fame != 0 || _karma != 0) {
                    host.SendDeltaTo(peerId, new PlayerStandingDelta {
                        Fame = _fame,
                        Karma = _karma,
                        Relative = true,
                    });
                }
            },
        };
    }

    internal static bool Redirect(int fame, int karma)
    {
        if (!IsActive) {
            return false;
        }

        _fame += fame;
        _karma += karma;
        return true;
    }
}
