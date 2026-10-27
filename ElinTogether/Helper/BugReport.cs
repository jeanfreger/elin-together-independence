using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ElinTogether.Net;
using Newtonsoft.Json;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace ElinTogether.Helper;

/// <summary>
///     One zip a player can send when something goes wrong: the game's own log (where the game writes its
///     exceptions, which the mod's log does not have), the last logs of the mod, and a short note of the
///     situation. Until now reports came as screenshots, or with the one log that did not hold the error <br />
///     Never in it: the saves, and the settings file (it holds the depot's password or access key)
/// </summary>
internal static class BugReport
{
    private const int ModLogs = 4;

    /// <returns>the zip written, or null with the reason logged</returns>
    internal static string? Make()
    {
        try {
            var data = Application.persistentDataPath;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var folder = Directory.Exists(desktop) ? desktop : data;
            var path = Path.Combine(folder, $"ElinTogether-rapport-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            using (var file = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) {
                Add(zip, "info.txt", Encoding.UTF8.GetBytes(Describe()));
                // the last seconds of the screen (ReplayBuffer), already JPEG: stored as they are
                foreach (var (name, jpg) in ReplayBuffer.Snapshot()) {
                    using var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.NoCompression).Open();
                    entry.Write(jpg, 0, jpg.Length);
                }

                if (ReplayBuffer.Gif() is { } gif) {
                    using var entry = zip.CreateEntry("replay.gif", System.IO.Compression.CompressionLevel.NoCompression).Open();
                    entry.Write(gif, 0, gif.Length);
                }

                foreach (var name in new[] { "Player.log", "Player-prev.log" }) {
                    AddFile(zip, Path.Combine(data, name), name);
                }

                var logs = Path.Combine(data, "ElinMP", "Logs");
                if (Directory.Exists(logs)) {
                    foreach (var log in new DirectoryInfo(logs).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(ModLogs)) {
                        AddFile(zip, log.FullName, "ElinMP/" + log.Name);
                    }
                }
            }

            EmpLog.Information("Bug report written to {Path}", path);
            return path;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            EmpLog.Warning(ex, "The bug report could not be written");
            return null;
        }
    }

    private static string Describe()
    {
        var session = NetSession.Instance;
        var text = new StringBuilder();
        text.AppendLine($"Elin Together {ModInfo.BuildVersion}");
        text.AppendLine($"Elin {EClass.core.version.GetText()}");
        // who sent it, to ask them more (user, 2026-10-27; the player is told it is public)
        text.AppendLine($"Player (Steam): {SteamName()}");
        text.AppendLine($"Written {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local), {DateTime.UtcNow:HH:mm:ss} UTC");
        text.AppendLine($"Role: {(session.Transport is null ? "no session" : session.Transport.GetType().Name)}, away: {session.IsAway}, guest: {session.IsGuest}");
        text.AppendLine($"Players: {session.CurrentPlayers.Count}");
        if (EClass.core.IsGameStarted) {
            text.AppendLine($"Zone: {EClass._zone?.id} #{EClass._zone?.uid}, date raw {EClass.world.date.GetRaw()}");
        }

        try {
            // (who the ally count on screen is made of: "one too many" was reported with nothing to tell which)
            if (EClass.pc?.party is { } party) {
                text.AppendLine($"Party of {EClass.pc.uid}: count {party.Count()}, leader {party.uidLeader}, own slots {CompanionHelper.UsedAllySlots(EClass.pc)}");
                foreach (var member in party.members.Where(c => c is not null)) {
                    text.AppendLine($"  {member.uid} {member.id}: owner {member.CompanionOwnerUid}, player {member.IsPC || member.GetBool("remote_chara")}, " +
                                    $"dead {member.isDead}, zone {member.currentZone?.id}, slots {1 + member.Evalue(1431)}");
                }
            }
        } catch (Exception) {
            // noexcept
        }

        try {
            text.AppendLine("Mods: " + string.Join(", ", EClass.core.mods.packages.Where(p => p.activated).Select(p => p.id)));
        } catch (Exception) {
            // (the list of mods is not worth losing the report for)
        }

        return text.ToString();
    }

    private static string SteamName()
    {
        try {
            return SteamFriends.GetPersonaName();
        } catch (Exception) {
            // (Steam not running)
            return "-";
        }
    }

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open();
        entry.Write(bytes, 0, bytes.Length);
    }

    // (the game and the mod keep their logs open: read without asking for them alone)
    private static void AddFile(ZipArchive zip, string path, string name)
    {
        if (!File.Exists(path)) {
            return;
        }

        try {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open();
            source.CopyTo(entry);
        } catch (IOException) {
            // noexcept
        }
    }

    // ---------------------------------------------------------------- sent from the game (user, 2026-10-27)

    /// <summary>
    ///     The relay that turns a report into a GitHub issue (dev/relay/worker.js): it holds the GitHub key, the game
    ///     never does. Empty until it is online: the report then opens the issue page, filled, in the browser
    /// </summary>
    internal const string Relay = "https://elin-report.yhhhuii095.workers.dev";

    private const string Issues = "https://github.com/jeanfreger/elin-together-independence/issues/new";

    // what the relay takes at most for the logs
    private const int MaxZip = 6 * 1024 * 1024;

    /// <summary>
    ///     The player's words and its logs go to the developers: a GitHub issue made by the relay, its link given
    ///     back; or, without the relay, the issue page opened filled in the browser and the zip shown to drag in
    /// </summary>
    internal static void Send(string words, Action<string?> done)
    {
        var zip = Make();
        if (Relay.Length == 0) {
            OpenIssuePage(words, zip);
            done(null);
            return;
        }

        EmpMod.Instance.StartCoroutine(Post(words, zip, done));
    }

    private static IEnumerator Post(string words, string? zip, Action<string?> done)
    {
        string payload;
        try {
            var bytes = zip is not null && new FileInfo(zip).Length <= MaxZip ? File.ReadAllBytes(zip) : null;
            payload = JsonConvert.SerializeObject(new {
                title = FirstLine(words),
                text = words,
                info = Describe(),
                zip = bytes is null ? "" : Convert.ToBase64String(bytes),
                version = ModInfo.BuildVersion,
                // what the developers open first, each a file of its own beside the zip (user, 2026-10-27: the logs
                // must be easy to reach): the replay moving, and the ends of the two logs, read in the browser
                files = ReadableFiles(),
            });
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            EmpLog.Warning(ex, "The bug report could not be read to send it");
            OpenIssuePage(words, zip);
            done(null);
            yield break;
        }

        using var request = new UnityWebRequest(Relay.TrimEnd('/') + "/report", "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("X-Emp-Report", "elin-together");
        request.timeout = 60;
        yield return request.SendWebRequest();

        string? url = null;
        if (request.result == UnityWebRequest.Result.Success) {
            try {
                url = JsonConvert.DeserializeAnonymousType(request.downloadHandler.text, new { url = "" })?.url;
            } catch (JsonException) {
            }
        }

        if (url is null) {
            EmpLog.Warning("The relay did not take the bug report ({Code}: {Error}), the issue page opens instead",
                request.responseCode, request.error);
            OpenIssuePage(words, zip);
        } else {
            EmpLog.Information("Bug report sent: {Url}", url);
        }

        done(url);
    }

    // the end of a log is where the bug is: past this, only the last part goes as its own file (the zip has it all)
    private const int LogTail = 1536 * 1024;

    private static List<object> ReadableFiles()
    {
        var files = new List<object>();
        if (ReplayBuffer.Gif() is { } gif) {
            files.Add(new { name = "replay.gif", content = Convert.ToBase64String(gif) });
        }

        var data = Application.persistentDataPath;
        var modLog = new DirectoryInfo(Path.Combine(data, "ElinMP", "Logs")) is { Exists: true } logs
            ? logs.GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault(f => f.Name.StartsWith("Session"))
            : null;
        foreach (var (path, name) in new[] { (Path.Combine(data, "Player.log"), "Player.log"), (modLog?.FullName, "ElinTogether.log") }) {
            if (path is not null && Tail(path) is { } text) {
                files.Add(new { name, content = Convert.ToBase64String(text) });
            }
        }

        return files;
    }

    private static byte[]? Tail(string path)
    {
        try {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(source.Length, LogTail);
            source.Seek(-length, SeekOrigin.End);
            var bytes = new byte[length];
            var read = 0;
            while (read < length && source.Read(bytes, read, length - read) is > 0 and var n) {
                read += n;
            }

            return bytes;
        } catch (IOException) {
            return null;
        }
    }

    private static string FirstLine(string words)
    {
        var line = words.Split('\n')[0].Trim();
        return line.Length > 80 ? line.Substring(0, 80) + "…" : line;
    }

    /// <summary>
    ///     Without the relay: GitHub's new issue page, filled (the player signs in and clicks Submit), and the zip shown
    ///     to drag into it
    /// </summary>
    private static void OpenIssuePage(string words, string? zip)
    {
        var body = $"### What happened\n\n{words}\n\n### The game\n\n```\n{Describe()}```\n\n(Drag the zip ElinTogether-rapport-*.zip from your desktop here.)";
        if (body.Length > 6000) {
            body = body.Substring(0, 6000);
        }

        Application.OpenURL($"{Issues}?labels=bug&title={Uri.EscapeDataString("[In-game report] " + FirstLine(words))}&body={Uri.EscapeDataString(body)}");
        if (zip is not null) {
            Util.ShowExplorer(zip);
        }
    }
}
