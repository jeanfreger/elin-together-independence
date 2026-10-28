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
                QuestAwaySync.Send(new CardModCurrencyDelta { Amount = a, CurrencyId = id });
            }

            return;
        }

        client.Delta.AddRemote(new CardModCurrencyDelta {
            Amount = a,
            CurrencyId = id,
        });
    }
}