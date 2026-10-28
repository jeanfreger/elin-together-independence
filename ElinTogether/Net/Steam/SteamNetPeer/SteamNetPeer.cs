using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ElinTogether.Common;
using HeathenEngineering.SteamworksIntegration;
using HeathenEngineering.SteamworksIntegration.API;
using Steamworks;
using UnityEngine;

namespace ElinTogether.Net.Steam;

internal class SteamNetPeer : ISteamNetPeer, IDisposable
{
    private const int MemoryArenaInitialSize = 4 * (1 << 10);
    private const int MemoryArenaGrowthRatio = 2;

    private static readonly Func<int, IntPtr> _allocator = Marshal.AllocHGlobal;
    private static readonly Action<IntPtr> _deallocator = Marshal.FreeHGlobal;
    private static readonly Func<IntPtr, IntPtr, IntPtr> _reallocator = Marshal.ReAllocHGlobal;

    private static int _nextId = -1;
    private static readonly Dictionary<UserData, int> _peerIdHistory = [];

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    protected readonly object ArenaLock = new();

    public readonly HSteamNetConnection Connection;
    public readonly SteamNetworkingIdentity RemoteIdentity;
    protected readonly ISteamNetSerializer Serializer;

    protected IntPtr Arena;
    protected int ArenaSize;
    private bool _disposed;

    public SteamNetPeer(HSteamNetConnection connection, ISteamNetSerializer serializer)
    {
        Connection = connection;

        SteamNetworkingSockets.GetConnectionInfo(connection, out var info);
        RemoteIdentity = info.m_identityRemote;
        IsLoopback = info.m_addrRemote.IsLocalHost();

        User = RemoteIdentity.GetSteamID64();
        Friends.Client.RequestUserInformation(User, true);

        if (_peerIdHistory.TryGetValue(User, out var preferredId)) {
            int current;
            while ((current = Volatile.Read(ref _nextId)) < preferredId) {
                Interlocked.CompareExchange(ref _nextId, preferredId, current);
            }
        } else {
            preferredId = Interlocked.Increment(ref _nextId);
        }
        _id = _peerIdHistory[User] = preferredId;

        Serializer = serializer;
        ArenaSize = MemoryArenaInitialSize;
        Arena = _allocator(ArenaSize);
    }

    public ESteamNetworkingConnectionState ConnectionState =>
        !NetShutdown.IsQuitting && SteamNetworkingSockets.GetConnectionInfo(Connection, out var info)
            ? info.m_eState
            : ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None;

    public virtual int Id => _id;

    /// <summary>
    ///     Connected from this very machine (127.0.0.1), not through Steam
    /// </summary>
    public bool IsLoopback { get; }
    public UserData User { get; private set; }

    private int _id;

    /// <summary>
    ///     Local udp debug sessions: several instances on one Steam account, told apart by the identity
    ///     they mix into the connection fingerprint, see SteamNetManager.AcceptIfHost
    /// </summary>
    internal void UseDevIdentity(int identity)
    {
        User = DevIdentityBase + (ulong)identity;

        if (!_peerIdHistory.TryGetValue(User, out var id)) {
            id = _peerIdHistory[User] = Interlocked.Increment(ref _nextId);
        }

        _id = id;
    }

    internal const ulong DevIdentityBase = 76561190000000000UL;

    public virtual bool IsConnected =>
        ConnectionState is
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting or
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute or
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;

    public SteamNetPeerStat Stat => field ??= new();

    public virtual bool Send<T>(T message, SteamNetSendFlag sendFlags = SteamNetSendFlag.Reliable)
    {
        var bytes = Serializer.Serialize(message);
        return Send(bytes, sendFlags);
    }

    public virtual bool Send(byte[] bytes, SteamNetSendFlag sendFlags = SteamNetSendFlag.Reliable)
    {
        if (NetShutdown.IsQuitting) {
            return false;
        }

        var size = bytes.Length;
        NetMetrics.Sent(Id, bytes);

        lock (ArenaLock) {
            if (Arena == IntPtr.Zero || BrokenReason is not null) {
                return false;
            }

            // unreliable messages are never cut nor kept: late is as good as lost
            var direct = (sendFlags & SteamNetSendFlag.Reliable) == 0
                         || (_backlog.Count == 0 && size <= NetFragments.PieceSize);
            if (direct) {
                var result = SendNow(bytes, sendFlags);
                if (result == EResult.k_EResultOK) {
                    return true;
                }

                if (result != EResult.k_EResultLimitExceeded || (sendFlags & SteamNetSendFlag.Reliable) == 0) {
                    EmpLog.Warning("Message of {Size} bytes not sent: {Result}", size, result);
                    return false;
                }

                // Steam's send queue is full: the message waits for its turn instead of being lost
            }

            if (_backlogBytes + size > NetFragments.MaxMessageSize) {
                // a reliable message refused is a hole for this peer for good
                Break($"message of {size} bytes not sent, {_backlogBytes} bytes already wait");
                return false;
            }

            // Steam refuses a message of 512 KB or more, and no more than its send queue (512 KB) at once:
            // a big message goes in pieces, a few per frame, and what is sent meanwhile goes behind it, in order
            if (size > NetFragments.PieceSize) {
                var pieces = NetFragments.Split(bytes, _nextMessageId++);
                EmpLog.Debug("Message of {Size} bytes sent in {Pieces} pieces", size, pieces.Count);

                foreach (var piece in pieces) {
                    _backlog.Enqueue((piece, sendFlags));
                    _backlogBytes += piece.Length;
                }
            } else {
                _backlog.Enqueue((bytes, sendFlags));
                _backlogBytes += size;
            }
        }

        Flush();

        return true;
    }

    private readonly Queue<(byte[] bytes, SteamNetSendFlag flags)> _backlog = new();
    private int _backlogBytes;
    private int _nextMessageId;

    // gauges for NetMetrics, read without the lock
    internal int BacklogCount => _backlog.Count;
    internal int BacklogBytes => _backlogBytes;

    private bool _stalled;

    /// <summary>
    ///     Pieces of the big messages this peer sent, see <see cref="SteamNetManager.Poll" />
    /// </summary>
    internal readonly NetFragmentAssembler Fragments = new();

    /// <summary>
    ///     The host let this player in (it plays a character here): its messages may be as big as any
    /// </summary>
    internal bool Trusted { get; set; }

    /// <summary>
    ///     Hands Steam as much of what waits as its send queue takes, the rest stays for the next frame
    /// </summary>
    internal void Flush()
    {
        if (NetShutdown.IsQuitting || BrokenReason is not null) {
            return;
        }

        lock (ArenaLock) {
            while (_backlog.Count > 0 && Arena != IntPtr.Zero) {
                var (bytes, flags) = _backlog.Peek();

                var result = SendNow(bytes, flags);
                if (result == EResult.k_EResultLimitExceeded) {
                    // once per episode, not once per frame
                    if (!_stalled) {
                        _stalled = true;
                        EmpLog.Warning("Send queue of {@Peer} is full: {Waiting} bytes in {Messages} messages wait here",
                            this, _backlogBytes, _backlog.Count);
                    }

                    return;
                }

                if (result != EResult.k_EResultOK) {
                    // a message with a hole in it is of no use to the other side
                    Break($"{_backlogBytes} bytes not sent: {result}");
                    return;
                }

                _backlog.Dequeue();
                _backlogBytes -= bytes.Length;
            }

            if (_stalled && _backlog.Count == 0) {
                _stalled = false;
                EmpLog.Debug("Send queue of {@Peer} caught up", this);
            }
        }
    }

    /// <summary>
    ///     Set once a reliable message was lost for this peer: its stream has a hole, so the connection must be
    ///     closed (<see cref="SteamNetManager.Poll" /> does) and the guest joins again, see NetReconnect
    /// </summary>
    internal string? BrokenReason { get; private set; }

    // under ArenaLock
    private void Break(string what)
    {
        EmpLog.Warning("Connection of {@Peer} will be closed: {What}", this, what);

        BrokenReason = EmpDisconnectInfo.RemoteClosed;
        _backlog.Clear();
        _backlogBytes = 0;
        _stalled = false;
    }

    // under ArenaLock
    private EResult SendNow(byte[] bytes, SteamNetSendFlag sendFlags)
    {
        var size = bytes.Length;

        PinArena(size);

        Marshal.Copy(bytes, 0, Arena, size);

        // crash on native side cannot be handled
        var result = SteamNetworkingSockets.SendMessageToConnection(Connection, Arena, (uint)size, (int)sendFlags, out _);
        if (result == EResult.k_EResultOK) {
            Stat.Sent(size);
            UpdateRealtime();
        }

        return result;
    }

    protected void PinArena(int size)
    {
        if (size <= ArenaSize) {
            return;
        }

        var newSize = Math.Max(size, ArenaSize * MemoryArenaGrowthRatio);

        Arena = _reallocator(Arena, (IntPtr)newSize);
        ArenaSize = newSize;
    }

    public void UpdateRealtime()
    {
        const float pingAlpha = 0.2f;
        const float bandwidthAlpha = 0.15f;

        if (NetShutdown.IsQuitting) {
            return;
        }

        var status = new SteamNetConnectionRealTimeStatus_t();
        var discard = new SteamNetConnectionRealTimeLaneStatus_t();
        var result = SteamNetworkingSockets.GetConnectionRealTimeStatus(Connection, ref status, 0, ref discard);
        if (result != EResult.k_EResultOK) {
            return;
        }

        Stat.LastPingMs = status.m_nPing;
        Stat.ConnectionQualityLocal = status.m_flConnectionQualityLocal;
        Stat.ConnectionQualityRemote = status.m_flConnectionQualityRemote;
        Stat.LastUpdated = DateTime.UtcNow;

        // use ema to smooth out the spikes
        Stat.AvgPingMs = Stat.AvgPingMs == 0f
            ? Stat.LastPingMs
            : Mathf.Lerp(Stat.AvgPingMs, Stat.LastPingMs, pingAlpha);

        Stat.AvgBpsOut = Stat.AvgBpsOut == 0f
            ? status.m_flOutBytesPerSec
            : Mathf.Lerp(Stat.AvgBpsOut, status.m_flOutBytesPerSec, bandwidthAlpha);

        Stat.AvgBpsIn = Stat.AvgBpsIn == 0f
            ? status.m_flInBytesPerSec
            : Mathf.Lerp(Stat.AvgBpsIn, status.m_flInBytesPerSec, bandwidthAlpha);
    }

#region Cleanups

    ~SteamNetPeer()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) {
            return;
        }

        _disposed = true;

        // the finalizer runs off-thread and must not take ArenaLock, so swap the pointer out
        // atomically instead: a racing Send then sees IntPtr.Zero and bails rather than writing
        // into freed native memory
        var arena = Interlocked.Exchange(ref Arena, IntPtr.Zero);
        if (arena != IntPtr.Zero) {
            _deallocator(arena);
        }
    }

#endregion
}