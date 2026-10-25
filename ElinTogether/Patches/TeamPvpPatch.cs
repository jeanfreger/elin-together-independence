using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;

namespace ElinTogether.Patches;

/// <summary>
///     Council 2026-10-25, host option "PvP between teams" (dev/PLAN_teams.md, step 8): two players of different
///     real teams are enemies to each other, as two duellists are (<see cref="DuelFoePatch" /> asks
///     <see cref="AreFoes" /> too). Every game agrees: each one asks the same shared book (<see cref="Teams" />)
///     and the same host rules. <br />
///     Never: a Wanderer (team 0), two of one team, in a civilized place (towns, Vernis, guilds...), on the world
///     map, on the spawn zone. A death follows the host's PlayerKill rule (<see cref="RemotePlayerKillPatch" />):
///     stopped at 0 hit points when off, the game's own death when on. Karma: none, as the game gives none for
///     hitting or killing one of the player's faction; no guard is called, towns being safe
/// </summary>
internal static class TeamPvp
{
    private static bool Enabled => NetSession.Instance.Rules is { UseTeams: true, TeamPvp: true };

    /// <summary>
    ///     These two player characters fight each other where this game stands
    /// </summary>
    internal static bool AreFoes(Chara one, Chara? other)
    {
        // (asked by every hostility check of the game: the rule first)
        if (!Enabled || other is null || one == other || !IsPlayer(one) || !IsPlayer(other) || !FightsHere()) {
            return false;
        }

        var team = Teams.TeamOf(one.uid);
        var otherTeam = Teams.TeamOf(other.uid);
        return team != Teams.Wanderers && otherTeam != Teams.Wanderers && team != otherTeam &&
               !IsBeaten(one.uid, otherTeam) && !IsBeaten(other.uid, team);
    }

    // players beaten by a team, and where: no longer that team's enemy on that map (council 2026-10-25: the fight
    // ends as a lost duel does; no timer, no move, no heal). Kept in every game, as the host tells it
    private static readonly HashSet<(int Victim, int Team, int Zone)> _beaten = [];

    private static bool IsBeaten(int victim, int team)
    {
        return _beaten.Count > 0 && EClass._zone is { } zone && _beaten.Contains((victim, team, zone.uid));
    }

    internal static void Learn(TeamBeatenDelta beaten)
    {
        if (_beaten.Add((beaten.Victim, beaten.Team, beaten.ZoneUid))) {
            beaten.Say();
        }
    }

    internal static void Clear()
    {
        _beaten.Clear();
    }

    /// <summary>
    ///     The host, after a strike stopped at 0 hit points (RemotePlayerKillPatch): a player beaten by a player of
    ///     another team, out of a duel, with player kills not allowed
    /// </summary>
    internal static void Struck(Card target, Card? origin)
    {
        if (target.hp > 0 || target is not Chara victim || !IsPlayer(victim) || NetSession.Instance.Rules.AllowPlayerKill ||
            PlayerDuel.Protects(victim) || PlayerKarma.PlayerBehind(origin) is not { } striker || !AreFoes(victim, striker) ||
            NetSession.Instance.Connection is not ElinNetHost host || (victim.currentZone ?? EClass._zone) is not { } zone) {
            return;
        }

        var beaten = new TeamBeatenDelta {
            Victim = victim.uid,
            Team = Teams.TeamOf(striker.uid),
            ZoneUid = zone.uid,
            Striker = striker.uid,
        };
        Learn(beaten);
        host.Delta.AddRemote(beaten);
    }

    private static bool IsPlayer(Chara chara)
    {
        return chara.IsPC || chara.IsRemotePlayer;
    }

    private static bool FightsHere()
    {
        // the start meadow: the same in every game (the host's spawn zone setting is not sent to the guests)
        return EClass._zone is { } zone && zone is not Zone_Civilized && !zone.IsRegion && zone != EClass.game.StartZone;
    }
}
