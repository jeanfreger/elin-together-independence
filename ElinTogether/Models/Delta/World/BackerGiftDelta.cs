using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Hunt 5 H5-09: the gift box of the backer reward (FactionBranch.ReceivePackages) came only for the host's own
///     reward code, once for the whole world. A guest whose game holds a reward code asks for its own box, once per
///     character: the host puts it with the base's parcels, as the game does for the host
/// </summary>
[MessagePackObject]
public class BackerGiftDelta : ElinDelta
{
    // on the player's character, kept with the world: one box per character
    internal const string Given = "emp_backer_gift";

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return NoCharacter(sender);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost { IsZoneSession: false } host || !FromPeer ||
            host.PlayerChara(OriginPeer) is not { } player || player.GetBool(Given)) {
            return;
        }

        player.SetBool(Given, true);
        // as FactionBranch.ReceivePackages does for the host: in the base's mail with the next parcels
        TeamMail.Send(player, ThingGen.Create("gift"));
        EmpLog.Information("Backer gift box sent for player {Uid}", player.uid);
    }
}
