using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A mutation is drawn at random. Drunk or cast by a guest, its own game draws it and sends the elements it got;
///     the host replayed the same act and drew its own on its copy of that player, which kept mutations the player
///     never had (hunt 2 row 32). The host leaves another player's mutations to that player's game
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.MutateRandom))]
internal static class RemoteMutationPatch
{
    [HarmonyPrefix]
    internal static bool OnMutateRandom(Chara __instance, ref bool __result)
    {
        if (NetSession.Instance.Connection is not ElinNetHost || __instance.IsPC || !__instance.IsRemotePlayer) {
            return true;
        }

        // (done, in the player's game: no "nothing happens" here)
        __result = true;
        return false;
    }
}
