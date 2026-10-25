using System.Collections.Generic;
using System.Reflection.Emit;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A crafted dish feeds more when "the player" has the gourmet feat (1650, level 3). A player's meal is played in
///     the host's game, where the player is the host: a guest's meal used the host's feat, and the guest's own did
///     nothing (hunt 4 row F25b). The feat of the player who eats, or whose companion eats
/// </summary>
[HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
internal static class FoodGourmetPatch
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> OnProc(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc))),
                new CodeMatch(i => i.LoadsConstant(1650)))
            .ThrowIfInvalid("FoodEffect.Proc: no EClass.pc.Evalue(1650)")
            // (the labels of the call stay: a branch may land on it)
            .SetAndAdvance(OpCodes.Ldarg_0, null)
            .Insert(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FoodGourmetPatch), nameof(Gourmet))))
            .InstructionEnumeration();
    }

    internal static Chara Gourmet(Chara c)
    {
        return CompanionHelper.OwnerOf(c) ?? EClass.pc;
    }
}
