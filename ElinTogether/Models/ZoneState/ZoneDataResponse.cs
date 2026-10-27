using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class ZoneDataResponse
{
    [Key(0)]
    public required string ZoneFullName { get; init; }

    [Key(1)]
    public required int ZoneUid { get; init; }

    [Key(2)]
    public required LZ4Bytes Zone { get; init; }

    // TODO use a map surrogate for more efficient transporting
    // TODO upcoming byte[] to int[] layout update from Elin
    [Key(3)]
    public required Dictionary<string, LZ4Bytes> Map { get; init; }

    /// <summary>
    ///     What the sender holds about the zone besides its map (<see cref="ZoneLeaseState.GetState" />). Taken by
    ///     a client under the host rule SoftRecall only: its world is no longer copied at each return, and the
    ///     numbers of a zone it keeps later go back to the host with its release
    /// </summary>
    [Key(4)]
    public int[]? ZoneState { get; set; } = null;

    [Key(5)]
    public string? IdCurrentSubset { get; set; } = null;

    [return: NotNullIfNotNull("zone")]
    public static implicit operator ZoneDataResponse?(Zone? zone)
    {
        return Create(zone);
    }

    [return: NotNullIfNotNull("zone")]
    public static ZoneDataResponse? Create(Zone? zone)
    {
        if (zone is null) {
            return null;
        }

        var start = Stopwatch.GetTimestamp();
        zone.map?.Save(zone.pathSave);
        NetMetrics.MapSave.Add(0, start);

        start = Stopwatch.GetTimestamp();
        var response = new ZoneDataResponse {
            ZoneFullName = zone.ZoneFullName,
            ZoneUid = zone.uid,
            Zone = LZ4Bytes.Create(zone),
            Map = Directory
                .GetFiles(zone.pathSave, "*.*", SearchOption.TopDirectoryOnly)
                .ToDictionary(ZoneLeaseState.MapFileKey, LZ4Bytes.CreateFromFile),
            ZoneState = ZoneLeaseState.GetState(zone),
            IdCurrentSubset = zone.idCurrentSubset,
        };

        var packed = (long)response.Zone.Bytes.Length;
        foreach (var file in response.Map.Values) {
            packed += file.Bytes.Length;
        }

        NetMetrics.MapPack.Add(packed, start);
        return response;
    }

    public Zone? FindZone()
    {
        return EClass.game?.spatials.Find(ZoneUid) ??
               SpatialGenEvent.TryPop(ZoneUid);
    }

    public ZoneDataReceivedResponse Ready()
    {
        return new() {
            ZoneUid = ZoneUid,
            ZoneFullName = ZoneFullName,
        };
    }

    public void WriteToTemp()
    {
        var basePath = ResourceFetch.GetEmpSavePath();

        foreach (var (id, asset) in Map) {
            if (!ZoneLeaseState.IsMapFileKey(id)) {
                continue;
            }
            var path = Path.Combine(basePath, ZoneUid.ToString(), id);
            asset.DecompressToFile(path);
        }

        EmpLog.Debug("Saved map {ZoneUid} to temp folder",
            ZoneUid);
    }
}