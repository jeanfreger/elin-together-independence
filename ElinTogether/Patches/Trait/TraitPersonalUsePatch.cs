using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A guest's use of a tape or of a key-item good is run by the host and played again on every game (see
///     CardOnUseDelta). What they give is the user's own (known songs, key items): only the user's game takes it,
///     the other games just use the item up. Host alone and offline: the game's own code
/// </summary>
[HarmonyPatch]
internal static class TraitPersonalUsePatch
{
    private static bool OtherPlayersUse(Chara c)
    {
        return NetSession.Instance.HasActiveConnection && c != EClass.pc;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitTape), nameof(TraitTape.OnUse))]
    internal static bool OnTape(TraitTape __instance, Chara c, ref bool __result)
    {
        if (!OtherPlayersUse(c)) {
            return true;
        }

        __instance.owner.ModNum(-1);
        __result = true;
        return false;
    }
}
