using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

/// <summary>
///     The game counts "the player"'s skills by their base for its maximum stamina, anyone else's with their bonuses:
///     the host's copy of a guest had another maximum than the guest's own game (66 against 27 for a new character,
///     hunt 2 row 37). Counted for another player as for the player
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.CalculateMaxStamina))]
internal static class RemoteMaxStaminaPatch
{
    [HarmonyPostfix]
    internal static void OnCalculateMaxStamina(Chara __instance)
    {
        if (NetSession.Instance.Connection is not ElinNetHost || __instance.IsPC || !__instance.IsRemotePlayer) {
            return;
        }

        // (Chara.CalculateMaxStamina, the player's branch)
        var num = __instance.END;
        foreach (var value in __instance.elements.dict.Values) {
            if (value.source.category == "skill") {
                num += Mathf.Max(value.vBase, 0);
            }
        }

        __instance._maxStamina = Mathf.Max(EClass.curve(num, 30, 10, 60), 10) + 15;
    }
}
