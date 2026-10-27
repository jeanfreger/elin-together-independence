using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net.Steam;

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
    ///     Team id -> the founder who received its free land deed: never twice for a team, nor for a player who
    ///     founds again
    /// </summary>
    [ElinGameIOProperty("team_deeds")]
    internal static Dictionary<int, int> TeamDeeds
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Host option "Teams learn on their own" (<see cref="TeamKnowledge" />): team id -> what that team knows, as
    ///     text (<see cref="TeamLore" />). The Wanderers (team 0) too. Empty while the box was never on
    /// </summary>
    [ElinGameIOProperty("team_lore")]
    internal static Dictionary<int, string> TeamLoreBook
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     [the ledger's schema version, 1 once every team was given a copy of the world's knowledge]
    /// </summary>
    [ElinGameIOProperty("team_lore_state")]
    internal static int[] TeamLoreState
    {
        get => field is { Length: 2 } ? field : field = new int[2];
        set;
    }

    /// <summary>
    ///     Host option "Teams pay their own land" (<see cref="TeamBills" />): team id -> its unpaid tax bills. The
    ///     world's count (<c>player.taxBills</c>) stays the sum the game knows; what it holds above these is older than
    ///     the box (bills of no team)
    /// </summary>
    [ElinGameIOProperty("team_tax_bills")]
    internal static Dictionary<int, int> TeamTaxBills
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     The chara uid of every player of this world, the host's own included
    /// </summary>
    internal static IEnumerable<int> PlayerCharaUids()
    {
        return SavedRemoteCharas.Values.Append(player.uidChara).Distinct();
    }

    /// <summary>
    ///     The fame of that player: the host's own, another's as its game last told it
    /// </summary>
    internal static int FameOfPlayer(int charaUid)
    {
        return charaUid == player.uidChara ? player.fame
            : PlayerStandings.TryGetValue(charaUid, out var standing) && standing.Length >= 1 ? standing[StandingFame] : 0;
    }

    /// <summary>
    ///     Karma the date costs the connected players of one team (its unpaid tax, <see cref="TeamBills" />), as
    ///     <see cref="GiveKarmaToEveryone" /> does for all. The host's own player is the caller's business
    /// </summary>
    internal void GiveKarmaToTeam(int team, int karma)
    {
        if (IsZoneSession) {
            return;
        }

        foreach (var peer in Socket.Peers.ToArray()) {
            if (!SavedRemoteCharas.TryGetValue(peer.User, out var uid) || Teams.TeamOf(uid) != team) {
                continue;
            }

            if (ActiveRemoteCharas.TryGetValue(peer.Id, out var chara)) {
                GiveKarma(chara, karma);
            } else {
                SendDeltaTo(peer.Id, new PlayerStandingDelta {
                    Fame = 0,
                    Karma = karma,
                    Relative = true,
                });
            }
        }
    }

    // the players sent to the spawn zone in this hosting: a reconnect finds them where they were
    private readonly HashSet<ulong> _spawned = [];

    /// <summary>
    ///     A player came in (its world and the host's map are on the way): a new player, or one whose team has no
    ///     base, travels to the host's spawn zone, the way a player travels alone. Nothing when teams are off
    /// </summary>
    internal void SendToSpawnZone(Chara chara, ISteamNetPeer peer, bool isNew)
    {
        var rules = NetSession.Instance.Rules;
        if (IsZoneSession || !rules.UseTeams || !rules.AllowIndependentTravel || (!isNew && Teams.HasBase(Teams.TeamOf(chara.uid))) ||
            Teams.SpawnZone() is not { } zone || zone == _zone || !_spawned.Add(peer.User)) {
            return;
        }

        EmpLog.Information("Player {@Peer} starts at the spawn zone {ZoneFullName}", peer, zone.ZoneFullName);
        SendDeltaTo(peer.Id, new TeamSpawnDelta { ZoneUid = zone.uid });
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
            Bills = TeamBills.Told(),
        });
    }
}
