using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper.Extensions;
using ElinTogether.Models;

namespace ElinTogether.Helper;

/// <summary>
///     The base of a zone a player holds alone (zone lease): its game simulates it there (research, expeditions,
///     happiness, daily resources, meetings, the lucky month, statistics, recruits, policies, the hearth skills, the
///     name), and none of it was told to the host. The branch goes whole with the lease both ways: the host's in the
///     grant (the player starts from the current base), the player's in each checkpoint and in the release (the host
///     takes it, when that player may manage that land, see ElinNetHost.ManagesLeasedBase)
/// </summary>
internal static class LeasedBase
{
    // longer than any name box of the game takes, as NameDelta
    private const int MaxName = 64;

    internal static LZ4Bytes? Pack(Zone zone)
    {
        return zone.branch is { } branch ? LZ4Bytes.Create(branch) : null;
    }

    /// <summary>
    ///     The hearth skills and land feats (the zone's elements, which the branch reads): base values
    /// </summary>
    internal static Dictionary<int, int>? Elements(Zone zone)
    {
        return zone.branch is null ? null : zone.elements.dict.ToDictionary(p => p.Key, p => p.Value.vBase);
    }

    internal static string? Name(Zone zone)
    {
        return zone.branch is null ? null : zone.name;
    }

    /// <summary>
    ///     What the base earned and did not pay yet (shop, inn, tourism): a player alone does not pay it to itself
    ///     (BaseIncome), the host pays it to the base's owners when it comes. Taken: it is paid once
    /// </summary>
    internal static int[]? TakeIncome(Zone zone)
    {
        if (zone.branch is not { } branch || branch.incomeShop == 0 && branch.incomeInn == 0 && branch.incomeTourism == 0) {
            return null;
        }

        int[] income = [branch.incomeShop, branch.incomeInn, branch.incomeTourism];
        branch.incomeShop = branch.incomeInn = branch.incomeTourism = 0;
        return income;
    }

    /// <summary>
    ///     Host side: the day's income of that base, paid now as the game pays it (to its owners, see BaseIncome)
    /// </summary>
    internal static void PayIncome(Zone zone, int[]? income)
    {
        if (income is not { Length: >= 3 } || zone.branch is not { } branch || income.Any(n => n < 0)) {
            return;
        }

        var kept = (branch.incomeShop, branch.incomeInn, branch.incomeTourism);
        branch.incomeShop = income[0];
        branch.incomeInn = income[1];
        branch.incomeTourism = income[2];
        branch.GetDailyIncome();
        (branch.incomeShop, branch.incomeInn, branch.incomeTourism) = kept;
    }

    /// <summary>
    ///     That copy of the base replaces this game's, as Zone.OnLoad sets a loaded one up (SetOwner lists its
    ///     residents again). Only a base this game has too: a land is never made or lost this way
    /// </summary>
    internal static void Apply(Zone zone, LZ4Bytes? data, Dictionary<int, int>? elements, string? name)
    {
        if (zone.branch is not { } old) {
            return;
        }

        if (data is not null) {
            var fresh = data.Decompress<FactionBranch>();

            // the residents count again for the faction through the new copy (SetOwner)
            foreach (var member in old.members) {
                EClass.pc.faction.charaElements.OnRemoveMember(member);
            }

            // the god of the base is one of the world's, not a copy of it
            fresh.faith = EClass.game.religions.Find(fresh.faith?.id ?? "") ?? EClass.game.religions.Eyth;
            fresh.stash ??= old.stash;
            zone.branch = fresh;
            fresh.SetOwner(zone);

            if (fresh.stash is { } stash) {
                foreach (var thing in stash.things.Flatten().ToList()) {
                    if (PendingUid.IsPending(thing.uid)) {
                        EClass.game.cards.AssignUID(thing);
                    }
                }
            }
        }

        if (elements is not null) {
            foreach (var id in zone.elements.dict.Keys.Union(elements.Keys).ToList()) {
                var current = zone.elements.dict.TryGetValue(id, out var element) ? element.vBase : 0;
                var value = elements.GetValueOrDefault(id);
                if (current != value && EClass.sources.elements.map.ContainsKey(id)) {
                    zone.elements.ModBase(id, value - current);
                }
            }
        }

        if (!string.IsNullOrEmpty(name) && name!.Length <= MaxName && !name.Any(char.IsControl)) {
            zone.name = name;
        }
    }
}
