using System;
using ElinTogether.Net;
using HarmonyLib;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A cuddle or a kiss of the player makes an egg one time in a hundred (Chara.Cuddle, Chara.Kiss). A guest's game
///     drew it and made the egg in its own copy of the map, which is never kept (CardGenEvent): the egg vanished (hunt
///     2 row 13). The host lays it, for the guest's draw
/// </summary>
[MessagePackObject]
public class CharaEggRequest : ElinDelta
{
    [Key(0)]
    public required RemoteCard Target { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
            Target.Find() is not Chara { isDestroyed: false } target || target.currentZone != EClass._zone ||
            sender.Dist(target) > 3) {
            return;
        }

        using var simulate = Simulate();
        target.MakeEgg();
    }

    [HarmonyPatch]
    internal static class Ask
    {
        [ThreadStatic]
        private static bool _petting;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Chara), nameof(Chara.Cuddle))]
        [HarmonyPatch(typeof(Chara), nameof(Chara.Kiss))]
        internal static void OnPet(Chara __instance)
        {
            _petting = __instance.IsPC;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Chara), nameof(Chara.Cuddle))]
        [HarmonyPatch(typeof(Chara), nameof(Chara.Kiss))]
        internal static void OnPetEnd()
        {
            _petting = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Card), nameof(Card.MakeEgg))]
        internal static bool OnMakeEgg(Card __instance, ref Thing? __result)
        {
            if (!_petting || IsApplying || __instance is not Chara chara ||
                NetSession.Instance.Connection is not ElinNetClient client) {
                return true;
            }

            client.Delta.AddRemote(new CharaEggRequest { Target = chara });
            __result = null;
            return false;
        }
    }
}
