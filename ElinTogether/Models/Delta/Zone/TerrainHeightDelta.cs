using System;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The terrain tool of the build mode (AM_Terrain) writes the height of the cells by hand, no setter to watch
///     (council 5). A player who does not simulate the map sends the heights its brush gave in place of keeping
///     them on its own copy; the game that keeps the map sets them and tells the cells back
///     (<see cref="TileStateDelta" />). The whole height is sent, not the stroke: applying it twice changes nothing
/// </summary>
[MessagePackObject]
public class TerrainHeightDelta : ElinDelta
{
    private const int Width = 4;

    // cells of one request: a brush is a few dozen, the same cap as a state of cells
    private const int MaxCells = 2048;

    /// <summary>
    ///     x, z, height and bridge height of each cell, one after the other
    /// </summary>
    [Key(0)]
    public required int[] Cells { get; init; }

    [Key(1)]
    public required int ZoneUid { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !NetSession.Instance.Rules.GuestsBuild ||
            !host.ActiveRemoteCharas.ContainsKey(OriginPeer) || Teams.RefusesPeer(host, OriginPeer) || Cells.Length > Width * MaxCells || _zone?.uid != ZoneUid ||
            _map is not { } map) {
            return;
        }

        for (var i = 0; i + Width <= Cells.Length; i += Width) {
            int x = Cells[i], z = Cells[i + 1];
            if (x < 0 || z < 0 || x >= map.Size || z >= map.Size) {
                continue;
            }

            // what AM_Terrain does to a cell, then the mark: the state of the cell goes to everyone
            var c = map.cells[x, z];
            // as high as the tool goes (or as the cell already was: a flattened map may be higher)
            c.height = (byte)Math.Clamp(Cells[i + 2], 0, Math.Max(setting.maxGenHeight, c.height));
            c.bridgeHeight = (byte)Math.Clamp(Cells[i + 3], 0, byte.MaxValue);
            c.room?.SetDirty();
            map.RefreshNeighborTiles(x, z);
            TileStateDelta.Mark(map, x, z);
        }
    }
}
