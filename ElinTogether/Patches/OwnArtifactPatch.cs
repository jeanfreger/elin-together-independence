using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
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
        // (Transport, not Connection: the latter is null while a player is alone on an away zone)
        if (NetSession.Instance.Transport is null) {
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
    ///     owner's (by its tag, in the party or not), an untagged party member its leader's, and its own otherwise
    /// </summary>
    private static int Side(Chara chara, int depth = 0)
    {
        if (chara.IsPC || chara.GetBool("remote_chara")) {
            return chara.uid;
        }

        if (chara.master is { } master && master != chara && depth < 4) {
            return Side(master, depth + 1);
        }

        if (chara.CompanionOwnerUid is var uid and not 0) {
            return uid;
        }

        // (issue 34: an untagged chara is the leader's only when really in that party, else nobody's but its own)
        return chara.party is { } p && p.members.Contains(chara) ? (p.leader ?? EClass.pc).uid : chara.uid;
    }

    /// <summary>
    ///     The player on the taker's side, for what the purge reads of "the player": the feat of Eyth (1228) that
    ///     turns the other Eyth artifacts back to their own god
    /// </summary>
    internal static Chara SidePlayer(Card card)
    {
        if (NetSession.Instance.Transport is null || card.GetRootCard() is not Chara taker) {
            return EClass.pc;
        }

        return EClass.game.cards.globalCharas.Find(Side(taker)) is { } player && (player.IsPC || player.GetBool("remote_chara"))
            ? player
            : EClass.pc;
    }

    // EClass.pc.HasElement(1228) -> SidePlayer(this).HasElement(1228)
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> OnPurgeIl(IEnumerable<CodeInstruction> instructions)
    {
        var getPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
        var list = instructions.ToList();
        var changed = 0;
        for (var i = 0; i < list.Count - 1; i++) {
            if (list[i].Calls(getPc) && list[i + 1].LoadsConstant(1228)) {
                var labels = list[i].labels;
                list[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OwnArtifactPatch), nameof(SidePlayer)));
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0) { labels = labels });
                changed++;
            }
        }

        if (changed != 1) {
            EmpLog.Warning("God artifact purge: {Changed} reads of the Eyth feat found, 1 expected", changed);
        }

        return list;
    }

    [HarmonyFinalizer]
    internal static void OnPurgeEnd(List<Thing>? __state)
    {
        foreach (var kept in __state ?? []) {
            kept.isReplica = false;
        }
    }
}
