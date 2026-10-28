using System;
using UnityEngine;

namespace ElinTogether.Net;

internal static class NetShutdown
{
    internal static bool IsQuitting { get; private set; }

    internal static void SetupApplicationHook()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        Application.wantsToQuit += OnWantsToQuit;
    }

    internal static void Shutdown()
    {
        if (IsQuitting) {
            return;
        }

        // (nothing is sent once IsQuitting is set)
        try {
            (NetSession.Instance.Transport as ElinNetHost)?.AnnounceLeaving();
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Exception while telling the guests that the host leaves");
            // noexcept
        }

        IsQuitting = true;

        Helper.SaveDepot.OnQuit();
        Helper.HiddenServer.OnQuit();
        Helper.ModFetch.OnQuit();

        try {
            NetSession.Instance.Transport?.Shutdown();
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Exception while closing sockets on quit");
            // noexcept
        }

        try {
            NetSession.Instance.Lobby.Shutdown();
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Exception while leaving lobby on quit");
            // noexcept
        }
    }

    private static bool OnWantsToQuit()
    {
        Shutdown();
        return true;
    }
}