using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The book of secrets gives a feat point to the local player and four to a resident: the host's copy of
///     another player is a resident for the game, and got four. A player gets its one point
/// </summary>
[HarmonyPatch]
internal static class TraitBookSecretPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBookSecret), nameof(TraitBookSecret.OnRead))]
    internal static void OnRead(Chara c, out int __state)
    {
        __state = c.feat;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraitBookSecret), nameof(TraitBookSecret.OnRead))]
    internal static void OnReadEnd(Chara c, int __state)
    {
        if (!c.IsPC && c.IsRemotePlayer && c.feat - __state == 4) {
            c.feat -= 3;
        }
    }
}
