using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net.Steam;

namespace ElinTogether.Net;

/// <summary>
///     Random quests, fame and karma per player (see <see cref="PersonalQuests" />): the host keeps them for
///     every player in the save, and runs the steps that create or give things for the players on its map
/// </summary>
internal partial class ElinNetHost
{
    private const int StandingFame = 0;
    private const int StandingKarma = 1;
    private const int NewPlayerKarma = 30;

    /// <summary>
    ///     Player chara uid -> quest uid -> the quest, as its player last told it
    /// </summary>
    [ElinGameIOProperty("personal_quests")]
    private static Dictionary<int, Dictionary<int, byte[]>> PersonalQuestLogs
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Player chara uid -> fame, karma
    /// </summary>
    [ElinGameIOProperty("player_standing")]
    private static Dictionary<int, int[]> PlayerStandings
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Player chara uid -> the date of the world when it was last seen connected (council 2026-10-26, stage 4):
    ///     what the date moved while it was gone is not taken from its own quests. Kept with the world, so that a
    ///     host who closed its game with players still in counts their absence too
    /// </summary>
    [ElinGameIOProperty("player_last_seen")]
    private static Dictionary<int, int> PlayerLastSeen
    {
        get => field ??= [];
        set;
    }

    // the characters of the players that settled on this map since they connected (their absence is behind them)
    private readonly HashSet<int> _backFromAbsence = [];

    /// <summary>
    ///     The players connected and settled are seen now; one who leaves is seen for the last time
    /// </summary>
    private void SeePlayers(NetPeerState? leaving = null)
    {
        // (also called while the session closes, the world sometimes already gone: a throw there stopped the
        // links from closing and the port stayed taken, the host could not open its session again, 2026-10-27)
        if (IsZoneSession || !core.IsGameStarted || world?.date is null) {
            if (leaving is not null) {
                _backFromAbsence.Remove(leaving.CharaUid);
            }

            return;
        }

        var now = world.date.GetRaw();
        foreach (var state in States.Values) {
            if (state.Index != 0 && _backFromAbsence.Contains(state.CharaUid)) {
                PlayerLastSeen[state.CharaUid] = now;
            }
        }

        if (leaving is not null) {
            _backFromAbsence.Remove(leaving.CharaUid);
        }
    }

    /// <summary>
    ///     A player back after an absence: the time the world went on without it is added to its own quests'
    ///     deadlines, bounded by the date it was last seen. Its character waited out of every map: no hunger, no
    ///     conditions. Nothing punishes being away
    /// </summary>
    private void ResumeAfterAbsence(int charaUid)
    {
        if (IsZoneSession || !_backFromAbsence.Add(charaUid) || !PlayerLastSeen.TryGetValue(charaUid, out var seen)) {
            return;
        }

        var away = world.date.GetRaw() - seen;
        PlayerLastSeen[charaUid] = world.date.GetRaw();
        if (away <= 0 || !PersonalQuestLogs.TryGetValue(charaUid, out var log)) {
            return;
        }

        foreach (var uid in log.Keys.ToArray()) {
            if (new LZ4Bytes { Bytes = log[uid] }.Decompress<Quest>() is { deadline: > 0 } quest) {
                quest.deadline += away;
                log[uid] = LZ4Bytes.Create(quest).Bytes;
            }
        }

        EmpLog.Debug("Player {CharaUid} back after {Minutes} minute(s) of the world: its quests keep their time", charaUid, away);
    }

    /// <summary>
    ///     The fame a player on this map last told, null when this game keeps none for it
    /// </summary>
    internal int? FameOf(Chara player)
    {
        return !IsZoneSession && PlayerStandings.TryGetValue(player.uid, out var standing) && standing.Length >= 2
            ? standing[StandingFame]
            : null;
    }

    private static Dictionary<int, byte[]> PersonalLogOf(int charaUid)
    {
        if (!PersonalQuestLogs.TryGetValue(charaUid, out var log)) {
            log = PersonalQuestLogs[charaUid] = [];
        }

        return log;
    }

    /// <summary>
    ///     The chara of a player, on this map or not
    /// </summary>
    private int PlayerUidOf(int peerId)
    {
        if (ActiveRemoteCharas.TryGetValue(peerId, out var chara)) {
            return chara.uid;
        }

        return Socket.Peers.FirstOrDefault(p => p.Id == peerId) is { } peer &&
               SavedRemoteCharas.TryGetValue(peer.User, out var uid)
            ? uid
            : 0;
    }

    /// <summary>
    ///     A player on this map takes the quest a resident offers
    /// </summary>
    internal void AcceptPersonal(int peerId, Quest quest)
    {
        if (!ActiveRemoteCharas.TryGetValue(peerId, out var taker)) {
            return;
        }

        // a zone hosted by a client keeps no books, the host of the world does
        var log = IsZoneSession ? null : PersonalLogOf(taker.uid);
        if (log is { Count: >= QuestManager.MaxRandomQuest }) {
            EmpLog.Debug("Rejecting quest accept, quest list of {CharaUid} full", taker.uid);
            return;
        }

        var giver = quest.chara;
        game.quests.globalList.Remove(quest);

        try {
            // what taking it sets up (the parcel to deliver, someone to escort) is for the taker
            using (PlayerStandIn.For(this, peerId, taker)) {
                // drawn by this game for the map: the taker's fame and skills set its level (H5-01)
                if (PlayerFame.Of(taker) is not null) {
                    QuestForTaker.Rescale(quest);
                }

                using (ElinDelta.Simulate()) {
                    quest.Start();
                }
            }
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Quest {QuestId} start failed for player {CharaUid}", quest.id, taker.uid);
        }

        if (quest.UseInstanceZone) {
            // the taker creates the zone of the quest right after, which lifts the deadline (as in the game):
            // this copy must not bring it back
            quest.deadline = 0;
        }

        var data = LZ4Bytes.Create(quest);
        log?[quest.uid] = data.Bytes;

        SendDeltaTo(peerId, new QuestStartDelta {
            Uid = quest.uid,
            Owner = giver,
            AssignQuest = true,
            Data = data,
            Now = world.date.GetRaw(),
        });

        ReleaseOffer(giver, quest.uid);
        EmpLog.Debug("Player {CharaUid} took quest {QuestUid} {QuestId}", taker.uid, quest.uid, quest.id);
    }

    /// <summary>
    ///     A player on this map turns in one of its quests: only that player holds it, it comes with the request
    /// </summary>
    /// <param name="lastWave">for a defense quest: the wave its player reached, and the bonus it earned</param>
    internal void CompletePersonal(int peerId, Quest quest, int lastWave = 0, int bonus = 0)
    {
        if (!ActiveRemoteCharas.TryGetValue(peerId, out var taker)) {
            return;
        }

        // the host of the world gives a reward once, for a quest it knows that player holds
        if (!IsZoneSession && !PersonalLogOf(taker.uid).ContainsKey(quest.uid)) {
            EmpLog.Warning("Player {CharaUid} turns in quest {QuestUid} {QuestId} it does not hold", taker.uid, quest.uid, quest.id);
            return;
        }

        var (hostWave, hostBonus) = (QuestDefenseGame.lastWave, QuestDefenseGame.bonus);
        if (quest is QuestDefenseGame) {
            QuestDefenseGame.lastWave = lastWave;
            QuestDefenseGame.bonus = bonus;
        }

        try {
            using (PlayerStandIn.For(this, peerId, taker)) {
                using (ElinDelta.Simulate()) {
                    quest.Complete();
                }
            }
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Quest {QuestId} completion failed for player {CharaUid}", quest.id, taker.uid);
        } finally {
            QuestDefenseGame.lastWave = hostWave;
            QuestDefenseGame.bonus = hostBonus;
        }

        if (!IsZoneSession) {
            PersonalLogOf(taker.uid).Remove(quest.uid);
        }

        EmpLog.Debug("Player {CharaUid} completed quest {QuestUid} {QuestId}", taker.uid, quest.uid, quest.id);
    }

    /// <summary>
    ///     A player tells what one of its quests holds now, or that it is gone (completed, failed, given up)
    /// </summary>
    /// <param name="now">the date on that player's clock: a deadline is kept as time left, on the host's</param>
    internal void StorePersonal(int peerId, int questUid, LZ4Bytes? data, int now)
    {
        if (IsZoneSession || PlayerUidOf(peerId) is not (> 0 and var uid)) {
            return;
        }

        if (data is null) {
            PersonalLogOf(uid).Remove(questUid);
            return;
        }

        var shift = now > 0 ? world.date.GetRaw() - now : 0;
        if (shift != 0 && data.Decompress<Quest>() is { deadline: > 0 } quest) {
            quest.deadline += shift;
            data = LZ4Bytes.Create(quest);
        }

        PersonalLogOf(uid)[questUid] = data.Bytes;
    }

    /// <summary>
    ///     On a map another player holds, the karma of those visiting it (the host of the world too), as they said
    ///     it. In memory only: it is theirs, nothing of it goes to this game's save
    /// </summary>
    private readonly Dictionary<int, int> _visitorKarma = [];

    internal void StoreStanding(int peerId, int fame, int karma)
    {
        if (IsZoneSession) {
            if (ActiveRemoteCharas.TryGetValue(peerId, out var visitor)) {
                var was = _visitorKarma.GetValueOrDefault(visitor.uid);
                _visitorKarma[visitor.uid] = karma;
                OnKarmaChanged(visitor, was, karma);
            }

            return;
        }

        if (PlayerUidOf(peerId) is not (> 0 and var uid)) {
            return;
        }

        var before = PlayerStandings.TryGetValue(uid, out var standing) && standing.Length >= 2 ? standing[StandingKarma] : 0;
        PlayerStandings[uid] = [fame, karma];

        if (ActiveRemoteCharas.TryGetValue(peerId, out var chara)) {
            OnKarmaChanged(chara, before, karma);
        }
    }

    /// <summary>
    ///     A deed of a player on this map costs or earns it karma: its game keeps the count, this one follows
    /// </summary>
    internal void GiveKarma(Chara player, int karma)
    {
        var peerId = 0;
        foreach (var (id, chara) in ActiveRemoteCharas) {
            if (chara == player) {
                peerId = id;
                break;
            }
        }

        if (peerId == 0 || karma == 0) {
            return;
        }

        SendDeltaTo(peerId, new PlayerStandingDelta {
            Fame = 0,
            Karma = karma,
            Relative = true,
        });

        if (IsZoneSession) {
            if (_visitorKarma.TryGetValue(player.uid, out var was)) {
                _visitorKarma[player.uid] = Math.Clamp(was + karma, -100, 100);
                OnKarmaChanged(player, was, _visitorKarma[player.uid]);
            }

            return;
        }

        if (!PlayerStandings.TryGetValue(player.uid, out var standing) || standing.Length < 2) {
            return;
        }

        var before = standing[StandingKarma];
        standing[StandingKarma] = Math.Clamp(before + karma, -100, 100);
        OnKarmaChanged(player, before, standing[StandingKarma]);

        EmpLog.Debug("Player {CharaUid} karma {Karma} for its deed, now {Total}", player.uid, karma, standing[StandingKarma]);
    }

    /// <summary>
    ///     Whether the guards of this map are after that player: the game only knows about the local one
    /// </summary>
    internal bool IsCriminal(Chara player)
    {
        return KarmaOf(player) < 0 && !player.HasCondition<ConIncognito>();
    }

    /// <summary>
    ///     The karma of another player, as its game last told it
    /// </summary>
    internal int KarmaOf(Chara player)
    {
        return IsZoneSession
            ? _visitorKarma.GetValueOrDefault(player.uid)
            : PlayerStandings.TryGetValue(player.uid, out var standing) && standing.Length >= 2 ? standing[StandingKarma] : 0;
    }

    internal bool HasCriminalHere()
    {
        foreach (var chara in ActiveRemoteCharas.Values) {
            if (chara.IsAliveInCurrentZone && IsCriminal(chara)) {
                return true;
            }
        }

        return false;
    }

    private static void OnKarmaChanged(Chara player, int before, int after)
    {
        if (before < 0 == after < 0 || !player.IsAliveInCurrentZone) {
            return;
        }

        // as the game does for the local player
        if (after < 0) {
            player.pos.TryWitnessCrime(player);
        }

        _zone.RefreshCriminal();
    }

    /// <summary>
    ///     A player settled on this map: its own quests and standing, and the offers others already took
    /// </summary>
    private void SendPersonalState(ISteamNetPeer peer, int charaUid)
    {
        ResumeAfterAbsence(charaUid);

        if (IsZoneSession || !Session.Rules.UsePersonalQuests) {
            return;
        }

        if (!PlayerStandings.TryGetValue(charaUid, out var standing) || standing.Length < 2) {
            standing = PlayerStandings[charaUid] = [0, NewPlayerKarma];
        }

        var taken = PersonalQuestLogs
            .Where(kv => kv.Key != charaUid)
            .SelectMany(kv => kv.Value.Keys)
            .Concat(game.quests.list.Where(PersonalQuests.IsPersonal).Select(q => q.uid))
            .ToArray();

        SendDeltaTo(peer.Id, new PersonalStateDelta {
            Quests = PersonalLogOf(charaUid).Values.Select(bytes => new LZ4Bytes { Bytes = bytes }).ToList(),
            Taken = taken,
            Fame = standing[StandingFame],
            Karma = standing[StandingKarma],
            Now = world.date.GetRaw(),
        });
    }

    /// <summary>
    ///     A map coming back from a player travelling alone still shows the quests it took there as offered
    /// </summary>
    internal void SweepTakenOffers()
    {
        if (IsZoneSession || PersonalQuestLogs.Count == 0) {
            return;
        }

        foreach (var chara in _map.charas) {
            if (chara.quest is not { } offer || game.quests.list.Contains(offer)) {
                continue;
            }

            if (PersonalQuestLogs.Values.Any(log => log.ContainsKey(offer.uid))) {
                ReleaseOffer(chara, offer.uid);
            }
        }
    }

    private void ReleaseOffer(Chara? giver, int questUid)
    {
        if (giver?.quest?.uid == questUid) {
            giver.quest = null;
        }

        Delta.AddRemote(new QuestTakenDelta {
            Giver = giver,
            Uid = questUid,
        });
    }
}
