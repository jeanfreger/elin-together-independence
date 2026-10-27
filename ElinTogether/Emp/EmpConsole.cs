using ElinTogether.Common;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Net.Steam;
using ReflexCLI.Attributes;
#if DEBUG
using System;
using System.Collections;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;
#endif

namespace ElinTogether;

[ConsoleCommandClassCustomizer("emp")]
internal class EmpConsole
{
    [ConsoleCommand("add_server")]
    internal static void AddServer()
    {
        var server = NetSession.Instance.InitializeComponent<ElinNetHost>();
        server.StartServer();
    }

    [ConsoleCommand("disconnect")]
    internal static void Disconnect()
    {
        ElinNetHost.LeaveWithLastCopy(true, NetSession.Instance.ResetSession);
    }

    [ConsoleCommand("kick")]
    internal static string KickPlayer(int playerIndex, string reason = "")
    {
        if (NetSession.Instance.Transport is not ElinNetHost host) {
            return "Only the host can kick players";
        }

        if (playerIndex == 0) {
            return "Cannot kick the host";
        }

        return host.Kick(playerIndex, reason);
    }

    [ConsoleCommand("ban")]
    internal static string BanPlayer(ulong steamId, string reason = "")
    {
        return NetSession.Instance.Transport is ElinNetHost host ? host.Ban(steamId, reason) : "Only the host can ban players";
    }

    [ConsoleCommand("unban")]
    internal static string UnbanPlayer(ulong steamId)
    {
        return NetSession.Instance.Transport is ElinNetHost ? ElinNetHost.Unban(steamId) : "Only the host can unban players";
    }

    [ConsoleCommand("reconnect")]
    internal static string ReconnectPlayer(int playerIndex)
    {
        if (NetSession.Instance.Transport is not ElinNetHost host) {
            return "Only the host can request a client to reconnect";
        }

        if (playerIndex == 0) {
            return "Cannot request the host to reconnect";
        }

        host.RequestClientReconnect(playerIndex);
        EmpLog.Information("Requested reconnect for player at index {PeerIndex}", playerIndex);
        return $"Requested reconnect for player {playerIndex}";
    }

    [ConsoleCommand("reconnect_self")]
    internal static string ReconnectSelf()
    {
        if (NetSession.Instance.Transport is not ElinNetClient client) {
            return "Only a client can manually reconnect";
        }

        client.ReconnectSelf();
        EmpLog.Information("Manual reconnect initiated");
        return "Manual reconnect initiated";
    }

    /// <summary>
    ///     The map checksum of this game, the last one compared with the game that keeps the map, the last warning
    /// </summary>
    [ConsoleCommand("desync")]
    internal static string Desync()
    {
        return NetDesync.Describe();
    }

    /// <summary>
    ///     Network and main-thread counters since the game started (the CSV in ElinMP/Logs has them per 10 s)
    /// </summary>
    [ConsoleCommand("metrics")]
    internal static string Metrics()
    {
        return NetMetrics.Describe();
    }

    /// <summary>
    ///     Bench: the teams as the host keeps them (or as this guest was told). The menus come later
    /// </summary>
    [ConsoleCommand("team.list")]
    internal static string TeamList()
    {
        return Teams.Describe();
    }

    [ConsoleCommand("team.found")]
    internal static string TeamFound(int charaUid, string name)
    {
        return PlayerOf(charaUid) is { } chara && Teams.Found(chara, name) is var id and > 0
            ? $"Team {id} founded"
            : "Not founded (host only, chara on this map, a name)";
    }

    [ConsoleCommand("team.join")]
    internal static string TeamJoin(int charaUid, int teamId)
    {
        return PlayerOf(charaUid) is { } chara && Teams.Join(chara, teamId) ? $"Joined team {teamId}" : "Not joined";
    }

    /// <summary>
    ///     Bench: what the team menus do for the local player (Found, Join, Leave, Accept, Refuse), asked of the host
    ///     from a guest's game
    /// </summary>
    [ConsoleCommand("team.ask")]
    internal static string TeamAsk(string kind, int id)
    {
        if (!System.Enum.TryParse<Models.TeamAsk>(kind, true, out var ask)) {
            return "Unknown ask";
        }

        Teams.Ask(ask, id);
        return $"Asked {ask} {id}";
    }

    [ConsoleCommand("team.leave")]
    internal static string TeamLeave(int charaUid)
    {
        return PlayerOf(charaUid) is { } chara && Teams.Leave(chara) ? "Left" : "Not in a team";
    }

    private static Chara? PlayerOf(int charaUid)
    {
        return EClass.pc?.uid == charaUid ? EClass.pc : EClass._map?.charas.Find(c => c.uid == charaUid);
    }

    [ConsoleCommand("connect_steam")]
    internal static void AddClientToSteamId(ulong steamId64)
    {
        var client = NetSession.Instance.InitializeComponent<ElinNetClient>();
        client.ConnectSteamUser(steamId64);
    }

    [ConsoleCommand("lobby.create_public")]
    internal static void CreatePublicLobby(int maxPlayers = 16)
    {
        NetSession.Instance.Lobby.CreateLobby(SteamNetLobbyType.Public, maxPlayers);
    }

    [ConsoleCommand("lobby.invite_steam")]
    internal static void InviteSteamUser(ulong steamId64)
    {
        NetSession.Instance.Lobby.InviteSteamUser(steamId64);
    }

    [ConsoleCommand("lobby.invite_overlay")]
    internal static void InviteSteamOverlay()
    {
        NetSession.Instance.Lobby.InviteSteamOverlay();
    }

    [ConsoleCommand("lobby.join")]
    internal static void JoinSteamLobby(ulong steamId64)
    {
        NetSession.Instance.Lobby.ConnectLobby(steamId64);
    }

#if DEBUG
    [ConsoleCommand("add_local")]
    internal static void AddLocalServerUdp()
    {
        var server = NetSession.Instance.InitializeComponent<ElinNetHost>();
        server.StartServer(true);
    }

    [ConsoleCommand("connect_udp")]
    internal static void AddClientToUdpPort()
    {
        var client = NetSession.Instance.InitializeComponent<ElinNetClient>();
        client.ConnectLocalPort();
    }

    /// <summary>
    ///     Bench: the link of this game drops as on a network failure, for that many seconds. Nothing gets
    ///     through either way and nobody is told, each side finds out by itself (Steam's own fake packet loss)
    /// </summary>
    [ConsoleCommand("cut_link")]
    internal static string CutLink(int seconds = 20)
    {
        ElinNetClient.UseTimeout = true;
        SetPacketLoss(100f);
        EmpMod.Instance.StartCoroutine(Restore());
        return $"Link cut for {seconds}s";

        IEnumerator Restore()
        {
            yield return new WaitForSecondsRealtime(seconds);
            SetPacketLoss(0f);
            EmpLog.Information("Link back after {Seconds}s", seconds);
        }
    }

    /// <summary>
    ///     Bench: a dead link ends the session after the timeout as in a release build, a debug build waits for good
    /// </summary>
    [ConsoleCommand("link_timeout")]
    internal static string LinkTimeout(int on = 1)
    {
        ElinNetClient.UseTimeout = on != 0;
        return $"Link timeout {(on != 0 ? "as in a release build" : "off")}";
    }

    /// <summary>
    ///     Bench: the world is saved by itself that often instead of every 2 minutes, 0 for the real pace
    /// </summary>
    [ConsoleCommand("autosave_every")]
    internal static string AutosaveEvery(int seconds = 0)
    {
        EmpAutoHost.SaveEvery(seconds);
        return seconds > 0 ? $"Autosave every {seconds}s" : "Autosave at its real pace";
    }

    /// <summary>
    ///     Bench: a world loaded in this window opens its session by itself, on the local port (see EmpAutoHost)
    /// </summary>
    [ConsoleCommand("auto_open")]
    internal static string AutoOpen(int on = 1)
    {
        EmpAutoHost.BenchOpens = on != 0;
        return $"Session opened by itself at load: {(on != 0 ? "on" : "off")}";
    }

    /// <summary>
    ///     Bench: this guest opens the world it was playing in from the copy it keeps of it (see WorldTakeover)
    /// </summary>
    [ConsoleCommand("take_over")]
    internal static string TakeOver()
    {
        return WorldTakeover.Begin() is { Length: > 0 } why ? $"Not taken over: {why}" : "Taking the world over";
    }

    /// <summary>
    ///     Bench, host: the rule "another player takes the world over when the host leaves", told to the guests
    /// </summary>
    [ConsoleCommand("takeover_rule")]
    internal static string TakeoverRule(int on = 1)
    {
        EmpConfig.Server.Takeover.Value = on != 0;
        (NetSession.Instance.Transport as ElinNetHost)?.UpdateRemoteSessionRules();
        return $"Takeover when the host leaves: {(on != 0 ? "on" : "off")}";
    }

    /// <summary>
    ///     Bench, host: the links of the guests drop the way Steam drops them by itself
    /// </summary>
    [ConsoleCommand("drop_links")]
    internal static string DropLinks()
    {
        return NetSession.Instance.Transport is ElinNetHost host
            ? $"Dropped {host.DropLinksAsSteam()} link(s) as Steam does"
            : "Only the host drops links";
    }

    private static void SetPacketLoss(float percent)
    {
        var value = Marshal.AllocHGlobal(sizeof(float));
        try {
            Marshal.Copy(new[] { percent }, 0, value, 1);
            foreach (var config in new[] {
                         ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_FakePacketLoss_Send,
                         ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_FakePacketLoss_Recv,
                     }) {
                SteamNetworkingUtils.SetConfigValue(config, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                    IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float, value);
            }
        } finally {
            Marshal.FreeHGlobal(value);
        }
    }

    [ConsoleCommand("d1")]
    internal static void AddClientD1()
    {
        AddClientToSteamId(76561198412175578UL);
    }

    [ConsoleCommand("d2")]
    internal static void AddClientD2()
    {
        AddClientToSteamId(76561198254677013UL);
    }
#endif
}