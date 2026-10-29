using System.Reflection;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Whether a harvested seed gains a level, and how many, is rolled with "the player's" Farming. The seed is
///     made by the host at the end of another player's task: it levelled with the host's skill. That player
///     stands in for the making of the seed, nothing more
/// </summary>
[HarmonyPatch]
internal static class TraitSeedPatch
{
    /// <summary>
    ///     Reaping a plant with a sickle: Map.MineObj hands the seed to "the player" (EClass.pc), which on the host is
    ///     the host's own character, not the one who reaped. The whole call stands in for the reaper
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.MineObj))]
    internal static class MineObjStandIn
    {
        [HarmonyPrefix]
        internal static void Before(Task? task, out ScopeExit? __state)
        {
            __state = null;
            if (task is not TaskHarvest || NetSession.Instance.Connection is not ElinNetHost ||
                CharaProgressCompleteEvent.Chara is not { } farmer || !farmer.IsRemotePlayer) {
                return;
            }

            var standIn = RemoteCraft.AsCrafter(farmer);
            __state = new() { OnExit = () => standIn.Dispose() };
        }

        [HarmonyFinalizer]
        internal static void After(ScopeExit? __state)
        {
            __state?.Dispose();
        }
    }

    internal static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(TraitSeed), nameof(TraitSeed.MakeSeed), [typeof(SourceObj.Row), typeof(PlantData)]);
    }

    [HarmonyPrefix]
    internal static void OnMakeSeed(out ScopeExit? __state)
    {
        __state = null;
        if (NetSession.Instance.Connection is not ElinNetHost ||
            CharaProgressCompleteEvent.Chara is not { } farmer || !farmer.IsRemotePlayer) {
            return;
        }

        // "this seed cannot grow further" is for the one who harvests
        var told = MsgRelayContext.RedirectTo(farmer);
        var standIn = RemoteCraft.AsCrafter(farmer);
        __state = new() {
            OnExit = () => {
                standIn.Dispose();
                told.Dispose();
            },
        };
    }

    [HarmonyFinalizer]
    internal static void OnMakeSeedEnd(ScopeExit? __state)
    {
        __state?.Dispose();
    }
}
