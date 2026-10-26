using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Stepping on a hitching post at the base mounts a free animal of the tile, or gets off and leaves the mount
///     there (hunt 5, H5-12). The trait calls ActRide.Ride / Unride directly, not through the ride act, so the
///     CharaActPerformDelta that carries every ride never left the game of the player who stepped. It is sent here
///     as the ride act the game would have made: a ride aimed at the animal's tile, a ride aimed at oneself to get off
///     (replayed as such, see RemoteRideHostPatch.OnRidePerform)
/// </summary>
[HarmonyPatch(typeof(TraitHitchingPost), nameof(TraitHitchingPost.OnActivateTrap))]
internal static class HitchingPostEvent
{
    [HarmonyPrefix]
    internal static void OnActivate(Chara c, out Chara? __state)
    {
        __state = c.IsPC ? c.ride : null;
    }

    [HarmonyPostfix]
    internal static void OnActivated(TraitHitchingPost __instance, Chara c, Chara? __state)
    {
        if (!c.IsPC || ElinDelta.IsApplying || NetSession.Instance.Connection is not { } connection || c.ride == __state) {
            return;
        }

        // got on: the animal is the one now carrying him; got off: aimed at himself
        var mount = c.ride;
        // where he stands first: the replay checks the ride's reach from there (a step onto the post that the other
        // games had not seen yet was out of reach, and nothing happened there)
        connection.Delta.AddRemote((CharaMoveDelta)c);
        connection.Delta.AddRemote(new CharaActPerformDelta {
            ActId = ABILITY.ActRide,
            Owner = c,
            TargetCard = mount ?? c,
            Pos = mount is null ? c.pos : __instance.owner.pos,
        });
    }
}
