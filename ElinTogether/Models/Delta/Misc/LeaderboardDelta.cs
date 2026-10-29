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

    // who each row is: a player character uid, a team id on the wealth board
    [Key(5)]
    public int[] Uids { get; set; } = [];

    [Key(6)]
    public long OwnScore { get; set; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return IsAnswer ? "not a request" : Board is < 0 or > 3 ? "no such board" : NoCharacter(sender);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            if (!IsAnswer && host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var from)) {
                Helper.Leaderboards.Handle(this, from, host);
            }

            return;
        }

        if (IsAnswer) {
            Helper.Leaderboards.Show(this);
        }
    }
}
