using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Zone), nameof(Zone.ClaimZone))]
internal static class ClaimZonePatch
{
    /// <summary>
    ///     The yes of a guest on the host's map: asked to the host, see <see cref="ClaimZoneDelta" />
    /// </summary>
    [HarmonyPrefix]
    internal static bool OnClaim(Zone __instance)
    {
        if (!RemoteBasePaidPatch.IsRequester) {
            return true;
        }

        NetSession.Instance.Connection?.Delta.AddRemote(new ClaimZoneDelta { ZoneUid = __instance.uid });
        return false;
    }

    /// <summary>
    ///     Claimed in the game that keeps the world: the players on this map get it again, with its new owner and
    ///     its branch (a base is too much to tell piece by piece, and it happens a few times in a game)
    /// </summary>
    [HarmonyPostfix]
    internal static void OnClaimed(Zone __instance, bool __runOriginal)
    {
        // the land is the team's of the player who read the deed
        if (__runOriginal) {
            Teams.OnClaimed(__instance);
        }

        if (__runOriginal && NetSession.Instance.Connection is ElinNetHost { IsZoneSession: false } host) {
            CoroutineHelper.Deferred(() => host.PropagateZoneChangeState(__instance), 2);
        }
    }
}
