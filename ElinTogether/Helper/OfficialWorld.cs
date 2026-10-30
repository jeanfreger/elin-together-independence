using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     Makes a new world for a server without anyone at the character screen (PLAN_official_server: the official
///     world). `-empnewworld &lt;save id&gt;` on the command line, Debug and Release, headless or not: at the title the
///     game starts a new game under that save id with the game's default character (what the quick start makes: a
///     random look of the player's saved ones), at the normal start (the meadow, Ashland and Fiama there), claims the
///     start land so that the session can be hosted on it (ElinNetHost.StartServer needs a home base), saves, writes
///     the result to ElinMP/newworld.txt and quits. Never over a save that already exists. <br />
///     The same two steps can be run by hand through the bench bridge on a Debug game:
///     `ElinTogether.Helper.OfficialWorld.Create("world_official")`, then once the game shows the new map,
///     `ElinTogether.Helper.OfficialWorld.Finish()`
/// </summary>
public static class OfficialWorld
{
    // the character of the world owner: on the server it is retired, the stand-in plays the seat (ServerAvatar)
    internal const string OwnerName = "Elin Together";

    /// <summary>
    ///     On the character a new official world is made with: the server's own, never a player's (user 2026-10-30:
    ///     "the server is not invisible and is considered a player"). See ElinNetHost.RetireServerOwnChara
    /// </summary>
    internal const string ServerOwnFlag = "emp_server_own";

    /// <summary>
    ///     That character is the one a server made its world with (-empnewworld). A world made before the flag knows it
    ///     by the name it was given
    /// </summary>
    internal static bool IsServerOwn(Chara? chara)
    {
        return chara is not null && (chara.GetBool(ServerOwnFlag) || chara.c_altName == OwnerName);
    }

    private static readonly string? _requested = Arg("-empnewworld");
    private static int _step;
    private static float _next;
    private static float _inZoneSince;

    internal static bool Requested => _requested is not null;

    private static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static string SavePath(string saveId) => CorePath.RootSave + saveId;

    /// <summary>
    ///     Starts the new game under that save id, from the title. Returns what happened, "error: ..." when refused
    /// </summary>
    public static string Create(string saveId)
    {
        if (string.IsNullOrEmpty(saveId) || saveId.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')) {
            return "error: the save id may only have letters, digits, '_' and '-'";
        }

        if (Directory.Exists(SavePath(saveId))) {
            return $"error: the save {saveId} already exists, nothing was made";
        }

        if (EClass.core.IsGameStarted || EClass.scene.mode != Scene.Mode.Title) {
            return "error: a new world is made from the title screen only";
        }

        EmpLog.Information("New world: creating {Save}", saveId);
        Game.Create(saveId);
        EClass.pc.c_altName = OwnerName;
        EClass.pc.SetBool(ServerOwnFlag, true);

        // without the title's actor the game starts where its debug setting says: the normal start (the home
        // meadow, at the prologue's place), as a player's new game ends
        var scene = EClass.debug.startScene;
        EClass.debug.startScene = CoreDebug.StartScene.Home;
        try {
            EClass.game.StartNewGame();
        } finally {
            EClass.debug.startScene = scene;
        }

        return $"creating {saveId}";
    }

    /// <summary>
    ///     Once the new map is shown: the start land is claimed (as the claim with Ashland does) and the world saved
    /// </summary>
    public static string Finish()
    {
        if (!EClass.core.IsGameStarted || EClass.pc is null || EClass._zone is null) {
            return "error: no game";
        }

        if (EClass.pc.homeBranch is null) {
            EClass._zone.ClaimZone(false, EClass.pc.pos);
            try {
                // what the game's claim scene gives (DramaOutcome.OnClaimLand): the home quest moves on
                var ash = EClass.game.cards.globalCharas.Find("ashland");
                if (ash is not null) {
                    EClass.game.quests.globalList.Add(Quest.Create("sharedContainer").SetClient(ash, assignQuest: false));
                    EClass.game.quests.globalList.Add(Quest.Create("crafter").SetClient(ash, assignQuest: false));
                    EClass.game.quests.globalList.Add(Quest.Create("defense").SetClient(ash, assignQuest: false));
                }

                EClass.game.quests.Get<QuestHome>()?.ChangePhase(2);
            } catch (Exception ex) {
                EmpLog.Warning(ex, "New world: the home quest did not move on (the land is claimed all the same)");
            }
        }

        if (EClass.pc.homeBranch?.owner is null) {
            return "error: the start land could not be claimed";
        }

        EClass.game.Save(false, true);
        var result = $"ok {Game.id}: {EClass._zone.ZoneFullName}, land claimed, saved";
        EmpLog.Information("New world: {Result}", result);
        return result;
    }

    /// <summary>
    ///     Called every frame: the command line's new world, step by step
    /// </summary>
    internal static void Update()
    {
        if (!Requested || _step >= 3 || Time.unscaledTime < _next) {
            return;
        }

        _next = Time.unscaledTime + 1f;
        AudioListener.volume = 0f;
        try {
            switch (_step) {
                case 0 when EClass.scene.mode == Scene.Mode.Title && !EClass.core.IsGameStarted:
                    // (a help page the title opens on a first start would stay over it)
                    foreach (var layer in EClass.ui.layers.FindAll(l => l is LayerHelp or Dialog)) {
                        layer.Close();
                    }

                    var created = Create(_requested!);
                    if (created.StartsWith("error")) {
                        Done(created);
                        return;
                    }

                    _step = 1;
                    return;
                case 1 when EClass.core.IsGameStarted && EClass.pc?.currentZone == EClass._zone && EClass._map is not null:
                    // a few seconds on the map, as a player who looks around before claiming
                    if (_inZoneSince <= 0f) {
                        _inZoneSince = Time.unscaledTime;
                    }

                    if (Time.unscaledTime - _inZoneSince < 5f) {
                        return;
                    }

                    _step = 2;
                    Done(Finish());
                    return;
            }
        } catch (Exception ex) {
            EmpLog.Error(ex, "New world: failed");
            Done("error: " + ex.Message);
        }
    }

    private static void Done(string result)
    {
        _step = 3;
        try {
            var folder = Path.Combine(Application.persistentDataPath, "ElinMP");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "newworld.txt"), result + Environment.NewLine);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            EmpLog.Warning(ex, "New world: the result file could not be written");
        }

        EmpLog.Information("New world: {Result}, quitting", result);
        Application.Quit();
    }
}
