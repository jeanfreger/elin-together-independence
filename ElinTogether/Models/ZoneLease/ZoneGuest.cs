using System.Collections.Generic;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Net packet: Host -> Client simulating a zone <br />
///     Another player wants to join the zone: open a zone session if needed and expect it
/// </summary>
[MessagePackObject]
public class ZoneGuestRequest
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    /// <summary>
    ///     Identity the guest connects with (Steam id, or the debug identity of local sessions)
    /// </summary>
    [Key(1)]
    public required ulong GuestUser { get; init; }

    /// <summary>
    ///     Guest character as the host last knew it
    /// </summary>
    [Key(2)]
    public required LZ4Bytes Chara { get; init; }

    /// <summary>
    ///     Its companions, they come along, see CompanionHelper
    /// </summary>
    [Key(3)]
    public List<LZ4Bytes>? Companions { get; init; }

    /// <summary>
    ///     It was on this map already (the one simulating it changed): the tile it stays on
    /// </summary>
    [Key(4)]
    public Position? Stood { get; init; }

    /// <summary>
    ///     It walks in: the way it comes by, see <see cref="ZoneArrival" />
    /// </summary>
    [Key(5)]
    public ZoneArrival? Arrival { get; init; }

    /// <summary>
    ///     <see cref="Stood" /> is the tile of an old upload (the one simulating the map dropped): kept for the
    ///     player for want of better, its companions gather around it
    /// </summary>
    [Key(6)]
    public bool StoodStale { get; init; }
}

/// <summary>
///     Net packet: Client simulating a zone -> Host
/// </summary>
[MessagePackObject]
public class ZoneGuestReady
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    [Key(1)]
    public required ulong GuestUser { get; init; }

    [Key(2)]
    public required bool Accepted { get; init; }

    /// <summary>
    ///     Local udp port of the zone session (debug sessions), Steam connects by id
    /// </summary>
    [Key(3)]
    public int Port { get; init; }

    /// <summary>
    ///     Steam virtual port of the zone session: 1 when the holder is the owner seat of a hidden server (the
    ///     server listens on 0 on the same account), else 0
    /// </summary>
    [Key(4)]
    public int VirtualPort { get; init; }

    /// <summary>
    ///     The zone session listens on the local port only (a bench or local session): Steam does not reach it
    /// </summary>
    [Key(5)]
    public bool Local { get; init; }
}

/// <summary>
///     Where a guest joins the zone session, sent with <see cref="ZoneLeaseDepart" />
/// </summary>
[MessagePackObject]
public class ZoneGuestAddress
{
    [Key(0)]
    public required ulong HostUser { get; init; }

    [Key(1)]
    public required int Port { get; init; }

    /// <summary>
    ///     Steam virtual port to connect to, see <see cref="ZoneGuestReady.VirtualPort" />
    /// </summary>
    [Key(2)]
    public int VirtualPort { get; init; }

    /// <summary>
    ///     The holder is reachable by the local port only: the joiner dials it, whatever its own link to the host is
    /// </summary>
    [Key(3)]
    public bool Local { get; init; }
}

/// <summary>
///     Net packet: Guest -> Zone session host <br />
///     Leaving the zone: like <see cref="ZoneLeaseAck" />, sent after the guest's last deltas
/// </summary>
[MessagePackObject]
public class ZoneGuestLeave
{
    [Key(0)]
    public required int ZoneUid { get; init; }
}

/// <summary>
///     Net packet: Zone session host -> Guest <br />
///     Last actions applied and their results sent, the guest may go
/// </summary>
[MessagePackObject]
public class ZoneGuestLeft
{
    [Key(0)]
    public required int ZoneUid { get; init; }
}
