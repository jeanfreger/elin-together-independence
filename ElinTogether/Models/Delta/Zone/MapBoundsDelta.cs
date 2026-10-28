using System;
using System.Linq;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Land bought from the maid of a base (daBuyLand) widens the bounds of the map one can build in, by hand:
///     no setter to watch. The player who bought sends the bounds its map now has, every game showing that map
///     takes them. Bounds only grow: one that does not hold the present one is not taken
/// </summary>
[MessagePackObject]
public class MapBoundsDelta : ElinDelta
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    /// <summary>
    ///     x, z, maxX, maxZ
    /// </summary>
    [Key(1)]
    public required int[] Bounds { get; init; }

    /// <summary>
    ///     The host's land, back to the game whose purchase it refused: taken even smaller than the present one
    /// </summary>
    [Key(2)]
    public bool Back { get; init; }

    internal static void Emit(MapBounds bounds)
    {
        if (IsApplying || EClass._map?.bounds != bounds || EClass._zone is not { } zone ||
            NetSession.Instance.Connection is not { } connection) {
            return;
        }

        connection.Delta.AddRemote(new MapBoundsDelta {
            ZoneUid = zone.uid,
            Bounds = [bounds.x, bounds.z, bounds.maxX, bounds.maxZ],
        });
    }

    /// <summary>
    ///     What the maid sells, paid: a tile more on each side of the base the sender stands on, for the gold bars the
    ///     game asks (CalcGold.ExpandLand). Security audit 2026-10-27, item 10: a whole map of land came for nothing
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        // (another map: the delta does nothing here)
        if (Bounds.Length != 4 || _zone?.uid != ZoneUid || _map is not { bounds: { } now } map) {
            return null;
        }

        int[] asked = [Math.Max(1, Bounds[0]), Math.Max(1, Bounds[1]), Math.Min(map.Size - 2, Bounds[2]), Math.Min(map.Size - 2, Bounds[3])];
        int[] next = [Math.Max(1, now.x - 1), Math.Max(1, now.z - 1), Math.Min(map.Size - 2, now.maxX + 1), Math.Min(map.Size - 2, now.maxZ + 1)];
        if (asked.SequenceEqual([now.x, now.z, now.maxX, now.maxZ])) {
            return null;
        }

        if (sender?.currentZone != _zone || !_zone.IsPCFaction) {
            return $"land of zone {ZoneUid} bought off the sender's base";
        }

        if (!asked.SequenceEqual(next)) {
            return $"land of zone {ZoneUid}: more than one purchase at once";
        }

        var cost = 4 + now.Width / 5;
        var paid = CardModCurrencyDelta.TakeSpent(OriginPeer, cost, currency: "money2");
        return paid < cost ? $"land of zone {ZoneUid} for {paid} of {cost} gold bars" : null;
    }

    /// <summary>
    ///     The sender's map is told the land the host has
    /// </summary>
    internal override void OnHostRefused(ElinNetHost host)
    {
        if (_zone?.uid == ZoneUid && _map?.bounds is { } now) {
            host.SendDeltaTo(OriginPeer, new MapBoundsDelta { ZoneUid = ZoneUid, Bounds = [now.x, now.z, now.maxX, now.maxZ], Back = true });
        }
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (Bounds.Length != 4) {
            return;
        }

        if (net is ElinNetHost host) {
            // (a player elsewhere drops it: the map comes whole when it walks in)
            host.SendDeltaToAllExcept(OriginPeer, this);
        }

        if (_zone?.uid != ZoneUid || _map is not { bounds: { } now } map) {
            return;
        }

        int x = Math.Max(1, Bounds[0]), z = Math.Max(1, Bounds[1]);
        int maxX = Math.Min(map.Size - 2, Bounds[2]), maxZ = Math.Min(map.Size - 2, Bounds[3]);
        if (!(Back && net is ElinNetClient) && (x > now.x || z > now.z || maxX < now.maxX || maxZ < now.maxZ)) {
            return;
        }

        (now.x, now.z, now.maxX, now.maxZ) = (x, z, maxX, maxZ);
        map.RefreshAllTiles();
        WidgetMinimap.UpdateMap();
    }
}
