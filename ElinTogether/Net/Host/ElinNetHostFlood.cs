using System;
using System.Collections.Generic;
using System.Diagnostics;
using ElinTogether.Models;

namespace ElinTogether.Net;

/// <summary>
///     Per player rate limit on what its game sends (council 2 on network trust, audit 2026-10-23): a budget per
///     delta type that refills at <see cref="FloodRate" /> a second up to <see cref="FloodBurst" />. Far above real
///     play (a full bag sorted, a level up's skills, a batch held while loading: one batch is at most
///     MaxDeltasPerBatch), far below what stalls the host. Over it, a delta is dropped when the trust checks refuse
///     for real (ElinDelta.TrustEnforced), only logged otherwise. Never a kick
/// </summary>
internal partial class ElinNetHost
{
    internal const double FloodRate = 500;
    internal const double FloodBurst = 2048;

    // and all its kinds together (security audit 2026-10-27: 500 a second for each of some 170 kinds was no limit)
    internal const double TotalRate = 3000;
    internal const double TotalBurst = 8192;
    private const double FloodLogWindow = 10;

    private static readonly Stopwatch FloodClock = Stopwatch.StartNew();
    private readonly Dictionary<(int Peer, Type Type), (double Budget, double At)> _deltaBudget = [];
    private readonly Dictionary<(int Peer, Type Type), (double At, int Count)> _floodLogged = [];

    /// <returns>true when that delta is over its player's budget and is dropped</returns>
    private bool Floods(ElinDelta delta, int peerId)
    {
        var now = FloodClock.Elapsed.TotalSeconds;
        var total = (peerId, typeof(ElinDelta));
        var key = (peerId, delta.GetType());
        var overall = !Spend(total, now, TotalRate, TotalBurst);
        if (!overall && Spend(key, now, FloodRate, FloodBurst)) {
            return false;
        }

        var enforce = ElinDelta.TrustEnforced;
        var (loggedAt, count) = _floodLogged.TryGetValue(key, out var logged) ? logged : (double.MinValue, 0);
        count++;
        if (now - loggedAt >= FloodLogWindow) {
            EmpLog.Warning(enforce
                    ? "Cut a flood of {DeltaType} from peer {Peer}: {Count} over {Rate} a second"
                    : "Would have cut a flood of {DeltaType} from peer {Peer}: {Count} over {Rate} a second",
                key.Item2.Name, peerId, count, overall ? TotalRate : FloodRate);
            _floodLogged[key] = (now, 0);
        } else {
            _floodLogged[key] = (loggedAt, count);
        }

        return enforce;
    }

    // one token of that budget, refilled at its rate up to its burst
    private bool Spend((int Peer, Type Type) key, double now, double rate, double burst)
    {
        var (budget, at) = _deltaBudget.TryGetValue(key, out var held) ? held : (burst, now);
        budget = Math.Min(burst, budget + (now - at) * rate);
        if (budget < 1) {
            _deltaBudget[key] = (budget, now);
            return false;
        }

        _deltaBudget[key] = (budget - 1, now);
        return true;
    }

    /// <summary>
    ///     A player that left: its budgets go with it (an id is given again to the next one)
    /// </summary>
    private void ForgetFlood(int peerId)
    {
        foreach (var key in new List<(int Peer, Type Type)>(_deltaBudget.Keys)) {
            if (key.Peer == peerId) {
                _deltaBudget.Remove(key);
                _floodLogged.Remove(key);
            }
        }
    }
}
