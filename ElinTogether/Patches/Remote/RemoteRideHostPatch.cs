using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal class RemoteRideHostPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BaseGameScreen), nameof(BaseGameScreen.RefreshPosition))]
    internal static void OnSetHostRideFocus(BaseGameScreen __instance)
    {
        // (a carried player does not tick: its rider gone from this map is seen here, each frame it is carried)
        if (EClass.pc.host is not null) {
            FreeIfDropped(EClass.pc);
        }

        if (EClass.pc.host is { } host) {
            EClass.player.position = host.renderer.position;
        }
    }

    /// <summary>
    ///     The game draws a mount with the ride look its renderer got when made while it was already carried
    ///     (Chara._CreateRenderer). A mount whose renderer was made before it was tied to its rider (a copy of it
    ///     brought from another game) has none: drawing its rider threw every frame, the camera stuck and parts of the
    ///     map were gone (issue #4). It is made again here, as mounting does
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardRenderer), nameof(CardRenderer.Draw), [typeof(RenderParam), typeof(UnityEngine.Vector3), typeof(bool)],
        [ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal])]
    internal static void OnDrawRider(CardRenderer __instance)
    {
        // (only one that says it is carried gets that look: another would be made again every frame)
        if (__instance.owner is Chara { ride: { host: not null, renderer: CharaRenderer { pccData: null } or CharaRenderer { pccData.ride: null } } mount }) {
            mount._CreateRenderer();
        }
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(BaseTileMap), nameof(BaseTileMap.DrawTile))]
    internal static IEnumerable<CodeInstruction> OnDrawTileIl(IEnumerable<CodeInstruction> instructions)
    {
        // if (!chara.IsPC && !chara.renderer.IsMoving && this.detail.charas.Count > 1 && (this.detail.charas.Count != 2 || !this.detail.charas[0].IsDeadOrSleeping || !this.detail.charas[0].IsPCC))
        // {
        //     this._actorPos += this.renderSetting.charaPos[1 + ((num29 < 4) ? num29 : 3)];
        // }
        return new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Card), nameof(Card.IsPC))))
            .SetInstruction(
                Transpilers.EmitDelegate((Card card) => card.IsPC || EClass.pc.host == card))
            .InstructionEnumeration();
    }

    /// <summary>
    ///     A ride between two players is a same-map tie (council 2026-10-24 ride): when the rider changes map, the
    ///     game that drops the tie clears the rider's side only, the carried one kept saying it was carried. It then
    ///     could not ride, be ridden or get off any more. Whoever says it is carried by one that does not carry it
    ///     is free, and a player learns he got off. Each tick (Chara.SyncRide) and before a ride act lists its targets
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.SyncRide), [])]
    internal static void OnSyncRide(Chara __instance)
    {
        FreeIfDropped(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ActRide), nameof(ActRide.Perform))]
    internal static bool OnRidePerform(ActRide __instance)
    {
        if (Act.TP is { IsValid: true } at) {
            foreach (var chara in at.ListCharas()) {
                FreeIfDropped(chara);
            }
        }

        // aimed at oneself while carrying: getting off, in every game. The game takes the first free one of the tile,
        // in an order each game has its own: with another free animal there one game got off and the other took
        // that animal (2026-10-25, ride2 dismount_crowded). (The act is still sent: its postfix runs)
        if (Act.CC is { } rider && (Act.TC == rider || (Act.TC is null && Act.TP?.Equals(rider.pos) == true)) &&
            (__instance.IsParasite ? rider.parasite : rider.ride) is not null) {
            ActRide.Unride(rider, __instance.IsParasite);
            return false;
        }

        return true;
    }

    internal static void FreeIfDropped(Chara chara)
    {
        if (chara.host is not { } host) {
            return;
        }

        // the world's own rider, not a copy of it left from another map (seen 2026-10-24: a copy still riding the
        // host's character while the guest's real character rode nothing); by uid, as CompanionHelper.RebindRide
        var rider = EClass.game.cards.globalCharas.Find(host.uid) ?? host;
        // a player carried by anyone gone from this map (who the rider is, a guest holding the map no longer knows:
        // the host's character is not one of its own session's players)
        var gone = PlayerPeoplePatch.IsPlayerChara(chara) && EClass._map?.charas is { } here && here.Contains(chara) &&
                   !here.Contains(rider);
        if (gone || (rider.ride?.uid != chara.uid && rider.parasite?.uid != chara.uid)) {
            Untie(rider, chara);
        }
    }

    /// <summary>
    ///     Two players: a same-map tie. One of them leaves this map (the rider changes map, a guest leaves the
    ///     host's map, the host hands it over): it ends here, the one who stays is free where he stands. Kept, it
    ///     pulled the one who stayed onto the frozen tile of the one gone at every step, or to (0, 0) on a smaller
    ///     map (Chara.SyncRide, ride2 nefia 2026-10-24)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Zone), nameof(Zone.RemoveCard))]
    internal static void OnRemoveCard(Zone __instance, Card t)
    {
        if (!__instance.IsActiveZone || t is not Chara leaving || !PlayerPeoplePatch.IsPlayerChara(leaving)) {
            return;
        }

        if (leaving.host is { } rider && PlayerPeoplePatch.IsPlayerChara(rider)) {
            Untie(rider, leaving);
        }

        foreach (var carried in new[] { leaving.ride, leaving.parasite }) {
            if (carried is not null && PlayerPeoplePatch.IsPlayerChara(carried)) {
                Untie(leaving, carried);
            }
        }
    }

    private static void Untie(Chara rider, Chara carried)
    {
        EmpLog.Debug("Chara {Uid} no longer carried by {Rider}: freed", carried.uid, rider.uid);
        if (rider.ride?.uid == carried.uid) {
            rider.ride = null;
        }

        if (rider.parasite?.uid == carried.uid) {
            rider.parasite = null;
        }

        // (and the world's own one: the rider may have carried a copy of it)
        if (EClass.game.cards.globalCharas.Find(carried.uid) is { } own && own != carried && own.host?.uid == rider.uid) {
            own.host = null;
            own._CreateRenderer();
            own.SetDirtySpeed();
            carried = own;
        }

        carried.host = null;
        carried._CreateRenderer();
        carried.SetDirtySpeed();
        rider.SetDirtySpeed();
        if (carried.IsPC || rider.IsPC) {
            rider.Say("ride_unride", rider, carried);
        }
    }

    /// <summary>
    ///     A player who goes to bed keeps his mount for the night (council 2026-10-25, ride sleep): the game got the
    ///     host's own player off (ConSleep.Tick, a direct call no other game hears of) while a guest never does
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ActRide), nameof(ActRide.Unride), [typeof(Chara), typeof(bool), typeof(bool)])]
    internal static bool OnUnrideAsleep(Chara host, bool talk)
    {
        return talk || !host.IsPC || host.conSleep is not { pcSleep: > 0 };
    }

    /// <summary>
    ///     ... and is not put on it again when he wakes: the game's ride on an animal already carrying him gets him
    ///     off and on again, and a mount noted at bedtime may have died or changed hands during the night
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConSleep), nameof(ConSleep.OnRemoved))]
    internal static void OnWakeUp(ConSleep __instance)
    {
        if (__instance.owner is { IsPC: true }) {
            __instance.uidRide = 0;
            __instance.uidParasite = 0;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ActRide), nameof(ActRide.Ride))]
    internal static bool OnRideRemoteHost(Chara host, Chara t)
    {
        // taken: a mount carries one rider. Two players asking for the same beast at the same instant (or a rider
        // put back on it when waking up) ended with a different rider in each game
        if (t.host is not null && t.host != host) {
            EmpLog.Debug("blocked ride, chara {TargetUid} already carries {OwnerUid}", t.uid, t.host.uid);

            if (host.IsPC) {
                Msg.SayInvalidAction();
            }

            return false;
        }

        if (!IsRidingBeRiddenTheRide(host, t)) {
            return true;
        }

        EmpLog.Debug("blocked cyclic ride, chara {OwnerUid} is already hosted by {TargetUid}", host.uid, t.uid);

        if (host.IsPC) {
            Msg.SayInvalidAction();
        }

        return false;
    }

    private static bool IsRidingBeRiddenTheRide(Chara host, Chara t)
    {
        var chara = host;
        for (var depth = 0; chara is not null && depth < 8; ++depth) {
            if (chara == t) {
                return true;
            }

            chara = chara.host;
        }

        return chara is not null;
    }
}