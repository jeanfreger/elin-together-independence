using System.Collections.Generic;
using System.Linq;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using Newtonsoft.Json;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-28, host option "Teams live their own story" (experimental, dev/COUNCIL_2026-10-28_team_story.md).
///     The story is the main quest (QuestMain, 0-700) and the exploration of Nymelle (QuestExploration, 0-6), with the
///     dialog choices told to Ash and Fiama: with the box on, each team has its own. The host keeps every team's in its
///     save (<see cref="ElinNetHost.TeamStoryBundles" />); every game, the host's own included, holds its team's in
///     its quest log while it plays. The save holds the WORLD TEAM's (the host's team when the box was ticked, changed
///     only by the host): a world opened without the mod, or with the box off, goes on with that team's story. Side
///     stories, key items and the rest of the quest log stay the world's (their union is the world's anyway). Random
///     and personal quests are never part of it. Copied from <see cref="TeamKnowledge" />
/// </summary>
[HarmonyPatch]
internal static class TeamOwnStory
{
    /// <summary>
    ///     The ledger's layout. A save written by a newer layout is left as it is: no team's story is swapped at all
    /// </summary>
    private const int Schema = 1;

    // the quests of the story, by id and by the type name QuestManager.completedTypes holds
    private static readonly HashSet<string> StoryIds = ["main", "exploration"];
    private static readonly HashSet<string> StoryTypes = [nameof(QuestMain), nameof(QuestExploration)];

    // the dialog choices of the story (Ash's and Fiama's dramas: setFlag ash1, tutorial1, fiama1)
    private static readonly string[] DialogIds = ["ash1", "tutorial1", "fiama1"];

    // the game that keeps the book: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    private static int[] State => ElinNetHost.TeamStoryBundleState;

    // (a ledger written by a newer version: one story for everyone)
    private static bool TooNew => KeepsBook && State[0] > Schema;

    /// <summary>
    ///     The box as this game knows it: the host's own setting, a guest's from the host's rules
    /// </summary>
    internal static bool On => !TooNew && (KeepsBook
        ? EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamOwnStory.Value
        : NetSession.Instance.Rules is { UseTeams: true, TeamOwnStory: true });

    private static bool Seeded => State[1] != 0;

    /// <summary>
    ///     The team whose story the save keeps
    /// </summary>
    internal static int WorldTeam => State[2];

    // the game the ledger was read against, and the team whose story its quest log holds (none: the world team's, as
    // the save has it)
    private static Game? _game;
    private static int? _team;

    /// <summary>
    ///     The team whose story the local quest log holds, in the game that keeps the book; none while it holds the
    ///     world team's as the save has it, or the box is off
    /// </summary>
    internal static int? LocalTeam => KeepsBook && ReferenceEquals(EClass.game, _game) ? _team : null;

    internal static bool IsStory(Quest? quest)
    {
        return quest is QuestMain or QuestExploration;
    }

    /// <summary>
    ///     Every frame, in the game that keeps the book: the box ticked gives every team the world's story (once), the
    ///     box unticked gives the world the world team's story
    /// </summary>
    internal static void Update()
    {
        if (!KeepsBook || !EClass.core.IsGameStarted || EClass.game?.quests is null || EClass.player is null) {
            return;
        }

        if (!ReferenceEquals(EClass.game, _game)) {
            // (a save just read: its quest log holds the world team's story)
            _game = EClass.game;
            _team = null;
            if (State[0] > Schema) {
                EmpLog.Warning("The teams' story was written by a newer version of the mod ({Schema}): one story for everyone",
                    State[0]);
            }
        }

        if (TooNew) {
            return;
        }

        if (On && !Seeded) {
            Seed();
        } else if (!On && Seeded) {
            MergeBack();
        }
    }

    /// <summary>
    ///     The box was ticked: every team, the Wanderers too, starts at the story point the world is at. The world team
    ///     is the host's
    /// </summary>
    private static void Seed()
    {
        var world = Take();
        var host = Teams.TeamOf(EClass.pc.uid);
        foreach (var team in Teams.Ids().Append(host).Distinct()) {
            Store(team, world);
        }

        ElinNetHost.TeamStoryBundleState = [Schema, 1, host, 0];
        _team = host;
        EmpLog.Information("Teams live their own story: every team starts at main {Main}, exploration {Exploration}; the world team is {TeamId}",
            world.Main, world.Exploration, host);
    }

    /// <summary>
    ///     The box was unticked: the world goes on with the world team's story, the ledger is emptied (ticked again,
    ///     every team starts again from the world's)
    /// </summary>
    private static void MergeBack()
    {
        if (LocalTeam is { } local && local != WorldTeam && Stored(WorldTeam) is { } world) {
            Put(world);
        }

        _team = null;
        ElinNetHost.TeamStoryBundles.Clear();
        ElinNetHost.TeamStoryBundleState = [Schema, 0, 0, 0];
        EmpLog.Information("One story for the world again: the world team's");
    }

    // ---- the save: the world team's story, then the local team's again

    /// <summary>
    ///     The game writes its save: the quest log holds the world team's story while it is written. A save opened
    ///     without the mod, or with the box off, goes on with it
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Game), nameof(Game.Save))]
    private static void BeforeSave(out Snapshot? __state)
    {
        __state = null;
        if (LocalTeam is not { } team || !On || EClass.game?.quests is null) {
            return;
        }

        // (the ledger is written with this save: the local team's as it is now)
        Store(team, Take());
        if (team == WorldTeam || Stored(WorldTeam) is not { } world) {
            return;
        }

        __state = Snap();
        Put(world);
    }

    // a finalizer: a save that throws does not leave the world team's story in place
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Game), nameof(Game.Save))]
    private static void AfterSave(Snapshot? __state)
    {
        if (__state is not null) {
            Restore(__state);
        }
    }

    // ---- the quest log

    /// <summary>
    ///     A copy of the story the local quest log holds
    /// </summary>
    internal static StoryBundle Take()
    {
        var quests = EClass.game.quests;
        return new() {
            Quests = LZ4Bytes.Create(quests.list.Where(IsStory).ToList()).Bytes,
            Offers = LZ4Bytes.Create(quests.globalList.Where(IsStory).ToList()).Bytes,
            Completed = [..quests.completedIDs.Where(StoryIds.Contains), ..quests.completedTypes.Where(StoryTypes.Contains)],
            Dialog = DialogIds.Where(EClass.player.dialogFlags.ContainsKey).ToDictionary(id => id, id => EClass.player.dialogFlags[id]),
            Main = quests.GetPhase<QuestMain>(),
            Exploration = quests.GetPhase<QuestExploration>(),
        };
    }

    /// <summary>
    ///     The local quest log holds this story, and no other (new objects: what was there is dropped)
    /// </summary>
    internal static void Put(StoryBundle bundle)
    {
        var quests = EClass.game.quests;
        quests.list.RemoveAll(IsStory);
        quests.globalList.RemoveAll(IsStory);
        quests.completedIDs.ExceptWith(StoryIds);
        quests.completedTypes.ExceptWith(StoryTypes);

        // (the game's own fix after a load, for one log: a story quest once)
        foreach (var quest in Read(bundle.Quests).GroupBy(q => q.GetType()).Select(g => g.First()).Reverse()) {
            quest.task?.SetOwner(quest);
            quests.list.Insert(0, quest);
        }

        foreach (var offer in Read(bundle.Offers).GroupBy(q => q.GetType()).Select(g => g.First())) {
            quests.globalList.Add(offer);
        }

        foreach (var done in bundle.Completed) {
            (StoryTypes.Contains(done) ? quests.completedTypes : quests.completedIDs).Add(done);
        }

        foreach (var id in DialogIds) {
            if (bundle.Dialog.TryGetValue(id, out var value)) {
                EClass.player.dialogFlags[id] = value;
            } else {
                EClass.player.dialogFlags.Remove(id);
            }
        }
    }

    private static List<Quest> Read(byte[] data)
    {
        if (data.Length == 0) {
            return [];
        }

        try {
            return new LZ4Bytes { Bytes = data }.Decompress<List<Quest>>()?.Where(IsStory).ToList() ?? [];
        } catch (System.Exception ex) {
            EmpLog.Warning(ex, "Story quests of a team could not be read");
            return [];
        }
    }

    /// <summary>
    ///     The story objects of the local quest log, the exact ones, to put back as they were
    /// </summary>
    internal sealed class Snapshot
    {
        internal required List<(int Index, Quest Quest)> List { get; init; }
        internal required List<(int Index, Quest Quest)> Offers { get; init; }
        internal required string[] Completed { get; init; }
        internal required Dictionary<string, int> Dialog { get; init; }
    }

    internal static Snapshot Snap()
    {
        var quests = EClass.game.quests;
        return new() {
            List = quests.list.Select((q, i) => (i, q)).Where(x => IsStory(x.q)).ToList(),
            Offers = quests.globalList.Select((q, i) => (i, q)).Where(x => IsStory(x.q)).ToList(),
            Completed = [..quests.completedIDs.Where(StoryIds.Contains), ..quests.completedTypes.Where(StoryTypes.Contains)],
            Dialog = DialogIds.Where(EClass.player.dialogFlags.ContainsKey).ToDictionary(id => id, id => EClass.player.dialogFlags[id]),
        };
    }

    internal static void Restore(Snapshot snap)
    {
        var quests = EClass.game.quests;
        quests.list.RemoveAll(IsStory);
        quests.globalList.RemoveAll(IsStory);
        quests.completedIDs.ExceptWith(StoryIds);
        quests.completedTypes.ExceptWith(StoryTypes);
        foreach (var (index, quest) in snap.List) {
            quests.list.Insert(System.Math.Min(index, quests.list.Count), quest);
        }

        foreach (var (index, quest) in snap.Offers) {
            quests.globalList.Insert(System.Math.Min(index, quests.globalList.Count), quest);
        }

        foreach (var done in snap.Completed) {
            (StoryTypes.Contains(done) ? quests.completedTypes : quests.completedIDs).Add(done);
        }

        foreach (var id in DialogIds) {
            if (snap.Dialog.TryGetValue(id, out var value)) {
                EClass.player.dialogFlags[id] = value;
            } else {
                EClass.player.dialogFlags.Remove(id);
            }
        }
    }

    // ---- the ledger

    /// <summary>
    ///     That team's story in the ledger, none when it has no entry (or one that cannot be read)
    /// </summary>
    internal static StoryBundle? Stored(int team)
    {
        if (!ElinNetHost.TeamStoryBundles.TryGetValue(team, out var text)) {
            return null;
        }

        try {
            return JsonConvert.DeserializeObject<StoryBundle>(text);
        } catch (System.Exception ex) {
            EmpLog.Warning(ex, "The story of team {TeamId} could not be read", team);
            return null;
        }
    }

    internal static void Store(int team, StoryBundle bundle)
    {
        ElinNetHost.TeamStoryBundles[team] = JsonConvert.SerializeObject(bundle);
    }

    /// <summary>
    ///     What the bench reads: each team's main and exploration phases, the world team
    /// </summary>
    internal static string Describe()
    {
        var lines = ElinNetHost.TeamStoryBundles.Keys.OrderBy(k => k).Select(team => Stored(team) is { } bundle
            ? $"{team} main {bundle.Main} exploration {bundle.Exploration} dialog {string.Join(",", bundle.Dialog.Select(d => $"{d.Key}={d.Value}"))}"
            : $"{team} unreadable");
        return $"schema {State[0]} seeded {State[1]} world {State[2]} lothria {State[3]} local {LocalTeam?.ToString() ?? "-"}\n" +
               string.Join("\n", lines);
    }

    /// <summary>
    ///     The host's explicit choice: the story of that team is the one the save keeps from now on
    /// </summary>
    internal static string SetWorldTeam(int team)
    {
        if (!KeepsBook || !On || !Seeded) {
            return "Host only, with the box on";
        }

        if (team != Teams.Wanderers && !ElinNetHost.TeamList.ContainsKey(team)) {
            return $"No team {team}";
        }

        if (Stored(team) is null && team != LocalTeam) {
            Store(team, Take());
        }

        State[2] = team;
        EmpLog.Information("The world team is now {TeamId}: the save keeps its story", team);
        return $"The world team is {team}";
    }
}
