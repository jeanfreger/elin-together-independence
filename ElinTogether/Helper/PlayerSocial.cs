using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElinTogether.Net;
using HeathenEngineering.SteamworksIntegration;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     A player's own ignore and friends lists (council 2026-10-28, gap 2), kept in this game's ElinMP folder by Steam
///     id: they follow the player to every world and tell nobody. One line each: "ignore|friend id name", "seen hash"
/// </summary>
internal static class PlayerSocial
{
    private static readonly Dictionary<ulong, string> Ignored = [];
    private static readonly Dictionary<ulong, string> Friends = [];
    private static readonly HashSet<string> Seen = [];
    private static bool _loaded;

    private static string FilePath => Path.Combine(Application.persistentDataPath, "ElinMP", $"social_{(ulong)UserData.Me}.txt");

    private static void Load()
    {
        if (_loaded) {
            return;
        }

        _loaded = true;
        try {
            if (!File.Exists(FilePath)) {
                return;
            }

            foreach (var line in File.ReadAllLines(FilePath)) {
                var words = line.Split([' '], 3);
                if (words.Length < 2) {
                    continue;
                }

                if (words[0] == "seen") {
                    Seen.Add(words[1]);
                } else if (ulong.TryParse(words[1], out var id)) {
                    (words[0] == "ignore" ? Ignored : Friends)[id] = words.Length > 2 ? words[2] : "";
                }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            EmpLog.Warning(ex, "Social lists not read");
        }
    }

    private static void Save()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, Ignored.Select(p => $"ignore {p.Key} {p.Value}")
                .Concat(Friends.Select(p => $"friend {p.Key} {p.Value}"))
                .Concat(Seen.Select(s => $"seen {s}")));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            EmpLog.Warning(ex, "Social lists not written");
        }
    }

    internal static bool HasSeen(string digest)
    {
        Load();
        return Seen.Contains(digest);
    }

    internal static void MarkSeen(string digest)
    {
        Load();
        Seen.Add(digest);
        Save();
    }

    internal static bool IsIgnored(ulong user)
    {
        Load();
        return Ignored.ContainsKey(user);
    }

    internal static bool IsFriend(ulong user)
    {
        Load();
        return Friends.ContainsKey(user);
    }

    internal static IReadOnlyDictionary<ulong, string> IgnoredList
    {
        get {
            Load();
            return Ignored;
        }
    }

    internal static IReadOnlyDictionary<ulong, string> FriendList
    {
        get {
            Load();
            return Friends;
        }
    }

    /// <summary>
    ///     The player whose character has that uid (the host's too), null for a game line or an unknown speaker
    /// </summary>
    internal static NetPeerState? PlayerOfUid(int uid)
    {
        return uid == 0 ? null : NetSession.Instance.CurrentPlayers.FirstOrDefault(p => p.CharaUid == uid);
    }

    /// <summary>
    ///     A line by this character is hidden for me (never my own)
    /// </summary>
    internal static bool IsIgnoredUid(int uid)
    {
        return PlayerOfUid(uid) is { } state && !state.User.IsMe && IsIgnored(state.User);
    }

    internal static void SetIgnored(ulong user, string name, bool on)
    {
        Load();
        if (on) {
            Ignored[user] = name;
        } else {
            Ignored.Remove(user);
        }

        Save();
    }

    internal static void SetFriend(ulong user, string name, bool on)
    {
        Load();
        if (on) {
            Friends[user] = name;
        } else {
            Friends.Remove(user);
        }

        Save();
    }

    /// <summary>
    ///     Menu entries on another player's character (text already in the player's language)
    /// </summary>
    internal static IEnumerable<(string Label, Action Act)> MenuFor(Chara chara)
    {
        if (!chara.IsPlayer || PlayerOfUid(chara.uid) is not { } state || state.User.IsMe) {
            yield break;
        }

        var (user, name) = ((ulong)state.User, state.User.Name);
        yield return ((IsIgnored(user) ? "emp_ui_unignore" : "emp_ui_ignore").lang(), () => SetIgnored(user, name, !IsIgnored(user)));
        yield return ((IsFriend(user) ? "emp_ui_unfriend" : "emp_ui_friend").lang(), () => SetFriend(user, name, !IsFriend(user)));
    }
}
