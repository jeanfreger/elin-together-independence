using System.Collections.Generic;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     What one team knows (host option "Teams learn on their own", see TeamKnowledge): the recipes it learnt and how
///     many times, the recipes it crafted once, its codex. Plain values: kept in the host's save as text, sent to a
///     guest with its copy of the world
/// </summary>
[MessagePackObject]
public class TeamLore
{
    /// <summary>
    ///     Recipe id -> times learnt (RecipeManager.knownRecipes)
    /// </summary>
    [Key(0)]
    public Dictionary<string, int> Known { get; set; } = [];

    /// <summary>
    ///     Recipes crafted once: no first-time bonus again (RecipeManager.craftedRecipes)
    /// </summary>
    [Key(1)]
    public List<string> Crafted { get; set; } = [];

    /// <summary>
    ///     Creature id -> its codex numbers (CodexCreature._ints: cards, flags, weak spots, kills, spawns)
    /// </summary>
    [Key(2)]
    public Dictionary<string, int[]> Codex { get; set; } = [];
}
