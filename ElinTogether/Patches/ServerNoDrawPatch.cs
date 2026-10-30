using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

/// <summary>
///     A dedicated server started without graphics has no instancing: every frame the map's mesh passes threw
///     "Instancing is not supported" and its log grew by about 30 MB an hour (2026-10-30). Nobody looks at its screen:
///     it draws nothing there
/// </summary>
[HarmonyPatch]
internal static class ServerNoDrawPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MeshPass), nameof(MeshPass.Draw))]
    internal static bool SkipOnServer()
    {
        return !(EmpServer.Requested && !SystemInfo.supportsInstancing);
    }
}
