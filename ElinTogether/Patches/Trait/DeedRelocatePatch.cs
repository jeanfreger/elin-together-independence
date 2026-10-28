using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The relocation deed (TraitDeedRelocate.OnRead), in a session: the game's own steps up to the yes (on the world
///     map, a free place, the list of bases, the question), then the base moves in the world the host keeps, for
///     everyone (<see cref="BaseRelocateDelta" />). A guest's yes is a request: alone on its world map, its own copy
///     moved the base and the host never saw it (hunt 6 item 2). The host's yes tells the players too. The list
///     holds the bases of the reader's team only (team lands). Alone: the game's own
/// </summary>
[HarmonyPatch(typeof(TraitDeedRelocate), nameof(TraitDeedRelocate.OnRead))]
internal static class DeedRelocatePatch
{
    [HarmonyPrefix]
    internal static bool OnRead(TraitDeedRelocate __instance, Chara c)
    {
        if (!NetSession.Instance.HasActiveConnection || !c.IsPC || ElinDelta.IsApplying || !EClass._zone.IsRegion) {
            return true;
        }

        // the game's own refusals, its own words
        var cell = EClass.scene.elomap.GetCell(EClass.pc.pos);
        if (cell == null || (cell.zone != null && (cell.zone is not Zone_Field || cell.zone.IsPCFaction))) {
            return true;
        }

        var world = NetSession.Instance.Transport as ElinNetHost;
        if (world is null && NetSession.Instance.Rules.HostManagesBase) {
            RemoteBasePaidPatch.Refuse(true);
            return false;
        }

        var deed = __instance.owner.Thing;
        var at = EClass.pc.pos.ToRegionPos();
        var bases = EClass.pc.faction.GetChildren().Where(b => Teams.CanTouch(EClass.pc, b.owner)).ToList();
        EClass.ui.AddLayer<LayerList>().SetNoSound().SetList2(bases, b => b.owner.NameWithLevel, (b, _) => {
            var zone = b.owner;
            Dialog.YesNo("dialog_relocateLand", () => {
                if (world is { IsZoneSession: false }) {
                    BaseRelocateDelta.Relocate(world, EClass.pc, zone, deed, at.x, at.z);
                    return;
                }

                var ask = new BaseRelocateDelta { ZoneUid = zone.uid, X = at.x, Y = at.z, DeedUid = deed.uid };
                // on the world map a guest is alone (away): the link to the host of the world still carries it
                if (NetSession.Instance is { IsAway: true, Transport: ElinNetClient main }) {
                    main.SendWhileAway(ask);
                } else if (NetSession.Instance.Connection is ElinNetClient client) {
                    client.Delta.AddRemote(ask);
                }
            });
        }, null).TryShowHint("h_relocate");
        return false;
    }
}
