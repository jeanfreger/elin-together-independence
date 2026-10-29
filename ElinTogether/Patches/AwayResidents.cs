using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A player alone on a map of its own plays in its copy of the world. What travels with it comes back with it
///     (its character, its companions). A character of the base that does NOT travel with it stayed in that copy
///     for good: a recruit sent home from the field, an egg hatched there, a companion told to go home or dead and
///     waiting at the base. Reported by a player on 2026-10-12: "the NPCs I recruit alone in the field always
///     disappear". They are noted here as they join the base or leave the party, and handed to the host with the
///     next release of the zone (ZoneLeaseRelease.Residents)
/// </summary>
[HarmonyPatch]
internal static class AwayResidents
{
    private static readonly HashSet<int> _changed = [];

    // the other way round: a character of the base that went for good while it was alone (a livestock butchered, a
    // member banished). Nothing said it went, and the host kept its copy: "dodos respawned after getting butchered"
    // (issues 26 and 27, 2026-10-29)
    private static readonly HashSet<int> _gone = [];

    private static bool Alone => !ElinDelta.IsApplying && NetSession.Instance is { IsZoneAuthority: true, Transport: ElinNetClient };

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.AddMemeber))]
    internal static void OnJoinedBase(Chara c)
    {
        if (Alone && c is { IsPlayer: false }) {
            _changed.Add(c.uid);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Party), nameof(Party.RemoveMember))]
    internal static void OnLeftParty(Chara c)
    {
        if (Alone && c is { IsPlayer: false }) {
            _changed.Add(c.uid);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.BanishMember))]
    internal static void OnBanished(Chara c)
    {
        NoteGone(c);
    }

    // (a character of the base that is destroyed any other way; one that stays a member is not destroyed, see Card.Destroy)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Card), nameof(Card.Destroy))]
    internal static void OnDestroyed(Card __instance)
    {
        if (__instance is Chara { IsGlobal: true } chara && !(chara.IsPCFaction && !chara.isSummon)) {
            NoteGone(chara);
        }
    }

    private static void NoteGone(Chara? c)
    {
        if (Alone && c is { IsPlayer: false, isDestroyed: false }) {
            _gone.Add(c.uid);
            _changed.Remove(c.uid);
        }
    }

    /// <summary>
    ///     The numbers of the characters of the base that went while this player was alone, see <see cref="NoteGone" />
    /// </summary>
    /// <param name="handed">the zone is handed back (not a checkpoint): what is sent now is not sent again</param>
    internal static List<int>? CollectGone(bool handed)
    {
        if (_gone.Count == 0) {
            return null;
        }

        var gone = _gone.ToList();
        if (handed) {
            _gone.Clear();
        }

        return gone;
    }

    /// <param name="handed">the zone is handed back (not a checkpoint): what is sent now is not sent again</param>
    internal static List<LZ4Bytes>? Collect(bool handed)
    {
        if (_changed.Count == 0) {
            return null;
        }

        // (a companion that died on the way still travels with its player, dead: it goes with the companions)
        var travelling = CompanionHelper.TravellingWith(EClass.pc);
        var residents = _changed
            .Select(uid => EClass.game.cards.globalCharas.Find(uid))
            .Where(c => c is { isDestroyed: false, IsPCParty: false, IsPlayer: false } && !travelling.Contains(c))
            .Select(c => LZ4Bytes.Create(c!))
            .ToList();
        if (handed) {
            _changed.Clear();
        }

        return residents.Count == 0 ? null : residents;
    }
}
