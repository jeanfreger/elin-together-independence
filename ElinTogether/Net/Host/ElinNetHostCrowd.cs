using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net.Steam;

namespace ElinTogether.Net;

/// <summary>
///     Host box "Large crowds" (PLAN_100_players stage 1, item 2): inside the host map each player is sent the
///     characters near its own at the snapshot's rate and their steps and acts as they come, the farther ones once a
///     second in the snapshot only. Its own character, its companions, its team and whoever it trades, duels or fights
///     with are always near. The full snapshot (every 4 s) still names everyone: a character out of range is never
///     taken off a guest's map by WorldStateSnapshot.RemoveLeftOverCharas, and its first partial entry back in range
///     puts it right
/// </summary>
internal partial class ElinNetHost
{
    internal const int CrowdNearTiles = 24;

    // the far ones in every 5th partial snapshot: once a second at 5 Hz
    private const int CrowdFarEvery = 5;

    // player -> character uid -> the key of the entry last sent to it (WorldStateSnapshot.ChangeTracker.Key)
    private readonly Dictionary<int, Dictionary<int, int>> _crowdSent = [];

    // player -> who is always near for it, from the last snapshot tick (the delta flush reads it at 50 Hz)
    private readonly Dictionary<int, CrowdView> _crowdViews = [];

    private int _crowdZoneUid;

    private sealed class CrowdView
    {
        internal Chara? Self;
        internal readonly HashSet<int> Always = [];

        internal bool Near(int uid, int x, int z)
        {
            return Self is null || Always.Contains(uid) || Self.pos.Distance(x, z) <= CrowdNearTiles;
        }
    }

    private static bool Crowds => WorldStateSnapshot.ChangeTracker.Crowds;

    /// <summary>
    ///     The snapshot of this tick, one per player on the host map. <paramref name="all" /> has every character
    /// </summary>
    private void SendCrowdSnapshot(WorldStateSnapshot all)
    {
        var tick = Session.Tick;
        var full = all.ZoneUid != _crowdZoneUid || tick % WorldStateSnapshot.ChangeTracker.CrowdFullEvery == 0;
        _crowdZoneUid = all.ZoneUid;
        var farSlot = tick % CrowdFarEvery == 0;

        var charas = new Dictionary<int, Chara>();
        foreach (var chara in _map.charas) {
            charas[chara.uid] = chara;
        }

        RefreshCrowdViews(charas);

        byte[]? fullBytes = null;
        byte[]? lean = null;
        foreach (var peer in Socket.Peers) {
            if (IsOffHostMap(peer)) {
                // back on the map, it starts from a full one
                _crowdSent.Remove(peer.Id);
                peer.Send(lean ??= Socket.Serializer.Serialize(all.Lean()));
                continue;
            }

            var isNew = !_crowdSent.TryGetValue(peer.Id, out var sent);
            if (full || isNew) {
                sent ??= _crowdSent[peer.Id] = [];
                sent.Clear();
                var view = _crowdViews.GetValueOrDefault(peer.Id);
                foreach (var entry in all.CharaSnapshots) {
                    sent[entry.Owner.Uid] = KeyFor(entry, charas, view);
                }

                peer.Send(fullBytes ??= Socket.Serializer.Serialize(all));
                continue;
            }

            peer.Send(Socket.Serializer.Serialize(all.With(PartialFor(peer.Id, all, sent!, charas, farSlot))));
        }
    }

    private ImmutableArray<CharaStateSnapshot> PartialFor(int peerId, WorldStateSnapshot all, Dictionary<int, int> sent,
        Dictionary<int, Chara> charas, bool farSlot)
    {
        var view = _crowdViews.GetValueOrDefault(peerId);
        var changed = new List<(CharaStateSnapshot Entry, int Key, int Distance)>();
        foreach (var entry in all.CharaSnapshots) {
            var uid = entry.Owner.Uid;
            if (!farSlot && view?.Near(uid, entry.Pos.X, entry.Pos.Z) == false) {
                // not marked as sent: it goes in the next far slot if it still differs
                continue;
            }

            var key = KeyFor(entry, charas, view);
            if (sent.TryGetValue(uid, out var was) && was == key) {
                continue;
            }

            var distance = view?.Self is not { } me || view.Always.Contains(uid) ? 0 : me.pos.Distance(entry.Pos.X, entry.Pos.Z);
            changed.Add((entry, key, distance));
        }

        // the budget per player and tick (item 3): the nearest first, the others stay unsent and differ next tick
        if (changed.Count > CrowdEntryBudget) {
            changed.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            changed.RemoveRange(CrowdEntryBudget, changed.Count - CrowdEntryBudget);
        }

        var kept = ImmutableArray.CreateBuilder<CharaStateSnapshot>(changed.Count);
        foreach (var (entry, key, _) in changed) {
            sent[entry.Owner.Uid] = key;
            kept.Add(entry);
        }

        return kept.MoveToImmutable();
    }

    // entries of a partial snapshot per player and tick at most: a crowd that all moves at once costs a player that
    // many entries a tick (about 1.5 KB), the farthest wait for the next one
    private const int CrowdEntryBudget = 48;

    // player -> what of the player list it was last sent, and when (item 3)
    private readonly Dictionary<int, (int Key, float At)> _playersSent = [];

    // the player list again even unchanged after that long: the pings it shows, and NetDesync compares on each list
    // (3 in a row: a gap is told within 18 s instead of 6)
    private const float PlayersResendSeconds = 6f;

    /// <summary>
    ///     The player list (every 2 s) to those on the host map whose last one differs, or is older than
    ///     <see cref="PlayersResendSeconds" />: with 100 players it was a third of all that was sent while idle
    /// </summary>
    private void SendPlayersOnChange(SessionPlayersSnapshot players)
    {
        var key = PlayersKey(players);
        var now = UnityEngine.Time.realtimeSinceStartup;
        byte[]? bytes = null;
        foreach (var peer in Socket.Peers) {
            if (IsOffHostMap(peer)) {
                _playersSent.Remove(peer.Id);
                continue;
            }

            if (_playersSent.TryGetValue(peer.Id, out var last) && last.Key == key && now - last.At < PlayersResendSeconds) {
                continue;
            }

            _playersSent[peer.Id] = (key, now);
            peer.Send(bytes ??= Socket.Serializer.Serialize(players));
        }
    }

    // all but the pings and the tick of the last report, which change every time
    private static int PlayersKey(SessionPlayersSnapshot players)
    {
        var hash = new System.HashCode();
        foreach (var s in players.Current) {
            hash.Add(s.Index);
            hash.Add(s.CharaUid);
            hash.Add(s.Speed);
            hash.Add(s.LastAct);
            hash.Add(s.Turbo);
            hash.Add(s.Talking);
            hash.Add(s.Idle);
        }

        if (players.Sums is { } sums) {
            hash.Add(sums.ZoneUid);
            hash.Add(sums.Charas);
            hash.Add(sums.CharaMix);
            hash.Add(sums.Things);
            hash.Add(sums.ThingMix);
            hash.Add(sums.Locals);
            hash.Add(sums.LocalMix);
            var bags = 0;
            foreach (var (uid, mix) in sums.Bags ?? []) {
                bags ^= System.HashCode.Combine(uid, mix);
            }

            hash.Add(bags);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    ///     A player near this one moves on that player's game through its CharaMoveDelta: its tile is left out of the
    ///     key (item 1). A far one gets no step: its tile is what tells where it went
    /// </summary>
    private int KeyFor(CharaStateSnapshot entry, Dictionary<int, Chara> charas, CrowdView? view)
    {
        var uid = entry.Owner.Uid;
        var player = charas.TryGetValue(uid, out var chara) && chara.IsPlayer;
        return player && (view?.Near(uid, entry.Pos.X, entry.Pos.Z) ?? true)
            ? WorldStateSnapshot.ChangeTracker.StateKey(entry)
            : WorldStateSnapshot.ChangeTracker.Key(entry);
    }

    private void RefreshCrowdViews(Dictionary<int, Chara> charas)
    {
        // once per tick for the whole map: the player behind each character, and each player's team
        var behind = new Dictionary<int, int>();
        foreach (var (uid, chara) in charas) {
            if (PlayerKarma.PlayerBehind(chara) is { } owner && owner.uid != uid) {
                behind[uid] = owner.uid;
            }
        }

        var teams = new Dictionary<int, int>();
        if (NetSession.Instance.Rules.UseTeams) {
            foreach (var player in ActiveRemoteCharas.Values) {
                teams[player.uid] = Teams.TeamOf(player.uid);
            }

            teams[pc.uid] = Teams.TeamOf(pc.uid);
        }

        foreach (var peer in Socket.Peers) {
            if (!_crowdViews.TryGetValue(peer.Id, out var view)) {
                view = _crowdViews[peer.Id] = new();
            }

            view.Always.Clear();
            view.Self = ActiveRemoteCharas.GetValueOrDefault(peer.Id) is { IsInActiveMap: true } self ? self : null;
            if (view.Self is not { } me) {
                continue;
            }

            view.Always.Add(me.uid);
            var team = teams.GetValueOrDefault(me.uid);
            foreach (var (uid, chara) in charas) {
                var of = behind.GetValueOrDefault(uid, uid);
                if (of == me.uid || (team != Teams.Wanderers && teams.GetValueOrDefault(of) == team) || chara.enemy == me) {
                    view.Always.Add(uid);
                }
            }

            if (me.enemy is { } enemy) {
                view.Always.Add(enemy.uid);
            }

            if (PlayerDuel.FoeOf(me.uid) is var foe and not 0) {
                view.Always.Add(foe);
            }

            if (PlayerTrade.PartnerOf(me) is { } partner) {
                view.Always.Add(partner.uid);
            }
        }

        if (_crowdViews.Count > Socket.Peers.Count) {
            var here = new HashSet<int>(Socket.Peers.Select(p => p.Id));
            var gone = _crowdViews.Keys.Where(id => !here.Contains(id)).ToList();

            foreach (var id in gone) {
                _crowdViews.Remove(id);
                _crowdSent.Remove(id);
            }
        }
    }

    /// <summary>
    ///     As SendByMap, with each player on the host map sent only the steps and acts near it
    /// </summary>
    private void SendCrowdDeltas(List<ElinDelta> deltas)
    {
        byte[]? all = null;
        byte[]? lean = null;
        var leanBuilt = false;
        foreach (var peer in Socket.Peers) {
            if (IsOffHostMap(peer)) {
                if (!leanBuilt) {
                    leanBuilt = true;
                    lean = deltas.Where(ElinNetClient.ReachesAwayPlayer).ToList() is { Count: > 0 } away
                        ? Socket.Serializer.Serialize(new WorldStateDeltaList { DeltaList = away })
                        : null;
                }

                if (lean is not null) {
                    peer.Send(lean);
                }

                continue;
            }

            if (CrowdDeltas(deltas, peer) is not { } kept) {
                peer.Send(all ??= Socket.Serializer.Serialize(new WorldStateDeltaList { DeltaList = deltas }));
            } else if (kept.Count > 0) {
                peer.Send(Socket.Serializer.Serialize(new WorldStateDeltaList { DeltaList = kept }));
            }
        }
    }

    /// <summary>
    ///     The steps and acts of this batch that a player on the host map is sent, null when all of them. Its own
    ///     steps come back to it as well without this box (its game drops them, CharaMoveDelta.OnApply)
    /// </summary>
    private List<ElinDelta>? CrowdDeltas(List<ElinDelta> deltas, ISteamNetPeer peer)
    {
        if (_crowdViews.GetValueOrDefault(peer.Id) is not { Self: { } me } view) {
            return null;
        }

        List<ElinDelta>? kept = null;
        for (var i = 0; i < deltas.Count; i++) {
            var delta = deltas[i];
            var drop = delta switch {
                CharaMoveDelta move => move.Owner.Uid == me.uid || !view.Near(move.Owner.Uid, move.Pos.X, move.Pos.Z),
                // (not an act with a tool: its replay waters the tiles on each game)
                CharaActPerformDelta { Tool: null } act => FarActor(act, view),
                _ => false,
            };

            if (drop && kept is null) {
                kept = deltas.GetRange(0, i);
            } else if (!drop) {
                kept?.Add(delta);
            }
        }

        return kept;
    }

    private static bool FarActor(CharaActPerformDelta act, CrowdView view)
    {
        if (act.Owner.Find() is not Chara actor || view.Always.Contains(act.Owner.Uid) ||
            act.TargetCard is { } target && view.Always.Contains(target.Uid)) {
            return false;
        }

        return !view.Near(actor.uid, actor.pos.x, actor.pos.z) &&
               (act.Pos is not { } at || !view.Near(0, at.X, at.Z));
    }
}
