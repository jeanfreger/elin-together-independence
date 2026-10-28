using System.Collections.Generic;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Where one team stands in the story (host option "Teams live their own story", see TeamOwnStory): the main quest
///     and the Nymelle exploration as that team plays them, the dialog choices of the story told to its actors. Plain
///     values: kept in the host's save as text, sent to a guest with its copy of the world
/// </summary>
[MessagePackObject]
public class StoryBundle
{
    /// <summary>
    ///     The story quests in the team's quest log (QuestMain, QuestExploration), as the game writes them (LZ4Bytes)
    /// </summary>
    [Key(0)]
    public byte[] Quests { get; set; } = [];

    /// <summary>
    ///     The story quests on offer to that team (Ash's exploration before it is taken)
    /// </summary>
    [Key(1)]
    public byte[] Offers { get; set; } = [];

    /// <summary>
    ///     The story quests that team finished: their ids and their types, as QuestManager.completedIDs/Types hold them
    /// </summary>
    [Key(2)]
    public List<string> Completed { get; set; } = [];

    /// <summary>
    ///     Dialog choices of the story (Player.dialogFlags) told to Ash and Fiama
    /// </summary>
    [Key(3)]
    public Dictionary<string, int> Dialog { get; set; } = [];

    /// <summary>
    ///     QuestManager.GetPhase of the main quest (999 finished, -1 not started): read without opening the quests
    /// </summary>
    [Key(4)]
    public int Main { get; set; } = -1;

    /// <summary>
    ///     The same for the exploration of Nymelle
    /// </summary>
    [Key(5)]
    public int Exploration { get; set; } = -1;

    /// <summary>
    ///     The key items (Player.keyItems: the licenses, the lucky coins, the well's wishes, the story's potions), id
    ///     -> count. None in a story written before they were part of it
    /// </summary>
    [Key(6)]
    public Dictionary<int, int>? KeyItems { get; set; }
}
