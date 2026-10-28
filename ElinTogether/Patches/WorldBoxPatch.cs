using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The bank, the shipping box and the delivery box are the host's, one for everyone. A player alone away
///     holds empty copies: their window shows what the host holds while it is open, see ShippingHelper.MirrorKey
/// </summary>
[HarmonyPatch]
internal static class WorldBoxPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LayerInventory), nameof(LayerInventory.CreateContainer), typeof(Card), typeof(Card))]
    internal static void OnOpen(Card container)
    {
        var box = ShippingHelper.WorldBoxIndex(container);
        if (box < 0 || NetSession.Instance is not { IsAway: true, Transport: ElinNetClient client }) {
            return;
        }

        if (!client.AskWorldBox(box)) {
            // with other players in a zone away from the host: what goes in arrives, nothing shows
            EmpPop.Pop("emp_ui_box_far".lang());
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerInventory), nameof(LayerInventory.OnKill))]
    internal static void OnClose(LayerInventory __instance)
    {
        if (NetSession.Instance.Transport is not ElinNetClient { MirrorsWorldBoxes: true } ||
            __instance.invs.Count == 0 || __instance.invs[0].tabs.Count == 0) {
            return;
        }

        var box = ShippingHelper.WorldBoxIndex(__instance.Inv.Container);
        if (box < 0) {
            return;
        }

        // the pictures go with the window (its box only: the bank and the shipping box may be open together)
        ElinNetClient.EmptyWorldContainer(ShippingHelper.WorldBox(box));
    }

    // the bank a gesture is about: the world's, or the host's own owner's (its window opens on it, see TeamBoxes)
    private static int BankGold(Card? bank = null)
    {
        return (bank ?? TeamBoxes.BankOf(EClass.pc))?.GetCurrency() ?? 0;
    }

    /// <summary>
    ///     The host's own gesture on the bank: the line is told for what the bank gained or lost (a gesture that
    ///     changes nothing says nothing). A player's gesture replayed here is told where it lands, see
    ///     CardAddThingDelta and <see cref="OnPlayerTake" />
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InvOwner.Transaction), nameof(InvOwner.Transaction.Process))]
    internal static void OnProcess(out int __state)
    {
        __state = NetSession.Instance.Connection is ElinNetHost { IsZoneSession: false } && !ElinDelta.IsApplying
            ? BankGold()
            : -1;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InvOwner.Transaction), nameof(InvOwner.Transaction.Process))]
    internal static void OnProcessed(int __state)
    {
        if (__state >= 0 && BankGold() - __state is var change and not 0 &&
            NetSession.Instance.Connection is ElinNetHost host) {
            BillPayDelta.TellBank(host, EClass.pc.NameSimple, Math.Abs(change), change > 0);
        }
    }

    /// <summary>
    ///     A player on this map asks for gold of the bank (every gesture starts with this request): told once the
    ///     gold is out of the bank (a refused request says nothing), the deposit is told when the gold lands in the
    ///     bank. ponytail: gold moved inside the bank window reads as a withdrawal then a deposit, and one the player
    ///     cannot take (full bag) is put back after 10 s without a line
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ThingRequest), "OnApply")]
    internal static void OnPlayerTake(ThingRequest __instance, ElinNetBase net, out (Card? Bank, int Gold) __state)
    {
        __state = net is ElinNetHost { IsZoneSession: false } && __instance.Num > 0 &&
                  __instance.Thing?.Find() is Thing { id: "money", parent: Card box } &&
                  ShippingHelper.OtherWorldBox(box) == ShippingHelper.BoxBank
            ? (box, BankGold(box))
            : (null, -1);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ThingRequest), "OnApply")]
    internal static void OnPlayerTaken(ThingRequest __instance, ElinNetBase net, (Card? Bank, int Gold) __state)
    {
        if (__state.Gold >= 0 && __state.Gold - BankGold(__state.Bank) is var taken and > 0 && net is ElinNetHost host &&
            host.ActiveRemoteCharas.GetValueOrDefault(__instance.OriginPeer) is { } who) {
            BillPayDelta.TellBank(host, who.NameSimple, taken, false);
        }
    }

    /// <summary>
    ///     A picture let go on the ground: the real one is asked for, it comes into the bag
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Zone), nameof(Zone.AddCard), typeof(Card), typeof(int), typeof(int))]
    internal static bool OnDrop(Card t, ref Card __result)
    {
        var hostUid = t.isThing ? t.GetInt(ShippingHelper.MirrorKey) : 0;
        if (hostUid == 0) {
            return true;
        }

        (NetSession.Instance.Transport as ElinNetClient)?.AskWorldBox(t.GetInt(ShippingHelper.MirrorBoxKey) - 1, hostUid, t.Num);
        t.Destroy();
        __result = t;
        return false;
    }
}
