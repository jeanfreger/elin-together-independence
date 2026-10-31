using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     GitHub issue 47, the lost puppy (QuestPuppy, one quest for the whole world). Its dialog outcome
///     (DramaOutcome.poppy_found) makes Poppy an ally and moves the quest one step on, from 0 to 1, whatever the step
///     is. Two players in that dialog at the same time: the first choice moved the world's quest to 1, the host told the
///     second game, whose choice then moved it from 1 to 2 (a step the host accepts, it starts where the world stands).
///     Step 2 is the moment of the reward dialog only: Fiama never asks for Poppy again (QuestPuppy.CanUpdateOnTalk
///     wants 1), the quest could not be turned in, killing Poppy changed nothing. <br />
///     The outcome now runs only while the quest still waits for Poppy (step 0): the second choice does nothing, Poppy
///     follows the first player. A late game whose journal still reads 0 sends 0 to 1, which the host already has and
///     drops (QuestChangePhaseDelta), and its recruit, Poppy being in the party already (CharaMakeAllyRequestDelta)
/// </summary>
[HarmonyPatch]
internal static class QuestPuppyGuard
{
    private const string QuestId = "puppy";

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DramaOutcome), nameof(DramaOutcome.poppy_found))]
    internal static bool OnPoppyFound()
    {
        // (no quest: the game's own code, as it is)
        if (EClass.game?.quests?.Get(QuestId) is not { } quest || quest.phase == 0) {
            return true;
        }

        EmpLog.Information("Poppy found again with the puppy quest at step {Phase}: already rescued, nothing done",
            quest.phase);
        return false;
    }

    /// <summary>
    ///     Host, when the world is hosted: a world broken that way before this fix stands at step 2 (or more), where
    ///     nothing moves it. Put back at 1, Poppy found: talking to Fiama with Poppy on the map ends it, as in the game.
    ///     Only here, never while playing: step 2 also stands for a moment during the reward dialog
    /// </summary>
    internal static void Repair()
    {
        if (!EClass.core.IsGameStarted || EClass.game.quests.Get(QuestId) is not { phase: >= 2 } quest) {
            return;
        }

        var poppy = EClass.game.cards.globalCharas.Find("poppy");
        EmpLog.Information("The puppy quest stood at step {Phase}, past the rescue (two players recruited Poppy at once): put back at 1. " +
                           "Poppy {Uid}, of the party {InParty}, dead {Dead}",
            quest.phase, poppy?.uid ?? 0, poppy?.IsPCFaction ?? false, poppy?.isDead ?? false);
        quest.phase = 1;
        quest.task = null;
        quest.UpdateJournal();
    }
}
