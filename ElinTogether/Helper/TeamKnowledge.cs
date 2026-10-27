using System.Collections.Generic;
using System.Linq;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using Newtonsoft.Json;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-25, host option "Teams learn on their own" (dev/PLAN_teams_knowledge.md). The recipes, the
///     recipes crafted once and the codex live in the one Player object of the world: with the box on, each team has
///     its own. The host keeps every team's in its save (<see cref="ElinNetHost.TeamLoreBook" />); every game, the
///     host's own included, holds its team's in that Player object while it plays; the vanilla save gets the union of
///     all teams, so a world opened without the mod, or with the box off, keeps everything anyone learnt
/// </summary>
[HarmonyPatch]
internal static class TeamKnowledge
{
    /// <summary>
    ///     The ledger's layout. A save written by a newer layout is left as it is (one book for everyone)
    /// </summary>
    private const int Schema = 1;

    // the game that keeps the book: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    /// <summary>
    ///     The box as this game knows it: the host's own setting, a guest's from the host's rules
    /// </summary>
    internal static bool On => KeepsBook
        ? EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamKnowledge.Value
        : NetSession.Instance.Rules is { UseTeams: true, TeamKnowledge: true };

    // the game the ledger was read against, and the team whose knowledge its Player object holds (none: the world's,
    // as the save has it)
    private static Game? _game;
    private static int? _team;

    /// <summary>
    ///     The team whose knowledge the local Player object holds, in the game that keeps the book; none while it holds
    ///     the world's
    /// </summary>
    internal static int? LocalTeam => KeepsBook && ReferenceEquals(EClass.game, _game) ? _team : null;

    private static bool Seeded => ElinNetHost.TeamLoreState[1] != 0;

    /// <summary>
    ///     Every frame, in the game that keeps the book: the box ticked gives every team a copy of what the world
    ///     knows (once), the box unticked gives the world back everything any team learnt
    /// </summary>
    internal static void Update()
    {
        if (!KeepsBook || !EClass.core.IsGameStarted || EClass.game?.player?.recipes is null) {
            return;
        }

        if (!ReferenceEquals(EClass.game, _game)) {
            // (a save just read: its Player object knows what every team knew)
            _game = EClass.game;
            _team = null;
            if (ElinNetHost.TeamLoreState[0] > Schema) {
                EmpLog.Warning("The teams' knowledge was written by a newer version of the mod ({Schema}): left as it is",
                    ElinNetHost.TeamLoreState[0]);
            }
        }

        if (ElinNetHost.TeamLoreState[0] > Schema) {
            return;
        }

        if (On && !Seeded) {
            Seed();
        } else if (!On && Seeded) {
            MergeBack();
        }

        // the host plays its team's knowledge like any player, and another team's when it changes team
        if (On && Seeded && Teams.TeamOf(EClass.pc.uid) is var team && team != _team) {
            Hold(team);
        }
    }

    /// <summary>
    ///     The local Player object holds what that team knows: what it held goes back to its team first
    /// </summary>
    private static void Hold(int team)
    {
        if (_team is { } was) {
            Store(was, Take(EClass.player));
        }

        var lore = Stored(team) ?? WorldUnion();
        Rebuild(lore);
        Put(EClass.player, lore);
        _team = team;
        EmpLog.Information("The host plays with the knowledge of team {TeamId}: {Recipes} recipes, {Creatures} creatures",
            team, lore.Known.Count, lore.Codex.Count);
    }

    /// <summary>
    ///     What the game itself writes in the recipes as a save is read (Game.OnLoad, older saves): written in the
    ///     world's list, each team gets the same from its own
    /// </summary>
    private static void Rebuild(TeamLore lore)
    {
        // (Game.cs, below 0.23.100: the heavy training dummy got a second recipe id)
        if (lore.Known.TryGetValue("trainingDummy_heavy", out var times) && times != 0 && !lore.Known.ContainsKey("trainingDummy_heavy1")) {
            lore.Known["trainingDummy_heavy1"] = times;
        }
    }

    /// <summary>
    ///     The box was ticked: every team, the Wanderers too, knows what the world knows now
    /// </summary>
    private static void Seed()
    {
        var world = Take(EClass.player);
        foreach (var team in ElinNetHost.TeamList.Keys.Append(Teams.Wanderers).Distinct()) {
            Store(team, world);
        }

        ElinNetHost.TeamLoreState = [Schema, 1];
        EmpLog.Information("Teams learn on their own: every team was given what the world knows ({Recipes} recipes, {Creatures} creatures)",
            world.Known.Count, world.Codex.Count);
    }

    /// <summary>
    ///     The box was unticked: the world knows everything any team learnt, the ledger is emptied (ticked again, every
    ///     team starts again from the world's)
    /// </summary>
    private static void MergeBack()
    {
        var all = WorldUnion();
        Put(EClass.player, all);
        _team = null;
        ElinNetHost.TeamLoreBook.Clear();
        ElinNetHost.TeamLoreState = [Schema, 0];
        EmpLog.Information("Teams learn together again: the world knows all the teams knew ({Recipes} recipes, {Creatures} creatures)",
            all.Known.Count, all.Codex.Count);
    }

    /// <summary>
    ///     What the local player knows, and every team of the ledger
    /// </summary>
    private static TeamLore WorldUnion()
    {
        return Union(ElinNetHost.TeamLoreBook.Keys.Select(Stored).OfType<TeamLore>().Prepend(Take(EClass.player)));
    }

    /// <summary>
    ///     What that team knows now: the local Player object's for the team it holds, the ledger's for another
    /// </summary>
    internal static TeamLore Of(int team)
    {
        return team == LocalTeam ? Take(EClass.player) : Stored(team) ?? WorldUnion();
    }

    // ---- the save: the union, then the local team's again

    /// <summary>
    ///     The game writes its save: the Player object holds what every team knows while it is written. A save opened
    ///     without the mod, or with the box off, keeps everything
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Game), nameof(Game.Save))]
    private static void BeforeSave(out (Dictionary<string, int>, HashSet<string>, Dictionary<string, CodexCreature>)? __state)
    {
        __state = null;
        if (LocalTeam is not { } team || EClass.game?.player?.recipes is not { } recipes || EClass.player.codex is not { } codex) {
            return;
        }

        // (the ledger is written with this save: the local team's as it is now)
        Store(team, Take(EClass.player));
        __state = (recipes.knownRecipes, recipes.craftedRecipes, codex.creatures);
        Put(EClass.player, WorldUnion());
    }

    // a finalizer: a save that throws does not leave the union in place
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Game), nameof(Game.Save))]
    private static void AfterSave((Dictionary<string, int> Known, HashSet<string> Crafted, Dictionary<string, CodexCreature> Codex)? __state)
    {
        if (__state is not { } own || EClass.game?.player?.recipes is not { } recipes) {
            return;
        }

        recipes.knownRecipes = own.Known;
        recipes.craftedRecipes = own.Crafted;
        EClass.player.codex.creatures = own.Codex;
    }

    // ---- lists

    /// <summary>
    ///     A copy of what that Player object knows
    /// </summary>
    internal static TeamLore Take(Player player)
    {
        return new() {
            Known = new(player.recipes.knownRecipes),
            Crafted = [..player.recipes.craftedRecipes],
            Codex = player.codex.creatures.ToDictionary(c => c.Key, c => (int[])c.Value._ints.Clone()),
        };
    }

    /// <summary>
    ///     That Player object knows this, and nothing else (copies)
    /// </summary>
    internal static void Put(Player player, TeamLore lore)
    {
        player.recipes.knownRecipes = new(lore.Known);
        player.recipes.craftedRecipes = [..lore.Crafted];
        player.codex.creatures = lore.Codex.ToDictionary(c => c.Key, c => new CodexCreature {
            id = c.Key,
            _ints = Ints(c.Value),
        });
    }

    // (five numbers, whatever was stored)
    private static int[] Ints(int[]? stored)
    {
        var ints = new int[5];
        stored?.Take(5).ToArray().CopyTo(ints, 0);
        return ints;
    }

    /// <summary>
    ///     Everything these teams know: the most times a recipe was learnt, the most of each codex number, the flags
    ///     of all
    /// </summary>
    internal static TeamLore Union(IEnumerable<TeamLore> lores)
    {
        var union = new TeamLore();
        var crafted = new HashSet<string>();
        foreach (var lore in lores) {
            foreach (var (id, times) in lore.Known) {
                union.Known[id] = System.Math.Max(union.Known.GetValueOrDefault(id), times);
            }

            crafted.UnionWith(lore.Crafted);
            foreach (var (id, ints) in lore.Codex) {
                var into = union.Codex.TryGetValue(id, out var have) ? have : union.Codex[id] = new int[5];
                var from = Ints(ints);
                for (var i = 0; i < into.Length; i++) {
                    // (the second number holds flags: the card given once)
                    into[i] = i == 1 ? into[i] | from[i] : System.Math.Max(into[i], from[i]);
                }
            }
        }

        union.Crafted = [..crafted];
        return union;
    }

    /// <summary>
    ///     What that team knows in the ledger, none when it has no entry (or one that cannot be read)
    /// </summary>
    internal static TeamLore? Stored(int team)
    {
        if (!ElinNetHost.TeamLoreBook.TryGetValue(team, out var text)) {
            return null;
        }

        try {
            return JsonConvert.DeserializeObject<TeamLore>(text);
        } catch (System.Exception ex) {
            EmpLog.Warning(ex, "The knowledge of team {TeamId} could not be read", team);
            return null;
        }
    }

    internal static void Store(int team, TeamLore lore)
    {
        ElinNetHost.TeamLoreBook[team] = JsonConvert.SerializeObject(lore);
    }

    /// <summary>
    ///     What the bench reads: each team's numbers of recipes, crafted recipes and creatures
    /// </summary>
    internal static string Describe()
    {
        var lines = ElinNetHost.TeamLoreBook.Keys.OrderBy(k => k).Select(team => Stored(team) is { } lore
            ? $"{team} recipes {lore.Known.Count} crafted {lore.Crafted.Count} codex {lore.Codex.Count}"
            : $"{team} unreadable");
        return $"schema {ElinNetHost.TeamLoreState[0]} seeded {ElinNetHost.TeamLoreState[1]}\n" + string.Join("\n", lines);
    }
}
