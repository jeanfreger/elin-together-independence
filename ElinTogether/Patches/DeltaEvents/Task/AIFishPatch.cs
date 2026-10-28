using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using ElinTogether.Helper;
using ElinTogether.Helper.Extensions;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal static class AIFishPatch
{
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(AI_Fish), nameof(AI_Fish.Run), MethodType.Enumerator)]
    internal static IEnumerable<CodeInstruction> OnRun(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            // if (!this.owner.IsPC)
            .MatchEndForward(
                new CodeMatch(OpCodes.Ldloc_2),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Card), nameof(Card.IsPC))))
            .SetInstructionAndAdvance(
                Transpilers.EmitDelegate((Chara chara) => NetSession.Instance.IsHost ? chara.IsPlayer : chara.IsPC))
            // if (this.pos != null)
            .MatchEndForward(
                new CodeMatch(OpCodes.Ldloc_2),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Brfalse))
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldloc_2),
                Transpilers.EmitDelegate((AI_Fish thiz) => {
                    if (NetSession.Instance.IsClient && thiz.owner.IsPC) {
                        EClass.player.TryEquipBait();
                    }
                }))
            // if (this.owner.IsPC)
            .MatchEndForward(
                new CodeMatch(OpCodes.Ldloc_2),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Card), nameof(Card.IsPC))))
            .SetInstructionAndAdvance(
                Transpilers.EmitDelegate((Chara chara) => NetSession.Instance.IsHost ? chara.IsPlayer : chara.IsPC))
            // this.owner.TryEquipBait();
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.player))),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Player), nameof(Player.TryEquipBait))))
            .RemoveInstructions(2)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldloc_2),
                Transpilers.EmitDelegate((AI_Fish thiz) => {
                    if (NetSession.Instance.IsClient) {
                        EClass.player.TryEquipBait();
                        return;
                    }

                    if (thiz.owner.IsPC) {
                        EClass.player.TryEquipBait();
                    }
                }))
            // if (EClass.player.eqBait == null)
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.player))))
            .RemoveInstructions(2)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldloc_2),
                Transpilers.EmitDelegate((AI_Fish thiz) => !thiz.owner.IsPC || EClass.player.eqBait is not null))
            // if (this.owner == EClass.pc)
            .MatchEndForward(
                new CodeMatch(OpCodes.Ldloc_2),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc))))
            .SetInstructionAndAdvance(
                Transpilers.EmitDelegate((Chara chara) => chara.IsPlayer))
            .SetOpcodeAndAdvance(OpCodes.Brfalse_S)
            .InstructionEnumeration();
    }

    /// <summary>
    ///     The catch is rolled by the host, where another player's character is not "the player": it fished with
    ///     the odds of a resident, 20 times fewer bonus catches and 50 times fewer starred fish. It stands in as
    ///     the player for the roll
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(AI_Fish), nameof(AI_Fish.Makefish))]
    internal static void OnMakefishAsPlayer(Chara c, out ScopeExit? __state)
    {
        __state = null;
        if (NetSession.Instance.Connection is not ElinNetHost || !c.IsRemotePlayer) {
            return;
        }

        // the catches of the day counted by the game are the host's own: that player's count stands in for the
        // roll (the fisher's fever is the first five catches of ITS day), and is noted back after it
        var fished = EClass.player.fished;
        var day = EClass.player.stats.days;
        EClass.player.fished = _fished.TryGetValue(c.uid, out var its) && its.Day == day ? its.Fished : 0;
        var standIn = RemoteCraft.AsCrafter(c);
        __state = new() {
            OnExit = () => {
                standIn.Dispose();
                _fished[c.uid] = (day, EClass.player.fished);
                EClass.player.fished = fished;
            },
        };
    }

    // ponytail: kept while this game runs, not saved: a host that restarts gives each guest its fever again that day
    private static readonly Dictionary<int, (int Day, int Fished)> _fished = [];

    /// <summary>
    ///     The catches of today counted for that player's character (the bench reads it)
    /// </summary>
    internal static int FishedToday(int uid)
    {
        return _fished.TryGetValue(uid, out var its) && its.Day == EClass.player.stats.days ? its.Fished : 0;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(AI_Fish), nameof(AI_Fish.Makefish))]
    internal static void OnMakefishAsPlayerEnd(ScopeExit? __state)
    {
        __state?.Dispose();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AI_Fish), nameof(AI_Fish.Makefish))]
    internal static bool OnMakefish(ref Thing? __result)
    {
        return !TaskProduct.TryClaim(nameof(AI_Fish.Makefish), out __result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AI_Fish), nameof(AI_Fish.Makefish))]
    internal static void OnMakefishEnd(Chara c, Thing? __result, ScopeExit? __state)
    {
        // the stand-in ends with the roll: below, the bait is looked up as another player's
        __state?.Dispose();

        if (!TaskProduct.Publish(nameof(AI_Fish.Makefish), __result)) {
            return;
        }

        if (c.IsRemotePlayer) {
            var bait = c.things.Find(t => t.trait is TraitBait tb && tb.EQ == t);
            bait.ModNum(-1);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AI_Fish.ProgressFish), nameof(AI_Fish.ProgressFish.OnProgressComplete))]
    internal static void OnProgressComplete(AI_Fish.ProgressFish __instance)
    {
        if (!CharaProgressCompleteDelta.IsReplaying) {
            return;
        }

        __instance.hit = TaskProduct.WasProduced(nameof(AI_Fish.Makefish)) ? 100 : 0;
    }

    /// <summary>
    ///     The thief's roll (one in a thousand: the fish is destroyed, "ehe") is made by the player's own game and
    ///     its result travels. The copies replaying that fishing for others do not roll again: they destroyed a fish
    ///     the player kept, or kept one it lost
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(AI_Fish.ProgressFish), nameof(AI_Fish.ProgressFish.OnProgressComplete))]
    internal static IEnumerable<CodeInstruction> OnProgressCompleteStolen(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 1000))
            .MatchEndForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(EClass), nameof(EClass.rnd))))
            .Advance(1)
            .InsertAndAdvance(Transpilers.EmitDelegate((int roll) => CharaProgressCompleteDelta.IsReplaying ? 1 : roll))
            .InstructionEnumeration();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AI_Fish.ProgressFish), nameof(AI_Fish.ProgressFish.OnProgress))]
    internal static bool OnProgress(AI_Fish.ProgressFish __instance)
    {
        OnProgress_Modified(__instance);
        return false;
    }

    internal static void OnProgress_Modified(AI_Fish.ProgressFish thiz)
    {
        if (thiz.owner.IsPC && (thiz.owner.Tool == null || !thiz.owner.Tool.HasElement(245))) {
            thiz.Cancel();
            return;
        }

        if (thiz.hit >= 0) {
            thiz.owner.renderer.PlayAnime(AnimeID.Fishing);
            thiz.owner.PlaySound("fish_fight");
            thiz.Ripple();

            if (NetSession.Instance.Connection is ElinNetClient) {
                return;
            }

            var num = Mathf.Clamp(10 - EClass.rnd(thiz.owner.Evalue(245) + 1) / 10, 5, 10);
            if (thiz.hit > EClass.rnd(num)) {
                thiz.hit = 100;
                thiz.progress = thiz.MaxProgress;
            }

            thiz.hit++;
            return;
        }

        if (EClass.rnd(Mathf.Clamp(10 - EClass.rnd(thiz.owner.Evalue(245) + 1) / 5, 2, 10)) == 0 && thiz.progress >= 10) {
            thiz.hit = 0;
            if (NetSession.Instance.Connection is ElinNetHost host) {
                host.Delta.AddRemote(new CharaHitFishDelta { Owner = thiz.owner });
            }
        }

        var progress = thiz.progress < 0 ? thiz.progress + int.MaxValue - 1 : thiz.progress;
        if (progress == 2 || (progress >= 8 && progress % 6 == 0 && EClass.rnd(3) == 0)) {
            thiz.owner.renderer.PlayAnime(AnimeID.Shiver);
            thiz.Ripple();
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBait), nameof(TraitBait.EQ), MethodType.Getter)]
    internal static bool OnGetEQ(TraitBait __instance, ref Thing __result)
    {
        if (__instance.owner.GetRootCard()?.IsPC is not false) {
            return true;
        }

        var field = AccessTools.Field(__instance.GetType(), "<EQ>k__BackingField");
        __result = (Thing)field.GetValue(__instance);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBait), nameof(TraitBait.EQ), MethodType.Setter)]
    internal static bool OnSetEQ(TraitBait __instance, Thing value)
    {
        if (__instance.owner.GetRootCard() is not Chara { IsPC: false } owner) {
            return true;
        }

        owner.things
            .Where(t => t.trait is TraitBait tb && tb.EQ == t && tb != __instance)
            .Select(t => t.trait as TraitBait)
            .ForEach(tb => {
                tb!.EQ = null;
            });

        var field = AccessTools.Field(__instance.GetType(), "<EQ>k__BackingField");
        field.SetValue(__instance, value);

        return false;
    }
}