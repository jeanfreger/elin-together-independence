using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The practice of another player, as this game keeps it: it only stands for the task (its progress, its
///     cancel). The blows come from the game of the one who practises, replayed one by one, and the game's own
///     progress throws as "the player": here it would strike a second time, or throw for the wrong character
/// </summary>
[HarmonyPatch(typeof(AI_PracticeDummy), nameof(AI_PracticeDummy.CreateProgress))]
internal static class AIPracticeDummyPatch
{
    [HarmonyPostfix]
    internal static void OnCreateProgress(AI_PracticeDummy __instance, AIProgress __result)
    {
        if (NetSession.Instance.Connection is not null && __instance.owner is { IsPC: false } &&
            __result is Progress_Custom progress) {
            progress.onProgress = null;
        }
    }
}

/// <summary>
///     A shot or a throw at a training dummy sets the practice on "the player" when the act's performer reads as
///     that player (ActThrow.Throw, ActRanged.Perform: Act.CC == EClass.pc). The replay of another player's throw
///     kept this game's Act.CC from its own last act: this game's player was set to practise with the other one's
///     weapon, threw it in a loop and could not move (GitHub issue 5). A practice comes from this player's own act,
///     never from a replay
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.SetAI))]
internal static class PracticeFromReplayPatch
{
    [HarmonyPrefix]
    internal static bool OnSetAI(Chara __instance, AIAct g, ref AIAct __result)
    {
        if (g is not AI_PracticeDummy || !__instance.IsPC || !Models.ElinDelta.IsApplying ||
            NetSession.Instance.Connection is null) {
            return true;
        }

        __result = __instance.ai;
        return false;
    }
}
