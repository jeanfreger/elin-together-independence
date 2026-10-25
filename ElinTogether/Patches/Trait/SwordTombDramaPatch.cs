using System;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Lighting the last sword at Where Winds Rest opens the swordkeeper scene. The light is replayed in every game
///     (the sword's state), and so was the scene: a guest's sword opened it on the host's screen too (hunt 2 row 21).
///     Only the game of the player who lit it opens it; its outcome reaches the others (StoryOutcomePatch)
/// </summary>
[HarmonyPatch]
internal static class SwordTombDramaPatch
{
    [ThreadStatic]
    private static Chara? _user;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitDaggerGrave), nameof(TraitDaggerGrave.OnUse), typeof(Chara))]
    internal static void OnUse(Chara c)
    {
        _user = c;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(TraitDaggerGrave), nameof(TraitDaggerGrave.OnUse), typeof(Chara))]
    internal static void OnUseEnd()
    {
        _user = null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerDrama), nameof(LayerDrama.Activate))]
    internal static bool OnActivate(string idStep, ref LayerDrama? __result)
    {
        if (_user is not { IsPC: false } || idStep != "swordkeeper" || !NetSession.Instance.HasActiveConnection) {
            return true;
        }

        __result = null;
        return false;
    }
}
