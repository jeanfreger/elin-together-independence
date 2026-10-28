using System;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     One affinity per resident for the whole group. The game of the player who acts (a gift, a chat) rolls the
///     dice; the host adds the result to its value and tells everyone. A game that only replays someone else's
///     action does not roll its own. Host option "Affinity per player": the same, in the acting player's own
///     column, see AffinityPerPlayer
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.ModAffinity))]
internal static class CharaAffinityPatch
{
    internal readonly record struct Before(int Value, IDisposable? StandIn);

    [HarmonyPrefix]
    internal static void OnModAffinity(Chara __instance, ref Chara c, out Before __state)
    {
        // (first: the value read below is the column of the player it is aimed at)
        var standIn = AffinityPerPlayer.StandIn(__instance, ref c);
        __state = new(__instance._affinity, standIn);
    }

    [HarmonyPostfix]
    internal static void OnModAffinityEnd(Chara __instance, Before __state)
    {
        var change = __instance._affinity - __state.Value;
        if (change == 0 || __instance.IsPC) {
            return;
        }

        if (AffinityPerPlayer.On) {
            AffinityPerPlayer.OnChanged(__instance, __state.Value, change);
            return;
        }

        switch (NetSession.Instance.Connection) {
            // (its own drop, resumed inside the answer to its item request, is its own act)
            case ElinNetClient client when !ElinDelta.IsRemoteStateLanding:
                client.Delta.AddRemote(new CharaAffinityDelta {
                    Owner = __instance,
                    Value = change,
                    Relative = true,
                });
                break;
            case ElinNetHost host when !ElinDelta.IsApplying:
                host.Delta.AddRemote(new CharaAffinityDelta {
                    Owner = __instance,
                    Value = __instance._affinity,
                });
                break;
            case not null:
                // replaying what another player did: that player's game rolled, the host will say the result
                __instance._affinity = __state.Value;
                break;
        }
    }

    // after the postfix, and when the game threw: "the player" is the host's own again
    [HarmonyFinalizer]
    internal static void OnModAffinityDone(Before __state)
    {
        __state.StandIn?.Dispose();
    }
}
