using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ElinTogether.Helper.Steam;
using ElinTogether.LangMod;
using ElinTogether.Net;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Mod = ElinTogether.Helper.ModListFile.Mod;

namespace ElinTogether.Helper;

/// <summary>
///     The mods of the game joined, without subscribing to them (client setting FetchMods, off: then nothing in
///     here runs but Boot, which only looks for what an earlier run left). As Civilization VI does, except that
///     Elin activates its mods when it starts: the Workshop mods missing here are downloaded (the Steam account
///     subscribes to nothing), Elin restarts once with exactly the mods of that game and joins it again by itself,
///     and the player's own list is back at the start after. <br />
///     What is on the disk, in order (dev/PLAN_mods_de_l_host.md has what a crash leaves at each step): <br />
///     1. `loadorder.txt`, the player's list, gets a line that switches off every folder about to appear (Hide);
///     2. the downloads (Steam's own folder); 3. the ticket (`ElinMP/modsession.txt`: where to come back);
///     4. the player's list copied beside itself (`loadorder.elintogether-player.txt`); 5. when the game only
///     loads the Workshop folders the account is subscribed to (its "sync mods" setting), one link a mod in
///     `Package/` (a directory junction, EmpSession_number); 6. `loadorder.txt` replaced by the list of the
///     session; 7. Elin closes and starts again. <br />
///     The restarted game has read the list of the session when this mod starts in it: Boot puts the player's
///     list back at once, so the list of the session is on the disk only between two runs. Nothing is ever
///     subscribed to, unsubscribed from or deleted in the Workshop; a link is removed, never what it points to
/// </summary>
internal static class ModFetch
{
    // first line of a loadorder.txt written here (no comma in it: the game takes it for nothing)
    private const string Marker = "# elintogether session";
    private const string App = "2135150";

    // as the game's own downloads: an item that neither waits nor moves, Steam silent, and never longer than this
    private const float IdleSeconds = 20f;
    private const float QuietSeconds = 90f;
    private const float TotalSeconds = 900f;
    private const int MaxMods = 200;
    private const double TicketMinutes = 30;

    private static readonly string _ticket = Path.Combine(Application.persistentDataPath, "ElinMP", "modsession.txt");

    // what was added to the game in this run, to keep switched off in the player's own list
    private static readonly List<string> _hidden = [];

    // this run was started for a session, or tried and gave up: it never restarts Elin (again)
    private static bool _done;
    private static bool _busy;
    private static bool _applied;
    private static bool _pending;
    private static float _titleAt;
    private static string? _return;
    private static string? _wanted;

    /// <summary>
    ///     This run of Elin has the mods of a multiplayer game, not the player's own
    /// </summary>
    internal static bool Session { get; private set; }

    private static string LoadOrder => CorePath.PathLoadOrder;
    private static string PlayerList => CorePath.rootExe + "loadorder.elintogether-player.txt";

    /// <summary>
    ///     As Core.StartCase and ModManager.RefreshMods decide it: the game only loads the Workshop folders the
    ///     account is subscribed to. A mod fetched without subscribing is then shown to it as a local one
    /// </summary>
    private static bool NeedsLinks => !BaseCore.IsOffline && !EClass.debug.skipModSync &&
                                      (EClass.core.config is null || EClass.core.config.other.syncMods) &&
                                      SteamUGC.GetNumSubscribedItems() != 0;

    /// <summary>
    ///     When this mod starts: the game has read loadorder.txt. The list of a session goes away, the player's
    ///     own comes back, the links of a session that is over are removed, and the ticket is read once
    /// </summary>
    internal static void Boot()
    {
        try {
            Session = File.Exists(LoadOrder) && File.ReadAllLines(LoadOrder).FirstOrDefault() == Marker;
            if (!Restore() && Session) {
                // the player's copy is gone: no list at all is every mod on, the game's own default
                File.Delete(LoadOrder);
            }

            if (Session) {
                // closing its mod viewer writes the list of this run, the session's, over the player's
                new Harmony(ModInfo.Guid + ".modsession").Patch(
                    AccessTools.Method(typeof(ModManager), nameof(ModManager.SaveLoadOrder)),
                    new(typeof(ModFetch), nameof(KeepPlayerList)));
            } else {
                RemoveLinks();
            }

            if (File.Exists(_ticket)) {
                var lines = File.ReadAllLines(_ticket);
                // read once, whatever it says: a restart is never followed by another
                File.Delete(_ticket);
                var fresh = lines.Length > 1 && long.TryParse(lines[0], out var ticks) &&
                            Math.Abs((DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMinutes) < TicketMinutes;
                EmpLog.Information("Elin was restarted for the mods of a game: back to {Where} ({State}, session list {Session})",
                    lines.ElementAtOrDefault(1), fresh ? "now" : "too long ago", Session);
                _done = true;
                _wanted = string.Join("\n", lines.Skip(2));
                if (fresh && EmpConfig.Client.FetchMods.Value) {
                    _return = lines[1];
                }
            }
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not put the player's mod list back");
        }

        _done |= Session;
        _pending = Session || _return is not null || _wanted is not null;
    }

    /// <summary>
    ///     Called every frame. Once the title screen is up: what this run is said, and the game is joined again
    /// </summary>
    internal static void Update()
    {
        if (!_pending) {
            return;
        }

        if (EClass.core.IsGameStarted || EClass.ui == null || EClass.ui.GetLayer<LayerTitle>() == null) {
            _titleAt = 0f;
            return;
        }

        if (_titleAt <= 0f) {
            _titleAt = Time.unscaledTime + 2f;
            return;
        }

        if (Time.unscaledTime < _titleAt) {
            return;
        }

        _pending = false;
        // (the mods are all activated by now: what the restart was for, against what this run has)
        if (_wanted is not null) {
            ModList.Compare(ModListFile.Parse(_wanted), "the list this restart was for");
            _wanted = null;
        }

        if (Session) {
            EmpPop.Information("emp_ui_mods_session".lang());
        }

        if (_return is { } where) {
            _return = null;
            Return(where);
        }
    }

    /// <summary>
    ///     The game closes: the links of the session go (the next start removes those a crash leaves), and what
    ///     was fetched without a restart stays switched off in the player's list
    /// </summary>
    internal static void OnQuit()
    {
        try {
            if (Session) {
                RemoveLinks();
            } else if (_hidden.Count > 0 && !_applied) {
                // (the game's mod viewer rewrites the list from the mods it knows: those lines would be gone)
                Hide([]);
            }
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not tidy the mods of the session at the exit");
        }
    }

    /// <summary>
    ///     Fetches what is missing, then restarts Elin with the mods of the reference and comes back to
    ///     <paramref name="returnTo" />. False: nothing is done here (setting off, a run that restarted already,
    ///     Steam away, a list that cannot be trusted) and the caller goes on as without it
    /// </summary>
    /// <param name="reference">the list of the game, as modlist.txt</param>
    internal static bool Begin(string reference, ModList.Diff diff, string returnTo)
    {
        if (!EmpConfig.Client.FetchMods.Value || _done || _busy || returnTo.Length == 0 || BaseCore.IsOffline ||
            ModManager.disableMod || EmpServer.Requested || diff.Missing.Count > MaxMods ||
            diff.Missing.Any(m => !ulong.TryParse(m.Workshop, out var id) || id == 0)) {
            return false;
        }

        _busy = true;
        EmpLog.Information("Fetching {Count} mods of the game, then restarting to come back to {Where}",
            diff.Missing.Count, returnTo);
        EmpMod.Instance.StartCoroutine(Fetch(reference, diff, returnTo));
        return true;
    }

    private static IEnumerator Fetch(string reference, ModList.Diff diff, string returnTo)
    {
        // the caller closes its link with the host first
        yield return null;

        var failure = "error";
        var line = "";
        Action kill = () => { };
        var folders = new Dictionary<string, string>();
        var results = new Dictionary<ulong, EResult>();
        Action<DownloadItemResult_t> onResult = result => {
            if (result.m_unAppID.m_AppId.ToString() == App) {
                results[result.m_nPublishedFileId.m_PublishedFileId] = result.m_eResult;
            }
        };

        try {
            line = "emp_ui_mods_fetching".Loc(0, diff.Missing.Count, "", 0);
            kill = EGui.CreatePopup(() => new(line), _ => !_busy).Kill;
            var links = NeedsLinks;
            var root = WorkshopRoot();
            var packages = EClass.core.mods.packages;
            // a mod that is on this PC, switched off in the player's list, is only switched on (not the link
            // of an earlier session: those are removed when this mod starts)
            var queue = diff.Missing
                .Where(m => !packages.Any(p => ModList.WorkshopOf(p) == m.Workshop && p.installed && p.dirInfo is not null &&
                                               !IsLink(p) && Directory.Exists(p.dirInfo.FullName)))
                .ToList();

            // before anything arrives: the player's own list hides it, a crash from here on leaves its game as it was
            Hide(queue.Select(m => Path.Combine(root, m.Workshop)).Concat(links ? queue.Select(m => LinkPath(m.Workshop)) : []));

            SteamCallback<DownloadItemResult_t>.Add(onResult);
            var started = Time.realtimeSinceStartup;
            for (var i = 0; i < queue.Count; i++) {
                var mod = queue[i];
                var number = ulong.Parse(mod.Workshop);
                var id = new PublishedFileId_t(number);
                if (!SteamUGC.DownloadItem(id, true)) {
                    failure = $"Steam did not start the download of {mod.Workshop}";
                    yield break;
                }

                var moved = Time.realtimeSinceStartup;
                ulong got = 0;
                while (true) {
                    var now = Time.realtimeSinceStartup;
                    var state = (EItemState)SteamUGC.GetItemState(id);
                    var waiting = (state & EItemState.k_EItemStateDownloadPending) != 0;
                    var coming = (state & EItemState.k_EItemStateDownloading) != 0;
                    var percent = 0UL;
                    if (results.TryGetValue(number, out var result) && result != EResult.k_EResultOK) {
                        failure = $"the download of {mod.Workshop} failed ({result})";
                        yield break;
                    }

                    if ((state & EItemState.k_EItemStateInstalled) != 0 && !waiting && !coming &&
                        (state & EItemState.k_EItemStateNeedsUpdate) == 0 &&
                        SteamUGC.GetItemInstallInfo(id, out _, out var folder, 1024, out _) && !string.IsNullOrEmpty(folder)) {
                        // what the list of another player names is a mod of Elin, or it is not loaded
                        if (!File.Exists(Path.Combine(folder, "package.xml"))) {
                            failure = $"{mod.Workshop} is not a mod of Elin";
                            yield break;
                        }

                        folders[mod.Workshop] = folder;
                        break;
                    }

                    if (coming && SteamUGC.GetItemDownloadInfo(id, out var have, out var total)) {
                        percent = total > 0 ? have * 100 / total : 0;
                        if (have != got) {
                            got = have;
                            moved = now;
                        }
                    }

                    if ((!waiting && !coming && now - moved > IdleSeconds) || now - moved > QuietSeconds ||
                        now - started > TotalSeconds) {
                        failure = $"the download of {mod.Workshop} does not move";
                        yield break;
                    }

                    if (Input.GetKey(KeyCode.Escape) || EClass.core.IsGameStarted) {
                        failure = "cancelled by the player";
                        yield break;
                    }

                    line = "emp_ui_mods_fetching".Loc(i + 1, queue.Count, ModList.Name(mod), percent);
                    yield return null;
                }
            }

            // (Elin is about to close: never under a game the player started meanwhile)
            // client setting KeepMods: the player keeps the mods of this game. Its Steam account subscribes to
            // them and they are switched on in its own list, so the next start of Elin has them and joining this
            // game again needs no restart (without it they are fetched, hidden, and Elin restarts at every start)
            var keep = new List<string>();
            if (EmpConfig.Client.KeepMods.Value && !EClass.core.IsGameStarted) {
                foreach (var mod in diff.Missing) {
                    var folder = folders.TryGetValue(mod.Workshop, out var fetched)
                        ? fetched
                        : packages.FirstOrDefault(p => ModList.WorkshopOf(p) == mod.Workshop && p.dirInfo is not null && !IsLink(p))?.dirInfo.FullName;
                    if (folder is null) {
                        continue;
                    }

                    keep.Add(folder);
                    SteamUGC.SubscribeItem(new PublishedFileId_t(ulong.Parse(mod.Workshop)));
                    EmpLog.Information("Keeping the mod {Workshop} of the game: subscribed, on in the player's own list", mod.Workshop);
                }
            }

            failure = EClass.core.IsGameStarted ? "the player started a game" :
                Apply(reference, returnTo, folders, links, keep) ?? "";
        } finally {
            SteamCallback<DownloadItemResult_t>.Remove(onResult);
            if (failure.Length > 0) {
                GiveUp(failure, returnTo);
            }

            kill();
        }

        if (!_applied) {
            yield break;
        }

        // the list of the session is written: Elin closes, and starts again by itself where it can
        var restarts = Relaunch();
        EmpPop.Information((restarts ? "emp_ui_mods_restart" : "emp_ui_mods_restart_manual").lang());
        yield return new WaitForSecondsRealtime(restarts ? 3f : 8f);
        if (EClass.core.IsGameStarted) {
            // the player started a game meanwhile: it goes on, and its own list is back
            _applied = false;
            try {
                File.Delete(_ticket);
                Restore();
                RemoveLinks();
            } catch (Exception ex) {
                EmpLog.Warning(ex, "Could not undo the mod list of the session");
            }

            yield break;
        }

        Application.Quit();
    }

    /// <summary>
    ///     Nothing was changed but Steam's own folder (hidden in the player's list): said, and the game is joined
    ///     as without this, where the message that names the mods is shown if it is refused
    /// </summary>
    private static void GiveUp(string why, string returnTo)
    {
        _busy = false;
        _done = true;
        EmpLog.Warning("The mods of the game were not fetched: {Why}", why);
        EmpPop.Information("emp_ui_mods_fetch_failed".lang());
        EClass.core.actionsNextFrame.Add(() => {
            if (!EClass.core.IsGameStarted) {
                Return(returnTo);
            }
        });
    }

    /// <returns>null when the list of the session is written, else why not (and everything is as it was)</returns>
    private static string? Apply(string reference, string returnTo, Dictionary<string, string> folders, bool links, List<string> keep)
    {
        try {
            // (where Steam really put them, should it not be where it was expected)
            Hide(folders.Values);
            // before the player's list is kept aside: what it keeps is on in it
            Show(keep);

            if (!File.Exists(LoadOrder)) {
                File.WriteAllText(LoadOrder, "");
            } else if (File.ReadAllLines(LoadOrder).FirstOrDefault() == Marker) {
                throw new IOException("the list of an earlier session is still in place");
            }

            File.Copy(LoadOrder, PlayerList, true);

            if (links) {
                foreach (var (workshop, folder) in folders) {
                    if (!Link(LinkPath(workshop), folder)) {
                        throw new IOException("no link for " + workshop);
                    }
                }
            }

            Write(LoadOrder, SessionList(ModListFile.Parse(reference), reference.Contains(ModList.LookOwn)));
            // last: a ticket without the list of the session would bring the player back without its mods
            Directory.CreateDirectory(Path.GetDirectoryName(_ticket)!);
            File.WriteAllText(_ticket, $"{DateTime.UtcNow.Ticks}\n{returnTo}\n{reference}");
            _applied = true;
            return null;
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not write the mod list of the session");
            try {
                File.Delete(_ticket);
                Restore();
                RemoveLinks();
            } catch (Exception undo) {
                // the next start does the same (Boot)
                EmpLog.Warning(undo, "Could not undo the mod list of the session");
            }

            return ex.Message;
        }
    }

    /// <summary>
    ///     loadorder.txt for one session: every mod the game knows, in the player's order, on when the reference
    ///     has it. This mod and what it runs on stay as the player has them, and the mods that only change the look
    ///     when the game says each player keeps their own. What was fetched is not listed: a mod that is not
    ///     listed is on
    /// </summary>
    internal static List<string> SessionList(List<Mod> reference, bool ownLooks)
    {
        var packages = EClass.core.mods.packages
            .Where(p => !p.builtin && p.dirInfo is not null && !IsLink(p) && ModLoadOrderPreset.IsValidId(p.id))
            .ToList();
        var workshops = new HashSet<string>(reference.Where(m => m.Workshop.Length > 0).Select(m => m.Workshop));
        var ids = new HashSet<string>(reference.Where(m => m.Workshop.Length == 0 && m.Id.Length > 0).Select(m => BaseModPackage.NormalizeId(m.Id)));

        // never switched off: this mod, the mods whose libraries it is built on (those that come neither with the
        // game nor with this mod: YK Framework), and what those declare they need
        var own = Path.GetDirectoryName(EmpMod.Assembly.Location) ?? "";
        var needed = EmpMod.Assembly.GetReferencedAssemblies()
            .Select(a => a.Name + ".dll")
            .Where(dll => !File.Exists(Path.Combine(Application.dataPath, "Managed", dll)) &&
                          !File.Exists(Path.Combine(BepInEx.Paths.BepInExAssemblyDirectory, dll)) &&
                          !File.Exists(Path.Combine(own, dll)))
            .ToList();
        // nor the mod that brings the language the player reads the game in (French...): switched off, the game
        // fell back to the English texts but still looked for that language's files, and every talk to a
        // character threw (DirectoryNotFoundException on Lang/EN/Dialog/dialog.xlsx, a real session, 0.26.597)
        var lang = global::Lang.langCode ?? "";
        var speaks = lang.Length == 0 || global::Lang.IsBuiltin(lang)
            ? []
            : packages.Where(p => Directory.Exists(Path.Combine(p.dirInfo.FullName, "Lang", lang))).ToList();
        var kept = new HashSet<string>();
        var todo = new Stack<string?>(packages
            .Where(p => needed.Any(dll => File.Exists(Path.Combine(p.dirInfo.FullName, dll))))
            .Concat(speaks)
            .Select(p => BaseModPackage.NormalizeId(p.id))
            .Append(ModInfo.Guid));
        while (todo.Count > 0) {
            if (todo.Pop() is not { } id || !kept.Add(id)) {
                continue;
            }

            foreach (var needs in packages.Where(p => BaseModPackage.NormalizeId(p.id) == id).SelectMany(p => p.dependency ?? [])) {
                foreach (var other in needs ?? []) {
                    todo.Push(BaseModPackage.NormalizeId(other));
                }
            }
        }

        var lines = new List<string> { Marker };
        foreach (var package in packages) {
            var id = BaseModPackage.NormalizeId(package.id);
            var workshop = ModList.WorkshopOf(package);
            var on = kept.Contains(id) || (ownLooks && ModList.OnlyLook(package)) ? package.willActivate :
                workshop.Length > 0 ? workshops.Contains(workshop) : ids.Contains(id);
            lines.Add($"{package.dirInfo.FullName},{(on ? 1 : 0)},{package.id.Trim()}");
        }

        return lines;
    }

    /// <summary>
    ///     The player's own list gets a line "folder,0" for each of these folders that it does not name yet (and
    ///     for those hidden earlier in this run): what is fetched never loads in a game that is not the session
    /// </summary>
    private static void Hide(IEnumerable<string> folders)
    {
        _hidden.AddRange(folders.Select(Path.GetFullPath).Where(f => !_hidden.Contains(f, StringComparer.OrdinalIgnoreCase)));
        var lines = File.Exists(LoadOrder) ? File.ReadAllLines(LoadOrder).ToList() : [];
        var count = lines.Count;
        foreach (var folder in _hidden) {
            var start = folder.Replace('\\', '/') + ",";
            if (!lines.Any(l => l.Replace('\\', '/').StartsWith(start, StringComparison.OrdinalIgnoreCase))) {
                lines.Add(folder + ",0");
            }
        }

        if (lines.Count > count) {
            Write(LoadOrder, lines);
        }
    }

    /// <summary>
    ///     The player's own list has these folders switched on, and they are no longer hidden at the exit: the
    ///     mods of a game the player chose to keep (KeepMods)
    /// </summary>
    private static void Show(List<string> folders)
    {
        if (folders.Count == 0) {
            return;
        }

        var kept = folders.Select(Path.GetFullPath).ToList();
        _hidden.RemoveAll(h => kept.Contains(h, StringComparer.OrdinalIgnoreCase));
        var lines = File.Exists(LoadOrder) ? File.ReadAllLines(LoadOrder).ToList() : [];
        foreach (var folder in kept) {
            var start = folder.Replace('\\', '/') + ",";
            var at = lines.FindIndex(l => l.Replace('\\', '/').StartsWith(start, StringComparison.OrdinalIgnoreCase));
            if (at < 0) {
                lines.Add(folder + ",1");
                continue;
            }

            // "folder,0" or "folder,0,id": only the switch changes
            var rest = lines[at].Substring(start.Length);
            var comma = rest.IndexOf(',');
            lines[at] = folder + ",1" + (comma < 0 ? "" : rest.Substring(comma));
        }

        Write(LoadOrder, lines);
    }

    /// <summary>
    ///     The player's list, kept aside, back in its place. False: none was kept
    /// </summary>
    private static bool Restore()
    {
        if (!File.Exists(PlayerList)) {
            return false;
        }

        File.Copy(PlayerList, LoadOrder, true);
        File.Delete(PlayerList);
        EmpLog.Information("The player's own mod list is back in loadorder.txt");
        return true;
    }

    // beside the file, then over it: a write cut short leaves the list as it was
    private static void Write(string file, List<string> lines)
    {
        File.WriteAllLines(file + ".tmp", lines);
        if (File.Exists(file)) {
            File.Replace(file + ".tmp", file, null);
        } else {
            File.Move(file + ".tmp", file);
        }
    }

    private static string WorkshopRoot()
    {
        return EClass.core.mods.dirWorkshop?.FullName ??
               Path.GetFullPath(Path.Combine(CorePath.rootExe, "../../workshop/content/" + App));
    }

    private static string LinkPath(string workshop)
    {
        return Path.GetFullPath(Path.Combine(BaseModManager.rootMod, ModList.SessionFolder + workshop));
    }

    /// <summary>
    ///     A directory junction in Package/ to a folder of the Workshop: the game loads it as a local mod. No
    ///     right is needed to make one, and nothing is copied
    /// </summary>
    private static bool Link(string link, string target)
    {
        var there = Path.Combine(link, "package.xml");
        if (File.Exists(there)) {
            return true;
        }

        Unlink(link);
        Run("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")?.WaitForExit(10000);
        return File.Exists(there);
    }

    private static bool IsLink(BaseModPackage package)
    {
        return package.dirInfo.Name.StartsWith(ModList.SessionFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveLinks()
    {
        foreach (var link in Directory.GetDirectories(BaseModManager.rootMod, ModList.SessionFolder + "*")) {
            Unlink(link);
        }
    }

    /// <summary>
    ///     Only ever a link, and only the link: a real folder of that name is not ours, and deleting without
    ///     going into it cannot touch what the link points to
    /// </summary>
    private static void Unlink(string link)
    {
        try {
            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) {
                Directory.Delete(link, false);
                EmpLog.Information("Removed the link {Link} of a mod fetched for a session", link);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // not there, or held: it is switched off in the player's list, and the next start tries again
        }
    }

    /// <summary>
    ///     Elin allows one instance, and a player's install only starts through Steam: something small waits for
    ///     this game to be gone, then asks Steam for it. False: the player starts Elin again (the ticket is there)
    /// </summary>
    private static bool Relaunch()
    {
        // a copy that starts without Steam (the bench, a developer's): Steam would start another Elin than this one
        if (File.Exists(CorePath.rootExe + "steam_appid.txt")) {
            return false;
        }

        var pid = Process.GetCurrentProcess().Id;
        return Run("cmd.exe",
            $"/c for /l %i in (1,1,120) do @(tasklist /fi \"PID eq {pid}\" | find \"{pid}\" >nul && ping -n 2 127.0.0.1 >nul || " +
            $"(ping -n 3 127.0.0.1 >nul & start \"\" \"steam://rungameid/{App}\" & exit))") is not null;
    }

    private static Process? Run(string exe, string args)
    {
        try {
            var start = new ProcessStartInfo(exe, args) {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // the mod loader marks this process as done, a game inheriting the mark starts without any mod
            foreach (var name in start.EnvironmentVariables.Keys.Cast<string>().ToArray()) {
                if (name.StartsWith("DOORSTOP", StringComparison.OrdinalIgnoreCase)) {
                    start.EnvironmentVariables.Remove(name);
                }
            }

            return Process.Start(start);
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not start {Exe}", exe);
            return null;
        }
    }

    private static void Return(string where)
    {
        EmpLog.Information("Coming back to the game by itself: {Where}", where);
        var words = where.Split([' '], 2);
        var session = NetSession.Instance;
        try {
            switch (words[0]) {
                case "depot" when SaveDepot.Enabled:
                    SaveDepot.Take();
                    break;
                case "lobby" when words.Length > 1 && ulong.TryParse(words[1], out var lobby) && lobby != 0:
                    session.Lobby.ConnectLobby(lobby);
                    break;
                case "address" when words.Length > 1:
                    session.InitializeComponent<ElinNetClient>().ConnectAddress(words[1]);
                    break;
#if DEBUG
                case "port" when words.Length > 1 && ushort.TryParse(words[1], out var port):
                    session.InitializeComponent<ElinNetClient>().ConnectLocalPort(port);
                    break;
#endif
            }
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not come back to the game by itself");
        }
    }

    // prefix of ModManager.SaveLoadOrder, in a session only (Boot)
    private static bool KeepPlayerList()
    {
        EmpPop.Information("emp_ui_mods_session_locked".lang());
        return false;
    }
}
