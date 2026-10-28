using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Net packet: Host -> Client
/// </summary>
[MessagePackObject]
public class SaveDataProbe
{
    [Key(0)]
    public required LZ4Bytes Game { get; init; }

    [Key(1)]
    public required int RemoteCharaUid { get; init; }

    /// <summary>
    ///     Host option "Teams learn on their own": what this player's team knows, in place of what the copy of the
    ///     world holds (the host's own team's). None when the box is off, and from a zone hosted by a client
    /// </summary>
    [Key(2)]
    public TeamLore? Lore { get; init; }

    /// <summary>
    ///     Host option "Teams live their own story": where this player's team stands in the story, in place of what the
    ///     copy of the world holds (the host's own team's). None when the box is off, when the copy holds it already,
    ///     and from a zone hosted by a client
    /// </summary>
    [Key(3)]
    public StoryBundle? Story { get; init; }

    public Game MakeGameSave()
    {
        return Game.Decompress<Game>();
    }

    /// <summary>
    ///     The copy of the world a joining player gets (shared by the players joining together)
    /// </summary>
    public static LZ4Bytes PackWorld()
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var game = LZ4Bytes.Create(EClass.game);
        NetMetrics.WorldPack.Add(game.Bytes.Length, start);
        return game;
    }
}