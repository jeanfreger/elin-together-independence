using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     What an investment gives (the zone's, or the shop's) is asked of the host, see <see cref="ZoneInvestDelta" />.
///     In a client's game the dialog pays (that reaches the host) and then raises its own copy of the zone or of the
///     merchant, which the host never sees: it simulates and saves them <br />
///     The two steps of DramaCustomSequence.Build each have a local function Invest(bool quick), which the compiler turns
///     into methods of generated classes: found by name, nothing is patched when the game changes them
/// </summary>
[HarmonyPatch]
internal static class DramaInvestPatch
{
    private static List<MethodBase>? _targets;

    internal readonly record struct InvestState(bool Active, int Money, int Investment, int MerchantInvest);

    private static List<MethodBase> FindTargets()
    {
        return typeof(DramaCustomSequence)
            .GetNestedTypes(AccessTools.all)
            .SelectMany(type => AccessTools.GetDeclaredMethods(type))
            .Where(m => !m.IsStatic && m.ReturnType == typeof(void) && m.Name.Contains("Invest") &&
                        m.GetParameters() is { Length: 1 } args && args[0].ParameterType == typeof(bool))
            .Cast<MethodBase>()
            .ToList();
    }

    [HarmonyPrepare]
    private static bool Prepare()
    {
        _targets = FindTargets();
        if (_targets.Count == 0) {
            EmpLog.Warning("Dialog investment: no Invest local function found in DramaCustomSequence, " +
                           "a client's investment will not reach the host");
            return false;
        }

        return true;
    }

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        return _targets ??= FindTargets();
    }

    [HarmonyPrefix]
    private static void OnInvest(out InvestState __state)
    {
        __state = default;
        if (NetSession.Instance.Connection is null || EClass.pc is null || EClass._zone is null) {
            return;
        }

        __state = new(true, EClass.pc.GetCurrency(), EClass._zone.investment, DramaManager.TG?.c_invest ?? 0);
    }

    [HarmonyPostfix]
    private static void OnInvested(InvestState __state)
    {
        // what was invested in went up: it was paid, otherwise "no money" was said and nothing changed. Not the
        // purse: a client's payment is a request, its money goes down when the host answers
        if (!__state.Active) {
            return;
        }

        if (NetSession.Instance.Connection is ElinNetHost host) {
            // the host's own investment in a merchant: the clients' copies of it take the count (issue #41)
            if (DramaManager.TG is { } invested && invested.c_invest > __state.MerchantInvest) {
                ZoneInvestDelta.Tell(host, invested);
            }

            return;
        }

        if (NetSession.Instance.Connection is not ElinNetClient client) {
            return;
        }

        // what the game took from the purse is its price, which the host checks (a merchant's was sent as 1)
        var price = System.Math.Max(0, __state.Money - EClass.pc.GetCurrency());
        if (EClass._zone.investment > __state.Investment) {
            client.Delta.AddRemote(new ZoneInvestDelta {
                Cost = System.Math.Max(price, EClass._zone.investment - __state.Investment),
            });
        } else if (DramaManager.TG is { } merchant && merchant.c_invest > __state.MerchantInvest) {
            // the merchant of the dialog (Build(tg.chara)): its own count went up, it is the one invested in
            client.Delta.AddRemote(new ZoneInvestDelta {
                Cost = System.Math.Max(price, 1),
                Merchant = merchant,
            });
        }
    }
}
