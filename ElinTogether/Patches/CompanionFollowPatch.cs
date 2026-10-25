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

    internal static Chara FollowTarget(Party party, AI_Idle ai)
    {
        return ai.owner?.FindCompanionOwnerHere() ?? party.leader;
    }
}
