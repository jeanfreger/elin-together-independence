using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Conditions the game writes for "the player" and its party, run in a game for a character that is another
///     player, or that player's companion (audit after the hallucination report, 2026-10-15) <br />
///     Strife (ConStrife): a party member's strife is a copy of the player's; in the host's game the guest's own
///     strife was replaced by the host's, and taken away when the host had none, and the host's strife was handed
///     to the guest and its companions. Each player's strife is its own, its companions copy that player's <br />
///     (Fear, ConFear, looked at the same way: another player's frightened character does not move in this game,
///     nothing to do)
/// </summary>
[HarmonyPatch]
internal static class OwnPlayerConditionPatch
{
    private static bool IsMultiplayer => NetSession.Instance.Transport is not null;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConStrife), nameof(ConStrife.SyncPC))]
    internal static bool OnSyncStrife(ConStrife __instance)
    {
        if (!IsMultiplayer || __instance.owner is not { } owner) {
            return true;
        }

        // another player: its own game keeps its strife. The host's copy (its kills are counted there too, see
        // StrifeKillDelta) wears off as that game's does: its ticks are replayed here one for one
        if (owner.IsRemotePlayer) {
            if (NetSession.Instance.IsHost && --__instance.turn < 0) {
                if (--__instance.lv >= 1) {
                    __instance.SetTurn();
                    __instance.exp = __instance.ExpToNext / 2;
                } else {
                    __instance.Kill();
                }
            }

            return false;
        }

        // a companion copies its own player
        if (CompanionHelper.OwnerOf(owner) is not { } lead || lead == EClass.pc) {
            return true;
        }

        var source = lead.GetCondition<ConStrife>();
        if (source is null || source.lv < 2) {
            __instance.turn = 0;
            __instance.Kill();
            return false;
        }

        __instance.turn = source.turn;
        __instance.lv = source.lv;
        __instance.exp = source.exp;
        var mine = Traverse.Create(__instance).Field<ElementContainer>("ec").Value;
        var theirs = Traverse.Create(source).Field<ElementContainer>("ec").Value;
        foreach (var id in new[] { 964, 662 }) {
            mine.SetBase(id, theirs.GetOrCreateElement(id).vBase);
        }

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConStrife), nameof(ConStrife.TryApplyParty))]
    internal static bool OnApplyStrife(ConStrife __instance)
    {
        if (!IsMultiplayer || __instance.owner is not { IsPC: true } || __instance.lv < 2) {
            return true;
        }

        // to the local player's own companions, not to the other players and theirs
        foreach (var member in CompanionHelper.CompanionsOf(EClass.pc).Where(m => !m.HasCondition<ConStrife>())) {
            (member.AddCondition<ConStrife>() as ConStrife)?.SyncPC();
        }

        return false;
    }
}
