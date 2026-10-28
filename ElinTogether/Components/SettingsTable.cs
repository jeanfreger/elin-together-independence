using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using ElinTogether.API.SourceValidation;
using ElinTogether.LangMod;
using ElinTogether.Net;
using ElinTogether.Net.Steam;

namespace ElinTogether.Components;

/// <summary>
///     Where a reader takes the values of the host's rules from: the config entries themselves, their defaults, a
///     preset, or the rules a guest received from its host. Null: not known there (a guest is not told every rule)
/// </summary>
internal sealed class RuleSource(Func<ConfigEntryBase, object?> read)
{
    internal static RuleSource Current => new(e => e.BoxedValue);
    internal static RuleSource Defaults => new(e => e.DefaultValue);

    internal bool? Get(ConfigEntryBase entry) => read(entry) as bool?;
    internal object? Raw(ConfigEntryBase entry) => read(entry);
}

/// <summary>
///     One question a host answers, written as several of the old boxes (council 2026-10-23): the boxes stay the
///     storage, a choice only reads them (Read, -1 when they match no option: "Custom (see Advanced)") and writes
///     them when the host picks an option. It never writes on open, nor for "Custom"
/// </summary>
internal sealed class Choice(string id, string[] options, Func<ConfigEntryBase[]> entries, Func<RuleSource, int> read,
    Func<int, (ConfigEntryBase Entry, object Value)[]> write)
{
    internal string Id => id;
    internal string[] Options => options;
    internal ConfigEntryBase[] Entries => entries();
    internal int Read(RuleSource source) => read(source);

    /// <summary>
    ///     What option i writes; nothing for an index past the options (the "Custom" line of the dropdown)
    /// </summary>
    internal (ConfigEntryBase Entry, object Value)[] WriteFor(int i) => i >= 0 && i < options.Length ? write(i) : [];

    internal string Label => $"emp_ui_sv_cfg_{id}".lang();
    internal string OptionLabel(int i) => i >= 0 && i < options.Length ? $"emp_ui_sv_opt_{id}_{options[i]}".lang() : "emp_ui_sv_custom".lang();
}

/// <summary>
///     A line of the host's rules. Entry: a box; Choice: a question over several boxes; neither: a line drawn by the
///     renderer itself (the joining checks, the tax bills). Under: the choice whose boxes these are, drawn indented
///     under it in Advanced. Requires: false greys the box out (what it needs is off). Apply: when a change counts,
///     for the rules that do not apply at once
/// </summary>
internal sealed record SettingRow(string Id, string Card, bool Advanced, ConfigEntry<bool>? Entry = null, Choice? Choice = null,
    Func<bool>? Requires = null, string? Apply = null, string? Under = null)
{
    internal string Label => Choice?.Label ?? $"emp_ui_sv_cfg_{Id}".lang();

    internal string Description
    {
        get {
            var desc = $"emp_ui_sv_desc_{Id}".lang();
            // (under a dropdown, one line per choice: the applies-when line comes on a line of its own, last)
            // (the official server's rule, said where the box is greyed)
            if (Id == "import_chara" && OfficialServer.Hosting) {
                desc = $"{desc} {"emp_ui_sv_import_official".lang()}";
            }

            if (Id is "team_own_story" or "team_knowledge" && OfficialServer.Hosting) {
                desc = $"{desc} {"emp_ui_sv_own_story_official".lang()}";
            }

            return Apply is null ? desc : $"{desc}{(Choice is null ? " " : "\n")}{$"emp_ui_sv_apply_{Apply}".lang()}";
        }
    }
}

/// <summary>
///     The host's rules as one table, drawn by the mod's own window (TabServerConfiguration) and by the game's mod
///     config page (ModConfigPage) alike. Entries are read each time (EmpConfig.Reset binds new ones)
/// </summary>
internal static class SettingsTable
{
    // WHERE A NEW OPTION GOES (council 2026-10-23, placement rule):
    // - a new option goes to Advanced (Advanced: true), in the group that fits it;
    // - it is visible only if a new host must decide it before friends join, and then by joining an existing
    //   choice (a new option of a Choice) or by replacing a visible row: never more than 13 visible rows;
    // - a box that only brings back the old host-centred behaviour always goes to the "fallbacks" group;
    // - its label is a sentence about what the players experience, says "host" and "friends" or "players",
    //   never "guest"; its texts come in English, Japanese and Chinese in the same commit;
    // - nothing is ever removed: a hidden box keeps its stored value and stays in Advanced.
    internal static readonly string[] Cards = ["joining", "together", "host_leaves", "friends", "fallbacks", "time_details", "comfort", "repair"];

    // the boxes of the host-centred fallbacks: "every player gets their own" on is all of them on
    private static ConfigEntry<bool>[] Fallbacks() => [
        EmpConfig.Server.PersonalQuests, EmpConfig.Server.PlayerShipping, EmpConfig.Server.OncePerPlayer,
        EmpConfig.Server.SharedTax, EmpConfig.Server.TownLife, EmpConfig.Server.TownBoards,
        EmpConfig.Server.ShopsFromEveryPlayer, EmpConfig.Server.NearestSpawns, EmpConfig.Server.ActivitiesFromActor,
        EmpConfig.Server.AutoCombatPerPlayer, EmpConfig.Server.PlayersAtBase, EmpConfig.Server.SummonsFollowMaster,
        EmpConfig.Server.GlobalChat, EmpConfig.Server.OwnJournal,
    ];

    internal static Choice WhoJoins => new("who_joins", ["anyone", "mods", "data", "both"],
        () => [EmpConfig.Server.SourceValidationSet],
        source => {
            if (source.Raw(EmpConfig.Server.SourceValidationSet) is not string raw || raw.Contains(':')) {
                return -1;
            }

            // (the old "All" box also checks game files: stronger than "both", so it reads as Custom and picking an
            // option never drops the file checks silently; review 2026-10-23)
            return ValidationConfig.GetConfiguredFlags(raw) switch {
                ValidationFlags.None => 0,
                ValidationFlags.Plugins => 1,
                ValidationFlags.Sources => 2,
                ValidationFlags.Sources | ValidationFlags.Plugins => 3,
                _ => -1,
            };
        },
        i => [(EmpConfig.Server.SourceValidationSet, new[] { "", "plugins", "sources", "sources,plugins" }[i])]);

    // (council 2026-10-25 settings trim: one question where there were two boxes; choose off with import on reads Custom)
    internal static Choice Characters => Pair("characters", ["new", "pick", "both"],
        EmpConfig.Server.ChooseCharacter, EmpConfig.Server.ImportCharacter, [(false, false), (true, false), (true, true)]);

    // together: nobody travels alone (the world map clock is left as it is, it means nothing then); a world map clock
    // "travel together" with independent travel on reads Custom
    internal static Choice Travel => new("travel", ["together", "alone", "own_pace"],
        () => [EmpConfig.Server.IndependentTravel, EmpConfig.Server.WorldMapTime],
        source => (source.Get(EmpConfig.Server.IndependentTravel), source.Raw(EmpConfig.Server.WorldMapTime)) switch {
            (false, _) => 0,
            (true, Net.WorldMapTime.SharedClock) => 1,
            (true, Net.WorldMapTime.PersonalPace) => 2,
            _ => -1,
        },
        i => i switch {
            0 => [(EmpConfig.Server.IndependentTravel, false)],
            1 => [(EmpConfig.Server.IndependentTravel, true), (EmpConfig.Server.WorldMapTime, Net.WorldMapTime.SharedClock)],
            _ => [(EmpConfig.Server.IndependentTravel, true), (EmpConfig.Server.WorldMapTime, Net.WorldMapTime.PersonalPace)],
        });

    // where a new player of a world with teams appears (another zone id typed in the file reads as Custom and is kept)
    internal static Choice SpawnZone => new("spawn_zone", ["start", "host"],
        () => [EmpConfig.Server.SpawnZone],
        source => source.Raw(EmpConfig.Server.SpawnZone) is string raw ? System.Array.IndexOf(new[] { "start", "host" }, raw) : -1,
        i => [(EmpConfig.Server.SpawnZone, new[] { "start", "host" }[i])]);

    internal static Choice WorldMapTime => new("world_map_choice", ["SharedClock", "TravelTogether", "PersonalPace"],
        () => [EmpConfig.Server.WorldMapTime],
        source => source.Raw(EmpConfig.Server.WorldMapTime) is Net.WorldMapTime mode ? (int)mode : -1,
        i => [(EmpConfig.Server.WorldMapTime, (Net.WorldMapTime)i)]);

    // Personal Pace: the hours a player travelling alone may run ahead of the world (another number typed in the
    // file reads as Custom and is kept)
    private static readonly int[] LeadCaps = [12, 24, 48, 72];

    internal static Choice LeadCap => new("world_map_lead", [..LeadCaps.Select(h => h.ToString())],
        () => [EmpConfig.Server.WorldMapLeadCap],
        source => source.Raw(EmpConfig.Server.WorldMapLeadCap) is int hours ? Array.IndexOf(LeadCaps, hours) : -1,
        i => [(EmpConfig.Server.WorldMapLeadCap, LeadCaps[i])]);

    // council 2026-10-26: how many of the active players must sleep for the night of the world
    internal static Choice NightPasses => new("night_passes", ["Everyone", "Half", "One"],
        () => [EmpConfig.Server.NightPasses],
        source => source.Raw(EmpConfig.Server.NightPasses) is NightRule rule ? (int)rule : -1,
        i => [(EmpConfig.Server.NightPasses, (NightRule)i)]);

    // minutes without input before a player is away from the keyboard, 0 off (another number typed in the file
    // reads as Custom and is kept)
    private static readonly int[] IdleSteps = [0, 5, 15, 30];

    // minutes a kicked player stays out (admin tools, council 2026-10-27)
    private static readonly int[] KeepOutSteps = [0, 5, 15, 60];

    internal static Choice KickKeepOut => new("kick_keep_out", [..KeepOutSteps.Select(m => m.ToString())],
        () => [EmpConfig.Server.KickKeepOutMinutes],
        source => source.Raw(EmpConfig.Server.KickKeepOutMinutes) is int minutes ? Array.IndexOf(KeepOutSteps, minutes) : -1,
        i => [(EmpConfig.Server.KickKeepOutMinutes, KeepOutSteps[i])]);

    // players per team and minutes before founding another (council 2026-10-28); another number typed in the file
    // reads as Custom and is kept
    private static readonly int[] TeamSizeSteps = [5, 10, 20, 50, 0];
    private static readonly int[] FoundWaitSteps = [0, 5, 30, 60];

    internal static Choice TeamMaxSize => new("team_max_size", [..TeamSizeSteps.Select(n => n.ToString())],
        () => [EmpConfig.Server.TeamMaxSize],
        source => source.Raw(EmpConfig.Server.TeamMaxSize) is int count ? Array.IndexOf(TeamSizeSteps, count) : -1,
        i => [(EmpConfig.Server.TeamMaxSize, TeamSizeSteps[i])]);

    internal static Choice TeamFoundCooldown => new("team_found_cooldown", [..FoundWaitSteps.Select(n => n.ToString())],
        () => [EmpConfig.Server.TeamFoundCooldown],
        source => source.Raw(EmpConfig.Server.TeamFoundCooldown) is int minutes ? Array.IndexOf(FoundWaitSteps, minutes) : -1,
        i => [(EmpConfig.Server.TeamFoundCooldown, FoundWaitSteps[i])]);

    // council 2026-10-28: real days away before a team loses its lands, and lands one team may claim (0 no limit)
    private static readonly int[] AbandonSteps = [7, 14, 30, 60, 90];
    private static readonly int[] LandCapSteps = [1, 2, 3, 5, 10, 0];

    internal static Choice AbandonDays => new("abandon_days", [..AbandonSteps.Select(n => n.ToString())],
        () => [EmpConfig.Server.AbandonDays],
        source => source.Raw(EmpConfig.Server.AbandonDays) is int days ? Array.IndexOf(AbandonSteps, days) : -1,
        i => [(EmpConfig.Server.AbandonDays, AbandonSteps[i])]);

    internal static Choice LandsPerTeam => new("lands_per_team", [..LandCapSteps.Select(n => n.ToString())],
        () => [EmpConfig.Server.LandsPerTeam],
        source => source.Raw(EmpConfig.Server.LandsPerTeam) is int count ? Array.IndexOf(LandCapSteps, count) : -1,
        i => [(EmpConfig.Server.LandsPerTeam, LandCapSteps[i])]);

    // characters per player (user, 2026-10-27), 0 no limit
    private static readonly int[] CharaSteps = [1, 3, 5, 10, 0];

    internal static Choice MaxCharacters => new("max_characters", [..CharaSteps.Select(n => n.ToString())],
        () => [EmpConfig.Server.MaxCharacters],
        source => source.Raw(EmpConfig.Server.MaxCharacters) is int count ? Array.IndexOf(CharaSteps, count) : -1,
        i => [(EmpConfig.Server.MaxCharacters, CharaSteps[i])]);

    // council 2026-10-26 (stage 5): the last days a long catch-up lives (another number typed in the file reads as
    // Custom and is kept)
    private static readonly int[] CatchUpSteps = [1, 3, 7];

    internal static Choice CatchUpDays => new("catch_up_days", [..CatchUpSteps.Select(d => d.ToString())],
        () => [EmpConfig.Server.CatchUpDays],
        source => source.Raw(EmpConfig.Server.CatchUpDays) is int days ? Array.IndexOf(CatchUpSteps, days) : -1,
        i => [(EmpConfig.Server.CatchUpDays, CatchUpSteps[i])]);

    // council 2026-10-26 (stage 6, Living world): real minutes per game day, 0 off (another number typed in the file
    // reads as Custom and is kept)
    private static readonly int[] LivingSteps = [0, 20, 40, 60, 120];

    internal static Choice LivingWorld => new("living_world", [..LivingSteps.Select(m => m.ToString())],
        () => [EmpConfig.Server.LivingWorldMinutes],
        source => source.Raw(EmpConfig.Server.LivingWorldMinutes) is int minutes ? Array.IndexOf(LivingSteps, minutes) : -1,
        i => [(EmpConfig.Server.LivingWorldMinutes, LivingSteps[i])]);

    internal static Choice IdleMinutes => new("idle_minutes", [..IdleSteps.Select(m => m.ToString())],
        () => [EmpConfig.Server.IdleMinutes],
        source => source.Raw(EmpConfig.Server.IdleMinutes) is int minutes ? Array.IndexOf(IdleSteps, minutes) : -1,
        i => [(EmpConfig.Server.IdleMinutes, IdleSteps[i])]);

    // own pace: PlayerCombatTime with its clock and step pace (turn-based does nothing then); turn-based, shared
    // average and classic: without PlayerCombatTime (clock and step pace do nothing then, they are left alone)
    internal static Choice CombatPace => new("combat_pace", ["own", "turns", "average", "classic"],
        () => [EmpConfig.Server.PlayerCombatTime, EmpConfig.Server.PlayerClock, EmpConfig.Server.PlayerStepPace,
            EmpConfig.Server.TurnBasedCombat, EmpConfig.Server.SharedAverageSpeed],
        source => {
            var (pct, pc, psp, tb, sas) = (source.Get(EmpConfig.Server.PlayerCombatTime), source.Get(EmpConfig.Server.PlayerClock),
                source.Get(EmpConfig.Server.PlayerStepPace), source.Get(EmpConfig.Server.TurnBasedCombat),
                source.Get(EmpConfig.Server.SharedAverageSpeed));
            return (pct, pc, psp, tb, sas) switch {
                (true, true, true, _, false) => 0,
                (false, _, _, true, false) => 1,
                (false, _, _, false, true) => 2,
                (false, _, _, false, false) => 3,
                _ => -1,
            };
        },
        i => i switch {
            0 => [(EmpConfig.Server.PlayerCombatTime, true), (EmpConfig.Server.PlayerClock, true),
                (EmpConfig.Server.PlayerStepPace, true), (EmpConfig.Server.SharedAverageSpeed, false)],
            1 => [(EmpConfig.Server.PlayerCombatTime, false), (EmpConfig.Server.TurnBasedCombat, true),
                (EmpConfig.Server.SharedAverageSpeed, false)],
            2 => [(EmpConfig.Server.PlayerCombatTime, false), (EmpConfig.Server.TurnBasedCombat, false),
                (EmpConfig.Server.SharedAverageSpeed, true)],
            _ => [(EmpConfig.Server.PlayerCombatTime, false), (EmpConfig.Server.TurnBasedCombat, false),
                (EmpConfig.Server.SharedAverageSpeed, false)],
        });

    // (a guest is told only some of the fallbacks: the ones it knows decide)
    internal static Choice OwnEverything => new("own_everything", ["yes", "no"],
        () => [..Fallbacks()],
        source => {
            var known = Fallbacks().Select(e => source.Get(e)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            return known.Length == 0 ? -1 : known.All(v => v) ? 0 : known.All(v => !v) ? 1 : -1;
        },
        i => [..Fallbacks().Select(e => ((ConfigEntryBase)e, (object)(i == 0)))]);

    // (the base's management also covers the build mode: "everyone builds, the host manages" has no boxes)
    internal static Choice Building => Pair("building", ["all", "manage", "host"],
        EmpConfig.Server.GuestBuild, EmpConfig.Server.HostManagesBase, [(true, false), (false, false), (false, true)]);

    internal static Choice Fights => Pair("fights", ["off", "duels", "kills"],
        EmpConfig.Server.Duels, EmpConfig.Server.PlayerKill, [(false, false), (true, false), (true, true)]);

    internal static Choice HostLeaves => Pair("host_leaves", ["waits", "backup", "carry_on"],
        EmpConfig.Server.WorldCopy, EmpConfig.Server.Takeover, [(false, false), (true, false), (true, true)]);

    private static Choice Pair(string id, string[] options, ConfigEntry<bool> a, ConfigEntry<bool> b, (bool A, bool B)[] values)
    {
        return new(id, options, () => [a, b],
            source => source.Get(a) is { } va && source.Get(b) is { } vb ? Array.IndexOf(values, (va, vb)) : -1,
            i => [(a, values[i].A), (b, values[i].B)]);
    }

    internal static IEnumerable<Choice> Choices() => Rows().Where(r => r.Choice is not null).Select(r => r.Choice!);

    internal static IEnumerable<SettingRow> Rows()
    {
        var pct = EmpConfig.Server.PlayerCombatTime;

        // Joining
        yield return new("who_joins", "joining", false, Choice: WhoJoins, Apply: "join");
        yield return new("checks", "joining", true, Under: "who_joins", Apply: "join");
        yield return new("same_game_version", "joining", true, EmpConfig.Server.SameGameVersion, Apply: "join");
        yield return new("characters", "joining", false, Choice: Characters, Apply: "join");
        yield return new("choose_chara", "joining", true, EmpConfig.Server.ChooseCharacter, Apply: "join", Under: "characters");
        yield return new("import_chara", "joining", true, EmpConfig.Server.ImportCharacter, Requires: () => !OfficialServer.Hosting, Apply: "join", Under: "characters");
        // (the way back is the import: without it a character that left could only be brought back unchanged)
        yield return new("export_chara", "joining", true, EmpConfig.Server.ExportCharacter,
            Requires: () => EmpConfig.Server.ImportCharacter.Value && !OfficialServer.Hosting, Apply: "join", Under: "characters");
        yield return new("permadeath_revive", "joining", true, EmpConfig.Server.PermadeathRevive);

        // Playing together
        yield return new("travel", "together", false, Choice: Travel);
        yield return new("independent_travel", "together", true, EmpConfig.Server.IndependentTravel, Under: "travel");
        yield return new("world_map_choice", "together", true, Choice: WorldMapTime, Under: "travel");
        yield return new("combat_pace", "together", false, Choice: CombatPace);
        yield return new("combat_time", "together", true, pct, Under: "combat_pace");
        yield return new("player_clock", "together", true, EmpConfig.Server.PlayerClock, Requires: () => pct.Value, Under: "combat_pace");
        yield return new("step_pace", "together", true, EmpConfig.Server.PlayerStepPace, Requires: () => pct.Value, Under: "combat_pace");
        yield return new("turn_combat", "together", true, EmpConfig.Server.TurnBasedCombat, Requires: () => !pct.Value, Under: "combat_pace");
        yield return new("shared_speed", "together", true, EmpConfig.Server.SharedAverageSpeed, Under: "combat_pace");
        yield return new("own_everything", "fallbacks", true, Choice: OwnEverything);
        yield return new("building", "together", false, Choice: Building);
        yield return new("guest_build", "together", true, EmpConfig.Server.GuestBuild, Under: "building");
        yield return new("host_manages_base", "together", true, EmpConfig.Server.HostManagesBase, Under: "building");
        yield return new("fights", "together", false, Choice: Fights);
        yield return new("duels", "together", true, EmpConfig.Server.Duels, Under: "fights");
        yield return new("player_kill", "together", true, EmpConfig.Server.PlayerKill, Under: "fights");
        yield return new("guest_bouts", "together", true, EmpConfig.Server.GuestBouts);
        yield return new("duel_arena", "together", true, EmpConfig.Server.DuelArena, Requires: () => EmpConfig.Server.Duels.Value);
        yield return new("duel_bets", "together", true, EmpConfig.Server.DuelBets, Requires: () => EmpConfig.Server.Duels.Value);
        yield return new("player_trade", "friends", true, EmpConfig.Server.PlayerTrade);
        yield return new("follow", "friends", true, EmpConfig.Server.Follow);
        yield return new("private_chat", "friends", true, EmpConfig.Server.PrivateChat);
        yield return new("guest_moongate", "friends", true, EmpConfig.Server.GuestMoongate);
        yield return new("guest_landing_portal", "friends", true, EmpConfig.Server.GuestLandingPortal);
        yield return new("guest_sky_helm", "friends", true, EmpConfig.Server.GuestSkyHelm);
        yield return new("shared_marriages", "friends", true, EmpConfig.Server.SharedMarriages);
        yield return new("affinity_per_player", "friends", true, EmpConfig.Server.AffinityPerPlayer);
        yield return new("public_game", "joining", true, EmpConfig.Server.PublicGame);
        yield return new("kick_keep_out", "joining", true, Choice: KickKeepOut);
        yield return new("members_only", "joining", true, EmpConfig.Server.MembersOnly);
        yield return new("admin_extras", "joining", true, EmpConfig.Server.AdminExtras);
        yield return new("container_log", "together", true, EmpConfig.Server.ContainerAccessLog);
        yield return new("max_characters", "joining", true, Choice: MaxCharacters);
        yield return new("teams", "together", true, EmpConfig.Server.Teams);
        yield return new("team_bases", "together", true, EmpConfig.Server.TeamBases, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("team_approval", "together", true, EmpConfig.Server.TeamApproval, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("team_deed", "together", true, EmpConfig.Server.TeamDeed, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("land_rules", "together", true, EmpConfig.Server.LandRules, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBases.Value);
        yield return new("sibling_lands", "together", true, EmpConfig.Server.SiblingLands, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBases.Value);
        yield return new("land_abandon", "together", true, EmpConfig.Server.LandAbandon, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBases.Value);
        yield return new("abandon_days", "together", true, Choice: AbandonDays, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBases.Value && EmpConfig.Server.LandAbandon.Value);
        yield return new("lands_per_team", "together", true, Choice: LandsPerTeam, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBases.Value);
        yield return new("team_leaders", "together", true, EmpConfig.Server.TeamLeaders, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("team_max_size", "together", true, Choice: TeamMaxSize, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamLeaders.Value);
        yield return new("team_found_cooldown", "together", true, Choice: TeamFoundCooldown, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamLeaders.Value);
        yield return new("team_personal", "together", true, EmpConfig.Server.TeamPersonal, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("spawn_zone", "together", true, Choice: SpawnZone, Requires: () => EmpConfig.Server.Teams.Value && EmpConfig.Server.IndependentTravel.Value);
        yield return new("team_pvp", "together", true, EmpConfig.Server.TeamPvp, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("team_knowledge", "together", true, EmpConfig.Server.TeamKnowledge, Requires: () => !OfficialServer.Hosting);
        yield return new("team_boxes", "together", true, EmpConfig.Server.TeamBoxes, Requires: () => EmpConfig.Server.Teams.Value && !OfficialServer.Hosting);
        yield return new("team_bills", "together", true, EmpConfig.Server.TeamBills, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("team_story", "together", true, EmpConfig.Server.TeamStory, Requires: () => EmpConfig.Server.Teams.Value);
        yield return new("team_own_story", "together", true, EmpConfig.Server.TeamOwnStory, Requires: () => !OfficialServer.Hosting);
        yield return new("team_own_guilds", "together", true, EmpConfig.Server.TeamOwnGuilds, Requires: () => Helper.TeamOwnStory.Wanted);

        // When the host leaves
        yield return new("host_leaves", "host_leaves", false, Choice: HostLeaves);
        yield return new("world_copy", "host_leaves", true, EmpConfig.Server.WorldCopy, Under: "host_leaves");
        yield return new("takeover", "host_leaves", true, EmpConfig.Server.Takeover, Requires: () => EmpConfig.Server.WorldCopy.Value,
            Under: "host_leaves");
        yield return new("auto_host", "host_leaves", true, EmpConfig.Server.AutoHost, Apply: "load");

        // Advanced: the way back to the host-centred game, one box each
        yield return new("personal_quests", "fallbacks", true, EmpConfig.Server.PersonalQuests);
        yield return new("player_shipping", "fallbacks", true, EmpConfig.Server.PlayerShipping);
        yield return new("once_per_player", "fallbacks", true, EmpConfig.Server.OncePerPlayer);
        yield return new("shared_tax", "fallbacks", true, EmpConfig.Server.SharedTax);
        yield return new("town_life", "fallbacks", true, EmpConfig.Server.TownLife);
        yield return new("town_boards", "fallbacks", true, EmpConfig.Server.TownBoards, Requires: () => EmpConfig.Server.PersonalQuests.Value);
        yield return new("shops_from_every_player", "fallbacks", true, EmpConfig.Server.ShopsFromEveryPlayer);
        yield return new("nearest_spawns", "fallbacks", true, EmpConfig.Server.NearestSpawns);
        yield return new("activities_from_actor", "fallbacks", true, EmpConfig.Server.ActivitiesFromActor);
        yield return new("auto_combat_per_player", "fallbacks", true, EmpConfig.Server.AutoCombatPerPlayer);
        yield return new("players_at_base", "fallbacks", true, EmpConfig.Server.PlayersAtBase);
        yield return new("summons_follow_master", "fallbacks", true, EmpConfig.Server.SummonsFollowMaster);
        yield return new("global_chat", "fallbacks", true, EmpConfig.Server.GlobalChat);
        yield return new("own_journal", "fallbacks", true, EmpConfig.Server.OwnJournal);

        yield return new("world_time", "time_details", true, EmpConfig.Server.SharedWorldTime);
        yield return new("world_keeper", "time_details", true, EmpConfig.Server.WorldKeeper);
        yield return new("own_sleep", "time_details", true, EmpConfig.Server.OwnSleep);
        yield return new("night_passes", "time_details", true, Choice: NightPasses, Requires: () => EmpConfig.Server.OwnSleep.Value);
        yield return new("idle_minutes", "time_details", true, Choice: IdleMinutes);
        yield return new("time_stops_empty", "time_details", true, EmpConfig.Server.TimeStopsWhenEmpty);
        yield return new("living_world", "time_details", true, Choice: LivingWorld, Requires: () => EmpConfig.Server.SharedWorldTime.Value);
        yield return new("absent_quests_wait", "time_details", true, EmpConfig.Server.AbsentQuestsWait, Requires: () => EmpConfig.Server.PersonalQuests.Value);
        yield return new("catch_up_days", "time_details", true, Choice: CatchUpDays);
        yield return new("world_map_lead", "time_details", true, Choice: LeadCap, Requires: () => EmpConfig.Server.WorldMapTime.Value == Net.WorldMapTime.PersonalPace);

        yield return new("delete_chara", "comfort", true, EmpConfig.Server.DeleteCharacter, Apply: "join");
        yield return new("dump_spares_belt", "comfort", true, EmpConfig.Server.DumpSparesBelt);
        yield return new("publish_mods", "comfort", true, EmpConfig.Server.PublishMods, Apply: "join");
        yield return new("look_mods_own", "comfort", true, EmpConfig.Server.LookModsOwn, Apply: "join", Requires: () => EmpConfig.Server.PublishMods.Value);

        yield return new("auto_reconnect", "repair", true, EmpConfig.Server.AutoReconnect);
        yield return new("auto_resync", "repair", true, EmpConfig.Server.AutoResync);
        yield return new("soft_recall", "repair", true, EmpConfig.Server.SoftRecall);
        yield return new("auto_save", "repair", true, EmpConfig.Server.AutoSave);
        yield return new("server_rests", "repair", true, EmpConfig.Server.ServerRestsWhenEmpty);
        yield return new("server_avatar", "repair", true, EmpConfig.Server.ServerAvatar, Apply: "restart");
        yield return new("enforce_ownership", "repair", true, EmpConfig.Server.EnforceOwnership);
        yield return new("bills", "repair", true);
    }

    /// <summary>
    ///     Every stored host rule the table shows: what a preset, the reset and the undo touch. Not the client's
    ///     own options, the depot, SoftRecallSwitchedOn or the keys only in the file: TravelCheckpointSeconds and
    ///     StrictValidationMode are file-only on purpose (a tuning value and a hardening switch, not a choice for the page)
    /// </summary>
    internal static ConfigEntryBase[] HostRules() => [
        ..Rows().Where(r => r.Entry is not null).Select(r => (ConfigEntryBase)r.Entry!).Distinct(),
        EmpConfig.Server.SourceValidationSet, EmpConfig.Server.WorldMapTime, EmpConfig.Server.WorldMapLeadCap,
        EmpConfig.Server.SpawnZone, EmpConfig.Server.NightPasses, EmpConfig.Server.IdleMinutes,
        EmpConfig.Server.KickKeepOutMinutes, EmpConfig.Server.MaxCharacters, EmpConfig.Server.CatchUpDays,
        EmpConfig.Server.LivingWorldMinutes, EmpConfig.Server.TeamMaxSize, EmpConfig.Server.TeamFoundCooldown,
        EmpConfig.Server.AbandonDays, EmpConfig.Server.LandsPerTeam,
    ];

    // presets: computed from the boxes each time a page opens, never stored (an old world with one box of its
    // own reads as Custom, with what differs listed, and stays as it is)
    internal static readonly string[] Presets = ["recommended", "classic", "custom"];

    /// <summary>
    ///     Classic host-led: the fallbacks off, turn-based combat, everyone follows the host, only the host
    ///     builds and manages the base; all else as recommended
    /// </summary>
    internal static RuleSource Classic
    {
        get {
            var values = new Dictionary<ConfigEntryBase, object>();
            foreach (var (entry, value) in CombatPace.WriteFor(1).Concat(OwnEverything.WriteFor(1)).Concat(Building.WriteFor(2))
                         .Concat(Travel.WriteFor(0))) {
                values[entry] = value;
            }
            return new(e => values.TryGetValue(e, out var v) ? v : e.DefaultValue);
        }
    }

    internal static RuleSource PresetSource(int preset) => preset == 0 ? RuleSource.Defaults : Classic;

    /// <summary>
    ///     The rows (choices and boxes not under a choice) whose value differs between two sources: what makes a
    ///     world "Custom". A box a choice leaves alone (turn-based under each player's pace) does not count
    /// </summary>
    internal static List<string> Differences(RuleSource current, RuleSource target)
    {
        var rows = Rows().Where(r => r.Under is null);
        return [
            ..rows.Where(r => r.Choice is { } c ? c.Read(current) != c.Read(target) : r.Entry is { } e && current.Get(e) != target.Get(e))
                .Select(r => r.Id),
        ];
    }

    internal static int Preset(RuleSource current) =>
        Differences(current, RuleSource.Defaults).Count == 0 ? 0 : Differences(current, Classic).Count == 0 ? 1 : 2;

    /// <summary>
    ///     Every host rule as the preset has it (Reset to recommended is preset 0); "Custom" writes nothing
    /// </summary>
    internal static void ApplyPreset(int preset)
    {
        if (preset is 0 or 1) {
            var target = PresetSource(preset);
            Write(HostRules().Select(e => (e, target.Raw(e)!)));
        } else {
            // "Custom" writes nothing: redraw so the line shows the real preset again
            MarkDirty();
        }
    }

    // what the host's rules were before each change made from a page, for this session only
    private static readonly Stack<(ConfigDefinition Key, object Value)[]> _undo = new();
    private static bool _undoing;

    internal static bool CanUndo => _undo.Count > 0;

    internal static void Undo()
    {
        if (_undo.Count == 0) {
            return;
        }

        var config = EmpMod.Instance.Config;
        var before = _undo.Pop();
        _undoing = true;
        try {
            Write(before.Where(b => config.ContainsKey(b.Key)).Select(b => (config[b.Key], b.Value)));
        } finally {
            _undoing = false;
        }
    }

    /// <summary>
    ///     The host's rules as a guest is told them (NetSessionRules, no new message): what it is not told reads null
    /// </summary>
    internal static RuleSource FromRules(NetSessionRules r)
    {
        var values = new Dictionary<ConfigEntryBase, object> {
            [EmpConfig.Server.IndependentTravel] = r.AllowIndependentTravel,
            [EmpConfig.Server.WorldMapTime] = r.WorldMapTime,
            [EmpConfig.Server.PlayerCombatTime] = r.UsePlayerCombatTime,
            [EmpConfig.Server.PlayerClock] = r.UsePlayerClock,
            [EmpConfig.Server.PlayerStepPace] = r.UsePlayerStepPace,
            [EmpConfig.Server.TurnBasedCombat] = r.UseTurnBasedCombat,
            [EmpConfig.Server.SharedAverageSpeed] = r.UseSharedSpeed,
            [EmpConfig.Server.PersonalQuests] = r.UsePersonalQuests,
            [EmpConfig.Server.PlayerShipping] = r.UsePlayerShipping,
            [EmpConfig.Server.OncePerPlayer] = r.OncePerPlayer,
            [EmpConfig.Server.SharedTax] = r.UseSharedTax,
            [EmpConfig.Server.TownBoards] = r.TownBoards,
            [EmpConfig.Server.PlayersAtBase] = r.PlayersAtBase,
            [EmpConfig.Server.GlobalChat] = r.GlobalChat,
            [EmpConfig.Server.TownLife] = r.TownLife,
            [EmpConfig.Server.ShopsFromEveryPlayer] = r.ShopsFromEveryPlayer,
            [EmpConfig.Server.NearestSpawns] = r.NearestSpawns,
            [EmpConfig.Server.ActivitiesFromActor] = r.ActivitiesFromActor,
            [EmpConfig.Server.AutoCombatPerPlayer] = r.AutoCombatPerPlayer,
            [EmpConfig.Server.SummonsFollowMaster] = r.SummonsFollowMaster,
            [EmpConfig.Server.Follow] = r.AllowFollow,
            [EmpConfig.Server.PrivateChat] = r.PrivateChat,
            [EmpConfig.Server.GuestMoongate] = r.GuestMoongate,
            [EmpConfig.Server.GuestLandingPortal] = r.GuestLandingPortal,
            [EmpConfig.Server.GuestSkyHelm] = r.GuestSkyHelm,
            [EmpConfig.Server.SharedMarriages] = r.SharedMarriages,
            [EmpConfig.Server.AffinityPerPlayer] = r.AffinityPerPlayer,
            [EmpConfig.Server.Teams] = r.UseTeams,
            [EmpConfig.Server.TeamBases] = r.TeamBases,
            [EmpConfig.Server.TeamPersonal] = r.TeamPersonal,
            [EmpConfig.Server.TeamPvp] = r.TeamPvp,
            [EmpConfig.Server.TeamKnowledge] = r.TeamKnowledge,
            [EmpConfig.Server.TeamBoxes] = r.TeamBoxes,
            [EmpConfig.Server.ContainerAccessLog] = r.ContainerLog,
            [EmpConfig.Server.AdminExtras] = r.AdminExtras,
            [EmpConfig.Server.TeamLeaders] = r.TeamLeaders,
            [EmpConfig.Server.LandRules] = r.LandRules,
            [EmpConfig.Server.SiblingLands] = r.SiblingLands,
            [EmpConfig.Server.TeamMaxSize] = r.TeamMaxSize,
            [EmpConfig.Server.TeamBills] = r.TeamBills,
            [EmpConfig.Server.TeamStory] = r.TeamStory,
            [EmpConfig.Server.TeamOwnStory] = r.TeamOwnStory,
            [EmpConfig.Server.TeamOwnGuilds] = r.TeamOwnGuilds,
            [EmpConfig.Server.GuestBuild] = r.AllowGuestBuild,
            [EmpConfig.Server.HostManagesBase] = r.HostManagesBase,
            [EmpConfig.Server.Duels] = r.AllowDuels,
            [EmpConfig.Server.GuestBouts] = r.GuestBouts,
            [EmpConfig.Server.DuelArena] = r.DuelArena,
            [EmpConfig.Server.DuelBets] = r.DuelBets,
            [EmpConfig.Server.PlayerKill] = r.AllowPlayerKill,
            [EmpConfig.Server.PlayerTrade] = r.AllowPlayerTrade,
            [EmpConfig.Server.PermadeathRevive] = r.PermadeathRevive,
            [EmpConfig.Server.OwnJournal] = r.OwnJournal,
            [EmpConfig.Server.WorldCopy] = r.KeepWorldCopy,
            [EmpConfig.Server.Takeover] = r.AllowTakeover,
            [EmpConfig.Server.NightPasses] = r.NightPasses,
            [EmpConfig.Server.IdleMinutes] = r.IdleMinutes,
            [EmpConfig.Server.TimeStopsWhenEmpty] = r.TimeStopsWhenEmpty,
            [EmpConfig.Server.CatchUpDays] = r.CatchUpDays,
            [EmpConfig.Server.LivingWorldMinutes] = r.LivingWorldMinutes,
            [EmpConfig.Server.OwnSleep] = r.UseOwnSleep,
            [EmpConfig.Server.SharedWorldTime] = r.UseSharedWorldTime,
            [EmpConfig.Server.WorldKeeper] = r.UseWorldKeeper,
            [EmpConfig.Server.DumpSparesBelt] = r.DumpSparesBelt,
            [EmpConfig.Server.AutoReconnect] = r.AllowReconnect,
            [EmpConfig.Server.AutoResync] = r.AutoResync,
            [EmpConfig.Server.SoftRecall] = r.SoftRecall,
        };
        return new(e => values.TryGetValue(e, out var v) ? v : null);
    }

    /// <summary>
    ///     The visible rows a guest is told, as label and value (the card "This world's rules", read only)
    /// </summary>
    internal static List<(string Label, string Value)> RuleLines(RuleSource source) => Lines(source, false);

    /// <summary>
    ///     The visible rows, and an Advanced one (not under a choice) only when it differs from its default: a hidden
    ///     box an old world keeps off is still told (council 2026-10-25 settings trim). all: every row not under a
    ///     choice, for notices
    /// </summary>
    private static List<(string Label, string Value)> Lines(RuleSource source, bool all)
    {
        var lines = new List<(string, string)>();
        var defaults = RuleSource.Defaults;
        foreach (var row in Rows()) {
            // (a box under a choice is told through its choice: turn-based under each player's pace means nothing)
            if (row.Advanced && (row.Under is not null || (!all && !Differs(row, source, defaults)))) {
                continue;
            }

            if (row.Choice is { } choice) {
                if (choice.Entries.All(e => source.Raw(e) is null)) {
                    continue;
                }
                var read = choice.Read(source);
                lines.Add((row.Label, read < 0 ? "emp_ui_rules_custom".lang() : choice.OptionLabel(read)));
            } else if (row.Entry is { } entry && source.Get(entry) is { } on) {
                lines.Add((row.Label, (on ? "emp_ui_rules_yes" : "emp_ui_rules_no").lang()));
            }
        }
        return lines;
    }

    // (what a guest is not told reads null: not a difference)
    private static bool Differs(SettingRow row, RuleSource source, RuleSource defaults) =>
        row.Choice is { } c ? c.Entries.Any(e => source.Raw(e) is not null) && c.Read(source) != c.Read(defaults)
        : row.Entry is { } e && source.Get(e) is { } v && v != defaults.Get(e);

    /// <summary>
    ///     The last change of the host's rules a guest was told, shown on the card
    /// </summary>
    internal static string? LastNotice { get; private set; }

    /// <summary>
    ///     Tells the player, one line per row, what the host changed; before is null for the first rules of a
    ///     session (the snapshot of the join). True if a line changed
    /// </summary>
    internal static bool NoticeChanges(NetSessionRules? before, NetSessionRules after)
    {
        if (before is null) {
            LastNotice = null;
            return true;
        }

        // (every row: a hidden box the host turns off is told too)
        var old = Lines(FromRules(before), true).ToDictionary(l => l.Label, l => l.Value);
        var changed = false;
        foreach (var (label, value) in Lines(FromRules(after), true)) {
            if (old.TryGetValue(label, out var was) && was != value) {
                LastNotice = "emp_ui_rules_changed".Loc(label, was, value);
                EmpPop.Information(LastNotice);
                changed = true;
            }
        }
        return changed;
    }

    private static volatile bool _dirty;

    /// <summary>
    ///     Both pages are drawn again when a rule they show changes, from here or elsewhere (the file watcher
    ///     reloads on its own thread: the next frame draws)
    /// </summary>
    internal static void Watch()
    {
        EmpMod.Instance.Config.SettingChanged += (_, e) => {
            var entry = e.ChangedSetting;
            if (entry == EmpConfig.Client.FetchMods || entry == EmpConfig.Client.KeepMods || HostRules().Contains(entry)) {
                _dirty = true;
            }
        };
    }

    internal static void MarkDirty() => _dirty = true;

    internal static void Update()
    {
        if (!_dirty) {
            return;
        }

        _dirty = false;
        LayerElinTogether.Instance?.Reopen();
        ModConfigPage.Refresh();
    }

    /// <summary>
    ///     The joining checks as one string: None and All stand alone (ticked with another, "none,all" read as no
    ///     check at all, audit of the council 2026-10-23), Source and Plugin go together
    /// </summary>
    internal static string ToggleCheck(string raw, string option, bool on)
    {
        var parts = raw.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0 && p != option).ToList();
        if (!on) {
            return string.Join(",", parts);
        }

        if (option is "none" or "all") {
            return option;
        }

        parts.RemoveAll(p => p is "none" or "all");
        parts.Insert(0, option);
        return string.Join(",", parts);
    }

    /// <summary>
    ///     Writes what differs, nothing else, and saves the file once
    /// </summary>
    internal static bool Write(IEnumerable<(ConfigEntryBase Entry, object Value)> changes)
    {
        var diff = changes.Where(c => !Equals(c.Entry.BoxedValue, c.Value)).ToArray();
        if (diff.Length == 0) {
            return false;
        }

        if (!_undoing) {
            _undo.Push([..HostRules().Select(e => (e.Definition, e.BoxedValue))]);
        }

        var config = EmpMod.Instance.Config;
        var save = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try {
            foreach (var (entry, value) in diff) {
                entry.BoxedValue = value;
            }
        } finally {
            config.SaveOnConfigSet = save;
        }

        if (save) {
            config.Save();
        }

        if (diff.Any(c => c.Entry == EmpConfig.Server.SourceValidationSet)) {
            NetSession.Instance.Transport?.CreateValidation();
        }

        return true;
    }
}
