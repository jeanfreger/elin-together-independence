using ElinTogether.Net;

namespace ElinTogether.Helper;

/// <summary>
///     Host box "The start land belongs to everyone" (user 2026-10-30, ON in the public server preset): the start
///     meadow (Zone_StartSite, the world's first base) is a commons, like a town. It stays claimed, the world's base and
///     the host's home (hosting needs one, and nobody can read a deed there), but it is no team's land (Teams.TeamOfZone),
///     no player's either (what it earns goes to the shared bank, BaseIncome), it has no tax bill (TeamBills), and every
///     player builds, digs, places and picks up furniture there whatever "Guests build" and "Only the host manages the
///     base" say (NetSessionRules.GuestsBuildIn, HostManagesBaseIn). The container log still records it (it is a base)
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
        return On && EClass.game?.StartZone is { } start && start.uid == zoneUid;
    }

    /// <summary>
    ///     The commons' zone, when the box is on
    /// </summary>
    internal static Zone? Zone => On ? EClass.game?.StartZone : null;
}
