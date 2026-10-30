using System.Collections.Generic;
using ElinTogether.Net;

namespace ElinTogether.Helper;

/// <summary>
///     Host box "The start land belongs to everyone" (user 2026-10-30, ON in the public server preset): the start
///     meadow (Zone_StartSite, the world's first base) is a commons, like a town. It stays claimed, the world's base and
///     the host's home (hosting needs one, and nobody can read a deed there), but it is no team's land (Teams.TeamOfZone),
///     no player's either (what it earns goes to the shared bank, BaseIncome), it has no tax bill (TeamBills), and every
///     player builds, digs, places and picks up furniture there whatever "Guests build" and "Only the host manages the
///     base" say (NetSessionRules.GuestsBuildIn, HostManagesBaseIn). The container log still records it (it is a base)
///     <br />
///     Vernis too (user 2026-10-30, "The start land and Vernis belong to everyone"): its town is no team's land and
///     every player builds, digs and places furniture there; build mode opens there for everyone while the town is
///     its own (Teams.OnCanEnterBuildMode). Its people, shops and quests are untouched: the story's deed still claims it
///     (QuestVernis phase 4 needs the town claimed), which makes it the world's like the meadow, never a team's
///     (Teams.ClaimZone). The way to its mine stays open (<see cref="MineWay" />)
/// </summary>
internal static class Commons
{
    /// <summary>
    ///     The box as this game knows it: the host's own rules, a guest's from the host
    /// </summary>
    internal static bool On => NetSession.Instance?.Rules is { CommonsStartLand: true };

    /// <summary>
    ///     That zone is the commons: the box is on and it is the world's start land
    /// </summary>
    internal static bool Is(Spatial? zone)
    {
        return zone is not null && Is(zone.uid);
    }

    internal static bool Is(int zoneUid)
    {
        if (!On || EClass.game is not { } game) {
            return false;
        }

        return game.StartZone?.uid == zoneUid || game.spatials.Find(zoneUid) is Zone_Vernis { lv: 0 };
    }

    /// <summary>
    ///     The commons' zones, when the box is on: the start land, then Vernis
    /// </summary>
    internal static IEnumerable<Zone> Zones
    {
        get
        {
            if (!On || EClass.game is not { } game) {
                yield break;
            }

            if (game.StartZone is { } start) {
                yield return start;
            }

            if (game.spatials.Find("vernis") is Zone_Vernis { lv: 0 } vernis && vernis != game.StartZone) {
                yield return vernis;
            }
        }
    }

    /// <summary>
    ///     Vernis while it is the town's own (not claimed): build mode opens there for the players
    /// </summary>
    internal static bool IsOpenTown(Zone? zone)
    {
        return zone is Zone_Vernis { lv: 0 } && !zone.IsPCFaction && Is(zone);
    }
}
