using System.Collections.Generic;
using System.Reflection;
using ElinTogether.Models;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     What changes the terrain of a cell without any Map.Set*: marked here so that its state is told too, see
///     <see cref="TileStateDelta" />. A game that does not simulate the map never grows its plants (its date does
///     not advance by itself), so nothing there repeats these <br />
///     Not marked: isWatered, written by the watering tasks, the rain and the daily reset; it only dries or wets
///     the look of the soil (it is told with a cell marked for another reason)
/// </summary>
[HarmonyPatch]
internal static class TileStateDirectEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return [
            // the roof mined by hand: _roofBlock = 0, and the mark of a tree that was harvested
            AccessTools.Method(typeof(Map), nameof(Map.MineBlock)),
            AccessTools.Method(typeof(Map), nameof(Map.MineObj)),
        ];
    }

    [HarmonyPostfix]
    internal static void OnMined(Map __instance, Point point)
    {
        if (point.IsValid) {
            TileStateDelta.Mark(__instance, point.x, point.z);
        }
    }
}

/// <summary>
///     The R key (Cell.RotateAll) turns the block, the floor or the object of a cell by hand
/// </summary>
[HarmonyPatch]
internal static class TileStateRotateEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return [
            AccessTools.Method(typeof(Cell), nameof(Cell.RotateBlock)),
            AccessTools.Method(typeof(Cell), nameof(Cell.RotateFloor)),
            AccessTools.Method(typeof(Cell), nameof(Cell.RotateObj)),
        ];
    }

    [HarmonyPostfix]
    internal static void OnRotate(Cell __instance)
    {
        TileStateDelta.Mark(EClass._map, __instance.x, __instance.z);
    }
}

/// <summary>
///     Growing, a new stage, a harvest: the plant is the static GrowSystem.cell, which a call inside may move to
///     another cell, so it is read before
/// </summary>
[HarmonyPatch]
internal static class TileStateGrowEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return [
            AccessTools.Method(typeof(GrowSystem), nameof(GrowSystem.Grow)),
            AccessTools.Method(typeof(GrowSystem), nameof(GrowSystem.SetStage)),
            AccessTools.Method(typeof(GrowSystem), nameof(GrowSystem.Harvest)),
        ];
    }

    [HarmonyPrefix]
    internal static void OnBefore(out Cell? __state)
    {
        __state = GrowSystem.cell;
    }

    [HarmonyPostfix]
    internal static void OnGrown(Cell? __state)
    {
        if (__state is not null) {
            TileStateDelta.Mark(EClass._map, __state.x, __state.z);
        }
    }
}

/// <summary>
///     Pouring water, drawing it, plowing: the floor or the bridge is written on every cell of the square the
///     tool reaches (Evalue 770), inside the progress of the task
/// </summary>
[HarmonyPatch(typeof(Progress_Custom), nameof(Progress_Custom.OnProgressComplete))]
internal static class TileStateTaskEvent
{
    [HarmonyPostfix]
    internal static void OnDone(Progress_Custom __instance)
    {
        if (__instance.parent is not (TaskPlow or TaskPourWater or TaskDrawWater) || __instance.parent is not TaskPoint task) {
            return;
        }

        // as the three tasks work out their square
        var power = __instance.owner?.Tool?.Evalue(770) ?? 0;
        EClass._map.ForeachSquare(task.pos.x, task.pos.z, power <= 0 ? 0 : 1 + power / 10,
            p => TileStateDelta.Mark(EClass._map, p.x, p.z));
    }
}

/// <summary>
///     A seed that sprouts, a fertilizer on bare soil, a plant that is given up: the plant data of the cell
///     (<see cref="PlantData" />, the level of the seed) is told with the cell, see <see cref="TileStateDelta" />
/// </summary>
[HarmonyPatch]
internal static class TileStatePlantEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return [
            AccessTools.Method(typeof(Map), nameof(Map.AddPlant)),
            AccessTools.Method(typeof(Map), nameof(Map.RemovePlant)),
        ];
    }

    [HarmonyPostfix]
    internal static void OnPlant(Map __instance, Point pos)
    {
        if (pos.IsValid) {
            TileStateDelta.Mark(__instance, pos.x, pos.z);
        }
    }
}

/// <summary>
///     Fire burning a floor or a bridge (Map.Burn writes _floor / _bridge = 49 without a setter), and what lies on a
///     cell: fire, puddles (cell.effect), blood and stains (cell.decal). Marked so that <see cref="TileStateDelta" />
///     tells them
/// </summary>
[HarmonyPatch]
internal static class TileStateEffectEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return [
            AccessTools.Method(typeof(Map), nameof(Map.Burn)),
            AccessTools.Method(typeof(Map), nameof(Map.ModFire)),
            AccessTools.Method(typeof(Map), nameof(Map.ModLiquid)),
            AccessTools.Method(typeof(Map), nameof(Map.SetEffect)),
            AccessTools.Method(typeof(Map), nameof(Map.SetLiquid), [typeof(int), typeof(int), typeof(CellEffect)]),
            AccessTools.Method(typeof(Map), nameof(Map.SetLiquid), [typeof(int), typeof(int), typeof(int), typeof(int)]),
            AccessTools.Method(typeof(Map), nameof(Map.AddDecal)),
            AccessTools.Method(typeof(Map), nameof(Map.SetDecal)),
        ];
    }

    [HarmonyPostfix]
    internal static void OnXz(Map __instance, int x, int z)
    {
        if (x >= 0 && z >= 0 && x < __instance.Size && z < __instance.Size) {
            TileStateDelta.Mark(__instance, x, z);
        }
    }
}
