using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Models;

namespace ElinTogether.Net;

/// <summary>
///     Council 2026-10-25 (<see cref="Teams" />): the teams, the team of each claimed land and the owner of a card, kept
///     in the host's save. An old save has none: everyone is a Wanderer (team 0), nothing is anyone's
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Team id -> leader chara uid, open (1) or not (0), then the member chara uids. Team 0 is never here
    /// </summary>
    [ElinGameIOProperty("teams")]
    internal static Dictionary<int, int[]> TeamList
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Team id -> its name (kept apart: the save only holds plain values)
    /// </summary>
    [ElinGameIOProperty("team_names")]
    internal static Dictionary<int, string> TeamNames
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Zone uid -> the team of the player who claimed it, none for a land claimed by a Wanderer
    /// </summary>
    [ElinGameIOProperty("zone_team")]
    internal static Dictionary<int, int> ZoneTeams
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Card uid -> the chara uid of the player it belongs to
    /// </summary>
    [ElinGameIOProperty("card_owner")]
    internal static Dictionary<int, int> CardOwners
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Chara uid -> the team it asks to join, until its leader answers. Not saved: a request is asked again
    /// </summary>
    internal static Dictionary<int, int> TeamRequests { get; } = [];

    /// <summary>
    ///     The teams changed, or a player joins: everyone is told the whole book
    /// </summary>
    internal void TellTeams()
    {
        // (a zone hosted by a client keeps no books, the host of the world does)
        if (IsZoneSession) {
            return;
        }

        Delta.AddRemote(new TeamsDelta {
            TeamList = new(TeamList),
            Names = new(TeamNames),
            Zones = new(ZoneTeams),
            Owners = new(CardOwners),
            Requests = new(TeamRequests),
        });
    }
}
