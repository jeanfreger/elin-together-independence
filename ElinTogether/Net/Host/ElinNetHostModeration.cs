using System.Collections.Generic;
using System.Linq;
using ElinTogether.Common;
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
        foreach (var peer in Socket.Peers.Where(p => (ulong)p.User == user).ToList()) {
            Socket.Disconnect(peer, EmpDisconnectInfo.With(EmpDisconnectInfo.Banned, reason));
        }

        return $"Banned {user}";
    }

    internal static string Unban(ulong user)
    {
        KeptOut.Remove(user);
        if (!Bans.Remove(user)) {
            return $"{user} was not banned";
        }

        EmpLog.Information("Admin: unbanned {User}", user);
        return $"Unbanned {user}";
    }
}
