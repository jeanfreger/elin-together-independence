using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The terrain of the cells that changed on the map of whoever simulates it: floor, block, tile object, bridge,
///     roof, deco, directions, heights, the harvested and watered marks of a plant, the cross wall (council 5).
///     The whole state of a cell is sent, not what was done to it: applying it twice, or after a replay that
///     already made the same change, changes nothing <br />
///     Without it terrain only followed by each game replaying the same act: what the build mode of the host made
///     (no act is told for it) reached the others at their next visit of the zone
/// </summary>
[MessagePackObject]
public class TileStateDelta : ElinDelta
{
    private const int Width = 25;

    // cells of one delta: a floor dragged over a whole map goes in several
    private const int MaxCells = 2048;

    private static readonly HashSet<int> _dirty = [];

    /// <summary>
    ///     x, z and the 23 terrain fields of each cell, one after the other (the last: the flags of the build mode's
    ///     cell tool, see <see cref="FlagsOf" />)
    /// </summary>
    [Key(0)]
    public required int[] Cells { get; init; }

    /// <summary>
    ///     The zone these cells are of: one told around a change of zone must not land on another map
    /// </summary>
    [Key(1)]
    public required int ZoneUid { get; init; }

    /// <summary>
    ///     The plant data (Map.plants: the seed with its level and food traits, water, fert, size) of the cells above
    ///     that have one, by cell index. A cell of <see cref="Cells" /> that is not in it has no plant. The seed of a
    ///     harvest is made from it: without it a +50 seed gave back a level 0 one (issue #31)
    /// </summary>
    [Key(2)]
    public LZ4Bytes? Plants { get; init; }

    internal static void Mark(Map map, int x, int z)
    {
        // generating or loading a map is not a change to tell: everyone gets the whole map then
        // alone: nobody to tell, who arrives gets the whole map
        if (!ZoneActivateEvent.IsHappening && core.IsGameStarted && !game.isLoading &&
            NetSession.Instance.Connection is ElinNetHost && NetCompany.HasCompany && map == _map) {
            _dirty.Add(x + z * map.Size);
        }
    }

    /// <summary>
    ///     The marks of a map that is left are not of the next one
    /// </summary>
    internal static void Forget()
    {
        _dirty.Clear();
    }

    /// <summary>
    ///     End of the frame, after the replays of the frame were queued (a task replayed on a cell that already
    ///     has its final state would act again: TaskMine digs stairs under a wall that is gone): keep it there
    /// </summary>
    internal static void Flush()
    {
        if (_dirty.Count == 0) {
            return;
        }

        if (NetSession.Instance.Connection is ElinNetHost host && NetCompany.HasCompany && !ZoneActivateEvent.IsHappening) {
            var size = _map.Size;
            var cells = new List<int>(Math.Min(_dirty.Count, MaxCells) * Width);
            var plants = new Dictionary<int, PlantData>();
            void Send()
            {
                host.Delta.AddRemote(new TileStateDelta {
                    Cells = [..cells], ZoneUid = _zone.uid, Plants = plants.Count == 0 ? null : LZ4Bytes.Create(plants),
                });
                cells.Clear();
                plants.Clear();
            }

            foreach (var index in _dirty) {
                int x = index % size, z = index / size;
                if (z >= size) {
                    continue;
                }

                var c = _map.cells[x, z];
                if (_map.plants.TryGetValue(c.index, out var plant)) {
                    plants[c.index] = plant;
                }

                cells.AddRange([
                    x, z, c._block, c._blockMat, c._floor, c._floorMat, c.obj, c.objMat, c._bridge, c._bridgeMat,
                    c._roofBlock, c._roofBlockMat, c._deco, c._decoMat, c._dirs, c.objVal, c._roofBlockDir,
                    c.bridgePillar, c.height, c.bridgeHeight, c.hidePillar ? 1 : 0, c.isHarvested ? 1 : 0, c.crossWall ? 1 : 0,
                    c.isWatered ? 1 : 0, FlagsOf(c),
                ]);
                if (cells.Count >= MaxCells * Width) {
                    Send();
                }
            }

            if (cells.Count > 0) {
                Send();
            }
        }

        _dirty.Clear();
    }

    /// <summary>
    ///     The plant data of the cell is the sender's (Map.SetObj dropped what this game had); null when the plants
    ///     could not be read: keep what is there then
    /// </summary>
    private static void SyncPlant(Map map, Cell c, Dictionary<int, PlantData>? plants)
    {
        if (plants is null) {
            return;
        }

        if (plants.TryGetValue(c.index, out var plant) && plant is not null) {
            map.plants[c.index] = plant;
        } else {
            map.plants.Remove(c.index);
        }
    }

    /// <summary>
    ///     The cell tool of the build mode (AM_FlagCell): no snow, floating, wall pillar, see-through, clear area, and a dyed tile object (bit 32: a dyed seed that
    ///     grows keeps its colour)
    /// </summary>
    internal static int FlagsOf(Cell c)
    {
        return (c.isClearSnow ? 1 : 0) | (c.isForceFloat ? 2 : 0) | (c.isToggleWallPillar ? 4 : 0) |
               (c.toggleOcclusion ? 8 : 0) | (c.isClearArea ? 16 : 0) | (c.isObjDyed ? 32 : 0);
    }

    internal static void SetFlags(Cell c, int flags)
    {
        c.isClearSnow = (flags & 1) != 0;
        c.isForceFloat = (flags & 2) != 0;
        c.isToggleWallPillar = (flags & 4) != 0;
        c.toggleOcclusion = (flags & 8) != 0;
        c.isClearArea = (flags & 16) != 0;
        c.isObjDyed = (flags & 32) != 0;
    }

    protected override void OnApply(ElinNetBase net)
    {
        // only ever told by the one simulating the map
        if (net.IsHost || _zone?.uid != ZoneUid || _map is not { } map) {
            return;
        }

        // many cells at once (a dragged floor): one refresh of the sight for all
        var many = Cells.Length > 64 * Width;
        Dictionary<int, PlantData>? plants = null;
        try {
            plants = Plants?.Decompress<Dictionary<int, PlantData>>();
        } catch (Exception e) {
            EmpLog.Warning("Plant data of a tile state unreadable: {Why}", e.Message);
        }

        for (var i = 0; i + Width <= Cells.Length; i += Width) {
            int x = Cells[i], z = Cells[i + 1];
            if (x < 0 || z < 0 || x >= map.Size || z >= map.Size) {
                continue;
            }

            var s = Cells.AsSpan(i + 2, Width - 2);
            var c = map.cells[x, z];
            var dirs = (byte)s[12];
            var same = c._block == s[0] && c._blockMat == s[1] && c._floor == s[2] && c._floorMat == s[3] &&
                       c.obj == s[4] && c.objMat == s[5] && c._bridge == s[6] && c._bridgeMat == s[7] &&
                       c._roofBlock == s[8] && c._roofBlockMat == s[9] && c._deco == s[10] && c._decoMat == s[11] &&
                       c._dirs == dirs && c.objVal == s[13] && c._roofBlockDir == s[14] && c.bridgePillar == s[15] &&
                       c.height == s[16] && c.bridgeHeight == s[17] && c.hidePillar == (s[18] != 0) && c.isHarvested == (s[19] != 0) &&
                       c.crossWall == (s[20] != 0) && c.isWatered == (s[21] != 0) && FlagsOf(c) == s[22];
            if (same) {
                SyncPlant(map, c, plants);
                continue;
            }

            // the game's own setters for what has side effects (rooms, light, fire on a removed block), then the rest
            if (c._block != s[0] || c._blockMat != s[1]) {
                map.SetBlock(x, z, s[1], s[0]);
            }

            if (c._floor != s[2] || c._floorMat != s[3]) {
                map.SetFloor(x, z, s[3], s[2]);
            }

            if (c.obj != s[4] || c.objMat != s[5]) {
                map.SetObj(x, z, s[5], s[4], s[13], 0, true);
            }

            c._bridge = s[6];
            c._bridgeMat = s[7];
            c._roofBlock = s[8];
            c._roofBlockMat = s[9];
            c._deco = s[10];
            c._decoMat = s[11];
            c._dirs = dirs;
            c.objVal = (byte)s[13];
            c._roofBlockDir = (byte)s[14];
            c.bridgePillar = s[15];
            c.height = (byte)s[16];
            c.bridgeHeight = (byte)s[17];
            c.hidePillar = s[18] != 0;
            c.isHarvested = s[19] != 0;
            c.crossWall = s[20] != 0;
            c.isWatered = s[21] != 0;
            SetFlags(c, s[22]);
            SyncPlant(map, c, plants);
            c.room?.SetDirty();
            map.RefreshNeighborTiles(x, z);
            if (!many) {
                map.RefreshFOV(x, z);
            }
        }

        if (many) {
            map.RefreshFOVAll();
        }
    }
}
