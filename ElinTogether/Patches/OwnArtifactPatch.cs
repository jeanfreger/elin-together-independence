using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Taking a god's artifact, the game destroys every other one of its kind held by "the player's side": the
///     whole party and the residents. Each player has its own here (RemoteGodGiftPatch): the second to take its
///     gift destroyed the first one's. What another player and its companions hold is left out of that purge, as
///     the game leaves replicas out: marked as one while it runs
/// </summary>
[HarmonyPatch(typeof(Card), nameof(Card.PurgeDuplicateArtifact))]
internal static class OwnArtifactPatch
{
    [HarmonyPrefix]
    internal static void OnPurge(Card __instance, Thing af, out List<Thing>? __state)
    {
        __state = null;
        if (NetSession.Instance.Connection is null) {
            return;
        }

        var taker = __instance.GetRootCard() as Chara ?? EClass.pc;
        var mine = Side(taker);
        // (residents included: a companion sent home is still its player's, GitHub issue 12)
        var others = EClass._map.charas
            .Concat(EClass.pc.faction.GetChildren().SelectMany(b => b.members))
            .Distinct()
            .Where(c => c.IsPCFactionOrMinion && Side(c) != mine);

        __state = [];
        // (issue 34: what the purge saw, so a report shows whose side each holder was taken for)
        EmpLog.Debug("Artifact {Id} taken by {Taker} (side {Side}), spared holders {Others}", af.id, taker.uid, mine,
            others.Select(c => $"{c.uid}:{Side(c)}").ToList());
        foreach (var chara in others) {
            foreach (var kept in chara.things.List(t => !t.isReplica && t != af && (t.id == af.id || t.HasTag(CTAG.godArtifact)))) {
                kept.isReplica = true;
                __state.Add(kept);
            }
        }
    }

    /// <summary>
    ///     The player whose side a chara is on: a player itself, a summon its master's, a companion or resident its
    ///     owner's (by its tag, in the party or not), and the host's when nobody else's
    /// </summary>
    private static int Side(Chara chara, int depth = 0)
    {
        if (chara.IsPC || chara.GetBool("remote_chara")) {
            return chara.uid;
        }

        if (chara.master is { } master && master != chara && depth < 4) {
            return Side(master, depth + 1);
        }

        return chara.CompanionOwnerUid is var uid and not 0 ? uid : (EClass.pc.party?.leader ?? EClass.pc).uid;
    }

    [HarmonyFinalizer]
    internal static void OnPurgeEnd(List<Thing>? __state)
    {
        foreach (var kept in __state ?? []) {
            kept.isReplica = false;
        }
    }
}
