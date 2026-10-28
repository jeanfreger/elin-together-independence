using System.Collections.Generic;
using ElinTogether.Helper;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

// hand of 105gun
[HarmonyPatch]
internal class FovCellLightOffsetPatch
{
    // player fov -> its owner
    internal static readonly Dictionary<Fov, Chara> PlayerFovs = [];

    // per cell of Fov.map: how many player fovs hold it in their lastPoints (pcSync = held > 0)
    private static ushort[] _held = [];
    private static Map? _heldMap;
    private static int _prunedFrame = -1;

    internal static void Register(Chara owner, Fov fov)
    {
        if (_prunedFrame != Time.frameCount) {
            _prunedFrame = Time.frameCount;
            Prune();
        }

        if (PlayerFovs.ContainsKey(fov)) {
            PlayerFovs[fov] = owner;
            return;
        }

        EnsureCounts();
        PlayerFovs[fov] = owner;
        Hold(fov);
    }

    internal static void Reset()
    {
        PlayerFovs.Clear();
        _heldMap = null;
    }

    // fovs whose owner left, died, went to another zone or got a new fov stop counting
    private static void Prune()
    {
        List<Fov>? gone = null;
        foreach (var (fov, owner) in PlayerFovs) {
            if (owner.isDestroyed || owner.isDead || !owner.ExistsOnMap || owner.fov != fov || !owner.IsPlayer) {
                (gone ??= []).Add(fov);
            }
        }

        if (gone is null) {
            return;
        }

        EnsureCounts();
        foreach (var fov in gone) {
            PlayerFovs.Remove(fov);
            foreach (var pos in fov.lastPoints.Keys) {
                Fov.map.GetCell(pos).pcSync = Release(pos);
            }
        }
    }

    // map changed: rebuild the counts from the fovs still listed (true when rebuilt)
    private static bool EnsureCounts()
    {
        var map = Fov.map;
        if (map is null || (_heldMap == map && _held.Length == map.cells.Length)) {
            return false;
        }

        _heldMap = map;
        _held = new ushort[map.cells.Length];
        foreach (var fov in PlayerFovs.Keys) {
            Hold(fov);
        }

        return true;
    }

    private static void Hold(Fov fov)
    {
        foreach (var pos in fov.lastPoints.Keys) {
            if ((uint)pos < (uint)_held.Length && _held[pos] < ushort.MaxValue) {
                _held[pos]++;
            }
        }
    }

    // one holder less; true while another player fov still holds the cell
    private static bool Release(int pos)
    {
        if ((uint)pos >= (uint)_held.Length) {
            return false;
        }

        if (_held[pos] > 0) {
            _held[pos]--;
        }

        return _held[pos] > 0;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Fov), nameof(Fov.ClearVisible))]
    internal static bool OnClearVisible(Fov __instance)
    {
        if (!PlayerFovs.ContainsKey(__instance)) {
            return true;
        }

        EnsureCounts();
        foreach (var (pos, offset) in __instance.lastPoints) {
            var cell = Fov.map.GetCell(pos);

            cell.light -= offset;
            cell.lightR -= (ushort)(offset * __instance.r / 2);
            cell.lightG -= (ushort)(offset * __instance.g / 2);
            cell.lightB -= (ushort)(offset * __instance.b / 2);

            cell.pcSync = Release(pos);
        }

        __instance.lastPoints.Clear();
        return false;
    }

    // the new lastPoints of a player fov count once Perform filled them (finalizer: also after an exception)
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Fov), nameof(Fov.Perform))]
    internal static void OnPerformEnd(Fov __instance)
    {
        if (!PlayerFovs.ContainsKey(__instance)) {
            return;
        }

        if (!EnsureCounts()) {
            Hold(__instance);
        }
    }
}
