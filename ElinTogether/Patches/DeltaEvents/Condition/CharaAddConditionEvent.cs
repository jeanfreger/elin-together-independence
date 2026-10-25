using System;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Chara), nameof(Chara.AddCondition), typeof(Condition), typeof(bool))]
internal static class CharaAddConditionEvent
{
    [HarmonyPostfix]
    internal static void OnCharaAddCondition(Chara __instance, Condition? __result, Condition c, bool force)
    {
        // only propagate successful add condition events
        if (__result is null) {
            return;
        }

        if (NetSession.Instance.Connection is not ElinNetHost host) {
            return;
        }

        host.Delta.AddRemote(new CharaAddConditionDelta {
            Owner = __instance,
            ConditionId = __result.id,
            Power = __result.power,
            Force = force,
            RefVal = __result.refVal,
            RefVal2 = __result.refVal2,
        });
    }

    [HarmonyPrefix]
    internal static bool OnClientAddCondition(Chara __instance, Condition c, bool force)
    {
        // clients cannot add conditions normally
        if (NetSession.Instance.IsHost) {
            // under a fifth of its hit points a resident that is hit may take fright, never "the player": here
            // another player read as a resident and could no longer strike (Card.DamageHP)
            return !(c is ConFear && CardDamageHpEvent.Depth > 0 && __instance.IsRemotePlayer);
        }

        // the trap this game's player walked on is rolled here only (RemoteTrapPatch): what it does to it is
        // asked of the game that simulates the map
        if (RemoteTrapPatch.IsOwnStep && __instance.IsPC && CharaAddConditionDelta.IsTrapCondition(c.source.alias) &&
            NetSession.Instance.Connection is ElinNetClient client) {
            client.Delta.AddRemote(new CharaAddConditionDelta {
                Owner = __instance,
                ConditionId = c.id,
                Power = c.power,
                Force = force,
            });
        }

        // what heavy rain does to this game's player, rolled here with its own feat (HeavyRainPatch)
        if (HeavyRainPatch.InOwnStorm && __instance.IsPC && c is ConConfuse or ConBlind && !ElinDelta.IsApplying &&
            NetSession.Instance.Connection is ElinNetClient wet) {
            wet.Delta.AddRemote(new CharaAddConditionDelta {
                Owner = __instance,
                ConditionId = c.id,
                Power = c.power,
                Force = force,
            });
        }

        // the priestess' blessing, given when this game's dialog closes to the player and its companions (not to
        // the other players of the party): the host gives it, and sets it as a perfume as the dialog does. Nothing
        // else adds these three on this side at that moment
        if (DramaBlessingPatch.IsClosingDrama && !ElinDelta.IsApplying &&
            CharaAddConditionDelta.IsBlessingCondition(c.source.alias) &&
            (__instance.IsPC || __instance.IsCompanionOf(EClass.pc)) &&
            NetSession.Instance.Connection is ElinNetClient asker) {
            asker.Delta.AddRemote(new CharaAddConditionDelta {
                Owner = __instance,
                ConditionId = c.id,
                Power = c.power,
                Force = force,
            });
        }

        // deep water takes the breath of "the player" only, and the host does not see another player as one: this
        // game says when its own player is under, the host gives the condition (and its phases come back from there)
        if (__instance.IsPC && c is ConSuffocation && !ElinDelta.IsApplying &&
            NetSession.Instance.Connection is ElinNetClient diver) {
            diver.Delta.AddRemote(new CharaAddConditionDelta {
                Owner = __instance,
                ConditionId = c.id,
                Power = c.power,
                Force = force,
            });
        }

        return false;
    }

    extension(Chara chara)
    {
        [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
        internal Condition Stub_AddCondition(Condition condition, bool force)
        {
            throw new NotImplementedException("Chara.AddCondition");
        }
    }
}