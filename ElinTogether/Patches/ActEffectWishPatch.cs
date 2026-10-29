using System.Collections.Generic;
using System.Linq;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A wish is answered in the game of the one who makes it: the host has no dialog for another player. What a
///     client's game creates is gone at the end of the frame, the wish with it: what is wished for goes to the
///     host as a story gift does, see ZoneAddCardEvent <br />
///     A god's artifact can be wished for once that god's gifts went far enough (Religion.giftRank, the world's,
///     so the host's): a client's wish reads its own count instead, the one RemoteGodGiftPatch keeps
/// </summary>
[HarmonyPatch(typeof(ActEffect), nameof(ActEffect.Wish))]
internal static class ActEffectWishPatch
{
    internal static bool IsWishing { get; private set; }

    [HarmonyPrefix]
    internal static void OnWish(out List<(Religion Religion, int GiftRank)>? __state)
    {
        IsWishing = NetSession.Instance.Connection is ElinNetClient;
        __state = null;
        if (NetSession.Instance.Transport is not ElinNetClient) {
            return;
        }

        __state = EClass.game.religions.list.Select(r => (r, r.giftRank)).ToList();
        foreach (var religion in EClass.game.religions.list) {
            religion.giftRank = EClass.pc.GetInt(RemoteGodGiftPatch.Key(religion));
        }
    }

    [HarmonyFinalizer]
    internal static void OnWishEnd(List<(Religion Religion, int GiftRank)>? __state)
    {
        IsWishing = false;
        foreach (var (religion, giftRank) in __state ?? []) {
            religion.giftRank = giftRank;
        }
    }
}
