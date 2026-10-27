using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Where the game's bout with an adventurer starts, ends and pays, see <see cref="AdventurerBout" />
/// </summary>
[HarmonyPatch]
internal static class AdventurerBoutPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpatialGen), nameof(SpatialGen.CreateInstance))]
    internal static void OnCreateInstance(Zone __result)
    {
        if (__result is not null) {
            AdventurerBout.OnCreated(__result);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.ShowDialog), typeof(string), typeof(string), typeof(string))]
    internal static void OnShowDialog(Chara __instance, string step)
    {
        AdventurerBout.OnDialog(__instance, step);
    }

    /// <summary>
    ///     A guest back in town: the host gives the reward (from its own adventurer). The host's own bout: everyone
    ///     learns the adventurer was beaten
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ZonePreEnterBoutWin), nameof(ZonePreEnterBoutWin.Execute))]
    internal static bool OnBoutWin()
    {
        return !AdventurerBout.SkipsReward;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ZonePreEnterBoutWin), nameof(ZonePreEnterBoutWin.Execute))]
    internal static void OnBoutWon(ZonePreEnterBoutWin __instance, bool __runOriginal)
    {
        if (__runOriginal && __instance.target is { } target && NetSession.Instance.Connection is ElinNetHost) {
            AdventurerBout.TellMarks(target.uid);
        }
    }
}
