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
    ///     No open window or menu when this frame began (the game's input not yet read): an Escape that closes a
    ///     window does not also open the system menu
    /// </summary>
    private static bool _uiWasIdle;

    /// <summary>
    ///     A dead player waiting for its revive: the game reads only the windows' input for a dead character
    ///     (Scene.OnUpdate calls UI.UpdateInput, not ActionMode.UpdateInput), because its death dialog came at once.
    ///     Here the wait has no end of its own, and Escape did nothing: no system menu, no Save and Quit, no
    ///     Multiplayer menu (in "mod"), no bug report (GitHub issue 45). The base game mode's own non-world keys
    ///     (AM_BaseGameMode.OnUpdateInput: Escape, help, log) and the read-only character sheet and journal are
    ///     handled here; movement, actions, hotbar and inventory keys stay ignored
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
    [HarmonyPriority(Priority.First)]
    internal static void BeforeSceneUpdate()
    {
        _uiWasIdle = EClass.core.IsGameStarted && EClass.ui is { } ui && ui.layers.Count == 0 && !ui.contextMenu.isActive;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
    internal static void AfterSceneUpdate(Scene __instance)
    {
        if (!_uiWasIdle || !IsWaitingForRevive(__instance) || EClass.ui.layers.Count != 0 || EClass.ui.contextMenu.isActive ||
            EClass.ui.BlockInput || EInput.waitReleaseAnyKey) {
            return;
        }

        switch (EInput.action) {
            case EAction.Cancel:
                if (!WidgetSearch.Instance) {
                    HotItemContext.Show("system", EInput.uiMousePosition);
                    EInput.Consume();
                }

                break;
            case EAction.Help:
                LayerHelp.Toggle("general", "1");
                break;
            case EAction.Log:
                WidgetMainText.ToggleLog();
                break;
            case EAction.MenuChara:
                EClass.ui.ToggleLayer<LayerChara>()?.SetChara(EClass.pc);
                break;
            case EAction.MenuJournal:
                EClass.ui.ToggleLayer<LayerJournal>();
                break;
        }
    }

    internal static bool IsWaitingForRevive(Scene scene)
    {
        return EClass.core.IsGameStarted && scene.mode == Scene.Mode.Zone && EClass.pc is { isDead: true } pc &&
               EClass.player.deathDialog && pc.GetBool(PlayerRevive.DeadFlag) && PlayerRevive.Active;
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
