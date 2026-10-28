using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(GameDate), nameof(GameDate.ShipGoods))]
internal static class WorldShipGoodsEvent
{
    // the world's own total, kept while the host's sale runs on its owner's
    private static (int, long)? _world;

    [HarmonyPrefix]
    internal static bool OnShipGoods(ref ShippingResult? __state)
    {
        __state = EClass.player.shippingResults.LastItem();
        // client can't initiate this, nor an away client in its copy of the world: the host ships the box,
        // a copy would sell the same goods again
        if (NetSession.Instance.Connection is ElinNetClient || NetSession.Instance.IsAway) {
            return false;
        }

        // every player is paid for its own goods, what is left in the box is the host's
        (NetSession.Instance.Connection as ElinNetHost)?.ShipPlayersGoods();
        if (NetSession.Instance.Connection is ElinNetHost) {
            // the host is a player like any other: the hearth of its own team land, and its owner's shipping total
            if (Teams.OwnLand(EClass.pc.uid, EClass.player.uidLastShippedZone) is { } land) {
                EClass.player.uidLastShippedZone = land.uid;
            }

            _world = TeamOwnStory.On ? ElinNetHost.SwapHostTotal(EClass.pc.uid) : null;
        }

        return true;
    }


    [HarmonyPostfix]
    internal static void OnAfterShipGoods(ShippingResult? __state)
    {
        if (NetSession.Instance.Connection is not ElinNetHost host) {
            return;
        }

        var hostTotal = (EClass.player.stats.shipNum, EClass.player.stats.shipMoney);
        if (_world is { } world) {
            ElinNetHost.SwapHostTotal(EClass.pc.uid, world);
            _world = null;
        }

        if (EClass.player.shippingResults.LastItem() is not { } result || result == __state) {
            return;
        }

        var zone = EClass.game.spatials.Find(result.uidZone);
        host.Delta.AddRemote(new ShippingResultDelta {
            Ints = [..result.ints],
            ItemStrs = [..result.items.Select(item => item._strs)],
            ShipNum = hostTotal.shipNum,
            ShipMoney = hostTotal.shipMoney,
            BranchLv = zone?.branch?.lv ?? 0,
            BranchExp = zone?.branch?.exp ?? 0,
        });

        EmpLog.Debug("Shipping result {ShipIncome} {ShipItemCount} {ZoneUid}",
            result.GetIncome(), result.items.Count, result.uidZone);
    }
}

/// <summary>
///     Morning deliveries are the host world's too, an away copy would deliver them again
/// </summary>
[HarmonyPatch(typeof(GameDate), nameof(GameDate.ShipPackages))]
internal static class WorldShipPackagesEvent
{
    [HarmonyPrefix]
    internal static bool OnShipPackages()
    {
        return NetSession.Instance.Connection is not ElinNetClient && !NetSession.Instance.IsAway;
    }
}
