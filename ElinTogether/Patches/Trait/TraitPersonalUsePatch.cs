using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A guest's use of a tape, of a key-item good or of a license is run by the host and played again on every game (see
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
        return OnlyUseUp(__instance, c, ref __result);
    }

    // the key item is its user's: its own game grants it (sound, message), the flags travel from there
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitGainPrecious), nameof(TraitGainPrecious.OnUse))]
    [HarmonyPatch(typeof(TraitGoodsCoin), nameof(TraitGoodsCoin.OnUse))]
    internal static bool OnKeyGoods(Trait __instance, Chara c, ref bool __result)
    {
        return OnlyUseUp(__instance, c, ref __result);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitLicense), nameof(TraitLicense.OnRead))]
    internal static bool OnLicense(TraitLicense __instance, Chara c)
    {
        if (!OtherPlayersUse(c)) {
            return true;
        }

        __instance.owner.ModNum(-1);
        return false;
    }

    private static bool OnlyUseUp(Trait trait, Chara c, ref bool result)
    {
        if (!OtherPlayersUse(c)) {
            return true;
        }

        trait.owner.ModNum(-1);
        result = true;
        return false;
    }
}
