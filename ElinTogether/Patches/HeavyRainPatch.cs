using System;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Heavy rain on the world map now and then confuses or blinds "the player" (Chara.TickConditions, feat 408
///     protects). Each game rolls it for its own player, but a guest's game adds no condition itself: a guest was
///     never caught (hunt 4 row Rain13). Its game asks the host, as for a trap (CharaAddConditionEvent)
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.TickConditions))]
internal static class HeavyRainPatch
{
    [ThreadStatic]
    private static bool _own;

    /// <summary>
    ///     The local player's conditions are being ticked under heavy rain on a region map
    /// </summary>
    internal static bool InOwnStorm => _own && EClass._zone.IsRegion && EClass.world.weather.CurrentCondition == Weather.Condition.RainHeavy;

    [HarmonyPrefix]
    internal static void OnTick(Chara __instance)
    {
        _own = __instance.IsPC;
    }

    [HarmonyFinalizer]
    internal static void OnTickEnd()
    {
        _own = false;
    }
}
