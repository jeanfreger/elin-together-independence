using System.Collections.Generic;
using System.Reflection;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal static class CardOnUseEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return OverrideMethodComparer.FindAllOverrides(typeof(Trait), nameof(Trait.OnUse), typeof(Chara));
    }

    [HarmonyPrefix]
    internal static bool OnUseCard(Trait __instance, Chara c)
    {
        // the moongate, the landing portal, the sky helm: replayed on the host they act for the host, see GateItems
        if (GateItems.OnUse(__instance, c) is { } runHere) {
            return runHere;
        }

        return !TryRequest(__instance, c, null, null);
    }

    /// <summary>
    ///     A client's use of a card is a request, the host runs it <br />
    ///     True when the request took the place of the call
    /// </summary>
    internal static bool TryRequest(Trait trait, Chara user, Point? pos, Card? target)
    {
        if (NetSession.Instance.Connection is not ElinNetClient client || ElinDelta.IsApplying) {
            return false;
        }

        var card = trait.owner;
        if (!CardCache.Contains(card)) {
            return true;
        }

        // moves the player's own equipment, which only its game may change: it does it there, as the host does,
        // and each move goes out as its own
        if (pos is null && target is null && trait is TraitMannequin) {
            return false;
        }

        // bait is equipped at once in the asker's own game, as for the host (the fishing that follows checks it in
        // the same step): the request says which state is wanted, the game's toggle runs here
        bool? equip = pos is null && target is null && trait is TraitEquipItem item ? item.EQ != card : null;

        client.Delta.AddRemote(new CardOnUseDelta {
            Card = card,
            RootCard = card.GetRootCard(),
            User = user,
            Pos = pos,
            Target = target,
            Equip = equip,
            Peace = trait is TraitWhistlePeace ? !EClass._zone.isPeace : null,
        });

        return equip is null;
    }
}

/// <summary>
///     The two other ways of using a card, on a tile and on another card, only ran in the client's own game: the
///     bottle was not used up and the water drawn was not kept
/// </summary>
[HarmonyPatch]
internal static class CardOnUseAtEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return OverrideMethodComparer.FindAllOverrides(typeof(Trait), nameof(Trait.OnUse), typeof(Chara), typeof(Point));
    }

    [HarmonyPrefix]
    internal static bool OnUseCardAt(Trait __instance, Chara __0, Point __1)
    {
        return !CardOnUseEvent.TryRequest(__instance, __0, __1, null);
    }
}

[HarmonyPatch]
internal static class CardOnUseOnEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        return OverrideMethodComparer.FindAllOverrides(typeof(Trait), nameof(Trait.OnUse), typeof(Chara), typeof(Card));
    }

    [HarmonyPrefix]
    internal static bool OnUseCardOn(Trait __instance, Chara __0, Card __1)
    {
        return !CardOnUseEvent.TryRequest(__instance, __0, null, __1);
    }
}
