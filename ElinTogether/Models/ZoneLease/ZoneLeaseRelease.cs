using System.Collections.Generic;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Net packet: Client -> Host <br />
///     Hand a leased zone back, with everything the client changed while simulating it
/// </summary>
[MessagePackObject]
public class ZoneLeaseRelease
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    /// <summary>
    ///     Client zone state, see <see cref="ZoneLeaseState.GetState" />
    /// </summary>
    [Key(1)]
    public required int[] ZoneState { get; init; }

    [Key(2)]
    public string? IdCurrentSubset { get; init; }

    /// <summary>
    ///     Client map files, null for regions (simulated locally only)
    /// </summary>
    [Key(3)]
    public Dictionary<string, LZ4Bytes>? Map { get; init; }

    /// <summary>
    ///     The client's own character, replacing the host copy
    /// </summary>
    [Key(4)]
    public required LZ4Bytes Chara { get; init; }

    [Key(5)]
    public required int UidNext { get; init; }

    /// <summary>
    ///     Return to the host zone, otherwise another lease request follows
    /// </summary>
    [Key(6)]
    public required bool Rejoin { get; init; }

    /// <summary>
    ///     Progress save while still away: applied by the host, the lease is kept
    /// </summary>
    [Key(7)]
    public bool Checkpoint { get; init; }

    /// <summary>
    ///     Characters of the players visiting the zone, simulated here, by identity
    /// </summary>
    [Key(8)]
    public Dictionary<ulong, LZ4Bytes>? GuestCharas { get; init; }

    /// <summary>
    ///     Companions travelling with the player, see CompanionHelper
    /// </summary>
    [Key(9)]
    public List<LZ4Bytes>? Companions { get; init; }

    /// <summary>
    ///     Companions of the players visiting the zone, by identity
    /// </summary>
    [Key(10)]
    public Dictionary<ulong, List<LZ4Bytes>>? GuestCompanions { get; init; }

    /// <summary>
    ///     The map the client stands on, held or only visited (<see cref="ZoneUid" /> is -1 for a visitor): if the
    ///     host is the one coming there, the player stays on its tile
    /// </summary>
    [Key(11)]
    public int StoodZoneUid { get; set; } = -1;

    /// <summary>
    ///     Returning by walking into the host's map: the way in
    /// </summary>
    [Key(12)]
    public ZoneArrival? Arrival { get; init; }

    /// <summary>
    ///     The numbers of <see cref="Map" /> (<see cref="ZoneLeaseState.Sums" />): the host hands both to the visitor
    ///     who takes the zone over, see ElinNetHost.HandOverZone
    /// </summary>
    [Key(13)]
    public int[]? MapSums { get; set; } = null;

    /// <summary>
    ///     Answer to a soft recall: this game keeps its scene and waits for <see cref="ZoneSoftRejoin" /> instead
    ///     of a copy of the world. The host may still answer with the world
    /// </summary>
    [Key(14)]
    public bool Soft { get; set; } = false;

    /// <summary>
    ///     Characters of the base this player made or let go while it was alone, and that do not travel with it:
    ///     a recruit sent home, an egg hatched as livestock, a companion that died or was told to go home. They
    ///     exist in its copy of the world only, the host takes them (see AwayResidents)
    /// </summary>
    [Key(15)]
    public List<LZ4Bytes>? Residents { get; init; }

    /// <summary>
    ///     Numbers of characters of the base that went for good while this player was alone (a livestock
    ///     butchered, a member banished): the host drops its copy, see AwayResidents
    /// </summary>
    [Key(16)]
    public List<int>? GoneResidents { get; init; }
}
