using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     During a duel the other duellist is an enemy to this game's player, as the opponent of the game's bouts is:
///     a left click attacks it, spells and the bow target it. See <see cref="PlayerDuel.IsFoe" />
/// </summary>
[HarmonyPatch]
internal static class DuelFoePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.IsHostile), new System.Type[0])]
    internal static void OnIsHostile(Chara __instance, ref bool __result)
    {
        if (!__result && IsFoe(__instance)) {
            __result = true;
        }
    }

    /// <summary>
    ///     A harmful spell aimed at one of "the player's" own needs Shift held (shiftToUseNegativeAbilityOnSelf): the
    ///     other duellist is not one of its own while they fight
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Act), nameof(Act.CanPerform), typeof(Chara), typeof(Card), typeof(Point))]
    internal static IEnumerable<CodeInstruction> OnCanPerform(IEnumerable<CodeInstruction> instructions)
    {
        var firstChara = AccessTools.PropertyGetter(typeof(Point), nameof(Point.FirstChara));
        var own = AccessTools.PropertyGetter(typeof(Card), nameof(Card.IsPCFactionOrMinion));
        var list = instructions.ToList();
        var from = list.FindIndex(c => c.Calls(firstChara));
        var at = from < 0 ? -1 : list.FindIndex(from, c => c.Calls(own));
        if (at >= 0 && at - from < 10) {
            list[at] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DuelFoePatch), nameof(IsOwn))) {
                labels = list[at].labels,
            };
        } else {
            EmpLog.Warning("Duel: the Shift rule for harmful spells on one's own was not found in Act.CanPerform");
        }

        return list;
    }

    /// <summary>
    ///     A blow between the two duellists (or two players of different teams, TeamPvp): not a blow at one of one's own ("frowns at you" after every hit)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.DoHostileAction))]
    internal static bool OnHostileAction(Chara __instance, Card _tg)
    {
        if (_tg is not Chara target || !AreFoes(__instance, target)) {
            return true;
        }

        if (target.IsPC) {
            EClass.pc.combatCount = 10;
        }

        return false;
    }

    // (the first one on the cell: when both duellists stand on one cell, it can be this game's player itself, so the
    // target of the spell decides)
    internal static bool IsOwn(Card card)
    {
        return card.IsPCFactionOrMinion && !(card is Chara chara && IsFoe(chara)) &&
               !(Act.TC is Chara target && IsFoe(target));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.IsFriendOrAbove), new System.Type[0])]
    internal static void OnIsFriend(Chara __instance, ref bool __result)
    {
        if (__result && IsFoe(__instance)) {
            __result = false;
        }
    }

    /// <summary>
    ///     Between the two duellists, asked one about the other: what bolts, balls and breaths ask before they spare a
    ///     friend (Control Magic) or hurt no friend at all. In the host's game the guest is of its party: the host's
    ///     spells went around the guest (duel_suite D8, 2026-10-23)
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.IsFriendOrAbove), typeof(Chara))]
    internal static void OnIsFriendOf(Chara __instance, Chara c, ref bool __result)
    {
        if (__result && AreFoes(__instance, c)) {
            __result = false;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.IsHostile), typeof(Chara))]
    internal static void OnIsHostileTo(Chara __instance, Chara c, ref bool __result)
    {
        if (!__result && AreFoes(__instance, c)) {
            __result = true;
        }
    }

    private static bool AreFoes(Chara one, Chara? other)
    {
        // (any two duellists: on the host, two guests' blows and spells are played there)
        // (and two players of different teams where they fight, TeamPvp)
        return other is not null && (PlayerDuel.AreDueling(one.uid, other.uid) || TeamPvp.AreFoes(one, other));
    }

    /// <summary>
    ///     An enemy to this game's player: the other duellist, a player of another team (TeamPvp)
    /// </summary>
    private static bool IsFoe(Chara chara)
    {
        return PlayerDuel.IsFoe(chara) || (EClass.pc is { } pc && TeamPvp.AreFoes(pc, chara));
    }
}
