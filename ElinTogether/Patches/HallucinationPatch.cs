using System.Linq;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The game hallucinates through one switch for the whole screen (Player.seedHallucination), set when a
///     character marked as a player (Card._IsPC) gets ConHallucination and cleared when it loses it. In a guest's
///     game the host's character carries that mark too: the host ate a white mushroom, the guest's screen
///     hallucinated, and stayed so after the host's had ended (player report, 2026-10-15). The switch follows the
///     local player only: on when it hallucinates, off when it does not
/// </summary>
[HarmonyPatch]
internal static class HallucinationPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ConHallucination), nameof(ConHallucination.SetOwner))]
    internal static void OnSetOwner()
    {
        Follow(null);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ConHallucination), nameof(ConHallucination.OnRemoved))]
    internal static void OnRemoved(ConHallucination __instance)
    {
        Follow(__instance);
    }

    /// <summary>
    ///     A world read from a save sets the switch from whichever player character hallucinates in it, while no
    ///     game is set yet to tell which one is the local player (Follow then gives up), and the mod's own way
    ///     of loading a world copy never clears it (vanilla does, Game.OnBeforeInstantiate). A guest that rejoined
    ///     from the title while another player hallucinated kept the effect without the condition (GitHub issue
    ///     37). Once the world is loaded and the local player known, the switch follows that player again
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), nameof(Game.OnLoad))]
    internal static void OnGameLoad()
    {
        Follow(null);
    }

    private static void Follow(Condition? leaving)
    {
        // (not EClass.pc: while a game is being loaded the game object is not set yet and EClass.pc throws. A
        // guest whose character hallucinated could not load the host's save any more: issue #1, 2026-10-23)
        if (EClass.core?.game?.player?.chara is not { } pc) {
            return;
        }

        var mine = pc.conditions.Any(c => c is ConHallucination && c != leaving);
        if (!mine) {
            Player.seedHallucination = 0;
        } else if (Player.seedHallucination == 0) {
            Player.seedHallucination = EClass.rnd(10000) + 1;
        }
    }
}
