using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElinTogether.Helper.Extensions;

namespace ElinTogether.Models;

/// <summary>
///     What travels with a zone lease: the map files and the packed zone state
/// </summary>
internal static class ZoneLeaseState
{
    // Spatial._ints entries owned by the host: uid, world map x, world map y
    private static readonly HashSet<int> _hostOwnedInts = [1, 3, 4];

    /// <summary>
    ///     Map files of the zone, saving the loaded map first. Null when there is nothing on disk
    /// </summary>
    internal static Dictionary<string, LZ4Bytes>? CollectMap(Zone zone)
    {
        zone.map?.Save(zone.pathSave);

        if (!Directory.Exists(zone.pathSave)) {
            return null;
        }

        return Directory
            .GetFiles(zone.pathSave, "*.*", SearchOption.TopDirectoryOnly)
            .ToDictionary(MapFileKey, LZ4Bytes.CreateFromFile);
    }

    /// <summary>
    ///     A file of a saved map, by its name. A layer of cell ids over 255 is written as "objs.w2" or "objs.w4"
    ///     (Map.WriteCellIds) and read back under that name: keyed without it, it reached the other game as "objs"
    ///     and loaded as an empty layer
    /// </summary>
    internal static string MapFileKey(string path)
    {
        var name = Path.GetFileName(path);
        return IsWideIds(name) ? name : Path.GetFileNameWithoutExtension(name);
    }

    internal static bool IsMapFileKey(string id)
    {
        return !id.Contains('/') && !id.Contains('\\') && (!id.Contains('.') || IsWideIds(id));
    }

    private static bool IsWideIds(string name)
    {
        return name.EndsWith(".w2") || name.EndsWith(".w4");
    }

    internal static void WriteMap(Zone zone, Dictionary<string, LZ4Bytes> map)
    {
        Directory.CreateDirectory(zone.pathSave);

        foreach (var (id, asset) in map) {
            if (!IsMapFileKey(id)) {
                continue;
            }

            asset.DecompressToFile(Path.Combine(zone.pathSave, id));
        }
    }

    /// <summary>
    ///     What a copy of the map carries, as numbers two games compare when the map changes hands: count then mix
    ///     of the things on the floor (uid, amount), of what they hold (uid, holder, amount) and of the
    ///     characters saved with the map (uid). <br />
    ///     Left out: the terrain, what characters carry, where they stand, where things lie (a thrown or scattered
    ///     thing lands by the dice of each game, see NetDesync: it would reload the map at every departure after
    ///     a fight), and the characters of the world (players, companions, residents), which Map.Save leaves out too
    /// </summary>
    internal static int[] Sums(Map map)
    {
        var sums = new int[6];

        unchecked {
            foreach (var thing in map.things) {
                if (Skipped(thing)) {
                    continue;
                }

                sums[0]++;
                sums[1] += Mix(thing.uid, thing.Num, 0, 0);

                foreach (var held in thing.things.Flatten()) {
                    if (!Skipped(held)) {
                        sums[2]++;
                        sums[3] += Mix(held.uid, held.parentCard?.uid ?? 0, held.Num, 0);
                    }
                }
            }

            foreach (var chara in map.charas) {
                if (!chara.IsGlobal && !chara.isDead && !PendingUid.IsPending(chara.uid)) {
                    sums[4]++;
                    sums[5] += Mix(chara.uid, 0, 0, 0);
                }
            }
        }

        return sums;
    }

    /// <summary>
    ///     What decides a reload when a map changes hands: the things on the floor and the characters. Not what
    ///     the things hold: nothing keeps the content of a container the same in every game while they play (the
    ///     map check leaves it out, see NetDesync; a stack changed without Card.ModNum is not told, a thing put
    ///     in stacks by the rules of each game), so it differs for less than a reload under the player is worth
    /// </summary>
    internal static bool SameFloor(int[] ours, int[] theirs)
    {
        return ours.Length >= 6 && theirs.Length >= 6 &&
               ours[0] == theirs[0] && ours[1] == theirs[1] && ours[4] == theirs[4] && ours[5] == theirs[5];
    }

    internal static string TellSums(int[] sums)
    {
        return sums.Length < 6
            ? "none"
            : $"things {sums[0]}:{sums[1]:X8}, held {sums[2]}:{sums[3]:X8}, charas {sums[4]}:{sums[5]:X8}";
    }

    // as NetDesync does: cards waiting for their number and ability tokens are each game's own
    private static bool Skipped(Thing thing)
    {
        return thing.isDestroyed || PendingUid.IsPending(thing.uid) || thing.trait is TraitAbility;
    }

    // summed over the cards: the order of the lists does not matter
    private static int Mix(int a, int b, int c, int d)
    {
        unchecked {
            var h = (uint)a * 0x9E3779B1u;
            h = (h ^ (uint)b) * 0x85EBCA6Bu;
            h = (h ^ (uint)c) * 0xC2B2AE35u;
            h = (h ^ (uint)d) * 0x27D4EB2Fu;
            return (int)(h ^ (h >> 15));
        }
    }

    /// <summary>
    ///     Flags, visit count, dates... everything Elin persists about a zone besides its map
    /// </summary>
    internal static int[] GetState(Zone zone)
    {
        var state = zone._ints.ToArray();
        // flags live in bits at runtime, _ints[0] is only refreshed when serializing
        state[0] = (int)zone.bits.Bits;
        return state;
    }

    /// <summary>
    ///     Zones this game is about to enter while another player is in them, with the state that player's game
    ///     sent. See BossFleePatch
    /// </summary>
    internal static readonly HashSet<int> Imported = [];

    internal static void ApplyState(Zone zone, int[] state, string? subset)
    {
        for (var i = 0; i < Math.Min(state.Length, zone._ints.Length); i++) {
            // "never" in a date (9 expiry, 12 regeneration) is what a client's copy of a map carries, see
            // ZoneDatesRepairPatch: not a date to keep
            if (!_hostOwnedInts.Contains(i) && !(i is 9 or 12 && state[i] == int.MaxValue)) {
                zone._ints[i] = state[i];
            }
        }

        zone.bits.Bits = (uint)state[0];
        zone.idCurrentSubset = subset;
    }
}
