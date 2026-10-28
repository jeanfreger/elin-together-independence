using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Host option "Affinity per player" (<see cref="AffinityPerPlayer" />): from the host to a player that joins,
///     what every resident it knows thinks of that player
/// </summary>
[MessagePackObject]
public class PlayerAffinitiesDelta : ElinDelta
{
    /// <summary>
    ///     Resident chara uid -> its affinity with the player this goes to
    /// </summary>
    [Key(0)]
    public required Dictionary<int, int> Values { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetClient) {
            AffinityPerPlayer.Learn(Values);
        }
    }
}
