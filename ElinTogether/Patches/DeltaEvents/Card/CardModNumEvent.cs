using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Card), nameof(Card.ModNum))]
internal static class CardModNumEvent
{
    [HarmonyPrefix]
    internal static bool OnCardModNum(Card __instance, ref int a)
    {
        if (CardModNumDelta.IsOverwriting || !__instance.IsResolved) {
            return true;
        }

        // the food a world-map auto-eat eats: sent by AutoEatPatch once the game is done with it
        AutoEatPatch.NoteSpent(__instance, a);

        // a dialog's payment (a recruit cost): the host takes it if a recruit follows, see CharaMakeAllyRequestDelta
        if (a < 0 && !ElinDelta.IsApplying && LayerDrama.IsActive()) {
            CharaMakeAllyRequestDelta.NotePayment(__instance, -a);
        }

        a = 0;
        return false;
    }

    [HarmonyPostfix]
    internal static void OnCardModNumEnd(Card __instance, int a)
    {
        if (NetSession.Instance.Connection is not { } connection || a == 0 || CardModNumDelta.IsOverwriting) {
            return;
        }

        if (!CardCache.Contains(__instance)) {
            return;
        }

        if (connection.IsClient && PendingUid.IsPending(__instance.uid)) {
            return;
        }

        var delta = new CardModNumDelta {
            Card = __instance,
            Num = __instance.Num,
        };

        if (CharaProgressCompleteEvent.ShouldPack(false)) {
            CharaProgressCompleteEvent.Pack(delta);
            return;
        }

        connection.Delta.AddRemote(delta);
    }
}