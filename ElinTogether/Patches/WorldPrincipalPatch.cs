using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using Newtonsoft.Json;

namespace ElinTogether.Patches;

/// <summary>
///     See <see cref="WorldPrincipalDelta" />. The window opens for everyone (to read); closing it applies the
///     changes, kept only in the host's game
/// </summary>
[HarmonyPatch(typeof(LayerWorldSetting), nameof(LayerWorldSetting.Apply))]
internal static class WorldPrincipalPatch
{
    [HarmonyPrefix]
    internal static bool OnApply(LayerWorldSetting __instance)
    {
        if (__instance.IsEmbark || NetSession.Instance.Connection is not ElinNetClient) {
            return true;
        }

        if (JsonConvert.SerializeObject(__instance.pp) != JsonConvert.SerializeObject(EClass.game.principal)) {
            EmpPop.Pop("emp_world_setting_host".lang());
        }

        return false;
    }

    [HarmonyPostfix]
    internal static void OnApplied(LayerWorldSetting __instance)
    {
        if (!__instance.IsEmbark && NetSession.Instance.Connection is ElinNetHost host) {
            host.Delta.AddRemote(new WorldPrincipalDelta { Json = JsonConvert.SerializeObject(EClass.game.principal) });
        }
    }
}
