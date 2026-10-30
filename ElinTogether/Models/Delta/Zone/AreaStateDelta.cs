using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The areas a player draws on the map of the base (stockpiles, farms, wards...) with their points, type, name
///     and settings: the base-area modes only changed the copy of their own game. All the areas of the map are
///     sent, as the game saves them, by whoever just changed them (<see cref="AreaWatch" />), the others take the
///     state as it is and the host relays it, both ways. The last one to speak wins: applying a state twice, or
///     an equal one, changes nothing <br />
///     Rooms (walls, plates) are not areas: each game finds them by itself
/// </summary>
[MessagePackObject]
public class AreaStateDelta : ElinDelta
{
    // what a base can hold: more is not a state a player drew
    private const int MaxAreas = 256;

    /// <summary>
    ///     RoomManager.listArea, in the game's own JSON (the same as the map file keeps)
    /// </summary>
    [Key(0)]
    public required LZ4Bytes Areas { get; init; }

    /// <summary>
    ///     The zone these areas are of: one told around a change of zone must not land on another map
    /// </summary>
    [Key(1)]
    public required int ZoneUid { get; init; }

    internal static AreaStateDelta Create()
    {
        return new() { Areas = LZ4Bytes.Create(_map.rooms.listArea), ZoneUid = _zone.uid };
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (_zone?.uid != ZoneUid || _map is not { } map) {
            return;
        }

        var host = net as ElinNetHost;
        // a guest's word, when the host lets it build
        if (host is not null && (!NetSession.Instance.Rules.GuestsBuildIn(_zone) || !host.ActiveRemoteCharas.ContainsKey(OriginPeer) ||
                                 Teams.RefusesPeer(host, OriginPeer, _zone))) {
            return;
        }

        // decoded and checked before anything is removed or told: a state that cannot be loaded would leave
        // every game without its areas
        List<Area> incoming;
        try {
            incoming = Areas.Decompress<List<Area>>();
        } catch (Exception) {
            return;
        }

        if (!IsValid(incoming, map.Size) || (host is null && AreaWatch.IsEcho(AreaWatch.Hash(incoming)))) {
            return;
        }

        host?.Delta.AddRemote(this);

        var rooms = map.rooms;
        var same = new HashSet<int>();
        var changed = new List<(Area Local, Area Told)>();

        // first every area that is gone or changed (its points are freed), then the new ones: an area that took
        // the cells of another must not lose them when the other is removed
        foreach (var local in rooms.listArea.ToList()) {
            var told = incoming.Find(a => a.uid == local.uid);
            if (told is null) {
                rooms.RemoveArea(local);
            } else if (AreaWatch.Sign(told) == AreaWatch.Sign(local)) {
                same.Add(local.uid);
            } else {
                foreach (var point in local.points) {
                    if (point.cell.detail is { } detail) {
                        detail.area = null;
                        point.cell.TryDespawnDetail();
                    }
                }

                foreach (var task in local.taskList.items.ToArray()) {
                    task.Destroy();
                }

                changed.Add((local, told));
            }
        }

        // an area that stays keeps its object (the area being edited, the lists), the rest is as the map loads it:
        // points on their cells, tasks, owner of the type, entry in the register
        foreach (var (local, told) in changed) {
            local.points = told.points;
            local.data = told.data;
            local.type = told.type;
            local.taskList = told.taskList;
            rooms.mapIDs.Remove(local.uid);
            local.OnLoad();
        }

        foreach (var area in incoming.Where(a => !same.Contains(a.uid) && changed.All(c => c.Local.uid != a.uid))) {
            rooms.mapIDs.Remove(area.uid);
            area.OnLoad();
            rooms.listArea.Add(area);
        }

        // the numbers of rooms and areas come from one counter: no area of another game may meet a new one
        rooms.uidRoom = Math.Max(rooms.uidRoom, incoming.Select(a => a.uid + 1).DefaultIfEmpty(0).Max());
        AreaWatch.Accept();
    }

    private static bool IsValid(List<Area> areas, int size)
    {
        if (areas.Count > MaxAreas) {
            return false;
        }

        var uids = new HashSet<int>();
        var cells = new HashSet<int>();
        foreach (var area in areas) {
            if (area?.points is null || area.data is null || area.type is null || area.taskList is null || !uids.Add(area.uid)) {
                return false;
            }

            // inside the map, and each cell in one area only
            foreach (var point in area.points) {
                if (point is null || point.x < 0 || point.z < 0 || point.x >= size || point.z >= size ||
                    !cells.Add(point.x + point.z * size)) {
                    return false;
                }
            }
        }

        return true;
    }
}
