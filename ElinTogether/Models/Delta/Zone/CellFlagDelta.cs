using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The cell tool of the build mode (AM_FlagCell: no snow, floating, wall pillar, see-through, clear area) used by a
///     player who does not keep the map: its game changed only its own copy. Asked of the game that keeps the map,
///     which sets the flag and tells the cell to everyone (<see cref="TileStateDelta" />). The value is sent, not the
///     toggle: asked twice, the same
/// </summary>
[MessagePackObject]
public class CellFlagDelta : ElinDelta
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    [Key(1)]
    public required int X { get; init; }

    [Key(2)]
    public required int Z { get; init; }

    /// <summary>
    ///     The bit of <see cref="TileStateDelta.FlagsOf" />
    /// </summary>
    [Key(3)]
    public required int Flag { get; init; }

    [Key(4)]
    public required bool Value { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !host.ActiveRemoteCharas.ContainsKey(OriginPeer) || !NetSession.Instance.Rules.GuestsBuild || Teams.RefusesPeer(host, OriginPeer) || _zone?.uid != ZoneUid ||
            _map is not { } map || X < 0 || Z < 0 || X >= map.Size || Z >= map.Size || Flag is not (1 or 2 or 4 or 8 or 16)) {
            return;
        }

        var cell = map.cells[X, Z];
        var flags = TileStateDelta.FlagsOf(cell);
        TileStateDelta.SetFlags(cell, Value ? flags | Flag : flags & ~Flag);
        var point = new Point(X, Z);
        // a clear area takes away what stands there, as the tool does
        if (Flag == 16 && Value && (flags & Flag) == 0) {
            using var simulate = Simulate();
            point.Things.ForeachReverse(t => t.Destroy());
        }

        point.RefreshNeighborTiles();
        TileStateDelta.Mark(map, X, Z);
    }
}
