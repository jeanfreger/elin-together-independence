using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A trap a player walks on was rolled twice, in its own game and in the one that simulates the map: two
///     chances to be hit, and both hits landed. It is rolled in the game of the one who walks on it, nowhere
///     else; what that game cannot do to its own player (sleep, blindness, paralysis) it asks for, see
///     <see cref="CharaAddConditionEvent" /> <br />
///     council decision of 2026-10-04, see MODLOG
/// </summary>
[HarmonyPatch(typeof(TraitFloorSwitch), nameof(TraitFloorSwitch.OnStepped))]
internal static class RemoteTrapPatch
{
    /// <summary>
    ///     This game's player, connected to another game, is walking on a trap right now
    /// </summary>
    internal static bool IsOwnStep { get; private set; }

    [HarmonyPrefix]
    internal static bool OnStepped(TraitFloorSwitch __instance, Chara c, out ScopeExit? __state)
    {
        __state = null;
        // traps only, not the other things one steps on (seesaw, trolley...); the one that wakes the monsters of
        // the map does it where they are simulated
        if (NetSession.Instance.Connection is null || __instance is not TraitTrap ||
            __instance.owner.sourceCard.vals is ["sister", ..]) {
            return true;
        }

        if (c.IsRemotePlayer) {
            return false;
        }

        // another player's companion, simulated here: the disarm, its scavenger feat (1656) and the scrap are
        // that player's, as the game asks "the player" for them (H5-06)
        if (NetSession.Instance.Connection is ElinNetHost && CompanionHelper.OwnerOf(c) is { IsRemotePlayer: true } owner &&
            owner != c) {
            __state = RemoteCraft.AsCrafter(owner, withFame: false);
        }

        IsOwnStep = c.IsPC && NetSession.Instance.Connection is ElinNetClient;
        return true;
    }

    [HarmonyFinalizer]
    internal static void OnSteppedEnd(ScopeExit? __state)
    {
        IsOwnStep = false;
        __state?.Dispose();
    }
}
