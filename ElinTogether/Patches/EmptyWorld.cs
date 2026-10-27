using ElinTogether.Net;

namespace ElinTogether.Patches;

/// <summary>
///     Council 2026-10-26 (MMO world time, stage 3): an empty world freezes. While no player is connected and
///     active, the date of the world stands still: no hour, no day, no waiting or resting passing time. The host
///     counts as a player only when it is not away from the keyboard (PlayerIdle); the game of an always-on server
///     (-empserver) never does. Its world is paused (PauseGame) and GameDate.AdvanceMin does nothing
///     (WorldDateAdvanceEvent), except a catch-up with what a player made pass elsewhere
/// </summary>
internal static class EmptyWorld
{
    internal static bool Frozen
    {
        get {
            var session = NetSession.Instance;
            if (!session.Rules.TimeStopsWhenEmpty || session.Transport is not ElinNetHost { IsZoneSession: false } ||
                !EClass.core.IsGameStarted) {
                return false;
            }

            if (!EmpServer.Requested && !PlayerIdle.IsIdle(0)) {
                return false;
            }

            foreach (var player in session.CurrentPlayers) {
                if (player.Index != 0 && !PlayerIdle.IsIdle(player.Index)) {
                    return false;
                }
            }

            return true;
        }
    }
}
