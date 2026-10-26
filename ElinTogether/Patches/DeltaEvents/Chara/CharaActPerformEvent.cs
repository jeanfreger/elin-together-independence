using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal static class CharaActPerformEvent
{
    private static readonly HashSet<int> _alwaysSuccessfulActs = [
        // for some reason these return false
        ABILITY.ActRide,
        ABILITY.ActParasite,
    ];

    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return OverrideMethodComparer.FindAllOverrides(typeof(Act), nameof(Act.Perform))
            .Where(mi => mi.DeclaringType != typeof(DynamicAct) &&
                         mi.DeclaringType != typeof(DynamicAIAct) &&
                         mi.DeclaringType != typeof(ActQuickCraft));
    }

    [HarmonyPostfix]
    internal static void OnCharaActPerform(Act __instance, bool __result)
    {
        if (NetSession.Instance.Connection is not { } connection) {
            return;
        }

        // to save bandwidth, only propagate successful act perform events
        if (!__result && !_alwaysSuccessfulActs.Contains(__instance.id)) {
            return;
        }

        // perform throw via ActThrowEvent
        if (__instance is ActThrow and not ActRanged) {
            return;
        }

        // a reload is a ranged act aimed at oneself, a reload only for "the player" (ActRanged.IsReload): replayed
        // for another player's character it was a shot at itself (hunt 2 row 18). The ammo and the reload
        // condition are the shooter's own, sent as they change
        if (__instance is ActRanged && Act.TC == Act.CC) {
            return;
        }

        // host propagates every act perform event
        // clients only propagate self
        if (connection.IsHost || Act.CC.IsPC) {
            var delta = CharaActPerformDelta.Create(__instance);

            // a blow struck inside another act (ActMeleeParry, ActMeleeCounter, each hit of a flurry) has no id to be
            // made again from: the replay of the act around it strikes its own, the damage comes as CardDamageHpDelta
            if (delta.ActId == 0 && delta.Tool is null) {
                return;
            }

            var ride = __instance.id is ABILITY.ActRide or ABILITY.ActParasite && Act.CC.IsPC;
            // where the rider stands, before too: the replay checks the reach from the rider's tile as that game last
            // saw it, one step behind, and an animal out of reach was not ridden there (ride2 speed, 2026-10-26)
            if (ride) {
                connection.Delta.AddRemote((CharaMoveDelta)Act.CC);
            }

            connection.Delta.AddRemote(delta);
            // the other games replay the ride where they last saw the rider: one step behind, the pair stood on
            // another tile there until someone moved (ride2 "on the spot", 2026-10-24). Where the rider stands, after
            if (ride) {
                connection.Delta.AddRemote((CharaMoveDelta)Act.CC);
            }

            EmpLog.Debug("Act {ActId} by chara {OwnerUid} at {@Pos}, target {TargetUid}",
                delta.ActId, delta.Owner.Uid, delta.Pos, delta.TargetCard?.Uid);

            // a can is filled by setting its charges, which nothing else sends
            if (connection.IsHost && delta.Tool?.Find() is { isDestroyed: false } tool) {
                connection.Delta.AddRemote(new CardChargeDelta {
                    Card = tool,
                    Charges = tool.c_charges,
                });
            }
        }
    }
}