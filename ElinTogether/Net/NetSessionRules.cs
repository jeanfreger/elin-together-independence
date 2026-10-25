using MessagePack;

namespace ElinTogether.Net;

[MessagePackObject]
public class NetSessionRules
{
    [Key(0)]
    public required bool UseSharedSpeed { get; set; }

    [Key(1)]
    public required bool UseTurnBasedCombat { get; set; }

    [Key(2)]
    public bool AllowIndependentTravel { get; set; }

    /// <summary>
    ///     Seconds between two checkpoints of an away client, 0 disables them
    /// </summary>
    [Key(3)]
    public int TravelCheckpointSeconds { get; set; }

    /// <summary>
    ///     Every player is paid for its own goods in the shipping box, otherwise the host gets everything
    /// </summary>
    [Key(4)]
    public bool UsePlayerShipping { get; set; }

    /// <summary>
    ///     What fights a player only acts on that player's turns, see PlayerCombatTime
    /// </summary>
    [Key(5)]
    public bool UsePlayerCombatTime { get; set; }

    /// <summary>
    ///     Random quests (from residents and boards) and what they earn, fame and karma, belong to the player who
    ///     takes them; story quests stay everyone's. Off: one quest log and one fame for the whole group
    /// </summary>
    [Key(6)]
    public bool UsePersonalQuests { get; set; }

    /// <summary>
    ///     Two players next to each other can trade items and gold through a window both confirm
    /// </summary>
    [Key(7)]
    public bool AllowPlayerTrade { get; set; }

    /// <summary>
    ///     The turns of a player's own character follow the clock of its own game, not the host's game time as
    ///     the network brings it. Only means something with <see cref="UsePlayerCombatTime" />: without it the
    ///     world runs on the host's clock and a guest on its own would act out of step with it
    /// </summary>
    [Key(8)]
    public bool UsePlayerClock { get; set; }

    [IgnoreMember]
    internal bool OwnClock => UsePlayerClock && UsePlayerCombatTime;

    /// <summary>
    ///     A player's own turn always lasts the base act time, as in a solo game, instead of being stretched or
    ///     shortened by the speed of the other players. Only with <see cref="UsePlayerCombatTime" />, which keeps
    ///     speed meaningful in a fight: what fights a player gets time for each of its turns
    /// </summary>
    [Key(9)]
    public bool UsePlayerStepPace { get; set; }

    [IgnoreMember]
    internal bool OwnPace => UsePlayerStepPace && UsePlayerCombatTime;

    /// <summary>
    ///     One date for the world, whoever makes time pass: a player alone on a map it simulates no longer has
    ///     a date of its own that the host's replaces when it comes back. The most advanced date is the world's
    /// </summary>
    [Key(10)]
    public bool UseSharedWorldTime { get; set; }

    /// <summary>
    ///     What a passing hour, day or month does to the world is done once, by the world keeper's game (see
    ///     Patches/WorldKeeper.cs), not by every game that makes the date advance
    /// </summary>
    [Key(11)]
    public bool UseWorldKeeper { get; set; }

    /// <summary>
    ///     The players who do not keep the map may use the build mode of the base: their clicks are asked of the
    ///     game that keeps it (AgentTaskDelta). Off: refused with a message, only that game builds
    /// </summary>
    [Key(12)]
    public bool AllowGuestBuild { get; set; } = true;

    /// <summary>
    ///     A player's strike can kill another player's character (see RemotePlayerKillPatch). Off: it is left at 0
    ///     hit points
    /// </summary>
    [Key(13)]
    public bool AllowPlayerKill { get; set; }

    /// <summary>
    ///     Only the game that keeps the base manages it: what the other players ask of it (research, hearth skills,
    ///     policies, names, settings of an object, residents, build mode) is refused with a message, and the host
    ///     sends them back the state it keeps. Off: every player manages the base as the host does
    /// </summary>
    [Key(14)]
    public bool HostManagesBase { get; set; }

    [IgnoreMember]
    internal bool GuestsBuild => AllowGuestBuild && !HostManagesBase;

    /// <summary>
    ///     A player can challenge another to a duel nobody dies of (see PlayerDuel). Off: no such entry in the menu
    /// </summary>
    [Key(15)]
    public bool AllowDuels { get; set; } = true;

    /// <summary>
    ///     A player whose link with the host dropped by itself joins the same game again by itself (see
    ///     NetReconnect). Off: it is left on the title screen
    /// </summary>
    [Key(16)]
    public bool AllowReconnect { get; set; } = true;

    /// <summary>
    ///     Council 10: a player who goes to bed sleeps at once for itself; the night passes for the world when all
    ///     the players sleep at the same time. Off: everyone waits for everyone
    /// </summary>
    [Key(17)]
    public bool UseOwnSleep { get; set; } = true;

    /// <summary>
    ///     Before the council of 2026-10-23 on world map time: true for Travel Together. Still sent for a peer of an
    ///     older version, read from <see cref="WorldMapTime" /> by this one
    /// </summary>
    [Key(18)]
    public bool TimeJumpsTogether { get; set; }

    /// <summary>
    ///     Council 2026-10-23: how the travel of one player on the world map moves the date of the world, see
    ///     RemoteTravelRegionPatch
    /// </summary>
    [Key(94)]
    public WorldMapTime WorldMapTime { get; set; } = WorldMapTime.SharedClock;

    /// <summary>
    ///     Council 10: auto-dump leaves the held item and the tool belt, as it leaves the hotbar
    /// </summary>
    [Key(19)]
    public bool DumpSparesBelt { get; set; } = true;

    /// <summary>
    ///     A game whose copy of the map differs from the one of the game that keeps it loads the map again by
    ///     itself (see NetDesync). Off: the difference is only written to the journals
    /// </summary>
    [Key(20)]
    public bool AutoResync { get; set; } = true;

    /// <summary>
    ///     After each save the host makes by itself, every guest keeps a whole copy of the world on its own disk
    ///     (see Net/Handover). Off: the world is on the host's disk only
    /// </summary>
    [Key(21)]
    public bool KeepWorldCopy { get; set; } = false;

    /// <summary>
    ///     Council 10: the tax is computed on the highest fame among the connected players (see SharedTaxPatch).
    ///     Off: on the host's fame
    /// </summary>
    [Key(22)]
    public bool UseSharedTax { get; set; } = true;

    /// <summary>
    ///     Council 11: when the host walks onto the map a player holds alone, that player keeps its game and its
    ///     scene and becomes a client of the host again in place (see ZoneSoftRejoin); when that player walks into
    ///     the map the host stands on, it keeps its game and loads that one map. Off: it hands the map back and
    ///     loads the whole world again
    /// </summary>
    [Key(23)]
    public bool SoftRecall { get; set; } = false;

    /// <summary>
    ///     Council 9, step 4: when the host leaves, a guest opens the world from the copy it keeps of it and the
    ///     game goes on there (see WorldTakeover). Needs KeepWorldCopy. Off: the game ends with its host
    /// </summary>
    [Key(24)]
    public bool AllowTakeover { get; set; } = false;

    /// <summary>
    ///     Council 14: what the game gives once per world, each player gets once: the limited stock of a shop
    ///     (LimitedStockPatch) and what the story gives (ElinNetHost.NoteStoryReward). Off: the game's own rule,
    ///     the first player is the only one
    /// </summary>
    [Key(25)]
    public bool OncePerPlayer { get; set; } = true;

    /// <summary>
    ///     Council 2026-10-23: a town board is drawn for the players on the map, not the host alone (TownBoardPatch)
    /// </summary>
    [Key(51)]
    public bool TownBoards { get; set; } = true;

    /// <summary>
    ///     Audit 2026-10-23: raids, the lucky month and the days with a player count every player on the base map,
    ///     not only the one whose game runs it, see Patches/BasePlayersPatch.cs. Off: the game's own "the player"
    /// </summary>
    [Key(26)]
    public bool PlayersAtBase { get; set; } = true;

    /// <summary>
    ///     Player chat reaches every player, whatever map or zone it is on (an away player included); NPC and game
    ///     lines stay on their map. Off: a chat line reaches only the players on the same map as its speaker
    /// </summary>
    [Key(95)]
    public bool GlobalChat { get; set; } = true;

    /// <summary>
    ///     The menu on another player's character offers "Follow": the local character walks after it, see PlayerFollow
    /// </summary>
    [Key(96)]
    public bool AllowFollow { get; set; } = true;

    /// <summary>
    ///     "Talk" on another player's character opens a private chat with that player, see PlayerWhisper
    /// </summary>
    [Key(97)]
    public bool PrivateChat { get; set; } = true;

    /// <summary>
    ///     A guest's moongate: the host makes the zone of the map in its world, the guest goes alone, see GateItems.
    ///     Off: a guest's use does nothing
    /// </summary>
    [Key(98)]
    public bool GuestMoongate { get; set; } = true;

    /// <summary>
    ///     A guest's landing portal takes it to the host's survival gate zone, the host stays. Off: nothing happens
    /// </summary>
    [Key(99)]
    public bool GuestLandingPortal { get; set; } = true;

    /// <summary>
    ///     A guest's sky helm: the host's list on its screen, its tickets pay, the host sets the gate zone for
    ///     everyone. Off: nothing happens
    /// </summary>
    [Key(100)]
    public bool GuestSkyHelm { get; set; } = true;

    /// <summary>
    ///     Council 2026-10-24: a resident is married to the character who married it, the host keeps who for every
    ///     player and runs marriage, wedding, divorce and the lost ring for whoever acts, see SharedMarriages
    /// </summary>
    [Key(101)]
    public bool SharedMarriages { get; set; } = true;

    /// <summary>
    ///     Council 2026-10-25: players can be in teams, the host keeps them in its save, see Teams
    /// </summary>
    [Key(102)]
    public bool UseTeams { get; set; }

    /// <summary>
    ///     A base is its claimer's team's: the other teams cannot open its chests, pick up, build or manage it there
    /// </summary>
    [Key(103)]
    public bool TeamBases { get; set; }

    /// <summary>
    ///     Inside a team, a container, door or bed a player installs is that player's until shared
    /// </summary>
    [Key(110)]
    public bool TeamPersonal { get; set; }

    /// <summary>
    ///     Two players of different teams are enemies outside towns and the spawn zone, see TeamPvp
    /// </summary>
    [Key(111)]
    public bool TeamPvp { get; set; }

    // the six fallbacks a guest was never told (council 2026-10-25 settings trim): its card names them when the host
    // turns one off; an older host sends none of them and they read as on, their default
    [Key(112)]
    public bool TownLife { get; set; } = true;

    [Key(113)]
    public bool ShopsFromEveryPlayer { get; set; } = true;

    [Key(114)]
    public bool NearestSpawns { get; set; } = true;

    [Key(115)]
    public bool ActivitiesFromActor { get; set; } = true;

    [Key(116)]
    public bool AutoCombatPerPlayer { get; set; } = true;

    [Key(117)]
    public bool SummonsFollowMaster { get; set; } = true;

    public static NetSessionRules Default => new() {
        AllowTakeover = EmpConfig.Server.Takeover.Value,
        SoftRecall = EmpConfig.Server.SoftRecall.Value,
        UseSharedTax = EmpConfig.Server.SharedTax.Value,
        OncePerPlayer = EmpConfig.Server.OncePerPlayer.Value,
        TownBoards = EmpConfig.Server.TownBoards.Value,
        PlayersAtBase = EmpConfig.Server.PlayersAtBase.Value,
        GlobalChat = EmpConfig.Server.GlobalChat.Value,
        AllowFollow = EmpConfig.Server.Follow.Value,
        PrivateChat = EmpConfig.Server.PrivateChat.Value,
        GuestMoongate = EmpConfig.Server.GuestMoongate.Value,
        GuestLandingPortal = EmpConfig.Server.GuestLandingPortal.Value,
        GuestSkyHelm = EmpConfig.Server.GuestSkyHelm.Value,
        SharedMarriages = EmpConfig.Server.SharedMarriages.Value,
        UseTeams = EmpConfig.Server.Teams.Value,
        TeamBases = EmpConfig.Server.TeamBases.Value,
        TeamPersonal = EmpConfig.Server.TeamPersonal.Value,
        TeamPvp = EmpConfig.Server.TeamPvp.Value,
        TownLife = EmpConfig.Server.TownLife.Value,
        ShopsFromEveryPlayer = EmpConfig.Server.ShopsFromEveryPlayer.Value,
        NearestSpawns = EmpConfig.Server.NearestSpawns.Value,
        ActivitiesFromActor = EmpConfig.Server.ActivitiesFromActor.Value,
        AutoCombatPerPlayer = EmpConfig.Server.AutoCombatPerPlayer.Value,
        SummonsFollowMaster = EmpConfig.Server.SummonsFollowMaster.Value,
        // (a takeover is made from that copy: asking for one asks for the other, a host that ticked only
        // "another player takes over" got nothing)
        KeepWorldCopy = EmpConfig.Server.WorldCopy.Value || EmpConfig.Server.Takeover.Value,
        AutoResync = EmpConfig.Server.AutoResync.Value,
        UseOwnSleep = EmpConfig.Server.OwnSleep.Value,
        TimeJumpsTogether = EmpConfig.Server.WorldMapTime.Value == WorldMapTime.TravelTogether,
        WorldMapTime = EmpConfig.Server.WorldMapTime.Value,
        DumpSparesBelt = EmpConfig.Server.DumpSparesBelt.Value,
        AllowReconnect = EmpConfig.Server.AutoReconnect.Value,
        HostManagesBase = EmpConfig.Server.HostManagesBase.Value,
        AllowDuels = EmpConfig.Server.Duels.Value,
        AllowPlayerKill = EmpConfig.Server.PlayerKill.Value,
        AllowGuestBuild = EmpConfig.Server.GuestBuild.Value,
        UseSharedSpeed = EmpConfig.Server.SharedAverageSpeed.Value,
        UseTurnBasedCombat = EmpConfig.Server.TurnBasedCombat.Value,
        AllowIndependentTravel = EmpConfig.Server.IndependentTravel.Value,
        TravelCheckpointSeconds = EmpConfig.Server.TravelCheckpointSeconds.Value,
        UsePlayerShipping = EmpConfig.Server.PlayerShipping.Value,
        UsePlayerCombatTime = EmpConfig.Server.PlayerCombatTime.Value,
        UsePersonalQuests = EmpConfig.Server.PersonalQuests.Value,
        AllowPlayerTrade = EmpConfig.Server.PlayerTrade.Value,
        UsePlayerClock = EmpConfig.Server.PlayerClock.Value,
        UsePlayerStepPace = EmpConfig.Server.PlayerStepPace.Value,
        UseSharedWorldTime = EmpConfig.Server.SharedWorldTime.Value,
        UseWorldKeeper = EmpConfig.Server.WorldKeeper.Value,
    };
}

/// <summary>
///     Council 2026-10-23, one choice of the host: who pays the hours a trip on the world map costs. <br />
///     SharedClock: the world map is a map like any other, every step of anyone moves the date of the game that
///     took it, the most advanced date is the world's and the others catch up without living it. <br />
///     TravelTogether: a step moves the date only when every player is on the world map, whoever steps. <br />
///     PersonalPace: as SharedClock, except that the world map hours of a player travelling while the others are
///     elsewhere are held back as that player's lead (its hunger and its own quests pay them, the date stays), up
///     to the host's cap; the world catches up with the largest lead once everyone is on the world map together or
///     sleeps the same night, see ElinNetHost.HoldLead. An older peer reads it as SharedClock
/// </summary>
public enum WorldMapTime
{
    SharedClock,
    TravelTogether,
    PersonalPace,
}
