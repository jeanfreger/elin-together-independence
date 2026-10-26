using System.Collections.Generic;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Card), nameof(Card.TryStackTo))]
internal static class CardTryStackEvent
{
    [HarmonyPrefix]
    internal static bool OnCardTryStackTo(Card __instance, Thing to, ref bool __result)
    {
        if (NetSession.Instance.Connection is not ElinNetClient client) {
            return true;
        }

        if (PendingSplit.Resolve(__instance.uid) == to.uid) {
            return true;
        }

        if (to.IsHostOwned != __instance.IsHostOwned) {
            return false;
        }

        if (!to.IsHostOwned) {
            return true;
        }

        if (!__instance.CanStackTo(to)) {
            return false;
        }

        // state landing from the network stacks here. The player's own take out of a chest, a shop or the bank
        // (resumed inside the answer to its item request) is asked like a pick-up from the ground: the host
        // replays the pick for a character that is not its own, at the root of the bag, and never found the
        // stack of a purse or a sub-bag: the gold a guest took out of the bank was missing in its own game
        if (ElinDelta.IsRemoteStateLanding) {
            return true;
        }

        // the inventory's sort ("always sort" runs it at every refresh) loops until no two stacks merge: a merge
        // asked of the host is not done yet, said done the loop never ended and the game froze (GitHub issue 6).
        // Asked once, not done for now: the host's answer merges them, the next sort finds one stack
        if (InventorySort) {
            if (Asked.Add(__instance.uid)) {
                client.Delta.AddRemote(new CardTryStackToDelta {
                    Card = __instance,
                    To = to,
                    Parent = to.parent as Card,
                });
            }

            __result = false;
            return false;
        }

        __result = true;
        client.Delta.AddRemote(new CardTryStackToDelta {
            Card = __instance,
            To = to,
            Parent = to.parent as Card,
        });

        return false;
    }

    internal static bool InventorySort;

    // things whose merge this sort asked for (one ask per thing and per sort)
    internal static readonly HashSet<int> Asked = [];
}

[HarmonyPatch(typeof(UIInventory), nameof(UIInventory.Sort))]
internal static class InventorySortEvent
{
    [HarmonyPrefix]
    internal static void OnSort()
    {
        CardTryStackEvent.InventorySort = true;
        CardTryStackEvent.Asked.Clear();
    }

    [HarmonyFinalizer]
    internal static void OnSortEnd()
    {
        CardTryStackEvent.InventorySort = false;
    }
}