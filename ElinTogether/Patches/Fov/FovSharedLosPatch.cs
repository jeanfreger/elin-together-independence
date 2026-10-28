using System.Collections.Generic;
using System.Reflection;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal class FovSharedLosPatch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return [
            AccessTools.Method(typeof(Player), nameof(Player.CanSee)),
            AccessTools.Method(typeof(Chara), nameof(Chara.CanSee)),
            AccessTools.Method(typeof(Chara), nameof(Chara.CanSeeLos), [typeof(Card), typeof(int)]),
        ];
    }

    [HarmonyPostfix]
    internal static void OnRemoteLosCheck(object __instance, Card c, ref bool __result)
    {
        // (not the server's stand-in, which nobody sees)
        if (__result || c is not Chara { IsPlayer: true } || ServerAvatar.Is(c)) {
            return;
        }

        // remote charas can see each other
        if (__instance is Chara { IsPlayer: true } or Player) {
            __result = true;
        }
    }
}