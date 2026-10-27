using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ElinTogether.Elements;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Companions follow the player who owns them instead of the party leader
/// </summary>
[HarmonyPatch]
internal static class CompanionFollowPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(AI_Idle), nameof(AI_Idle.Run), MethodType.Enumerator)]
    internal static IEnumerable<CodeInstruction> OnRun(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var self = AccessTools.Field(original.DeclaringType, "<>4__this");
        var getLeader = AccessTools.PropertyGetter(typeof(Party), nameof(Party.leader));
        var getPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
        var list = instructions.ToList();

        // "EClass.pc.ai is AI_Fish", then "EClass.pc.pos": a companion fishes beside the player who owns it, when that
        // one fishes (hunt 1 row I2b: a guest's companion fished on the host's cue)
        var fish = list.FindIndex(i => i.opcode == OpCodes.Isinst && Equals(i.operand, typeof(AI_Fish)));
        if (fish >= 2 && list[fish - 2].Calls(getPc)) {
            var pos = AccessTools.Field(typeof(Card), nameof(Card.pos));
            var at = list.FindIndex(fish, i => i.Calls(getPc));
            if (at >= 0 && list[at + 1].LoadsField(pos)) {
                ToFishMaster(list, at, self);
            }

            // "EClass.pc.ai" -> FishMasterAct(this)
            list[fish - 2] = new CodeInstruction(OpCodes.Ldarg_0) { labels = list[fish - 2].labels, blocks = list[fish - 2].blocks };
            list[fish - 1] = new CodeInstruction(OpCodes.Ldfld, self);
            list.Insert(fish, CodeInstruction.Call(typeof(CompanionFollowPatch), nameof(FishMasterAct)));
        }

        // the jealousy of apostles and spouses (GitHub issue 11): each player's own count, not the whole session's
        // party. "party.members" before GetEnumerator -> OwnGroup(members, this); EvalueTotal -> OwnEvalueTotal;
        // "EClass.pc" before Evalue(1276) -> OwnerOrPc(this)
        var members = AccessTools.PropertyGetter(typeof(Party), nameof(Party.members));
        var enumerate = AccessTools.Method(typeof(List<Chara>), nameof(List<Chara>.GetEnumerator));
        var evalueTotal = AccessTools.Method(typeof(Party), nameof(Party.EvalueTotal));
        var evalue = AccessTools.Method(typeof(Card), nameof(Card.Evalue), [typeof(int)]);
        for (var i = 0; i < list.Count - 1; i++) {
            if (list[i].Calls(members) && list[i + 1].Calls(enumerate)) {
                list.InsertRange(i + 1, [new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, self),
                    CodeInstruction.Call(typeof(CompanionFollowPatch), nameof(OwnGroup))]);
                i += 3;
            } else if (list[i].Calls(evalueTotal)) {
                var call = list[i];
                list.InsertRange(i, [new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(call), new CodeInstruction(OpCodes.Ldfld, self)]);
                list[i + 2] = CodeInstruction.Call(typeof(CompanionFollowPatch), nameof(OwnEvalueTotal)).MoveBlocksFrom(call);
                i += 2;
            } else if (list[i].Calls(getPc) && i + 2 < list.Count && list[i + 1].LoadsConstant(1276) && list[i + 2].Calls(evalue)) {
                list[i] = new CodeInstruction(OpCodes.Ldarg_0) { labels = list[i].labels, blocks = list[i].blocks };
                list.InsertRange(i + 1, [new CodeInstruction(OpCodes.Ldfld, self), CodeInstruction.Call(typeof(CompanionFollowPatch), nameof(OwnerOrPc))]);
            }
        }

        // party.leader -> FollowTarget(party, this), only the follow block of AI_Idle reads it
        foreach (var instruction in list) {
            if (!instruction.Calls(getLeader)) {
                yield return instruction;
                continue;
            }

            yield return new CodeInstruction(OpCodes.Ldarg_0) {
                labels = instruction.labels,
                blocks = instruction.blocks,
            };
            yield return new CodeInstruction(OpCodes.Ldfld, self);
            yield return CodeInstruction.Call(typeof(CompanionFollowPatch), nameof(FollowTarget));
        }
    }

    // EClass.pc -> FishMaster(this)
    private static void ToFishMaster(List<CodeInstruction> list, int at, FieldInfo self)
    {
        list[at] = new CodeInstruction(OpCodes.Ldarg_0) { labels = list[at].labels, blocks = list[at].blocks };
        list.InsertRange(at + 1, [new CodeInstruction(OpCodes.Ldfld, self), CodeInstruction.Call(typeof(CompanionFollowPatch), nameof(FishMaster))]);
    }

    internal static Chara FishMaster(AI_Idle ai)
    {
        return ai.owner?.FindCompanionOwnerHere() ?? EClass.pc;
    }

    // a guest's task runs on the host under its GoalRemote
    internal static AIAct? FishMasterAct(AI_Idle ai)
    {
        var master = FishMaster(ai);
        return master.ai is GoalRemote remote ? remote.child : master.ai;
    }

    /// <summary>
    ///     The members of the party on the side of this chara's player (that player and its own companions). Alone, or
    ///     a chara of nobody, the whole party as in the game
    /// </summary>
    internal static List<Chara> OwnGroup(List<Chara> members, AI_Idle ai)
    {
        if (ElinTogether.Net.NetSession.Instance.Connection is null || ai.owner is not { } chara ||
            CompanionHelper.OwnerOf(chara) is not { } owner) {
            return members;
        }

        return members.FindAll(m => m == owner || CompanionHelper.OwnerOf(m) == owner);
    }

    // Party.EvalueTotal over that player's side, the player's own feat counted (the game counts only EClass.pc's)
    internal static int OwnEvalueTotal(Party party, int ele, System.Func<Chara, bool>? func, AI_Idle ai)
    {
        var group = OwnGroup(party.members, ai);
        if (ReferenceEquals(group, party.members)) {
            return party.EvalueTotal(ele, func);
        }

        var owner = CompanionHelper.OwnerOf(ai.owner);
        return group.Where(m => m == owner || func is null || func(m)).Sum(m => m.Evalue(ele));
    }

    internal static Chara OwnerOrPc(AI_Idle ai)
    {
        return ElinTogether.Net.NetSession.Instance.Connection is not null && ai.owner is { } chara &&
               CompanionHelper.OwnerOf(chara) is { } owner ? owner : EClass.pc;
    }

    internal static Chara FollowTarget(Party party, AI_Idle ai)
    {
        return ai.owner?.FindCompanionOwnerHere() ?? party.leader;
    }
}
