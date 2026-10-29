using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     PLAN_hub_features D, host box "Leaderboards": the host ranks players (level, deepest floor, duels won) and teams
///     (wealth). Everything is read on the host from its own copies: the level of the player characters, a record of
///     the deepest floor and the duels won kept in the world save (<see cref="ElinNetHost.LeaderStats" />), the team
///     bank and lands. A guest's own stats are never read. The boards are worked out at most every 30 seconds, when a
///     player asks; each player, the host's own too, asks and is answered by the same functions
/// </summary>
[HarmonyPatch]
internal static class Leaderboards
{
    internal const int Level = 0;
    internal const int Deepest = 1;
    internal const int Duels = 2;
    internal const int Wealth = 3;
    internal const int BoardCount = 4;
    internal const int Top = 10;

    // what a land is worth in the wealth score, next to the gold of the team bank
    private const long LandWorth = 10000;
    private const float CacheSeconds = 30f;
    private const float AskSeconds = 2f;

    // stats row: [deepest floor, duels won]
    private const int DeepestAt = 0;
    private const int DuelsAt = 1;

    internal static bool On => NetSession.Instance is { InGame: true, Rules.Leaderboards: true };

    // ---------------------------------------------------------------- host: records

    private static int[] StatsOf(int charaUid)
    {
        var book = ElinNetHost.LeaderStats;
        if (!book.TryGetValue(charaUid, out var row) || row.Length < 2) {
            book[charaUid] = row = new int[2];
        }

        return row;
    }

    private static bool KeepsBook => NetSession.Instance.Transport is ElinNetHost;

    // a player character reaches a floor: the host notes the deepest it has been
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.MoveZone), typeof(Zone), typeof(ZoneTransition))]
    private static void OnMoveZone(Chara __instance, Zone z)
    {
        if (!KeepsBook || !On || z is null || !__instance.IsPlayer || z.DangerLv <= 0) {
            return;
        }

        var row = StatsOf(__instance.uid);
        row[DeepestAt] = Math.Max(row[DeepestAt], z.DangerLv);
    }

    // the duel box settled a win (winnerUid is only given then)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerDuel), "Tell")]
    private static void OnDuelWon(int winnerUid)
    {
        if (winnerUid > 0 && KeepsBook && On) {
            StatsOf(winnerUid)[DuelsAt]++;
        }
    }

    // ---------------------------------------------------------------- host: boards

    private sealed class Entry
    {
        internal int Uid;
        internal string Name = "";
        internal long Score;
    }

    private static List<Entry>[]? _boards;
    private static float _computedAt = float.MinValue;
    private static Game? _game;
    private static readonly Dictionary<int, float> _asked = [];

    private static IEnumerable<int> Roster(ElinNetHost host)
    {
        var uids = new HashSet<int>(ElinNetHost.LeaderStats.Keys);
        foreach (var player in NetSession.Instance.CurrentPlayers) {
            uids.Add(player.CharaUid);
        }

        foreach (var chara in host.ActiveRemoteCharas.Values) {
            uids.Add(chara.uid);
        }

        if (EClass.pc is { } pc) {
            uids.Add(pc.uid);
        }

        foreach (var id in ElinNetHost.TeamList.Keys) {
            uids.UnionWith(Teams.MembersOf(id));
        }

        uids.Remove(0);
        return uids;
    }

    private static List<Entry>[] Compute(ElinNetHost host)
    {
        if (_boards is not null && ReferenceEquals(_game, EClass.game) && Time.realtimeSinceStartup - _computedAt < CacheSeconds) {
            return _boards;
        }

        var boards = new List<Entry>[BoardCount];
        for (var i = 0; i < BoardCount; i++) {
            boards[i] = [];
        }

        foreach (var uid in Roster(host)) {
            var chara = Teams.FindChara(uid);
            if (chara is null) {
                continue;
            }

            var row = StatsOf(uid);
            // (a floor the record missed: the one the character stands on)
            if (chara.currentZone is { DangerLv: > 0 } zone) {
                row[DeepestAt] = Math.Max(row[DeepestAt], zone.DangerLv);
            }

            var name = Teams.PlayerName(uid);
            boards[Level].Add(new() { Uid = uid, Name = name, Score = chara.LV });
            boards[Deepest].Add(new() { Uid = uid, Name = name, Score = row[DeepestAt] });
            boards[Duels].Add(new() { Uid = uid, Name = name, Score = row[DuelsAt] });
        }

        foreach (var team in ElinNetHost.TeamList.Keys) {
            var gold = TeamBoxes.BankOfTeam(team)?.GetCurrency() ?? 0;
            var lands = Teams.LandsOf(team).Count();
            boards[Wealth].Add(new() { Uid = team, Name = Teams.NameOf(team), Score = gold + lands * LandWorth });
        }

        foreach (var board in boards) {
            board.RemoveAll(e => e.Score <= 0);
            board.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.Uid.CompareTo(b.Uid));
        }

        _boards = boards;
        _game = EClass.game;
        _computedAt = Time.realtimeSinceStartup;
        return boards;
    }

    internal static LeaderboardDelta? Answer(int board, Chara asker, ElinNetHost host)
    {
        if (board is < 0 or >= BoardCount) {
            return null;
        }

        var list = Compute(host)[board];
        var own = board == Wealth ? Teams.TeamOf(asker.uid) : asker.uid;
        var rank = own == 0 || board == Wealth && own == Teams.Wanderers ? 0 : list.FindIndex(e => e.Uid == own) + 1;
        var top = list.Take(Top).ToArray();
        return new() {
            Board = board,
            IsAnswer = true,
            Names = top.Select(e => e.Name).ToArray(),
            Scores = top.Select(e => e.Score).ToArray(),
            Uids = top.Select(e => e.Uid).ToArray(),
            OwnRank = rank,
            OwnScore = rank > 0 ? list[rank - 1].Score : 0,
            Total = list.Count,
        };
    }

    /// <summary>
    ///     A player's request (the host's own player too): answered to that player alone
    /// </summary>
    internal static void Handle(LeaderboardDelta request, Chara from, ElinNetHost host)
    {
        if (!On) {
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (_asked.TryGetValue(from.uid, out var last) && now - last < AskSeconds) {
            return;
        }

        _asked[from.uid] = now;
        if (Answer(request.Board, from, host) is not { } answer) {
            return;
        }

        if (from == EClass.pc) {
            Show(answer);
            return;
        }

        // (also a player away on a leased zone: its chara is the kept one, not in ActiveRemoteCharas)
        foreach (var peerId in host.PeerIds.ToArray()) {
            if (host.PlayerChara(peerId)?.uid == from.uid) {
                host.SendDeltaTo(peerId, answer);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- player side

    private static readonly LeaderboardDelta?[] Latest = new LeaderboardDelta?[BoardCount];

    /// <summary>
    ///     Told when an answer lands (the open page redraws)
    /// </summary>
    internal static Action<int>? Changed { get; set; }

    internal static LeaderboardDelta? Of(int board) => board is >= 0 and < BoardCount ? Latest[board] : null;

    internal static void Show(LeaderboardDelta answer)
    {
        if (answer.Board is < 0 or >= BoardCount) {
            return;
        }

        Latest[answer.Board] = answer;
        Changed?.Invoke(answer.Board);
    }

    /// <summary>
    ///     Ask the host for a board (the tab does, when it opens or the player switches)
    /// </summary>
    internal static void Ask(int board)
    {
        var request = new LeaderboardDelta { Board = board };
        switch (NetSession.Instance.Connection) {
            case ElinNetHost host:
                if (EClass.pc is { } pc) {
                    Handle(request, pc, host);
                }

                break;
            case ElinNetClient client:
                client.Delta.AddRemote(request);
                break;
        }
    }
}
