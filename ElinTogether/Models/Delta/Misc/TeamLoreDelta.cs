using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to one player (host option "Teams learn on their own", <see cref="TeamKnowledge" />): what its
///     team knows now, in place of what its game knew. Sent when the box is ticked or unticked (unticked: what the
///     world knows) and when the player's team changes; a copy of the world carries it too (SaveDataProbe.Lore)
/// </summary>
[MessagePackObject]
public class TeamLoreDelta : ElinDelta
{
    [Key(0)]
    public required TeamLore Lore { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        // (a zone hosted by a client: the visitor hears it from the host of the world)
        if (net is ElinNetHost || FromPeer || player?.recipes is null) {
            return;
        }

        TeamKnowledge.Put(player, Lore);
        EmpLog.Information("The host says what this player's team knows: {Recipes} recipes, {Creatures} creatures",
            Lore.Known.Count, Lore.Codex.Count);
    }
}
