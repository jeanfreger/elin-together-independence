using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Leaderboards (host box "Leaderboards"): a request (player -> host, Board 0 to 3) and its answer (host ->
///     player, the top ten and the player's own rank) in one class
/// </summary>
[MessagePackObject]
public class LeaderboardDelta : ElinDelta
{
    [Key(0)]
    public required int Board { get; init; }

    // false: a request, true: the host's answer
    [Key(1)]
    public bool IsAnswer { get; set; }

    [Key(2)]
    public string[] Names { get; set; } = [];

    [Key(3)]
    public long[] Scores { get; set; } = [];

    [Key(4)]
    public int OwnRank { get; set; }

    protected override void OnApply(ElinNetBase net)
    {
        // TODO(PLAN_hub_features WP-D, Helper/Leaderboards.cs): host computes and answers, guest hands it to TabLeaderboard
    }
}
