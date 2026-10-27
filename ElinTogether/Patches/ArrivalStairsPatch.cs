using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

/// <summary>
///     Arrived on a map the host handed over, a player lands in front of the stairs it came by: a walk still running
///     from the other map stepped onto them and took it back at once (GitHub issue 10, leaving the Vernis mine). The
///     stairs do not take a player in by a step in the first second after such an arrival; a click still does
/// </summary>
[HarmonyPatch(typeof(TraitNewZone), nameof(TraitNewZone.OnStepped))]
internal static class ArrivalStairsPatch
{
    private const float Settle = 1.5f;

    [HarmonyPrefix]
    internal static bool OnStepped(Chara c)
    {
        if (!c.IsPC || Time.realtimeSinceStartup - ElinNetClient.ArrivedAt > Settle) {
            return true;
        }

        EmpLog.Debug("Stairs not taken by a step right after arriving on a leased map");
        c.ai?.Cancel();
        return false;
    }
}
