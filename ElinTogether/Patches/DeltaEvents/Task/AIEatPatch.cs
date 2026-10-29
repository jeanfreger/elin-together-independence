using System.Runtime.CompilerServices;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A player who starts eating while full keeps the food, as the local player does. Only its own game knows it
///     is full: it says so with the task, see AIEatArgs.Full
/// </summary>
[HarmonyPatch]
internal static class AIEatPatch
{
    private static readonly ConditionalWeakTable<AI_Eat, object> _full = new();

    internal static void MarkFull(AI_Eat eat)
    {
        _full.Remove(eat);
        _full.Add(eat, _full);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
    internal static bool OnFoodEffect(Chara c, out ScopeExit? __state)
    {
        __state = null;
        if (NetSession.Instance.Connection is not ElinNetHost || !c.IsRemotePlayer) {
            return true;
        }

        // poison and sins cost karma to the eater's own game, not to the host's player
        __state = PlayerKarma.Skip();

        for (var ai = c.ai.Current; ai is not null; ai = ai.parent) {
            if (ai is AI_Eat eat) {
                return !_full.TryGetValue(eat, out _);
            }
        }

        return true;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
    internal static void OnFoodEffectEnd(ScopeExit? __state)
    {
        __state?.Dispose();
    }
}
