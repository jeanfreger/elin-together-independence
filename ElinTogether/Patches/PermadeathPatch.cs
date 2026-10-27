using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Permadeath with PermadeathRevive (council 2026-10-27): the game deleted the save of whoever died, before its
///     last words, and offered only "game over". The host's death deleted the shared world; a guest's its copy. Here
///     the player dies as in the game (the message, the sounds, its last words once), then its character waits in the
///     world for a revive at a bartender; nothing is deleted. Without the box, or outside permadeath: the game's own
/// </summary>
[HarmonyPatch]
internal static class PermadeathPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
    internal static void OnSceneUpdate()
    {
        if (!EClass.core.IsGameStarted || EClass.pc is not { isDead: true } pc || EClass.player.deathDialog ||
            !PlayerRevive.Active) {
            return;
        }

        // the game's block that follows only runs while deathDialog is false
        EClass.player.deathDialog = true;
        EClass.player.returnInfo = null;
        EClass.player.ignoreFallOver = 1;

        if (pc.GetBool(PlayerRevive.DeadFlag)) {
            Msg.Say("emp_permadeath_wait".lang());
            return;
        }

        pc.SetBool(PlayerRevive.DeadFlag, true);
        Msg.Say("diebye");
        EClass.Sound.Play("dead_pc2");
        EClass.Sound.Play("dead_pc");
        var lastWord = Lang.GetList("lastWords").RandomItem();
        EClass.ui.CloseLayers();
        EInput.haltInput = false;
        Dialog.InputName("dialogLastword", lastWord, (cancel, text) => {
            if (!cancel) {
                lastWord = text;
            }

            Msg.SetColor(Msg.colors.Talk);
            Msg.Say(lastWord.Bracket(1));
            Msg.Say("emp_permadeath_wait".lang());
        });
    }

    /// <summary>
    ///     No game in a session deletes a save: a guest's is its copy of the world; the host's is the world, while the
    ///     box keeps its dead players waiting
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameIO), nameof(GameIO.DeleteGame))]
    internal static bool OnDeleteGame(string id)
    {
        if (NetSession.Instance.Connection is ElinNetClient) {
            EmpLog.Information("Save {Id} not deleted: a guest's copy of the world", id);
            return false;
        }

        if (PlayerRevive.Active && id == Game.id) {
            EmpLog.Information("Save {Id} not deleted: permadeath with revives at the bartender", id);
            return false;
        }

        return true;
    }
}
