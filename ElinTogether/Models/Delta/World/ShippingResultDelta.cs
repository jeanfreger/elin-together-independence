using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class ShippingResultDelta : ElinDelta
{
    [Key(0)]
    public required long[] Ints { get; init; }

    [Key(1)]
    public required string[][] ItemStrs { get; init; }

    [Key(2)]
    public required int ShipNum { get; init; }

    [Key(3)]
    public required long ShipMoney { get; init; }

    [Key(4)]
    public required int BranchLv { get; init; }

    [Key(5)]
    public required int BranchExp { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net.IsHost) {
            return;
        }

        // the sale of the host's own goods: its report is its own (ours comes with ShippingPayout),
        // the shipping total and the base are everyone's
        var result = new ShippingResult {
            ints = [..Ints],
        };

        // (one total per owner: the host's is not ours, ours comes with ShippingPayout)
        if (!Helper.TeamOwnStory.On) {
            player.stats.shipNum = ShipNum;
            player.stats.shipMoney = ShipMoney;
        }

        var zone = game.spatials.Find(result.uidZone) ?? pc.homeZone;
        if (BranchLv > 0 && zone?.branch is { } branch) {
            branch.lv = BranchLv;
            branch.exp = BranchExp;
        }
    }
}