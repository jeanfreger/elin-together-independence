using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Trait), nameof(Trait.OnBarter))]
internal class OnBarterEvent
{
    [HarmonyPrefix]
    internal static bool OnBarter(Trait __instance, out (OnBarterDelta? delta, ElinDelta.PatchScope scope) __state)
    {
        __state = default;

        if (NetSession.Instance.Connection is not { } connection) {
            return true;
        }

        var owner = __instance.owner;
        if (connection.IsClient && owner.things.Find("chest_merchant") is null) {
            // create a temp chest
            using var _ = PendingContext.Begin();
            var chest = ThingGen.Create("chest_merchant");
            chest.parent = owner;
            owner.things.Add(chest);
        }

        // restock is from host
        __state = (new() {
            ShopOwner = owner,
            Gates = connection.IsClient ? ShopStock.Local() : null,
        }, ElinDelta.PatchScope.Simulate(connection.IsHost));

        return connection.IsHost;
    }

    [HarmonyPostfix]
    internal static void OnBarterEnd(Trait __instance, (OnBarterDelta? delta, ElinDelta.PatchScope scope) __state)
    {
        if (__state.delta is null) {
            return;
        }

        if (NetSession.Instance.IsHost) {
            // the date this open left, restocked or not (issue #40: clients reset it to a full RestockDay)
            __state.delta.StockExpire = __instance.owner.c_dateStockExpire;
        }

        NetSession.Instance.Connection?.Delta.AddRemote(__state.delta);
    }

    [HarmonyFinalizer]
    internal static void OnBarterCleanup((OnBarterDelta? delta, ElinDelta.PatchScope scope) __state)
    {
        __state.scope.Exit();
    }
}

[HarmonyPatch(typeof(TraitVendingMachine), nameof(TraitVendingMachine.OnUse))]
internal static class TraitVendingMachineUseEvent
{
    [HarmonyPrefix]
    internal static bool OnVendingUse(TraitVendingMachine __instance, Chara c, ref bool __result)
    {
        if (NetSession.Instance.Connection is null || !ElinDelta.IsApplying || c.IsPC) {
            return true;
        }

        // replaying remote player use restock only
        __instance.OnBarter();
        __result = false;
        return false;
    }
}