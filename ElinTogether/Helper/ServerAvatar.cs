using ElinTogether.Net.Steam;
using HeathenEngineering.SteamworksIntegration;

namespace ElinTogether.Helper;

/// <summary>
///     A server (-empserver) plays no character (council 2026-10-28, "no host at all"): the save owner's character
///     stops being the game's local one and becomes an ordinary player character tied to its Steam id, which joins
///     like any guest. The server's own local character is a hidden stand-in that nobody can see, fight, hurt or
///     talk to. Only under -empserver, and only with EmpConfig.Server.ServerAvatar (on by default there): a game
///     hosted from the menu is never touched
/// </summary>
internal static class ServerAvatar
{
    /// <summary>
    ///     The Steam id this game is known by in its own world. Test builds with a server on the same Steam account as
    ///     the player (two processes, one account) give the server an identity of its own, reserved here, so that
    ///     neither takes the other's place: a Release build needs a real answer to this (council blocker 3), until
    ///     then it is the account the machine runs
    /// </summary>
    internal static ulong HostUser =>
#if DEBUG
        EmpServer.Requested && EmpConfig.Dev.Identity.Value == 0 ? SteamNetPeer.DevIdentityBase + ServerDevIdentity :
#endif
        UserData.Me;

#if DEBUG
    /// <summary>
    ///     The test identity of a server (the windows of the bench use 1 to 8, the ones with a local port): the last
    ///     one the setting allows
    /// </summary>
    internal const ulong ServerDevIdentity = 999;
#endif
}
