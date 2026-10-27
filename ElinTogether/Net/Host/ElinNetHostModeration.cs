using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElinTogether.Common;
using ElinTogether.Models;
using ElinTogether.Net.Steam;
using HeathenEngineering.SteamworksIntegration;
using UnityEngine;

namespace ElinTogether.Net;

/// <summary>
///     Admin tools (council 2026-10-27, dev/COUNCIL_2026-10-27_admin_tools.md): a kick that says why and keeps the
///     player out a few minutes, bans by Steam id kept in the world save (they follow the depot, the world copy and a
///     takeover). Checked when a player connects, before the world is sent
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Steam id -> why it is banned (may be empty)
    /// </summary>
    [ElinGameIOProperty("mod_bans")]
    internal static Dictionary<ulong, string> Bans
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Steam id -> why it is muted (may be empty)
    /// </summary>
    [ElinGameIOProperty("mod_mutes")]
    internal static Dictionary<ulong, string> Mutes
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Steam ids the host made admins of this world: they kick, ban and mute other players (not the host, not
    ///     another admin). Only the host or the server's operator makes or unmakes them
    /// </summary>
    [ElinGameIOProperty("mod_admins")]
    internal static Dictionary<ulong, int> Admins
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Steam ids let in by the host or the operator (allow) while MembersOnly is ticked, besides the players of
    ///     this world
    /// </summary>
    [ElinGameIOProperty("mod_members")]
    internal static Dictionary<ulong, int> Members
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     A player of this world (it has a character here, or played the host's), an admin, or one let in
    /// </summary>
    internal static bool IsMember(ulong user)
    {
        return SavedRemoteCharas.ContainsKey(user) || PlayerRosters.ContainsKey(user) || PcOwners.ContainsKey(user) ||
               Admins.ContainsKey(user) || Members.ContainsKey(user);
    }

    /// <summary>
    ///     Members only (box MembersOnly): nobody new comes in, a Steam friend or a public game's stranger alike
    /// </summary>
    internal static bool MembersOnlyRefuses(ulong user)
    {
        return EmpConfig.Server.MembersOnly.Value && !IsMember(user);
    }

    internal static string Allow(ulong user)
    {
        Members[user] = 1;
        AdminLog($"allow {user}");
        return $"{user} may come in";
    }

    // Steam id -> until when a kicked player is kept out (real time; forgotten at a restart)
    private static readonly Dictionary<ulong, float> KeptOut = [];

    /// <summary>
    ///     Why this player may not come in now, as the reason its game shows; null when it may
    /// </summary>
    private static string? Refusal(ulong user)
    {
        if (Bans.TryGetValue(user, out var why)) {
            return EmpDisconnectInfo.With(EmpDisconnectInfo.Banned, why);
        }

        if (MembersOnlyRefuses(user)) {
            return EmpDisconnectInfo.MembersOnly;
        }

        if (KeptOut.TryGetValue(user, out var until)) {
            var left = until - Time.realtimeSinceStartup;
            if (left > 0f) {
                return EmpDisconnectInfo.With(EmpDisconnectInfo.KickWait, Mathf.CeilToInt(left / 60f).ToString());
            }

            KeptOut.Remove(user);
        }

        return null;
    }

    /// <summary>
    ///     The player at that index leaves, told why; it may come back after KickKeepOutMinutes
    /// </summary>
    internal string Kick(int peerIndex, string? reason = null)
    {
        if (peerIndex == 0 || Socket.Peers.FirstOrDefault(p => p.Id == peerIndex) is not { } peer) {
            return $"No player at index {peerIndex}";
        }

        var minutes = EmpConfig.Server.KickKeepOutMinutes.Value;
        if (minutes > 0) {
            KeptOut[peer.User] = Time.realtimeSinceStartup + minutes * 60f;
        }

        EmpLog.Information("Admin: kicked {@Peer} ({Reason}), kept out {Minutes} min", peer, reason ?? "", minutes);
        AdminLog($"kick {(ulong)peer.User} {peer.User.Name} ({reason}), kept out {minutes} min");
        Socket.Disconnect(peer, EmpDisconnectInfo.With(EmpDisconnectInfo.HostKick, reason));
        return $"Kicked {peer.User.Name} ({peer.User})";
    }

    /// <summary>
    ///     That Steam account no longer comes into this world, playing now or not
    /// </summary>
    internal string Ban(ulong user, string? reason = null)
    {
        ulong me = UserData.Me;
        if (user == me) {
            return "The host cannot ban itself";
        }

        Bans[user] = reason?.Trim() ?? "";
        EmpLog.Information("Admin: banned {User} ({Reason})", user, reason ?? "");
        AdminLog($"ban {user} ({reason})");
        foreach (var peer in Socket.Peers.Where(p => (ulong)p.User == user).ToList()) {
            Socket.Disconnect(peer, EmpDisconnectInfo.With(EmpDisconnectInfo.Banned, reason));
        }

        return $"Banned {user}";
    }

    internal string Mute(ulong user, string? reason = null)
    {
        ulong me = UserData.Me;
        if (user == me) {
            return "The host cannot mute itself";
        }

        Mutes[user] = reason?.Trim() ?? "";
        AdminLog($"mute {user} ({reason})");
        TellModeration();
        return $"Muted {user}";
    }

    internal string Unmute(ulong user)
    {
        if (!Mutes.Remove(user)) {
            return $"{user} was not muted";
        }

        AdminLog($"unmute {user}");
        TellModeration();
        return $"Unmuted {user}";
    }

    /// <summary>
    ///     Every game learns who is muted: at each change and when a player comes in (SendJoinCopies)
    /// </summary>
    internal void TellModeration()
    {
        if (IsZoneSession) {
            return;
        }

        ModerationDelta.Muted.Clear();
        ModerationDelta.Muted.UnionWith(Mutes.Keys);
        ModerationDelta.AdminIds.Clear();
        ModerationDelta.AdminIds.UnionWith(Admins.Keys);
        Delta.AddRemote(new ModerationDelta { Mutes = [..Mutes.Keys], Admins = [..Admins.Keys] });
    }

    /// <summary>
    ///     A line that came from a muted player (hard check, whatever EnforceOwnership says)
    /// </summary>
    internal bool FromMuted(int peerId)
    {
        return Socket.Peers.FirstOrDefault(p => p.Id == peerId) is { } peer && ModerationDelta.IsMuted(peer.User);
    }

    internal string MakeAdmin(ulong user, bool admin)
    {
        if (admin) {
            Admins[user] = 1;
        } else if (!Admins.Remove(user)) {
            return $"{user} was not an admin";
        }

        AdminLog($"{(admin ? "admin" : "unadmin")} {user}");
        TellModeration();
        return $"{user} {(admin ? "is now an admin" : "is no longer an admin")}";
    }

    /// <summary>
    ///     An admin's order (AdminCommandDelta), checked here: from an admin of this world, about a player who is
    ///     neither the host nor an admin
    /// </summary>
    internal void RunAdminOrder(int originPeer, AdminCommandDelta order)
    {
        ulong host = UserData.Me;
        var sender = Socket.Peers.FirstOrDefault(p => p.Id == originPeer);
        ulong from = sender is null ? 0UL : sender.User;
        if (sender is null || !Admins.ContainsKey(from) || order.Target == host || Admins.ContainsKey(order.Target)) {
            AdminLog($"refused order {order.Verb} {order.Target} from {from}: not an admin, or about the host or an admin");
            return;
        }

        var target = Socket.Peers.FirstOrDefault(p => (ulong)p.User == order.Target);
        var done = order.Verb switch {
            "kick" => target is null ? "not playing" : Kick(target.Id, order.Reason),
            "ban" => Ban(order.Target, order.Reason),
            "unban" => Unban(order.Target),
            "mute" => Mute(order.Target, order.Reason),
            "unmute" => Unmute(order.Target),
            _ => "unknown order",
        };
        AdminLog($"by admin {from} {sender.User.Name}: {order.Verb} {order.Target}: {done}");
    }

    internal static string Unban(ulong user)
    {
        KeptOut.Remove(user);
        if (!Bans.Remove(user)) {
            return $"{user} was not banned";
        }

        EmpLog.Information("Admin: unbanned {User}", user);
        AdminLog($"unban {user}");
        return $"Unbanned {user}";
    }

    /// <summary>
    ///     One line per order, refusal, arrival and departure, with the Steam id: ElinMP/Logs/admin.log of the machine
    ///     that hosts (never in the world save, which would grow for ever)
    /// </summary>
    internal static void AdminLog(string line)
    {
        try {
            var folder = Path.Combine(Application.persistentDataPath, "ElinMP", "Logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "admin.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            EmpLog.Warning(ex, "Admin log not written: {Line}", line);
        }
    }

    /// <summary>
    ///     An order of the server's operator (ElinMP/server.cmd, see EmpServer), one line:
    ///     say &lt;text&gt;, or kick|ban|unban|mute|unmute &lt;Steam id or exact name&gt; [reason]. Returns what was done
    /// </summary>
    internal string RunOrder(string line)
    {
        // a line to every player, wherever they are, from the server's operator
        if (line.TrimStart().StartsWith("say ", StringComparison.OrdinalIgnoreCase)) {
            var text = line.Trim().Substring(4).Trim();
            if (text.Length == 0) {
                return "error: nothing to say";
            }

            SendDeltaToAllExcept(int.MinValue, new MsgSayDelta {
                Text = text.Length > 300 ? text.Substring(0, 300) : text,
                R = 1,
                G = 1,
                B = 1,
                A = 1,
                Chat = true,
                ChatId = UnityEngine.Random.Range(1, int.MaxValue),
                SpeakerUid = 0,
                Name = "[Server]",
            });
            AdminLog($"say {text}");
            return "Said to everyone";
        }

        var words = line.Trim().Split([' '], 3, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) {
            return "error: " + line;
        }

        var reason = words.Length > 2 ? words[2] : null;
        var peer = Socket.Peers.FirstOrDefault(p => ((ulong)p.User).ToString() == words[1] || p.User.Name == words[1]);
        ulong user = peer is not null ? peer.User : ulong.TryParse(words[1], out var id) ? id : 0UL;
        if (user == 0UL) {
            return "error: no player " + words[1];
        }

        var done = words[0].ToLowerInvariant() switch {
            "kick" => peer is null ? "error: " + words[1] + " is not playing" : Kick(peer.Id, reason),
            "ban" => Ban(user, reason),
            "unban" => Unban(user),
            "mute" => Mute(user, reason),
            "unmute" => Unmute(user),
            "allow" => Allow(user),
            "admin" => MakeAdmin(user, true),
            "unadmin" => MakeAdmin(user, false),
            _ => "error: unknown order " + words[0],
        };
        AdminLog($"order \"{line.Trim()}\": {done}");
        return done;
    }
}
