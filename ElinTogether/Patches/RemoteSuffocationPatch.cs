using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The breath lost under deep water (ConSuffocation) comes back as a player's does: none while under, 25 a turn
///     once out. The host's copy of a guest is not "the player" and got the rule of the others, 5 a turn even under
///     water, so its damage (settled there) came later and its breath back slower (audit of 2026-10-25, after
///     cf62040). The experience of swimming stays with the guest's own game
/// </summary>
[HarmonyPatch(typeof(ConSuffocation), nameof(ConSuffocation.Tick))]
internal static class RemoteSuffocationPatch
{
    [HarmonyPrefix]
    internal static bool OnTick(ConSuffocation __instance)
    {
        if (__instance.owner is not { } owner || !owner.IsRemotePlayer || !NetSession.Instance.IsHost) {
            return true;
        }

        // the game's own lines, with the branch of the player
        if (owner.HasElement(429)) {
            __instance.Kill();
            return false;
        }

        if (__instance.value >= 200) {
            __instance.value = 200;
        }

        if (__instance.value >= 100 && !EClass._zone.IsRegion) {
            owner.DamageHP((__instance.value - 100) / 10 + owner.MaxHP / 20, AttackSource.Condition);
        }

        if (owner.IsAliveInCurrentZone && !owner.Cell.CanSuffocate()) {
            __instance.Mod(-25);
        }

        return false;
    }
}
