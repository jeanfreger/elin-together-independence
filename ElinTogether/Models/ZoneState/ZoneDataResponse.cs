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
        ZoneLeaseState.CommitStaged(zone.uid);
        zone.map?.Save(zone.pathSave);
        NetMetrics.MapSave.Add(0, start);
        // (FastJoinsAndSaves: what the last save wrote beside the game is on the disk before it is read)
        SaveWriter.Wait();

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

    /// <summary>
    ///     What <see cref="Create" /> reads in the game, without the compression: <see cref="Captured.Pack" /> may
    ///     run beside the game (host setting FastJoinsAndSaves)
    /// </summary>
    public static Captured Capture(Zone zone)
    {
        var start = Stopwatch.GetTimestamp();
        ZoneLeaseState.CommitStaged(zone.uid);
        zone.map?.Save(zone.pathSave);
        NetMetrics.MapSave.Add(0, start);
        SaveWriter.Wait();

        return new(zone.ZoneFullName, zone.uid, LZ4Bytes.ToJson(zone),
            Directory
                .GetFiles(zone.pathSave, "*.*", SearchOption.TopDirectoryOnly)
                .ToDictionary(ZoneLeaseState.MapFileKey, File.ReadAllBytes),
            ZoneLeaseState.GetState(zone), zone.idCurrentSubset);
    }

    // plain text and bytes only: nothing of the game is touched off its thread
    public sealed record Captured(
        string ZoneFullName,
        int ZoneUid,
        string Zone,
        Dictionary<string, byte[]> Files,
        int[]? ZoneState,
        string? IdCurrentSubset)
    {
        public ZoneDataResponse Pack()
        {
            return new() {
                ZoneFullName = ZoneFullName,
                ZoneUid = ZoneUid,
                Zone = LZ4Bytes.CreateFromBytes(System.Text.Encoding.UTF8.GetBytes(Zone)),
                Map = Files.ToDictionary(f => f.Key, f => LZ4Bytes.CreateFromBytes(f.Value)),
                ZoneState = ZoneState,
                IdCurrentSubset = IdCurrentSubset,
            };
        }
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

    // a real big map is ~40 MB compressed: this only stops a malicious host from filling the disk with 64 big files
    private const long MaxMapBytes = 512L * 1024 * 1024;

    // the same files unpacked, all together (security audit 2026-10-29: each was capped at 256 MB alone, 64 of
    // them were 16 GB on the disk from one 64 MB message); a real map is some MB
    internal const long MaxMapUnpacked = 512L * 1024 * 1024;

    /// <summary>
    ///     All the files of a received map together stay under <see cref="MaxMapBytes" />, as sent (compressed)
    /// </summary>
    internal static bool IsMapSizeSafe(Dictionary<string, LZ4Bytes> map)
    {
        var total = 0L;
        foreach (var file in map.Values) {
            total += file?.Bytes?.Length ?? 0;
        }

        if (total <= MaxMapBytes) {
            return true;
        }

        EmpLog.Warning("Map refused: its files hold {Bytes} bytes, over {Max}", total, MaxMapBytes);
        return false;
    }

    public void WriteToTemp()
    {
        if (!IsMapSizeSafe(Map)) {
            return;
        }

        var basePath = ResourceFetch.GetEmpSavePath();

        // (as ZoneLeaseState.WriteFiles: the game writes some twenty files for a map, a host sends no more)
        var left = MaxMapUnpacked;
        foreach (var (id, asset) in Map.Take(64)) {
            if (!ZoneLeaseState.IsMapFileKey(id)) {
                continue;
            }
            var path = Path.Combine(basePath, ZoneUid.ToString(), id);
            left -= asset.DecompressToFile(path, left);
        }

        EmpLog.Debug("Saved map {ZoneUid} to temp folder",
            ZoneUid);
    }
}