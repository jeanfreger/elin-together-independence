using System.Collections.Generic;
using System.Reflection;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Ends of story quests the game wrote for "the player", run by the host for the player who made the step
/// </summary>
[HarmonyPatch]
internal static class StoryForPlayerPatch
{
    /// <summary>
    ///     Nasu takes the backpack back from "the player": it is in the bag of the player who talked to her. For
    ///     the host it was not there, the end of the quest threw and its rewards were lost <br />
    ///     The debt starts with Loytel joining "the player" and a cassette in its bag: the player who took the debt
    ///     from Loytel, not the host (when that player has a home, the game reads it)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(QuestNasu), nameof(QuestNasu.OnComplete))]
    [HarmonyPatch(typeof(QuestDebt), nameof(QuestDebt.OnStart))]
    internal static void OnNasuComplete(MethodBase __originalMethod, out ScopeExit? __state)
    {
        __state = QuestRewardPatch.Receiver is { isDead: false } earner &&
                  (earner.homeBranch is not null || __originalMethod.DeclaringType != typeof(QuestDebt))
            ? RemoteCraft.AsCrafter(earner)
            : null;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(QuestNasu), nameof(QuestNasu.OnComplete))]
    [HarmonyPatch(typeof(QuestDebt), nameof(QuestDebt.OnStart))]
    internal static void OnNasuCompleteEnd(MethodBase __originalMethod, ScopeExit? __state)
    {
        var earner = __state is null ? null : EClass.pc;
        __state?.Dispose();

        // the game puts Loytel in "the party" with no word of whose companion he is: the player's who took the debt
        if (earner is null || __originalMethod.DeclaringType != typeof(QuestDebt) ||
            NetSession.Instance.Connection is not ElinNetHost host ||
            EClass.game.cards.globalCharas.Find("loytel") is not { } loytel || !EClass.pc.party.members.Contains(loytel)) {
            return;
        }

        loytel.SetCompanionOwner(earner);
        host.Delta.AddRemote(new CharaMakeAllyDelta {
            Owner = loytel,
            ShowMsg = false,
            TemporaryAllyName = loytel.c_altName,
            OwnerUid = earner.uid,
            JoinOnly = true,
        });
    }

    /// <summary>
    ///     The end of a story quest brings a resident (Demitas, Kettle, Quru, Sorin, Loytel, Corgon): the game puts
    ///     it next to "the player" and tells "the player" it joined. The host ran it with its own character: the
    ///     resident stood by the host and the host read the message. It stands by the player who completed the
    ///     quest, when that player is on this map (else the game's own place), and that player reads the message
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Quest), nameof(Quest.AddResident))]
    internal static bool OnAddResident(Quest __instance, string id, ref Chara __result)
    {
        if (NetSession.Instance.Connection is not ElinNetHost ||
            QuestRewardPatch.Receiver is not { isDead: false, IsAliveInCurrentZone: true, pos: { } where } receiver) {
            return true;
        }

        var chara = CharaGen.Create(id);
        EClass.Branch.AddMemeber(EClass._zone.AddCard(chara, where.GetNearestPoint(allowBlock: false, allowChara: false)) as Chara);
        using (MsgRelayContext.RedirectTo(receiver)) {
            Msg.Say("hire".langGame(chara.Name));
        }

        __result = chara;
        return false;
    }

    /// <summary>
    ///     The dreams of Lesimas: every game moves the quest one step when its player enters a base, at steps 0 to
    ///     3. On one map two games did it one after the other, and a dream was skipped. The game that simulates
    ///     the map does it, alone
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Player.Flags), nameof(Player.Flags.OnEnterZone))]
    internal static IEnumerable<CodeInstruction> OnEnterZone(IEnumerable<CodeInstruction> instructions)
    {
        var getPhase = AccessTools.Method(typeof(QuestManager), nameof(QuestManager.GetPhase)).MakeGenericMethod(typeof(QuestIntoDarkness));
        foreach (var instruction in instructions) {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method == getPhase) {
                yield return CodeInstruction.Call(typeof(StoryForPlayerPatch), nameof(DreamPhase));
            }
        }
    }

    internal static int DreamPhase(int phase)
    {
        var session = NetSession.Instance;
        return session.Connection is ElinNetClient && !session.IsZoneAuthority ? -99 : phase;
    }
}
