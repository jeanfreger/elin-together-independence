using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The game counts the gifts of a god once per world: with several players, the first to pray took the pet
///     and the artifact of that god from everyone else. Each player who is not the host of the save has its own
///     count, kept on its character; the world's count stays the host's <br />
///     council decision of 2026-10-04, see MODLOG
/// </summary>
[HarmonyPatch(typeof(Religion), nameof(Religion.TryGetGift))]
internal static class RemoteGodGiftPatch
{
    // an int key, not a string one: Card.SetInt(string) of "the player" also writes the shared dialog flags
    internal static int Key(Religion religion)
    {
        return ("emp_gift_" + religion.id).GetHashCode();
    }

    [HarmonyPrefix]
    internal static bool OnTryGetGift(Religion __instance, Chara? chara, ref bool __result, out int? __state)
    {
        __state = null;
        chara ??= EClass.pc;

        var session = NetSession.Instance;
        if (session.Connection is ElinNetClient) {
            // given by the game that simulates the map, when it plays the prayer again (ActPrayEvent)
            __result = false;
            return false;
        }

        if (chara.IsRemotePlayer || (chara.IsPC && session.Transport is ElinNetClient)) {
            __state = __instance.giftRank;
            __instance.giftRank = chara.GetInt(Key(__instance));
        }

        return true;
    }

    [HarmonyFinalizer]
    internal static void OnTryGetGiftEnd(Religion __instance, Chara? chara, int? __state)
    {
        if (__state is not { } world) {
            return;
        }

        (chara ?? EClass.pc).SetInt(Key(__instance), __instance.giftRank);
        __instance.giftRank = world;
    }
}
