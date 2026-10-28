using System;
using System.IO;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using UnityEngine;

namespace ElinTogether;

/// <summary>
///     A game started with `-empserver &lt;save id&gt;` is a server, as one runs a Minecraft server: it loads that
///     save and opens the session by itself, nobody plays it, the players join by address
///     (`&lt;this machine&gt;:55556`) whenever they want. `cloud:&lt;id&gt;` for a Steam Cloud save,
///     `world_depot` to take the world from the save depot when one is set (see SaveDepot). <br />
///     It tells its state in `ElinMP/server.txt` and stops, after saving, when `ElinMP/server.stop` appears:
///     that is how the server application (dev/server) shows and drives it
/// </summary>
internal static class EmpServer
{
    private const float SaveSeconds = 300f;

    // a copy of the world in the game's own backups (GameIO.MakeBackup, it keeps the last ones): what an operator
    // restores after a griefer or a broken save (council 2026-10-27, 50 players: snapshots)
    private const float BackupSeconds = 3600f;

    private static readonly string? _save = Arg("-empserver");
    private static readonly string _folder = Path.Combine(Application.persistentDataPath, "ElinMP");

    private static float _next;
    private static float _nextSave;
    private static float _nextBackup;
    private static bool _loading;
    private static string _saved = "-";
    private static string _order = "";
    private static int _lastPlayers;
    private static int _ownFrameRate;
    private static bool _resting;
    private static readonly DateTime _started = DateTime.Now;

    internal static bool Requested => _save is not null;

    private static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>
    ///     Called every frame
    /// </summary>
    internal static void Update()
    {
        // every frame, not on the 2-second tick: a player who connects to a resting server is served at full rate
        // from the next frame, the join (several round trips) does not crawl at 5 frames a second
        if (Requested) {
            Rest();
        }

        if (!Requested || Time.unscaledTime < _next) {
            return;
        }

        _next = Time.unscaledTime + 2f;
        AudioListener.volume = 0f;

        var session = NetSession.Instance;
        if (!EClass.core.IsGameStarted) {
            Tell("loading");
            // (the title has its own layers open: no waiting for an idle interface)
            if (_loading || EClass.scene.mode != Scene.Mode.Title) {
                return;
            }

            _loading = true;
            File.Delete(Path.Combine(_folder, "server.stop"));
            EmpLog.Information("Server: loading {Save}", _save);
            if (_save == SaveDepot.WorldId && SaveDepot.Enabled) {
                SaveDepot.Take();
            } else {
                // "cloud:<id>" for a Steam Cloud save, as the game's own list loads them
                var cloud = _save!.StartsWith("cloud:");
                Game.Load(cloud ? _save.Substring(6) : _save, cloud);
            }

            return;
        }

        if (EClass.pc is null || EClass._map is null) {
            return;
        }

        if (session.Transport is null) {
            // a question left open by the load (a mod the save knew is missing) would hold the world
            foreach (var dialog in EClass.ui.layers.FindAll(l => l is Dialog)) {
                dialog.Close();
            }

            // without these two the session never opens: say why and stop, instead of trying for ever
            if (EClass.player?.chara?.homeBranch?.owner is null) {
                Fail("this save has no home base (claim a land in it first)");
                return;
            }

            EmpLog.Information("Server: opening the session on port {Port}", Common.EmpConstants.LocalPort);
            try {
                session.InitializeComponent<ElinNetHost>().StartServer(true, alsoSteam: true);
            } catch (Exception ex) {
                Fail($"port {Common.EmpConstants.LocalPort} could not be opened ({ex.Message})");
                return;
            }

            _nextSave = Time.unscaledTime + SaveSeconds;
            _nextBackup = Time.unscaledTime + BackupSeconds;
            return;
        }

        if (File.Exists(Path.Combine(_folder, "server.stop"))) {
            EmpLog.Information("Server: asked to stop, saving");
            File.Delete(Path.Combine(_folder, "server.stop"));
            EClass.game.Save(false, true);
            Tell("stopped");
            Application.Quit();
            return;
        }

        // orders of the operator (kick, ban, unban), one per line; taken whole, so a line half written is not run
        var orders = Path.Combine(_folder, "server.cmd");
        if (File.Exists(orders) && session.Connection is ElinNetHost host) {
            try {
                var taken = orders + ".run";
                File.Delete(taken);
                File.Move(orders, taken);
                foreach (var line in File.ReadAllLines(taken).Where(l => l.Trim().Length > 0)) {
                    _order = line.Trim() == "backup" ? Backup() : host.RunOrder(line);
                    EmpLog.Information("Server: order {Order}: {Result}", line, _order);
                }

                File.Delete(taken);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // the application writes it at the same time: next tick
            }
        }

        // nobody is there to save: the server does, while someone plays
        if (Time.unscaledTime >= _nextSave) {
            _nextSave = Time.unscaledTime + SaveSeconds;
            if (session.CurrentPlayers.Count > 1) {
                SaveNow();
            }
        }

        // the last player out: what it did is in the world, saved at once (reliability audit 2026-10-27)
        var players = session.CurrentPlayers.Count;
        if (players < _lastPlayers && players <= 1) {
            SaveNow();
        }

        _lastPlayers = players;

        if (Time.unscaledTime >= _nextBackup) {
            Backup();
        }

        Tell("running");
    }

    /// <summary>
    ///     ServerRestsWhenEmpty: nobody connected, nobody to simulate for (the world is frozen, EmptyWorld), so the
    ///     machine that keeps the server on all day mostly sleeps. Only the frame rate and the sound: the world, the
    ///     session and the saves stay as they are, and the game's own rate comes back as soon as a peer is there
    /// </summary>
    private static void Rest()
    {
        var rest = EmpConfig.Server.ServerRestsWhenEmpty.Value && EClass.core.IsGameStarted &&
                   NetSession.Instance.Transport is ElinNetHost host && !host.PeerIds.Any();
        if (rest == _resting) {
            return;
        }

        _resting = rest;
        if (rest) {
            _ownFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 5;
        } else {
            Application.targetFrameRate = _ownFrameRate;
        }

        AudioListener.pause = rest;
        EmpLog.Information("Server: {State}", rest ? "nobody connected, resting at 5 frames a second" : "awake");
    }

    /// <summary>
    ///     The world as it is now, saved then copied to the game's backups (also the operator's order "backup")
    /// </summary>
    private static string Backup()
    {
        _nextBackup = Time.unscaledTime + BackupSeconds;
        try {
            EClass.game.Save(true, true);
            GameIO.MakeBackup(new GameIndex { id = Game.id, cloud = EClass.game.isCloud });
            EmpLog.Information("Server: backup of {Save} made", Game.id);
            return "Backup made " + DateTime.Now.ToString("HH:mm");
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Server: backup of {Save} failed", Game.id);
            return "error: backup failed";
        }
    }

    private static void SaveNow()
    {
        if (EClass.game.Save(true, true)) {
            _saved = DateTime.Now.ToString("HH:mm");
        } else {
            _saved = "FAILED " + DateTime.Now.ToString("HH:mm");
            EmpLog.Warning("Server: the game could not save the world, see Player.log");
        }
    }

    private static void Fail(string reason)
    {
        EmpLog.Error("Server: cannot start, {Reason}", reason);
        Tell("error:" + reason.Split('\n')[0].Trim());
        Application.Quit();
    }

    private static void Tell(string state)
    {
        try {
            var playing = state == "running" ? NetSession.Instance.CurrentPlayers.Where(p => p.Index != 0).ToList() : [];
            var players = playing.Select(p => EClass.game.cards.globalCharas.Find(p.CharaUid)?.Name ?? "?");
            Directory.CreateDirectory(_folder);
            File.WriteAllLines(Path.Combine(_folder, "server.txt"), [
                "state=" + state,
                "save=" + _save,
                "port=" + Common.EmpConstants.LocalPort,
                "players=" + string.Join("|", players),
                // (the Steam ids in the same order, for the operator's orders)
                "ids=" + string.Join("|", playing.Select(p => (ulong)p.User)),
                "order=" + _order,
                // (the banned accounts of this world, id and reason, for the app's list with Unban)
                "banned=" + string.Join("|", ElinNetHost.Bans.Select(b => b.Key + ":" + b.Value.Replace("|", " "))),
                "started=" + _started.ToString("yyyy-MM-dd HH:mm:ss"),
                "date=" + (state == "running" ? EClass.world.date.GetText(Date.TextFormat.Log) : ""),
                "saved=" + _saved,
                "time=" + DateTime.Now.ToString("HH:mm:ss"),
            ]);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // the application reads it at the same time: next tick
        }
    }
}
