using System.Collections.Generic;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The brush of the terrain tool writes cell.height by hand. Where the map is simulated the cells whose height
///     changed are marked (<see cref="TileStateDelta" />); in a game that does not keep the map the brush works on
///     the local copy as it always did (so a held button keeps stacking) and the heights it gave are sent to the
///     game that does (<see cref="TerrainHeightDelta" />): what comes back is the same state, so it changes nothing
/// </summary>
[HarmonyPatch(typeof(AM_Terrain), nameof(AM_Terrain.OnProcessTiles))]
internal static class RemoteTerrainPatch
{
    [HarmonyPrefix]
    internal static void OnBrush(AM_Terrain __instance, Point point, out List<int>? __state)
    {
        __state = null;
        // the game's own pace: the brush acts at most once a tenth of a second
        var connection = NetSession.Instance.Connection;
        if (__instance.timer < 0.1f || ElinDelta.IsApplying || connection is null ||
            (connection is ElinNetClient && !NetSession.Instance.Rules.GuestsBuildIn(EClass._zone))) {
            return;
        }

        // x, z, height and bridge height of the cells the brush reaches, before it acts
        var before = new List<int>();
        var centre = __instance.lastPoint ?? point;
        EClass._map.ForeachSphere(centre.x, centre.z, __instance.brushRadius, p => {
            var c = p.cell;
            before.AddRange([p.x, p.z, c.height, c.bridgeHeight]);
        });
        __state = before;
    }

    [HarmonyPostfix]
    internal static void OnBrushDone(List<int>? __state)
    {
        if (__state is null || EClass._map is not { } map) {
            return;
        }

        var changed = new List<int>();
        for (var i = 0; i < __state.Count; i += 4) {
            int x = __state[i], z = __state[i + 1];
            var c = map.cells[x, z];
            if (c.height != __state[i + 2] || c.bridgeHeight != __state[i + 3]) {
                changed.AddRange([x, z, c.height, c.bridgeHeight]);
            }
        }

        switch (NetSession.Instance.Connection) {
            case ElinNetHost:
                for (var i = 0; i < changed.Count; i += 4) {
                    TileStateDelta.Mark(map, changed[i], changed[i + 1]);
                }

                break;
            case ElinNetClient client when changed.Count > 0:
                client.Delta.AddRemote(new TerrainHeightDelta { Cells = [..changed], ZoneUid = EClass._zone.uid });
                break;
        }
    }
}
