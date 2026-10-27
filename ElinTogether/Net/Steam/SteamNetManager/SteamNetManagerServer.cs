using System;
using System.Collections.Generic;
using ElinTogether.Common;
using ElinTogether.LangMod;
using HeathenEngineering.SteamworksIntegration;
using Serilog.Events;
using Steamworks;
using UnityEngine;

namespace ElinTogether.Net.Steam;

public partial class SteamNetManager
{
    public static readonly Dictionary<UserData, string> ConnectionKeys = [];

    /// <summary>
    ///     Listening on the local udp port, for debugging
    /// </summary>
    public bool IsLocalUdp { get; private set; }

    /// <summary>
    ///     Accepted local debug connections -> identity, applied once the peer exists
    /// </summary>
    private readonly Dictionary<HSteamNetConnection, int> _devIdentities = [];

    /// <summary>
    ///     Debug: one extra local udp listen socket per test identity, port = local port + identity
    /// </summary>
    private readonly Dictionary<HSteamListenSocket, int> _devListenSockets = [];

    private const int DevIdentityPorts = 8;

    /// <summary>
    ///     A dedicated server (-empserver) on the local port also takes players coming through Steam
    /// </summary>
    private HSteamListenSocket _steamListenSocket = HSteamListenSocket.Invalid;

    /// <summary>
    ///     Start server on valve SDR
    /// </summary>
    public void StartServerSdr()
    {
        EmpLog.Debug("Starting relay server via SDR");

        var options = SteamNetConfig.Default.Create();
        _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
        if (_listenSocket == HSteamListenSocket.Invalid) {
            throw new InvalidOperationException("Failed to create listen socket via SDR");
        }

        SetupSteamCallback();
    }

    /// <summary>
    ///     Mainly just for debugging
    /// </summary>
    /// <param name="alsoSteam">
    ///     also listen through Steam (SDR): a dedicated server makes a Steam lobby, whose players connect that way
    ///     (two-PC test 2026-10-27: they entered the lobby and never connected). Only there: a game that hosts by
    ///     the local port otherwise is a test bench, where it broke reloads
    /// </param>
    public void StartServerUdp(ushort port = EmpConstants.LocalPort, bool alsoSteam = false)
    {
        EmpLog.Debug("Starting local udp server at port {Port}",
            port);

        var localhost = new SteamNetworkingIPAddr();
        localhost.Clear();
        localhost.m_port = port;

        var options = SteamNetConfig.Default.Create();
        _listenSocket = SteamNetworkingSockets.CreateListenSocketIP(ref localhost, options.Length, options);
        if (_listenSocket == HSteamListenSocket.Invalid) {
            throw new InvalidOperationException("Failed to create listen socket via UDP");
        }

#if DEBUG
        for (var identity = 1; identity <= DevIdentityPorts; identity++) {
            var address = new SteamNetworkingIPAddr();
            address.Clear();
            address.m_port = (ushort)(port + identity);

            var socket = SteamNetworkingSockets.CreateListenSocketIP(ref address, options.Length, options);
            if (socket != HSteamListenSocket.Invalid) {
                _devListenSockets[socket] = identity;
            }
        }
#endif

        if (alsoSteam) {
            _steamListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
            if (_steamListenSocket == HSteamListenSocket.Invalid) {
                EmpLog.Warning("Server: no Steam listen socket, only the local port {Port} takes players", port);
            }
        }

        IsLocalUdp = true;
        SetupSteamCallback();
    }

    private void AcceptIfHost(HSteamNetConnection connection, SteamNetConnectionInfo_t info)
    {
        UserData user = info.m_identityRemote.GetSteamID64();

        EmpLog.Debug("Received connection request from {RemoteIdentity}",
            user);

        // local udp connections never go through the steam lobby that issues the keys
        var isLocalDebug = IsLocalUdp && info.m_addrRemote.IsLocalHost();

        // debug instance sharing the Steam account, see SteamNetPeer.UseDevIdentity
        if (isLocalDebug && _devListenSockets.TryGetValue(info.m_hListenSocket, out var devIdentity)) {
            _devIdentities[connection] = devIdentity;
        }

        var connectionKey = BuildVersionIntegrity.VersionStringToLong();
        if (info.m_nUserData != connectionKey) {
            EmpLog.Warning("Rejecting {RemoteIdentity}: fingerprint {ClientFingerprint} != host {HostFingerprint}",
                user, info.m_nUserData, connectionKey);

            EmpPop.Debug("emp_version_rejected_host".Loc(
                user.Name,
                ModInfo.BuildVersion.TagColor(Color.green),
                BuildVersionIntegrity.GameVersion.TagColor(Color.green)));

            // only connect if we have same build version
            SteamNetworkingSockets.CloseConnection(connection, 0, BuildVersionIntegrity.GtfoReason(), false);
            return;
        }

        if (!isLocalDebug && !ConnectionKeys.ContainsKey(user)) {
            // only connect if host allows it in the lobby
            SteamNetworkingSockets.CloseConnection(connection, 0, "emp_not_allowed", false);
            return;
        }

        EmpLog.Debug("Accepting connection request from {RemoteIdentity}",
            user);

        var result = SteamNetworkingSockets.AcceptConnection(connection);
        if (result != EResult.k_EResultOK) {
            EmpPop.Popup(LogEventLevel.Warning, "emp_accept_failed".lang());
        }
    }

    private void DiscardListenSocket()
    {
        if (_listenSocket != HSteamListenSocket.Invalid) {
            SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            _listenSocket = HSteamListenSocket.Invalid;
        }

        if (_steamListenSocket != HSteamListenSocket.Invalid) {
            SteamNetworkingSockets.CloseListenSocket(_steamListenSocket);
            _steamListenSocket = HSteamListenSocket.Invalid;
        }

        foreach (var socket in _devListenSockets.Keys) {
            SteamNetworkingSockets.CloseListenSocket(socket);
        }

        _devListenSockets.Clear();
        _devIdentities.Clear();

        IsHost = false;
        IsListening = false;
        IsLocalUdp = false;
    }

    private void DestroyPollGroup()
    {
        if (_pollGroup == HSteamNetPollGroup.Invalid) {
            return;
        }

        SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
        _pollGroup = HSteamNetPollGroup.Invalid;
    }

    private void SetupSteamCallback()
    {
        if (IsListening) {
            return;
        }

        IsHost = true;
        IsListening = true;
    }
}