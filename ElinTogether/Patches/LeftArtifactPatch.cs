using System.Collections.Generic;
using System.Linq;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Leaving a zone, the game puts the god artifacts left on its ground in the bag of "the player" (Zone.Deactivate):
///     each one goes to the player of its god instead, and stays on the ground when nobody here follows that god.
///     The ones left there are hidden from the game's pick (no longer artifacts) while it runs
/// </summary>
[HarmonyPatch(typeof(Zone), nameof(Zone.Deactivate))]
internal static class LeftArtifactPatch
{
    [HarmonyPrefix]
    internal static void OnDeactivate(Zone __instance, out List<(Thing Thing, int RarityLv)>? __state)
    {
        __state = null;
        var session = NetSession.Instance;
        if (session.Transport is null || __instance.IsUserZone || __instance.IsPCFaction || __instance.map is not { } map) {
            return;
        }

        __state = [];
        foreach (var thing in map.things.Where(t => t.rarity >= Rarity.Artifact && t.HasTag(CTAG.godArtifact)).ToList()) {
            var deity = thing.c_idDeity.IsEmpty() ? EClass.game.religions.GetArtifactDeity(thing.id)?.id : thing.c_idDeity;
            // (no god known: the game's way)
            if (deity.IsEmpty() || EClass.pc.faith?.id == deity) {
                continue;
            }

            if (session.Transport is ElinNetHost host &&
                host.ActiveRemoteCharas.Values.FirstOrDefault(p => p is { isDead: false } && p.faith?.id == deity) is { } owner) {
                owner.AddCard(thing);
                continue;
            }

            __state.Add((thing, thing.rarityLv));
            thing.rarity = Rarity.Normal;
        }
    }

    [HarmonyFinalizer]
    internal static void OnDeactivateEnd(List<(Thing Thing, int RarityLv)>? __state)
    {
        foreach (var (thing, rarityLv) in __state ?? []) {
            thing.rarityLv = rarityLv;
        }
    }
}
