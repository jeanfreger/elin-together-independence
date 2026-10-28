using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The world map eats for the party on its own (Chara.InstantEat, with auto eat on): no eating task runs, so no
///     host copy ever spent the food, and a stack the host owns refuses the client's own ModNum (CardModNumEvent). The
///     client says what is left of the stack, as it does for any card it uses up
/// </summary>
[HarmonyPatch]
internal static class AutoEatPatch
{
    private static Thing? _food;
    private static bool _spent;

    internal static void NoteSpent(Card card, int a)
    {
        if (a < 0 && ReferenceEquals(card, _food)) {
            _spent = true;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
    internal static void OnProc(Chara c, Thing food)
    {
        _spent = false;
        _food = null;
        if (NetSession.Instance.Connection is not ElinNetClient || ElinDelta.IsApplying || !food.IsResolved) {
            return;
        }

        // an eating task (the player's own, or a companion's) is replayed by the host, which spends it there
        for (var ai = c.ai.Current; ai is not null; ai = ai.parent) {
            if (ai is AI_Eat) {
                return;
            }
        }

        _food = food;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
    internal static void OnProcEnd()
    {
        var food = _food;
        _food = null;
        if (food is null || !_spent || NetSession.Instance.Connection is not { } connection) {
            return;
        }

        _spent = false;
        var left = food.Num - 1;
        if (left <= 0) {
            // the destroy event tells the host the stack is gone
            food.Destroy();
            return;
        }

        CardModNumDelta.SetNumHere(food, left);
        connection.Delta.AddRemote(new CardModNumDelta {
            Card = food,
            Num = left,
        });
    }
}
