using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class WorldStateSnapshot : EClass
{
    private static Dictionary<int, int> _missingLeftOverCharas = [];


    [Key(0)]
    public required int ServerTick { get; init; }

    [Key(1)]
    public required ImmutableArray<int> GameDate { get; init; }

    [Key(2)]
    public required ImmutableArray<CharaStateSnapshot> CharaSnapshots { get; init; }

    [Key(3)]
    public required int GlobalUidNext { get; init; }

    [Key(4)]
    public required int SharedSpeed { get; init; }

    /// <summary>
    ///     The zone of the characters whose own <see cref="CharaStateSnapshot.CurrentZoneUid" /> is 0: written once
    ///     here instead of in every entry (efficiency audit 2026-10-27)
    /// </summary>
    [Key(5)]
    public int ZoneUid { get; init; }

    /// <summary>
    ///     Only the characters whose state changed since the last snapshot to everyone; every
    ///     <see cref="ChangeTracker.FullEvery" />th one, and the first on a new map, has them all (efficiency audit
    ///     2026-10-27: most characters of a map stand still, and every one went to every player 5 times a second).
    ///     A character missing from a partial one is not gone: only a full one says who left
    /// </summary>
    [Key(6)]
    public bool Partial { get; init; }

    [ElinPreLoad]
    private static void ClearSweepStrikes(GameIOContext context)
    {
        _missingLeftOverCharas = [];
    }

    /// <summary>
    ///     What the snapshots to everyone last said of each character, for the next one to carry only what changed
    /// </summary>
    internal sealed class ChangeTracker
    {
        // 1 s at 5 Hz
        internal const int FullEvery = 5;

        private Dictionary<int, int> _sent = [];
        private Dictionary<int, int> _next = [];
        private int _zoneUid;

        /// <summary>
        ///     Whether the entry goes into this snapshot, and remember it for the next one
        /// </summary>
        internal bool Keep(CharaStateSnapshot snapshot, bool full)
        {
            var key = Key(snapshot);
            var uid = snapshot.Owner.Uid;
            _next[uid] = key;
            return full || !_sent.TryGetValue(uid, out var sent) || sent != key;
        }

        internal bool Begin(int zoneUid, int tick)
        {
            var full = zoneUid != _zoneUid || tick % FullEvery == 0;
            _zoneUid = zoneUid;
            _next.Clear();
            return full;
        }

        internal void End()
        {
            (_sent, _next) = (_next, _sent);
        }

        private static int Key(CharaStateSnapshot s)
        {
            return HashCode.Combine(HashCode.Combine(s.Pos.X, s.Pos.Z, s.CurrentZoneUid, s.Hp, s.IsDead),
                s.Hostility, s.OriginalHostility, s.UidMaster, s.MinionType, s.Hunger);
        }
    }

    /// <param name="tracker">For a snapshot to everyone: only what changed since the last one, see <see cref="Partial" /></param>
    internal static WorldStateSnapshot Create(ChangeTracker? tracker = null)
    {
        var selfState = NetSession.Instance.Self;
        if (selfState != null) {
            selfState.Speed = pc.Stub_get_Speed();
        }

        var zoneUid = _zone.uid;
        var full = tracker?.Begin(zoneUid, NetSession.Instance.Tick) ?? true;
        var snapshots = new Dictionary<int, CharaStateSnapshot>();
        foreach (var chara in _map.charas) {
            // (a server's stand-in is nobody for the guests: it is not told to them, see ServerAvatar)
            if (!chara.IsInActiveMap || ServerAvatar.Is(chara)) {
                continue;
            }

            var snapshot = CharaStateSnapshot.Create(chara);
            if (snapshot.CurrentZoneUid == zoneUid) {
                snapshot.CurrentZoneUid = 0;
            }

            if (tracker?.Keep(snapshot, full) ?? true) {
                snapshots[chara.uid] = snapshot;
            }
        }

        tracker?.End();

        // (the players' own state, LastAct, speed, turbo, is not attached any more: no game reads it here, the host
        // keeps it from each guest's own report, and it was a quarter of every snapshot to everyone, efficiency audit
        // 2026-10-27)

        return new() {
            ServerTick = NetSession.Instance.Tick,
            GameDate = [..game.world.date.raw],
            CharaSnapshots = [..snapshots.Values],
            GlobalUidNext = game.cards.uidNext,
            SharedSpeed = NetSession.Instance.SharedSpeed,
            ZoneUid = zoneUid,
            Partial = !full,
        };
    }

    /// <summary>
    ///     The same tick for a player away from the host map: its game drops the characters of a map it is not on
    /// </summary>
    internal WorldStateSnapshot Lean()
    {
        return new() {
            ServerTick = ServerTick,
            GameDate = GameDate,
            CharaSnapshots = ImmutableArray<CharaStateSnapshot>.Empty,
            GlobalUidNext = GlobalUidNext,
            SharedSpeed = SharedSpeed,
            ZoneUid = ZoneUid,
            Partial = Partial,
        };
    }

    public void ApplyReconciliation()
    {
        if (NetSession.Instance.Connection is not ElinNetClient client) {
            return;
        }

        foreach (var snapshot in CharaSnapshots) {
            if (snapshot.CurrentZoneUid == 0) {
                snapshot.CurrentZoneUid = ZoneUid;
            }
        }

        client.Delta.AddLocal(new DynamicDelta {
            Action = _ => {
                // 1
                WorldDateAdvanceDelta.SetClientDate([..GameDate]);

                // 2
                foreach (var snapshot in CharaSnapshots) {
                    snapshot.ApplyReconciliation();
                }

                // (only a full snapshot names everyone on the map)
                if (!Partial) {
                    RemoveLeftOverCharas();
                }

                // 3
                game.cards.uidNext = GlobalUidNext;

                // 4
                // (always: the host sends 0 when the shared speed is off, a value kept from before stayed; upstream 403f6fb)
                NetSession.Instance.SharedSpeed = SharedSpeed;
            },
        });
    }

    private void RemoveLeftOverCharas()
    {
        var uids = new HashSet<int>(CharaSnapshots.Select(s => s.Owner.Uid));
        if (!uids.Contains(pc.uid)) {
            _missingLeftOverCharas = [];
            return;
        }

        var missing = new Dictionary<int, int>();
        List<Chara>? leftovers = null;
        foreach (var chara in _map.charas) {
            if (chara.IsPC || chara.IsRemotePlayer || PendingUid.IsPending(chara.uid) || uids.Contains(chara.uid)) {
                continue;
            }

            var count = _missingLeftOverCharas.GetValueOrDefault(chara.uid) + 1;
            if (count < 2) {
                missing[chara.uid] = count;
                continue;
            }

            leftovers ??= [];
            leftovers.Add(chara);
        }

        _missingLeftOverCharas = missing;

        if (leftovers is null) {
            return;
        }

        foreach (var chara in leftovers) {
            _zone.RemoveCard(chara);
        }
    }
}