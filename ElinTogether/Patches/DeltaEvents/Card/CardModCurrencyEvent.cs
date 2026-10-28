using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal static class CardModCurrencyEvent
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Card), nameof(Card.ModCurrency))]
    internal static void OnModCurrency(Card __instance, int a, string id)
    {
        if (a == 0) {
            return;
        }

        if (ElinDelta.IsRemoteStateLanding || !__instance.IsPC) {
            return;
        }

        if (NetSession.Instance.Connection is not ElinNetClient client) {
            // away on its own map (or hosting one): there is no session to sync through, but what it pays is still
            // told to the host, which checks the debt, the land, the investment it pays against it (a real payment
            // was refused: gold gone, debt unchanged)
            if (a < 0) {
                QuestAwaySync.Send(new CardModCurrencyDelta { Amount = a, CurrencyId = id, Purpose = _purpose });
            }

            return;
        }

        client.Delta.AddRemote(new CardModCurrencyDelta {
            Amount = a,
            CurrencyId = id,
            Purpose = a < 0 ? _purpose : null,
        });
    }

    // what the payment being made is for, said by the game's own steps below
    private static string? _purpose;

    /// <summary>
    ///     A shop's goods and a build's tiles are paid by the same ModCurrency as a debt: marked, so that the host does
    ///     not count the same gold twice (once as goods, once as a debt, a plot or an investment)
    /// </summary>
    [HarmonyPatch]
    internal static class PurposePatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InvOwner.Transaction), nameof(InvOwner.Transaction.Process));
            yield return AccessTools.Method(typeof(HitSummary), nameof(HitSummary.Execute));
        }

        [HarmonyPrefix]
        private static void Begin(System.Reflection.MethodBase __originalMethod, out string? __state)
        {
            __state = _purpose;
            _purpose = __originalMethod.DeclaringType == typeof(HitSummary) ? "build" : "shop";
        }

        [HarmonyFinalizer]
        private static void End(string? __state)
        {
            _purpose = __state;
        }
    }
}
