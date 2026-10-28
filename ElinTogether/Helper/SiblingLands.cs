using System.Collections.Generic;
using System.Linq;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-28 section 4, host option "Several lands per tile": the world map has one zone per tile, a land
///     of another team there used to close the tile to everyone else. A land is a Zone_Field child of the region with an
///     x,y: nothing stops two from sharing one, only the map cell (one zone, one icon) and the places that ask the region
///     for "the" zone at x,y assume one. So a sibling is a second claimed Zone_Field at the same x,y, the cell keeps the
///     first one's icon, and walking onto the tile lists the lands. Teams.Zones is per zone uid, nothing changes there
/// </summary>
[HarmonyPatch]
internal static class SiblingLands
{
    internal static bool On => Teams.BasesOwned && NetSession.Instance.Rules.SiblingLands;

    /// <summary>
    ///     The lands of a world-map tile: a town or a claimed field. A wild field is not one, it goes away when nobody
    ///     is in it (Region.OnLoad)
    /// </summary>
    internal static List<Zone> LandsAt(int x, int y)
    {
        var region = EClass.world.region;
        return region.children.OfType<Zone>().Where(z => z.x == x && z.y == y && !z.destryoed && !z.IsInstance &&
                                                        (z is not Zone_Field || z.IsPCFaction)).ToList();
    }

    /// <summary>
    ///     A land a player made its own: a claimed field or the start site (the first base of most worlds), not a town
    /// </summary>
    private static bool IsPlayerLand(Zone zone)
    {
        return zone is Zone_Field or Zone_StartSite;
    }

    /// <summary>
    ///     A deed read where the player stands on the world map: a tile that already holds a land is the case this
    ///     option is for
    /// </summary>
    internal static bool ReadsOnLand(out List<Zone> lands, out Point at)
    {
        lands = [];
        at = EClass.pc.pos;
        if (!On || !EClass._zone.IsRegion) {
            return false;
        }

        at = EClass.pc.pos.ToRegionPos();
        lands = LandsAt(at.x, at.z);
        return lands.Count > 0;
    }

    /// <summary>
    ///     Why that player may not make another plot on that tile, null when it may: read again by the host from what it
    ///     holds, as the game's own read (TraitDeed.OnRead) and ClaimZoneDelta do
    /// </summary>
    internal static string? WhyNot(Chara sender, int x, int y)
    {
        if (!On) {
            return "several lands per tile is off";
        }

        if (NetSession.Instance.Rules.HostManagesBase) {
            return "only the host manages the base";
        }

        if (!sender.things.List(t => t.trait is TraitDeed, true).Any()) {
            return "the sender holds no deed";
        }

        if (Teams.LandCapReached(sender)) {
            return "the team holds as many lands as the host allows";
        }

        var lands = LandsAt(x, y);
        return lands.Count == 0 || lands.Any(l => !IsPlayerLand(l)) ? $"the tile {x},{y} holds no land of players" : null;
    }

    /// <summary>
    ///     The second plot: a wild field at the same x,y, which the deed then claims the way it claims any field (the
    ///     reader walks in and reads it again). One at a time per tile: a plot nobody claimed is reused
    /// </summary>
    internal static Zone? Create(int x, int y)
    {
        var region = EClass.world.region;
        if (region.children.OfType<Zone_Field>().FirstOrDefault(z => z.x == x && z.y == y && !z.IsPCFaction && !z.destryoed) is { } plot) {
            return plot;
        }

        var primary = LandsAt(x, y).FirstOrDefault();
        var zone = region.CreateZone(new Point(x, y));
        if (zone is not null && primary is not null) {
            zone._dangerLv = primary._dangerLv;
        }

        return zone;
    }

    /// <summary>
    ///     The step onto a zone from the world map, the way the game takes it (Player.EnterLocalZone)
    /// </summary>
    internal static void Enter(Zone zone, ZoneTransition? trans = null)
    {
        EClass.pc.MoveZone(zone, trans ?? new ZoneTransition {
            state = zone.RegionEnterState == ZoneTransition.EnterState.Dir
                ? ZoneTransition.DirToState(EClass.pc.GetCurrentDir())
                : zone.RegionEnterState,
        });
    }

    /// <summary>
    ///     Walking onto a tile with two lands or more (Player.EnterLocalZone asks the region for "the" zone there and takes
    ///     the first): a list of the lands, the team that holds each and what that land lets this player do, a closed one
    ///     greyed. The choice is the zone's uid, so the travel and the lease request name that zone (the host reads it
    ///     again with Teams.Allowed, see ElinNetHost.LeaseZone). One land: the game as it was
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.EnterLocalZone), typeof(Point), typeof(ZoneTransition), typeof(bool), typeof(Chara))]
    private static bool OnEnterLocalZone(Point p, ZoneTransition? trans, bool encounter)
    {
        if (encounter || !On || ElinDelta.IsApplying) {
            return true;
        }

        var map = EClass.scene.elomap;
        var lands = EClass._zone.IsRegion ? LandsAt(p.x + map.minX, p.z + map.minY) : LandsAt(p.x, p.z);
        if (lands.Count < 2) {
            return true;
        }

        var shut = new HashSet<Zone>(lands.Where(l => !Teams.Allowed(EClass.pc, l, Teams.Visit)));
        EClass.ui.AddLayer<LayerList>().SetNoSound().SetList2(lands.OrderBy(l => l.uid).ToList(), Label, (zone, _) => {
            if (shut.Contains(zone)) {
                SE.Beep();
                Msg.Say("emp_land_closed".lang());
            } else if (zone.IsClosed) {
                Msg.Say("zoneClosed");
            } else {
                Enter(zone, trans);
            }
        }, (zone, item) => {
            if (shut.Contains(zone)) {
                item.button1.mainText.color = new UnityEngine.Color(0.55f, 0.55f, 0.55f);
            }
        }).SetSize(480f).SetHeader("emp_sibling_pick");
        return false;
    }

    /// <summary>
    ///     The map cell holds one zone and one icon: the first land set there keeps it, a sibling claimed or loaded after
    ///     it (a higher uid) does not take the tile's icon (Region.OnLoad and Zone.ClaimZone both set the cell of their zone)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EloMap), nameof(EloMap.SetZone))]
    private static bool OnSetZone(EloMap __instance, int gx, int gy, Zone? z)
    {
        // (the older land keeps it even when a game sets the younger one first: a guest in the plot is told of it
        // before its world map loads Red's land, bench 2026-10-28 ICON check)
        return z is null || !On || __instance.GetCell(gx, gy) is not { zone: { } there } || there == z || there.destryoed ||
               there.x != gx || there.y != gy || z.uid < there.uid;
    }

    /// <summary>
    ///     A land left a tile (relocation clears the cell it left): the land that still stands there gets its icon back
    /// </summary>
    internal static void Refresh(int x, int y)
    {
        var map = EClass.scene.elomap;
        if (On && map.w > 0 && LandsAt(x, y).FirstOrDefault() is { } first) {
            map.SetZone(x, y, first, EClass._zone.IsRegion);
        }
    }

    private static string Label(Zone zone)
    {
        return $"{Teams.NameOf(Teams.TeamOfZone(zone))} - {zone.Name} ({$"emp_ui_team_rule_{Teams.LevelOn(EClass.pc, zone)}".lang()})";
    }

    /// <summary>
    ///     The answer of the host: the plot exists, it is announced to every game just before (SpatialGenDelta), step in
    ///     as soon as this game knows it
    /// </summary>
    internal static void EnterWhenKnown(int zoneUid, int tries = 20)
    {
        if (EClass.game.spatials.Find(zoneUid) is Zone zone) {
            Enter(zone);
        } else if (tries > 0) {
            CoroutineHelper.Deferred(() => EnterWhenKnown(zoneUid, tries - 1), 10);
        }
    }

    /// <summary>
    ///     A deed read on a tile that holds a land: the question, then another plot there. The host's own player makes
    ///     it; a guest asks the host (<see cref="SiblingLandDelta" />), alone on its world map as well
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(TraitDeed), nameof(TraitDeed.OnRead))]
    private static bool OnReadDeed(Chara c)
    {
        if (!c.IsPC || ElinDelta.IsApplying || !ReadsOnLand(out var lands, out var at)) {
            return true;
        }

        if (lands.Any(l => !IsPlayerLand(l))) {
            Msg.Say("invalidClaimZone");
            return false;
        }

        var world = NetSession.Instance.Transport as ElinNetHost;
        if (world is null && NetSession.Instance.Rules.HostManagesBase) {
            Patches.RemoteBasePaidPatch.Refuse(true);
            return false;
        }

        if (Teams.LandCapReached(c)) {
            Msg.Say("emp_team_land_cap".Loc(EmpConfig.Server.LandsPerTeam.Value));
            return false;
        }

        Dialog.YesNo("emp_sibling_ask".lang(), () => {
            if (world is { IsZoneSession: false }) {
                if (Create(at.x, at.z) is { } zone) {
                    Enter(zone);
                }

                return;
            }

            var ask = new SiblingLandDelta { X = at.x, Y = at.z };
            if (NetSession.Instance is { IsAway: true, Transport: ElinNetClient main }) {
                main.SendWhileAway(ask);
            } else if (NetSession.Instance.Connection is ElinNetClient client) {
                client.Delta.AddRemote(ask);
            }
        });
        return false;
    }
}
