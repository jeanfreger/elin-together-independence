using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using UnityEngine;

namespace ElinTogether.Patches;

/// <summary>
///     Council 2026-10-23, on the host: the hours of the trips on the world map, the host's and those its players
///     report. Two players walking at the same time move the date by the longer walk, not by the sum of both,
///     and the host believes no more hours from a player than travel can make pass
/// </summary>
internal static class WorldTravelClock
{
    /// <summary>
    ///     A step of the game costs 6 minutes per point of 30 to 65 (heavy snow, swamp), some 520 minutes at most
    ///     with the worst feats; an hour of the express travel is 60. More than this from a player is no step
    /// </summary>
    internal const int MaxStepMinutes = 600;

    /// <summary>
    ///     The id of the host itself among the walkers (peer ids start at 0)
    /// </summary>
    internal const int Host = -1;

    // a player who did not step for this long stopped walking: its next step starts a new walk
    private const float WalkGapSeconds = 2f;

    // what a player may report at once (a day), and how fast that refills: the express travel makes an hour
    // pass every 0.035 s, some 1700 minutes a second
    private const float BurstMinutes = Date.DayToken;
    private const float MinutesPerSecond = 1800f;

    // walkers: the hours the others' steps made pass during their walk (theirs already paid), when they last stepped
    private static readonly Dictionary<int, (int Credit, float At)> Walks = [];

    private static readonly Dictionary<int, (float Budget, float At)> Budgets = [];

    /// <summary>
    ///     A step of that player costs this much: how far it moves the date. The part of it another player walking
    ///     at the same time already made pass is not passed twice. A player walking alone pays its whole step
    /// </summary>
    internal static int Step(int who, int minutes)
    {
        var now = Time.realtimeSinceStartup;
        var credit = Walks.TryGetValue(who, out var walk) && now - walk.At < WalkGapSeconds ? walk.Credit : 0;
        var moves = Math.Max(0, minutes - credit);
        Walks[who] = (credit - (minutes - moves), now);

        if (moves > 0) {
            foreach (var other in Walks.Keys.ToList()) {
                var (otherCredit, at) = Walks[other];
                if (other != who && now - at < WalkGapSeconds) {
                    Walks[other] = (Math.Min(Date.DayToken, otherCredit + moves), at);
                }
            }
        }

        return moves;
    }

    // time on a map of its own, not a trip: a night's sleep or a rest jumps hours, nothing there makes days pass in
    // seconds (security audit 2026-10-27, item 10: the express travel's rate was believed from any player anywhere)
    private const float StayMinutesPerSecond = 360f;
    private static readonly Dictionary<int, (float Budget, float At)> StayBudgets = [];

    /// <summary>
    ///     Whether that many minutes may pass at once on the map a player keeps alone (a day, then six hours a
    ///     second); taken from its budget when they may
    /// </summary>
    internal static bool StayFits(int who, int minutes)
    {
        if (minutes <= 0) {
            return true;
        }

        var now = Time.realtimeSinceStartup;
        var budget = StayBudgets.TryGetValue(who, out var known)
            ? Math.Min(BurstMinutes, known.Budget + (now - known.At) * StayMinutesPerSecond)
            : BurstMinutes;
        if (minutes > budget) {
            StayBudgets[who] = (budget, now);
            return false;
        }

        StayBudgets[who] = (budget - minutes, now);
        return true;
    }

    /// <summary>
    ///     How much of the minutes a player reports passing the host believes: a day at once, then what travel
    ///     can make pass in the time since. More is cut and written in the log
    /// </summary>
    internal static int Plausible(int who, int minutes, string what)
    {
        if (minutes <= 0) {
            return 0;
        }

        var now = Time.realtimeSinceStartup;
        var budget = Budgets.TryGetValue(who, out var known)
            ? Math.Min(BurstMinutes, known.Budget + (now - known.At) * MinutesPerSecond)
            : BurstMinutes;
        var granted = (int)Math.Min(minutes, budget);
        Budgets[who] = (budget - granted, now);

        if (granted < minutes) {
            EmpLog.Warning("World time: player {Peer} reported {Minutes} minute(s) of {What}, more than travel " +
                           "makes pass: {Granted} kept", who, minutes, what, granted);
        }

        return granted;
    }
}
