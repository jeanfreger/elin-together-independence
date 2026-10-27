using System.Linq;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The interaction menu on another player's character: the game offers to open its bag as it does for an
///     ally (and the host could then help itself). Between players it is a trade both agree to
/// </summary>
[HarmonyPatch(typeof(ActPlan), nameof(ActPlan._Update))]
internal static class PlayerTradePatch
{
    [HarmonyPostfix]
    internal static void OnUpdatePlan(ActPlan __instance)
    {
        if (!PlayerTrade.Enabled && !PlayerDuel.Enabled && !PlayerFollow.Enabled) {
            return;
        }

        // on one's own tile: giving up the duel (the other duellist's menu shows its blows during the fight)
        if (PlayerDuel.Enabled && PlayerDuel.InDuel && __instance.pos.Equals(EClass.pc.pos)) {
            __instance.TrySetAct("emp_act_duel_giveup", () => {
                PlayerDuel.GiveUp();
                return false;
            }, EClass.pc, null, 2);
        }

        foreach (var item in __instance.list.ToArray()) {
            if (item.act is not DynamicAct { id: "actTrade" } || item.tc is not Chara { IsRemotePlayer: true } other) {
                continue;
            }

            if (PlayerTrade.Enabled) {
                __instance.list.Remove(item);
                __instance.TrySetAct("emp_act_trade", () => {
                    PlayerTrade.Invite(other.uid);
                    return false;
                }, other, null, 2);
            }

            // next to it, the challenge to a duel (PlayerDuel), or giving up the one being fought
            if (PlayerDuel.Enabled) {
                if (PlayerDuel.InDuel) {
                    __instance.TrySetAct("emp_act_duel_giveup", () => {
                        PlayerDuel.GiveUp();
                        return false;
                    }, other, null, 2);
                } else {
                    __instance.TrySetAct("emp_act_duel", () => {
                        PlayerDuel.Challenge(other.uid);
                        return false;
                    }, other, null, 2);
                }
            }

            // and walking after it (PlayerFollow), not on the world map
            if (PlayerFollow.Enabled && !EClass._zone.IsRegion) {
                __instance.TrySetAct("emp_act_follow", () => {
                    PlayerFollow.Start(other);
                    return false;
                }, other, null, 2);
            }
        }
    }
}
