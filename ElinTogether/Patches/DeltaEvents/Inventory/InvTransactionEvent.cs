using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(InvOwner.Transaction), nameof(InvOwner.Transaction.Process))]
internal static class InvTransactionEvent
{
    [HarmonyPrefix]
    internal static bool OnTransactionProcess(InvOwner.Transaction __instance, bool startTransaction, bool __runOriginal)
    {
        // an earlier prefix took it (the trade window puts a thing on the table, it moves nothing): Harmony still
        // runs the others, and this one asked the host to move the thing away (a drop on "my offer", 2026-10-23)
        if (!__runOriginal) {
            return false;
        }

        if (NetSession.Instance.Connection is not ElinNetClient client || ElinDelta.IsApplying) {
            return true;
        }

        // ability fake card
        if (__instance.thing.trait is TraitAbility) {
            return true;
        }

        // effect windows is fake inv just like Windows 8
        if (__instance.destInv is InvOwnerEffect) {
            return true;
        }

        if (!CardCache.Contains(__instance.thing) && !CardCache.TryAdopt(__instance.thing)) {
            EmpLog.Warning("Refusing transaction of uncached thing {Uid}", __instance.thing.uid);
            return false;
        }

        if (__instance.thing.parent is null) {
            return true;
        }

        // check so replay can be canceled
        InvOwner.Transaction.error = new() {
            card = __instance.thing,
        };
        if (!__instance.IsValid()) {
            return true;
        }

        ThingRequest
            .Create(__instance.thing, __instance.num)
            .Send()
            .Then(thing => {
                // the host gave what was left of the stack: pay for and take that, not what was asked
                if (thing.Num > 0 && thing.Num < __instance.num) {
                    __instance.num = thing.Num;
                }

                __instance.thing = thing;
                __instance.Process(startTransaction);
            }, () => EmpPop.Pop("emp_ui_thing_gone".lang()));

        return false;
    }
}