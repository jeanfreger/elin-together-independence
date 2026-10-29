using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     GitHub issue 32 ("riding speed 0 again", suspected cause, not reproduced): Chara.RefreshSpeed of a rider asks its
///     mount to refresh and copies the mount's speed, but a character whose currentZone is not the active zone only
///     marks itself dirty and returns, leaving a speed of 0 (never computed in this game) for the rider. A mount that
///     arrived with its world (a reconnect, a soft return, a map change of the guest) can name a zone object other
///     than the one on screen while it stands in the rider's cell, RideOffMapPatch only repairs the mount whose tile
///     is off the map. A mount or parasite of a rider that is in the active zone is of that zone too: say so before
///     the game decides to skip it
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.RefreshSpeed))]
internal static class MountZonePatch
{
    [HarmonyPrefix]
    internal static void OnRefreshSpeed(Chara __instance)
    {
        if (EClass._zone is not { } zone || __instance.currentZone == zone || __instance.host is not { } rider ||
            rider.currentZone != zone || rider.ride != __instance && rider.parasite != __instance) {
            return;
        }

        EmpLog.Debug("Mount {Uid} of chara {Rider} was of another zone than its rider's, speed refreshed in {ZoneFullName}",
            __instance.uid, rider.uid, zone.ZoneFullName);
        __instance.currentZone = zone;
    }
}
