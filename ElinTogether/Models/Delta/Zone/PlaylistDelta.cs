using System.Collections.Generic;
using ElinTogether.Net;
using MessagePack;
using UnityEngine;

namespace ElinTogether.Models;

/// <summary>
///     The music of a map chosen in its playlist window (LayerEditPlaylist, the record player): kept on the map of the
///     game where it was chosen, the others went on with the old one. The game where it is chosen sends it, every
///     game that has that map takes it
/// </summary>
[MessagePackObject]
public class PlaylistDelta : ElinDelta
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    [Key(1)]
    public required List<int> Day { get; init; }

    [Key(2)]
    public required List<int> Night { get; init; }

    [Key(3)]
    public bool Shuffle { get; init; }

    // Elin nightly 23.356 only: the stable branch has no shuffle, and naming the field would break this delta there
    private static readonly System.Reflection.FieldInfo? ShufflePL = HarmonyLib.AccessTools.Field(typeof(MapConfig), "shufflePL");

    internal static void Emit()
    {
        if (!IsApplying && NetSession.Instance.Connection is { } connection && EClass._zone is { map: { } map } zone) {
            connection.Delta.AddRemote(new PlaylistDelta {
                ZoneUid = zone.uid,
                Day = [..map._plDay],
                Night = [..map._plNight],
                Shuffle = ShufflePL?.GetValue(map.config) is true,
            });
        }
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            host.SendDeltaToAllExcept(OriginPeer, this);
        }

        if (game.spatials.Find(ZoneUid) is not Zone { map: { } map } zone) {
            return;
        }

        map._plDay = [..Day];
        map._plNight = [..Night];
        ShufflePL?.SetValue(map.config, Shuffle);
        if (map.plDay != null) {
            Object.DestroyImmediate(map.plDay);
            map.plDay = null;
        }

        if (zone == EClass._zone) {
            zone.RefreshPlaylist();
            EClass.Sound.StopBGM();
            zone.RefreshBGM();
        }
    }
}
