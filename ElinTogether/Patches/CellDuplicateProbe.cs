#if DEBUG
using System.Collections.Generic;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Debug build only: who adds a character to a cell that already lists it (the 100-player run of 2026-10-28 swept
///     about 7500 such doubles, ElinNetHostUpdate.SweepStaleCellEntries). The first few stacks per character are logged
/// </summary>
[HarmonyPatch(typeof(Cell), nameof(Cell.AddCard))]
internal static class CellDuplicateProbe
{
    private static readonly Dictionary<int, int> _told = [];

    [HarmonyPrefix]
    internal static void OnAddCard(Cell __instance, Card c)
    {
        if (!c.isChara || __instance.detail?.charas is not { } list || !list.Contains(c.Chara)) {
            return;
        }

        var told = _told.GetValueOrDefault(c.uid);
        if (told >= 3) {
            return;
        }

        _told[c.uid] = told + 1;
        EmpLog.Warning("Cell probe: chara {Uid} added again to its cell {X},{Z} {StackTrace}",
            c.uid, __instance.x, __instance.z, System.Environment.StackTrace);
    }
}
#endif
