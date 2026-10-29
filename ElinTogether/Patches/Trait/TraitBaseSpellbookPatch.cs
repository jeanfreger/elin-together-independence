using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The end of a reading runs for another player as for a resident: the ancient book it deciphers is used up
///     in one go, and a god's book converts it, which its own game never hears of. A player keeps the book and
///     its god, as the local one does
/// </summary>
[HarmonyPatch]
internal static class TraitBaseSpellbookPatch
{
    private static Chara? _reader;
    private static int _featBefore;

    // the reading of this game's player that is being rolled, and the one that failed (until it is stopped)
    private static AIProgress? _rolling;
    private static AIProgress? _failed;

    /// <summary>
    ///     This reading of this game's player failed its roll
    /// </summary>
    internal static bool HasFailed(AIProgress reading)
    {
        return _failed == reading;
    }

    // every step of a reading was rolled twice, in the reader's game and in the one that simulates the map:
    // twice the failures. It is rolled in the reader's game, nowhere else; a failure still uses up the book,
    // see CharaTaskCancelDelta. Council decision of 2026-10-04, see MODLOG
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBaseSpellbook), nameof(TraitBaseSpellbook.TryProgress))]
    internal static bool OnTryProgress(AIProgress p, ref bool __result)
    {
        if (NetSession.Instance.Connection is not { } connection || p.owner is not { } reader) {
            return true;
        }

        if (reader.IsRemotePlayer) {
            __result = true;
            return false;
        }

        if (connection is not ElinNetClient || !reader.IsPC) {
            return true;
        }

        // failed already: the host stops it, no second failure meanwhile
        if (_failed == p) {
            __result = false;
            return false;
        }

        _rolling = p;
        return true;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(TraitBaseSpellbook), nameof(TraitBaseSpellbook.TryProgress))]
    internal static void OnTryProgressEnd()
    {
        _rolling = null;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBaseSpellbook), nameof(TraitBaseSpellbook.ReadFailEffect))]
    internal static void OnReadFail()
    {
        if (_rolling is not null) {
            _failed = _rolling;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBaseSpellbook), nameof(TraitBaseSpellbook.OnRead))]
    internal static void OnRead(Chara c, out Chara? __state)
    {
        __state = _reader;
        _reader = c is { IsPC: false, IsRemotePlayer: true } ? c : null;
        _featBefore = c.feat;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(TraitBaseSpellbook), nameof(TraitBaseSpellbook.OnRead))]
    internal static void OnReadEnd(TraitBaseSpellbook __instance, Chara? __state)
    {
        // the black note gives a feat point to a player as it does to the local one, not the four of a resident
        if (_reader is not null && __instance.BookType == TraitBaseSpellbook.Type.BlackNote &&
            _reader.feat - _featBefore == 4) {
            _reader.feat -= 3;
        }

        _reader = __state;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitBaseSpellbook), nameof(TraitBaseSpellbook.ModCharge))]
    internal static bool OnModCharge(TraitBaseSpellbook __instance, Chara c, ref int a)
    {
        if (_reader is null || c != _reader) {
            return true;
        }

        switch (__instance.BookType) {
            // the game takes no charge from the local player for a book it deciphered
            case TraitBaseSpellbook.Type.Ancient:
                return false;
            // one charge, not the whole book
            case TraitBaseSpellbook.Type.Dojin:
                a = -1;
                return true;
            default:
                return true;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Religion), nameof(Religion.JoinFaith))]
    internal static bool OnJoinFaith(Chara c)
    {
        return _reader is null || c != _reader;
    }
}
