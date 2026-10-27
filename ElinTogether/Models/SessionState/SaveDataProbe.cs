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