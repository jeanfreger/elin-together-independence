using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to one player: a station craft it ran for that player taught it a new crafting idea (the game
///     adds it to "the player", which is the host's, and plays "idea" there, see TraitCrafter.Craft). The host takes
///     it back out of its own set; the crafter's game gets it here
/// </summary>
[MessagePackObject]
public class KnownCraftDelta : ElinDelta
{
    [Key(0)]
    public required int Id { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost || FromPeer || EClass.player is not { } p || !p.knownCraft.Add(Id)) {
            return;
        }

        SE.Play("idea");
        Msg.Say("newKnownCraft");
        if (LayerDragGrid.Instance) {
            LayerDragGrid.Instance.info.Refresh();
        }
    }
}
