using ElinTogether.Net;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-28 (dev/COUNCIL_2026-10-28_team_boxes.md), host option "Teams keep their own bank and delivery
///     box". The game has one bank and one delivery box for the whole world; with the box on, each OWNER
///     (<see cref="StoryOwner" />: a team, or a player without one alone) has its own two, real ones, kept in the host's
///     save (<see cref="ElinNetHost.TeamBoxBook" />). Off by default, always on for the official public server. The
///     boxes the world already had stay where they are, as the shared "world" boxes anyone can still take from; what
///     is put in from now on goes to the owner's own
/// </summary>
internal static class TeamBoxes
{
    /// <summary>
    ///     The ledger's layout. A save written by a newer layout is left as it is (the world's boxes only)
    /// </summary>
    internal const int Schema = 1;

    // the game that keeps the boxes: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    /// <summary>
    ///     The box as this game knows it: the host's own setting, a guest's from the host's rules
    /// </summary>
    internal static bool On => KeepsBook ? Wanted : NetSession.Instance.Rules is { TeamBoxes: true };

    /// <summary>
    ///     The host's box: needs Teams, read as on while this game hosts the official public server
    /// </summary>
    internal static bool Wanted => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBoxes.Value || StoryOwner.OfficialHosting;
}
