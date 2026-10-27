using System.Diagnostics;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     How long the world save holds the main thread, see NetMetrics. A save a guest does not make is not counted
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.Save))]
internal class SaveMetrics
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    internal static void Before(out long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    internal static void After(long __state, bool __runOriginal)
    {
        if (__runOriginal && __state != 0) {
            NetMetrics.GameSave.Add(0, __state);
        }
    }
}
