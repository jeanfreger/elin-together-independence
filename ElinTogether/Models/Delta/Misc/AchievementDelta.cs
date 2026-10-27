using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Hunt 5 H5-10: a Steam achievement the host's game earned for another player (see PlayerAchievements): given
///     in that player's own game, on its own Steam account
/// </summary>
[MessagePackObject]
public class AchievementDelta : ElinDelta
{
    [Key(0)]
    public required int Id { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "a guest does not give achievements";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net.IsHost) {
            return;
        }

        EmpLog.Information("Achievement {Id} earned in the host's game for this player", (ID_Achievement)Id);
        Steam.GetAchievement((ID_Achievement)Id);
    }
}
