using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A companion comes back next to "the player" and into the party of "the player" (Chara.GetRevived). When the act
///     of another player brings it back (the barman's list, a scroll, a spell, see <see cref="CharaReviveRequestDelta" />),
///     the host's game asked its own chara: it stood up next to the host. That player stands in, and it is the host's
///     own gesture so that everyone is told. The companion keeps its owner (nothing here changes it) <br />
///     The click of the barman's list in a guest's game sends the request in place of paying
/// </summary>
[HarmonyPatch]
internal static class RemoteRevivePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.GetRevived))]
    internal static void OnGetRevived(Chara __instance, out ScopeExit? __state)
    {
        __state = null;
        // same rule as an ally made on the way: the player whose delta is applied or whose turn runs
        if (CharaMakeAllyEvent.Recruiter(__instance) is not { } who) {
            return;
        }

        var sent = ElinDelta.Simulate();
        var told = MsgRelayContext.RedirectTo(who);
        var standIn = RemoteCraft.AsCrafter(who);
        __state = new() {
            OnExit = () => {
                standIn.Dispose();
                told.Dispose();
                sent.Dispose();
            },
        };
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Chara), nameof(Chara.GetRevived))]
    internal static void OnGetRevivedEnd(ScopeExit? __state)
    {
        __state?.Dispose();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ListPeopleRevive), nameof(ListPeopleRevive.OnClick))]
    internal static bool OnReviveClick(Chara c)
    {
        if (NetSession.Instance.Connection is not ElinNetClient client || ElinDelta.IsApplying) {
            return true;
        }

        // the host in a guest's zone cannot ask the world's host: refused, as the base's plans are
        if (client.IsZoneSession) {
            SE.Beep();
            EmpPop.Pop("emp_base_host_only".lang());
            return false;
        }

        client.Delta.AddRemote(new CharaReviveRequestDelta {
            Target = c,
        });
        SE.Click();
        return false;
    }
}
