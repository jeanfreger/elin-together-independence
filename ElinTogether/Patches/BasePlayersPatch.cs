using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The base (FactionBranch) simulation reads "the player" as the local one only: raids waited for the host to be
///     awake, the lucky month used the host's god, the days spent with the player counted the host's zone. Every
///     player on the base counts; alone, the local player is the only one and nothing changes
/// </summary>
[HarmonyPatch]
internal static class BasePlayersPatch
{
    // (host checkbox "players at base", NetSessionRules.PlayersAtBase: off, the game's own "the player")
    private static bool IsMultiplayer => NetSession.Instance is { Transport: not null, Rules.PlayersAtBase: true };

    // the other players standing on the map this game runs
    private static IEnumerable<Chara> OthersOnMap(Chara pc)
    {
        return IsMultiplayer && EClass._map?.charas is { } charas
            ? charas.Where(c => c != pc && c.IsPlayer && !c.isDead)
            : [];
    }

    /// <summary>
    ///     Raids are held back while "the player" sleeps (<c>!pc.IsDeadOrSleeping</c>): a sleeper is protected while
    ///     another player is awake on the base, and the host asleep no longer shields the awake ones
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.OnSimulateHour))]
    internal static IEnumerable<CodeInstruction> OnSimulateHour(IEnumerable<CodeInstruction> instructions)
    {
        var isPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
        var list = instructions.ToList();
        for (var i = 0; i < list.Count - 1; i++) {
            if (list[i].Calls(isPc) && list[i + 1].operand is System.Reflection.MethodInfo { Name: "get_IsDeadOrSleeping" }) {
                list[i + 1] = CodeInstruction.Call(typeof(BasePlayersPatch), nameof(AllAsleep)).MoveLabelsFrom(list[i + 1]);
                break;
            }
        }

        return list;
    }

    internal static bool AllAsleep(Chara pc)
    {
        // (a server's stand-in never sleeps: the players on the base decide)
        return (pc.IsDeadOrSleeping || IsMultiplayer && ServerAvatar.Is(pc)) && OthersOnMap(pc).All(c => c.IsDeadOrSleeping);
    }

    /// <summary>
    ///     The first of the month rolls the luck of the base by the god of "the player": the Luck god of any player on
    ///     the base counts (the bonus is the base's, not one player's)
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.OnSimulateDay))]
    internal static IEnumerable<CodeInstruction> OnSimulateDay(IEnumerable<CodeInstruction> instructions)
    {
        var isPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
        var list = instructions.ToList();
        for (var i = 0; i < list.Count - 1; i++) {
            if (list[i].Calls(isPc) && list[i + 1].operand is System.Reflection.MethodInfo { Name: "get_faith" }) {
                list[i + 1] = CodeInstruction.Call(typeof(BasePlayersPatch), nameof(FaithForLuck)).MoveLabelsFrom(list[i + 1]);
                break;
            }
        }

        return list;
    }

    internal static Religion FaithForLuck(Chara pc)
    {
        var luck = EClass.game.religions.Luck;
        return pc.faith != luck && OthersOnMap(pc).Any(c => c.faith == luck) ? luck : pc.faith;
    }

    /// <summary>
    ///     A resident spends the day "with the player" in the zone of the player or in its party. Counted too: the
    ///     zone and party of every other player (only the world keeper runs this, so no day is counted twice)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.OnAdvanceDay))]
    internal static void OnAdvanceDay(FactionBranch __instance, out Dictionary<Chara, int>? __state)
    {
        __state = null;
        if (!IsMultiplayer || NetSession.Instance.Connection is not ElinNetHost host || EClass.pc is not { } pc) {
            return;
        }

        var others = host.ActiveRemoteCharas.Values.Where(c => !c.isDead).ToList();
        // lands owned by teams: only the days with a player of the land's own team count (the game's own count for
        // the host's zone and party is taken back after it, see OnAdvancedDay)
        var team = Teams.BasesOwned ? Teams.TeamOfZone(__instance.owner) : Teams.Wanderers;
        if (team != Teams.Wanderers) {
            __state = __instance.members.ToDictionary(m => m, m => m.c_daysWithPC);
            others = others.Where(o => Teams.TeamOf(o.uid) == team).ToList();
        }

        foreach (var member in __instance.members) {
            if (member.IsPC || member.isDead || member.IsPCParty || member.currentZone == pc.currentZone) {
                continue;
            }

            // (a player holding the map alone is no longer among the host's remote characters: its map counts too,
            // worldmove_suite DAYS 2026-10-23)
            if (others.Any(o => member.currentZone == o.currentZone) || team == Teams.Wanderers && member.currentZone is { } zone && host.IsLeased(zone.uid) || CompanionHelper.OwnerOf(member) is { } owner && others.Contains(owner)) {
                member.c_daysWithPC++;
            }
        }
    }

    /// <summary>
    ///     ...the game counted the host's zone and party whoever the host is: not when the host is of another team
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.OnAdvanceDay))]
    internal static void OnAdvancedDay(FactionBranch __instance, Dictionary<Chara, int>? __state)
    {
        if (__state is null || EClass.pc is not { } pc || Teams.TeamOf(pc.uid) == Teams.TeamOfZone(__instance.owner)) {
            return;
        }

        // (the members the host's zone or party explains; the other players' days were added by the prefix, on top
        // of the game's, so only a day the host alone explains is taken back)
        var others = NetSession.Instance.Connection is ElinNetHost host
            ? host.ActiveRemoteCharas.Values.Where(c => !c.isDead && Teams.TeamOf(c.uid) == Teams.TeamOfZone(__instance.owner)).ToList()
            : [];
        foreach (var (member, days) in __state) {
            var withTeam = others.Any(o => member.currentZone == o.currentZone) ||
                           CompanionHelper.OwnerOf(member) is { } owner && others.Contains(owner);
            if (!withTeam && member.c_daysWithPC > days) {
                member.c_daysWithPC = days;
            }
        }
    }
}
