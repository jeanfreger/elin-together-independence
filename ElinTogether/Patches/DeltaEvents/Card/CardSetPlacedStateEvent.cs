using System;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Card), nameof(Card.SetPlaceState))]
internal static class CardSetPlacedStateEvent
{
    [HarmonyPrefix]
    internal static bool OnSetCardPlacedState(Card __instance, PlaceState newState, bool byPlayer)
    {
        if (NetSession.Instance.Connection is not { } connection) {
            return true;
        }

        // avoid duplicate actions sending: a build reaches the others as a whole, see CharaBuildDelta
        if (CharaProgressCompleteEvent.IsHappening && CharaProgressCompleteEvent.Action is TaskBuild) {
            // on the host the placed state rides along with the other side effects of the build: a build
            // with nothing held (the host's build mode) sends only those, and a client whose replay stops
            // early gets the piece through them. Without it the piece lay loose there, free to pick up
            if (newState != PlaceState.none && CharaProgressCompleteEvent.ShouldPack(false)) {
                CharaProgressCompleteEvent.Pack(new CardPlacedDelta {
                    Owner = __instance,
                    PlaceState = newState,
                    Dir = __instance.dir,
                    ByPlayer = byPlayer,
                });
            }

            return true;
        }

        if (newState == PlaceState.none) {
            return true;
        }

        // we propagate every place event to remotes
        // so clients can help with placing stuff
        // Elin: Build Together
        connection.Delta.AddRemote(new CardPlacedDelta {
            Owner = __instance,
            PlaceState = newState,
            Dir = __instance.dir,
            ByPlayer = byPlayer,
        });

        // the host's own player installs a mail box: it owns it
        if (connection.IsHost && newState == PlaceState.installed && byPlayer && !ElinDelta.IsApplying) {
            Helper.TeamMail.Claim(__instance, EClass.pc.uid);
        }

        return connection.IsHost;
    }

    extension(Card card)
    {
        [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
        internal void Stub_SetPlacedState(PlaceState newState, bool byPlayer = false)
        {
            throw new NotImplementedException("Card.SetPlacedState");
        }
    }
}