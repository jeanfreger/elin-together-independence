using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     For spatial gen, we don't necessarily need to send zone instances in packets
///     It is not a random instantiation unlike CardGen
/// </summary>
[HarmonyPatch]
internal static class SpatialGenEvent
{
    internal static readonly Dictionary<int, Zone> HeldRefZones = [];

    /// <summary>
    ///     Set while the host creates a zone nobody else is to hear about (the zone of a quest a player took)
    /// </summary>
    internal static bool Quiet { get; set; }

    internal static Zone? TryPop(int uid)
    {
        if (!HeldRefZones.Remove(uid, out var card)) {
            return null;
        }

        foreach (var staleUid in HeldRefZones.Keys.ToArray()) {
            if (staleUid < uid) {
                HeldRefZones.Remove(staleUid, out _);
            }
        }

        return card;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpatialGen), nameof(SpatialGen.Create))]
    internal static void OnSpatialGen(Spatial __result)
    {
        // zones a client creates on its own (not replicated from the host) are unknown to the host,
        // travelling there sends it a blueprint, see ZoneLeaseRequest
        if (NetSession.Instance.Transport is ElinNetClient client) {
            if (!ElinDelta.IsApplying && __result is Zone zone) {
                client.OnLocalZoneCreated(zone);
            }

            return;
        }

        if (NetSession.Instance.Connection is not ElinNetHost host || Quiet) {
            return;
        }

        // host propagates all zone creation for clients to hold references
        // must defer this because dungeon levels are assigned after creation
        CoroutineHelper.Deferred(() => host.Delta.AddRemote(SpatialGenDelta.Create(__result as Zone)));
    }

    /// <summary>
    ///     A new random site (nefia) whose level the game draws from "the player" (not a treasure map's level): drawn
    ///     for a player in turn, see PlayerInTurn (H5-04)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Region), nameof(Region.CreateRandomSite), typeof(Point), typeof(string), typeof(bool), typeof(int))]
    internal static void OnRandomSite(int lv, out ScopeExit? __state)
    {
        __state = lv <= 0 ? PlayerInTurn.World() : null;
    }

    // a finalizer: the host is "the player" again even if the game's own method throws
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Region), nameof(Region.CreateRandomSite), typeof(Point), typeof(string), typeof(bool), typeof(int))]
    internal static void OnRandomSiteDone(ScopeExit? __state)
    {
        __state?.Dispose();
    }

    [ElinPostLoad]
    private static void ClearRef(GameIOContext context)
    {
        HeldRefZones.Clear();
    }
}