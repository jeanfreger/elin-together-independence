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

    // the game the ledger was read against, and whether every team was given its copy in it
    private static Game? _game;

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
            _game = EClass.game;
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
        if (!KeepsBook || !Seeded || EClass.game?.player?.recipes is not { } recipes || EClass.player.codex is not { } codex) {
            return;
        }

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
