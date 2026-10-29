using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     The moongate, the landing portal and the sky helm act on "the player": replayed on the host for a guest, they
///     moved the host or opened the list on its screen. The host does the world part (the zone of a user map, the
///     gate zone), the guest picks on its own screen and travels alone (llm-council 2026-10-24). Each a host option:
///     off, a guest's use does nothing
/// </summary>
[HarmonyPatch]
internal static class GateItems
{
    internal const string Moongate = "moongate";
    internal const string MoongateEx = "moongate_ex";
    internal const string SkyHelm = "sky_helm";

    /// <summary>
    ///     The moongates the host draws a map with for a guest, and for whom: where they would take the host, the
    ///     guest goes instead
    /// </summary>
    private static readonly Dictionary<TraitMoongate, int> _drawing = [];

    private static int _toldGate = -1;

    /// <summary>
    ///     A guest uses one of them: null when it is not one, else whether the game's own use runs here
    /// </summary>
    internal static bool? OnUse(Trait trait, Chara user)
    {
        if (NetSession.Instance.Transport is not ElinNetClient client || ElinDelta.IsApplying || !user.IsPC ||
            trait is not (TraitMoongate or TraitLandingPortal or TraitSkyHelm)) {
            return null;
        }

        var rules = NetSession.Instance.Rules;
        if (!(trait switch {
                TraitMoongate => rules.GuestMoongate,
                TraitLandingPortal => rules.GuestLandingPortal,
                _ => rules.GuestSkyHelm,
            })) {
            Msg.SayNothingHappen();
            return false;
        }

        switch (trait) {
            // the gate zone is the host's, told to everyone (GateZoneDelta): the game's own trip, this player's alone
            case TraitLandingPortal:
            // outside survival the game only says nothing happens
            case TraitSkyHelm when !EClass.game.IsSurvival:
            case TraitMoongate when EClass.core.version.demo:
                return true;
            case TraitMoongate when EClass.pc.burden.GetPhase() == 4:
                Msg.Say("returnOverweight");
                return false;
        }

        // (straight to the host, also from a map of one's own)
        client.SendWhileAway(new GateItemRequest {
            Item = trait switch {
                TraitMoongateEx => MoongateEx,
                TraitMoongate => Moongate,
                _ => SkyHelm,
            },
        });
        return false;
    }

    internal static void OnRequest(ElinNetHost host, GateItemRequest request)
    {
        var peer = request.OriginPeer;
        var rules = NetSession.Instance.Rules;

        if (request.Item is Moongate or MoongateEx && rules.GuestMoongate) {
            // the moongate that lists the maps: the host's, the guest picks
            if (request.Item == MoongateEx) {
                var maps = SavedMaps();
                if (request.MapId is null && maps.Count > 0) {
                    host.SendDeltaTo(peer, new GateItemAnswer {
                        Item = MoongateEx,
                        Ids = maps.Select(m => m.id).ToList(),
                        Names = maps.Select(m => m.name).ToList(),
                    });
                    return;
                }

                if (maps.Find(m => m.id == request.MapId) is { } map) {
                    var trait = new TraitMoongate();
                    _drawing[trait] = peer;
                    trait.LoadMap(map);
                    return;
                }
            } else {
                // the one that draws a map from the net, downloaded by the host
                Draw(host, peer).Forget();
                return;
            }
        }

        if (request.Item == SkyHelm && rules.GuestSkyHelm && EClass.game.IsSurvival) {
            var survival = EClass.game.survival;
            var gate = survival.gateZone?.uid ?? 0;
            if (request.ZoneUid == 0) {
                survival.RefreshGateZones();
                var zones = survival.listGateZone;
                host.SendDeltaTo(peer, new GateItemAnswer {
                    Item = SkyHelm,
                    Ids = zones.Select(z => z.uid.ToString()).ToList(),
                    Costs = zones.Select(z => z.GetCostSkyTravel()).ToList(),
                    Gate = gate,
                    Zones = zones.Select(SpatialGenDelta.Create).ToList(),
                });
                return;
            }

            // another player moved the gate since that list was shown: refused at no cost
            if (request.Gate == gate && survival.listGateZone.Find(z => z.uid == request.ZoneUid) is { } pick) {
                survival.gateZone = pick;
                survival.listGateZone.Clear();
                host.SendDeltaTo(peer, new GateItemAnswer {
                    Item = SkyHelm,
                    ZoneUid = pick.uid,
                    Cost = pick.GetCostSkyTravel(),
                });
                return;
            }
        }

        // refused: nothing happens, nothing is paid
        host.SendDeltaTo(peer, new GateItemAnswer {
            Item = request.Item,
        });
    }

    private static async UniTaskVoid Draw(ElinNetHost host, int peer)
    {
        var trait = new TraitMoongate();
        _drawing[trait] = peer;
        try {
            await trait.UseMoongate();
        } finally {
            // no map came of it
            if (_drawing.Remove(trait)) {
                host.SendDeltaTo(peer, new GateItemAnswer {
                    Item = Moongate,
                });
            }
        }
    }

    private static List<MapMetaData> SavedMaps()
    {
        return new TraitMoongate().ListSavedUserMap()
            .Where(m => !(EClass.core.config.net.noAdult && m.tag.HasTag("adult")))
            .OrderBy(m => m.date)
            .ToList();
    }

    /// <summary>
    ///     TraitMoongate.LoadMap without its weight check: the one that goes was checked in its own game
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitMoongate), nameof(TraitMoongate.LoadMap))]
    internal static bool OnLoadMap(TraitMoongate __instance, MapMetaData m)
    {
        if (!_drawing.ContainsKey(__instance)) {
            return true;
        }

        var zone = EClass.game.spatials.Find((Zone_User z) => z.idUser == m.id);
        if (zone is null) {
            zone = (Zone_User)SpatialGen.Create("user", EClass.world.region, true);
            zone.path = m.path;
            zone.idUser = m.id;
            zone.dateExpire = EClass.world.date.GetRaw(1);
            zone.name = m.name;
            if (m.underwater) {
                zone.elements.SetBase(3606, 1);
            }
        }

        __instance.MoveZone(zone);
        return false;
    }

    /// <summary>
    ///     The zone is made in the host's world, the guest goes there on its own trip
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitMoongate), nameof(TraitMoongate.MoveZone))]
    internal static bool OnMoveZone(TraitMoongate __instance, Zone zone)
    {
        if (!_drawing.Remove(__instance, out var peer)) {
            return true;
        }

        if (zone is Zone_User user && NetSession.Instance.Transport is ElinNetHost host) {
            host.SendDeltaTo(peer, new GateItemAnswer {
                Item = Moongate,
                ZoneUid = user.uid,
                Zones = [SpatialGenDelta.Create(user)],
                // (a map the host visited goes with the lease, the file is for one never generated)
                Map = File.Exists(user.path) ? LZ4Bytes.CreateFromFile(user.path) : null,
                FileName = Path.GetFileName(user.path),
                IdUser = user.idUser,
                Name = user.name,
                Underwater = user.IsUnderwater,
            });
        }

        return false;
    }

    internal static void OnAnswer(ElinNetBase net, GateItemAnswer answer)
    {
        foreach (var known in answer.Zones ?? []) {
            known.Apply(net);
        }

        switch (answer.Item) {
            case MoongateEx when answer.Ids is { Count: > 0 } ids && answer.Names is { } names:
                EClass.ui.AddLayer<LayerList>().SetList2(Enumerable.Range(0, ids.Count).ToList(), i => names[i],
                        (i, _) => Ask(new() { Item = MoongateEx, MapId = ids[i] }), (_, _) => { })
                    .SetSize(500f)
                    .SetTitles("wMoongate");
                return;
            case Moongate or MoongateEx when EClass.game.spatials.Find(answer.ZoneUid) is Zone_User zone:
                // in the session's temp folder, under a name of letters and digits: written among the player's own
                // user maps, the other game's file replaced any of them (or the game's cache.txt) and was then
                // picked by a moongate in a game played alone, read with any type its text names (security audit
                // 2026-10-29)
                var name = new string(Path.GetFileNameWithoutExtension(answer.FileName ?? zone.idUser ?? "")
                    .Where(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-').Take(64).ToArray());
                if (name.Length == 0 || !WorldCopyStore.IsSafePath(name)) {
                    name = "map";
                }

                var path = Path.Combine(ResourceFetch.TempFolder, "UserZones", name + ".z");
                answer.Map?.DecompressToFile(path);
                zone.path = path;
                zone.idUser = answer.IdUser;
                zone.name = answer.Name;
                if (answer.Underwater) {
                    zone.elements.SetBase(3606, 1);
                }

                // the game's own way through the gate, from where this player stands
                new TraitMoongate().MoveZone(zone);
                return;
            case SkyHelm when answer.Ids is { } uids && answer.Costs is { } costs:
                ShowSkyList(uids.Select(int.Parse).ToList(), costs, answer.Gate);
                return;
            case SkyHelm when answer.ZoneUid != 0: {
                // what the game does for the player who picked, the rest is the host's (the gate zone, told to all)
                using var simulate = ElinDelta.Simulate();
                EClass.ui.FlashCover(1f, 0.5f, 1f, null, null, Color.black);
                EClass.pc.ModCurrency(-answer.Cost, "ticket_sky");
                EClass.game.survival.gateZone = EClass.game.spatials.Find(answer.ZoneUid);
                SE.Play("ship_bell");
                EClass.pc.pos.TalkWitnesses(EClass.pc, "ahoy", 6, WitnessType.everyone, _ => true);
                EClass.pc.ModExp(240, answer.Cost * 20);
                return;
            }
        }

        Msg.SayNothingHappen();
    }

    private static void Ask(GateItemRequest request)
    {
        if (NetSession.Instance.Transport is ElinNetClient client) {
            client.SendWhileAway(request);
        }
    }

    /// <summary>
    ///     TraitSkyHelm.OnUse with the host's list: the pick is asked of the host, paid once it says yes
    /// </summary>
    private static void ShowSkyList(List<int> uids, List<int> costs, int gate)
    {
        var ticket = EClass.pc.GetCurrency("ticket_sky");
        var zones = uids.Select(uid => EClass.game.spatials.Find(uid)).ToList();
        EClass.ui.AddLayer<LayerList>().ManualList(delegate(UIList list, LayerList l) {
                list.moldItem = Resources.Load<ButtonElement>("UI/Element/Button/ButtonSkyTravel").transform;
                list.callbacks = new UIList.Callback<int, ButtonElement> {
                    onClick = delegate(int i, ButtonElement b) {
                        if (costs[i] > 0 && costs[i] > ticket) {
                            SE.Beep();
                            return;
                        }

                        EClass.ui.CloseLayers();
                        Ask(new() { Item = SkyHelm, ZoneUid = uids[i], Gate = gate });
                    },
                    onRedraw = delegate(int i, ButtonElement b, int _) {
                        var cost = costs[i];
                        b.mainText.text = zones[i]?.NameWithDangerLevel ?? "?";
                        b.subText2.text = (cost == 0 ? "-" : cost.ToString()).TagColor(() => ticket >= cost || cost == 0);
                        b.RebuildLayout();
                    },
                    onInstantiate = delegate { },
                    onList = delegate {
                        for (var i = 0; i < uids.Count; i++) {
                            list.Add(i);
                        }
                    },
                };
            }).SetSize()
            .SetTitles("wSkyTravel")
            .windows[0].AttachCurrency().Build(new UICurrency.Options {
                ticketSky = true,
            });
    }

    /// <summary>
    ///     On the host: the gate zone changed (its own sky helm, a guest's), everyone is told
    /// </summary>
    internal static void TellGateZone(ElinNetHost host)
    {
        if (NetSession.Instance.Transport != host || EClass.game is not { IsSurvival: true } game) {
            return;
        }

        var gate = game.survival.gateZone;
        var uid = gate?.uid ?? 0;
        if (uid != _toldGate) {
            _toldGate = uid;
            host.Delta.AddRemote(new GateZoneDelta {
                ZoneUid = uid,
                Zone = gate is null ? null : SpatialGenDelta.Create(gate),
            });
        }
    }
}
