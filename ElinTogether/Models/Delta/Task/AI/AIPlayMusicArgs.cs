using System.Collections.Generic;
using System.Linq;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models.AI;

[MessagePackObject]
public class AIPlayMusicArgs : TaskArgsBase
{
    [Key(0)]
    public required RemoteCard Tool { get; init; }

    /// <summary>
    ///     The levels of the songs the player knows (id -> level): the copies playing along judge its mistakes and
    ///     rewards by the level of the song it plays, not by a new song of level 0
    /// </summary>
    [Key(1)]
    public Dictionary<string, int>? SongLv { get; init; }

    public static AIPlayMusicArgs Create(AI_PlayMusic task)
    {
        return new() {
            Tool = task.tool,
            SongLv = EClass.player.knownSongs.ToDictionary(p => p.Key, p => p.Value.lv),
        };
    }

    public override AIAct CreateSubAct()
    {
        var task = new AI_PlayMusic {
            id = ABILITY.AI_PlayMusic,
            tool = Tool,
        };
        AIPlayMusicPatch.NoteSongLv(task, SongLv);
        return task;
    }
}