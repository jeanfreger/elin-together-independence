using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Helper.Extensions;
using ElinTogether.LangMod;
using ElinTogether.Models;
using UnityEngine;

namespace ElinTogether.Net;

/// <summary>
///     Independent travel: go to a zone the host is not in, simulate it locally, hand it back when leaving <br />
///     A zone another player simulates is joined as a guest of its zone session, see NetSession.ZoneSession
/// </summary>
internal partial class ElinNetClient
{
    private (Zone Zone, ZoneTransition Transition)? _pendingTravel;

    /// <summary>
    ///     Lease granted while on the host map (or as a guest), waiting for <see cref="ZoneLeaseDepart" />
    /// </summary>
    private ZoneLeaseGrant? _pendingGrant;

    /// <summary>
    ///     Zones this client created itself (world map fields, dungeon floors...), unknown to the host until leased
    /// </summary>
    private readonly HashSet<Zone> _localZones = [];

    private float _nextCheckpoint;

    /// <summary>
    ///     Zone of the host while away, kept up to date from its zone changes
    /// </summary>
    private int _hostZoneUid = -1;

    internal void OnLocalZoneCreated(Zone zone)
    {
        _localZones.Add(zone);
    }

    /// <summary>
    ///     Away zone handed back, waiting for the host save probe
    /// </summary>
    private bool _rejoining;

    /// <summary>
    ///     Guest whose zone owner left: how long we wait for the host to hand the zone over before going back
    /// </summary>
    private const float HandoffWaitSeconds = 30f;

    private float _handoffDeadline;

    /// <summary>
    ///     Zone the host moved to while we were leaving its map to stay behind, see StayAsGuest
    /// </summary>
    private int? _hostZoneAfterDeparture;

    /// <summary>
    ///     The local player moves to a zone other than the one it replicates from the host <br />
    ///     Returns true to let the move happen now
    /// </summary>
    internal bool TryTravel(Zone zone, ZoneTransition transition)
    {
        // moving into the zone just granted
        if (Session.AwayZone == zone && !_rejoining) {
            return true;
        }

        if (!Session.IsAway && (pc.isDead || player.deathZoneMove)) {
            // the host revives remote players where they fell, see CharaReviveEvent. Only that move is dropped:
            // the game clears the mark inside the move we skip, left set it refused every travel that followed
            player.deathZoneMove = false;
            return false;
        }

        // leaving an instance, Elin sends the player back where it started, see Chara.MoveZone
        if (pc.currentZone?.instance is { } instance) {
            zone = game.spatials.Find(instance.uidZone) ?? pc.homeZone;

            // out of a bout with an adventurer: the host puts its adventurer back (AdventurerBout)
            if (instance is ZoneInstanceBout bout) {
                AdventurerBout.Leave(bout);
            }

            // the zone of our quest, run by the host who came along: it takes everyone out, and tells us how
            // the quest went on the way, see ElinNetHost.LeaveAccompaniedZone
            if (!Session.IsAway && PersonalQuests.InstancesEnabled &&
                instance is ZoneInstanceRandomQuest { uidQuest: not 0 } ours &&
                game.quests.list.Exists(q => q.uid == ours.uidQuest && PersonalQuests.IsPersonal(q))) {
                Delta.AddRemote(new QuestFollowDelta {
                    Kind = QuestFollowDelta.Leave,
                    Name = pc.Name,
                    ZoneUid = pc.currentZone.uid,
                });
                return false;
            }

            // how the quest went is settled now: where we arrive may be someone else's map
            PersonalQuests.LeaveInstance(pc.currentZone);
        }

        if (!Session.Rules.AllowIndependentTravel) {
            EmpPop.Debug("emp_party_gather".lang());
            return false;
        }

        if (_pendingTravel is not null || _rejoining) {
            // a request is in flight
            return false;
        }

        // a guest leaves the zone session first, its owner applies our last actions, see OnGuestLeftZone
        if (Session.IsGuest && Session.ZoneSession is ElinNetClient zoneClient) {
            _pendingTravel = (zone, transition);
            StopWalk();
            zoneClient.LeaveZoneSession(Session.AwayZone!.uid);
            EmpPop.Debug("emp_travel_requesting".lang());
            return false;
        }

        if (Session.IsAway && zone.uid == _hostZoneUid && !_localZones.Contains(zone)) {
            EmpLog.Information("Returning from {AwayZone} to host zone {ZoneFullName}",
                Session.AwayZone!.ZoneFullName, zone.ZoneFullName);

            StopWalk();
            SendRejoin(ZoneArrival.Create(zone.uid, transition));
            EmpPop.Debug("emp_travel_returning".lang());
            return false;
        }

        StopWalk();
        RequestLease(zone, transition);
        return false;
    }

    /// <summary>
    ///     The game stops the walk at the step onto the stairs (Chara.MoveZone, skipped while the host answers): kept
    ///     walking, the player landed in front of the stairs it came by and stepped back in (GitHub issue 10, Vernis
    ///     mine)
    /// </summary>
    private static void StopWalk()
    {
        pc.ai?.Cancel();
        player.nextMove = UnityEngine.Vector2.zero;
        EInput.Consume(consumeAxis: true);
    }

    /// <summary>
    ///     When this game last arrived on a map it was handed: the stairs there do not take it in by a step for a
    ///     moment, see <see cref="Patches.ArrivalStairsPatch" />
    /// </summary>
    internal static float ArrivedAt { get; private set; } = float.MinValue;

    private void RequestLease(Zone zone, ZoneTransition transition)
    {
        EmpLog.Information("Requesting zone lease {ZoneFullName}",
            zone.ZoneFullName);

        _pendingTravel = (zone, transition);
        Host.Send(ZoneLeaseRequest.Create(zone, _localZones.Contains(zone)));
        EmpPop.Debug("emp_travel_requesting".lang());
    }

    /// <summary>
    ///     Net event: Travel accepted, this client simulates the zone from now on (or joins its owner)
    /// </summary>
    private void OnZoneLeaseGrant(ZoneLeaseGrant grant)
    {
        if (grant.Handoff) {
            TakeOverZone(grant);
            return;
        }

        if (_pendingTravel is not { } travel || travel.Zone.uid != grant.RequestedUid) {
            EmpLog.Warning("Received unexpected lease for zone {ZoneUid}", grant.RequestedUid);
            Host.Send(new ZoneLeaseDecline {
                ZoneUid = grant.ZoneUid,
            });
            return;
        }

        AdoptHostUid(travel.Zone, grant.ZoneUid);

        if (grant.Guest) {
            // leave where we are (handing our zone back if we hold one, or only our character when we were
            // a guest: the host forwards it to the owner of the zone we go to), then that owner expects us,
            // see ZoneLeaseDepart
            if (Session.IsAway) {
                HandBackAwayZone(false);
            } else if (!Session.IsAway) {
                FlushDeltasNow();
            }

            Host.Send(new ZoneLeaseAck {
                ZoneUid = grant.ZoneUid,
                Arrival = ZoneArrival.Create(grant.ZoneUid, travel.Transition),
            });
            _pendingGrant = grant;
            StopWorldStateUpdate();
            return;
        }

        if (Session.IsAway) {
            _pendingTravel = null;

            // hand back the zone we are leaving, while it is still active; a guest holds none and only
            // brings its character up to date on the host
            HandBackAwayZone(false);

            TravelTo(travel.Zone, travel.Transition, grant);
            return;
        }

        // leaving the host map: everything done there reaches the host first, then we wait for
        // the results of those actions before going, see ZoneLeaseDepart
        FlushDeltasNow();
        Host.Send(new ZoneLeaseAck {
            ZoneUid = grant.ZoneUid,
        });
        _pendingGrant = grant;
        StopWorldStateUpdate();
    }

    /// <summary>
    ///     Acknowledged a departure: the host already took us off its map and its player list,
    ///     only the results of our last actions still matter until ZoneLeaseDepart
    /// </summary>
    private bool IsAwaitingDeparture => _pendingGrant is not null && !Session.IsAway;

    /// <summary>
    ///     Net event: The host applied our last actions on its map and sent their results, now we go
    /// </summary>
    private void OnZoneLeaseDepart(ZoneLeaseDepart depart)
    {
        // the zone we visit changed hands, its new owner expects us, see HandOverZone
        if (_pendingTravel is null && _pendingGrant is null && depart.Guest is { } moved &&
            Session.IsGuest && Session.AwayZone is { } here && here.uid == depart.ZoneUid) {
            _handoffDeadline = 0;
            JoinZoneSession(here, moved);
            return;
        }

        if (_pendingTravel is not { } travel || _pendingGrant is not { } grant || grant.ZoneUid != depart.ZoneUid) {
            EmpLog.Warning("Received unexpected departure for zone {ZoneUid}", depart.ZoneUid);
            return;
        }

        _pendingTravel = null;
        _pendingGrant = null;

        // the results arrived right before this packet, apply them while still synced
        WorldStateDeltaProcess();

        if (depart.Guest is { } address) {
            JoinZoneSession(travel.Zone, address);
            return;
        }

        TravelTo(travel.Zone, travel.Transition, grant);
    }

    /// <summary>
    ///     A zone created here gets the uid the host assigned, nothing refers to it by uid yet
    /// </summary>
    private void AdoptHostUid(Zone zone, int uid)
    {
        _localZones.Remove(zone);

        if (zone.uid == uid) {
            return;
        }

        var spatials = game.spatials;
        spatials.uidNext = Math.Max(spatials.uidNext, uid + 1);

        if (spatials.map.TryGetValue(uid, out var other) && other != zone) {
            spatials.map.Remove(uid);

            if (other.id == zone.id && other.x == zone.x && other.y == zone.y) {
                // host copy announced through SpatialGenDelta before the grant
                other.parent?.RemoveChild(other);
            } else {
                // another zone created here took that uid
                spatials.AssignUID(other);
            }
        }

        EmpLog.Debug("Zone {ZoneFullName} created locally as {LocalUid}, host uid {ZoneUid}",
            zone.ZoneFullName, zone.uid, uid);

        // what led there (stairs, a tent) points at the zone by number
        var local = zone.uid;
        foreach (var thing in _map.things.Concat(pc.things.Flatten().OfType<Thing>())) {
            if (thing.c_uidZone == local) {
                thing.c_uidZone = uid;
            }
        }

        spatials.map.Remove(zone.uid);
        zone.uid = uid;
        spatials.map[uid] = zone;

        // the zone of a quest sits on the tile of the town it comes from, without taking its place on the map
        if (zone.parent is Region region && !zone.IsInstance) {
            region.elomap.SetZone(zone.x, zone.y, zone, true);
        }
    }

    private void TravelTo(Zone zone, ZoneTransition transition, ZoneLeaseGrant grant)
    {
        if (grant.Map is not null) {
            if (zone.map is not null) {
                // stale copy from an earlier visit alongside the host
                zone.UnloadMap();
            }

            ZoneLeaseState.WriteMap(zone, grant.Map);
        }

        ZoneLeaseState.ApplyState(zone, grant.ZoneState, grant.IdCurrentSubset);
        // cards created here get uids the host does not use
        game.cards.uidNext = Math.Max(game.cards.uidNext, grant.UidRangeStart);
        AdoptQuestUidRange(grant);

        EnterAway(zone);
        Session.IsGuest = false;

        EmpLog.Information("Leased zone {ZoneFullName}, uid range from {UidRangeStart}, map {HasMap}",
            zone.ZoneFullName, grant.UidRangeStart, grant.Map is not null);

        ArrivedAt = Time.realtimeSinceStartup;
        pc.MoveZone(zone, transition);
    }

    /// <summary>
    ///     Quests created here (residents of the zone we simulate) get uids the host does not use
    /// </summary>
    private static void AdoptQuestUidRange(ZoneLeaseGrant grant)
    {
        if (grant.QuestUidRangeStart > 0) {
            game.quests.uid = Math.Max(game.quests.uid, grant.QuestUidRangeStart);
        }
    }

    /// <summary>
    ///     Another player simulates the zone: connect to its zone session, it sends its world and the zone
    /// </summary>
    private void JoinZoneSession(Zone zone, ZoneGuestAddress address)
    {
        EnterAway(zone);
        Session.IsGuest = true;

        EmpLog.Information("Joining the zone session of {RemoteIdentity} in {ZoneFullName}",
            address.HostUser, zone.ZoneFullName);

        var session = Session.InitializeZoneSession<ElinNetClient>();
        if (IsLocalConnection) {
            session.ConnectLocalPort((ushort)address.Port);
        } else {
            session.ConnectSteamUser(address.HostUser);
        }
    }

    /// <summary>
    ///     Zone session client: leaving the zone, send our last actions and wait for their results
    /// </summary>
    internal void LeaveZoneSession(int zoneUid)
    {
        FlushDeltasNow();
        Host.Send(new ZoneGuestLeave {
            ZoneUid = zoneUid,
        });
    }

    /// <summary>
    ///     Zone session client, net event: the owner applied our last actions, we may go
    /// </summary>
    private void OnZoneGuestLeft(ZoneGuestLeft left)
    {
        if (!IsZoneSession) {
            return;
        }

        WorldStateDeltaProcess();
        (Session.Transport as ElinNetClient)?.OnGuestLeftZone();
    }

    /// <summary>
    ///     Out of the zone session, go on with the travel that started it
    /// </summary>
    internal void OnGuestLeftZone()
    {
        Session.RemoveZoneSession();

        if (_pendingTravel is not { } travel) {
            return;
        }

        if (travel.Zone.uid == _hostZoneUid && !_localZones.Contains(travel.Zone)) {
            _pendingTravel = null;
            SendRejoin(ZoneArrival.Create(travel.Zone.uid, travel.Transition));
            return;
        }

        RequestLease(travel.Zone, travel.Transition);
    }

    /// <summary>
    ///     The zone session ended on its own: the zone owner left or dropped, back to the host
    /// </summary>
    internal void OnZoneSessionEnded(ElinNetBase session, string reason)
    {
        if (session is not ElinNetClient || !Session.IsGuest) {
            return;
        }

        // meanwhile we play on alone: the host gives the zone to one of us (or calls us back),
        // see TakeOverZone, and only if it stays silent we go back
        EmpLog.Warning("Zone session ended ({Reason}), waiting for the host to hand the zone over",
            reason);

        _pendingTravel = null;
        _handoffDeadline = Time.realtimeSinceStartup + HandoffWaitSeconds;
    }

    private void UpdateHandoffWait()
    {
        if (_handoffDeadline <= 0 || Time.realtimeSinceStartup < _handoffDeadline) {
            return;
        }

        _handoffDeadline = 0;

        if (!Session.IsGuest || Session.ZoneSession is not null || _rejoining) {
            return;
        }

        EmpLog.Warning("Nobody took the zone over, returning to the host");
        EmpPop.Information("emp_travel_zone_closed".lang());
        SendRejoin();
    }

    /// <summary>
    ///     Net event: the owner of the zone we visit left it, we simulate it from now on, as it is here
    /// </summary>
    private void TakeOverZone(ZoneLeaseGrant grant)
    {
        // either we visit the zone of a player who left it, or we stand on the host map and the host leaves it
        var visiting = Session.IsGuest && Session.AwayZone is { } away && away.uid == grant.ZoneUid && _zone == away;
        var withHost = !Session.IsAway && _zone?.uid == grant.ZoneUid;

        if (withHost && grant.Guest) {
            StayAsGuest(grant);
            return;
        }

        if ((!visiting && !withHost) || _rejoining) {
            // gone meanwhile, the host drops that lease when we rejoin
            EmpLog.Warning("Handed zone {ZoneUid} while not in it (here {Here}, away {Away}, rejoining {Rejoining})",
                grant.ZoneUid, _zone?.uid, Session.AwayZone?.uid, _rejoining);

            // not away and not on our way back: we still follow the host (that map was not on our screen yet),
            // but the host took our character off its map with this message and answers nothing we do from
            // here. Back to it the way a player away comes back: it drops that lease and puts us on its map
            if (!Session.IsAway && !_rejoining && core.IsGameStarted) {
                SendRejoin();
            }

            return;
        }

        if (_zone is not { } zone) {
            return;
        }

        // everyone else leaves this copy of the map: the players (with the host), their companions,
        // those staying join us again with theirs. Listed while the party and the player list still stand
        var players = Session.CurrentPlayers.Where(p => p is not null).Select(p => p.CharaUid).ToHashSet();
        List<Chara> Leaving() => _map.charas
            .Where(c => c != pc && (players.Contains(c.uid) || c.GetBool("remote_chara") ||
                                    (c.party is not null && c.party == pc.party && !c.IsCompanionOf(pc)) ||
                                    (c.CompanionOwnerUid != 0 && c.CompanionOwnerUid != pc.uid)))
            .ToList();
        var leaving = Leaving();

        // cards created here get uids the host does not use (before a map is loaded here on our own)
        game.cards.uidNext = Math.Max(game.cards.uidNext, grant.UidRangeStart);
        AdoptQuestUidRange(grant);

        if (withHost) {
            // the results of our last actions on the host map arrived right before this packet
            WorldStateDeltaProcess();

            // while still a client of the host: its map is loaded as any map it sends
            if (AdoptHostCopy(zone, grant, "the host")) {
                leaving = Leaving();
            }

            if (_rejoining) {
                // the copy could not be loaded, we are on our way back to the host
                _pendingTravel = null;
                _handoffDeadline = 0;
                return;
            }

            EnterAway(zone);
        } else {
            // still open if the owner has not closed it yet
            Session.RemoveZoneSession();
            Session.IsGuest = false;

            // the copy the owner handed back to the host is the one everyone else gets: loaded as a leased map is
            if (AdoptHostCopy(zone, grant, "its owner")) {
                leaving = Leaving();
            }

            if (_rejoining) {
                _pendingTravel = null;
                _handoffDeadline = 0;
                return;
            }
        }

        Session.IsGuest = false;
        // our own move asked just before the host left (to another map than this one) goes on: its grant comes next,
        // and finds us holding this map as any player away who moves on. Dropped here, the grant was "unexpected"
        // and refused, and we stayed behind instead of going where we went (simultaneous moves, 2026-10-14)
        if (_pendingTravel is { } going && going.Zone.uid == grant.ZoneUid) {
            _pendingTravel = null;
        }

        _handoffDeadline = 0;
        _nextCheckpoint = Time.realtimeSinceStartup + Session.Rules.TravelCheckpointSeconds;

        // those still waiting for a uid from the owner get one of ours
        foreach (var card in _map.things.Concat<Card>(_map.charas).ToList()) {
            if (PendingUid.IsPending(card.uid)) {
                game.cards.AssignUID(card);
            }

            foreach (var thing in card.things.Flatten()) {
                if (PendingUid.IsPending(thing.uid)) {
                    game.cards.AssignUID(thing);
                }
            }
        }

        foreach (var chara in leaving) {
            if (chara.party is { } party && party.members.Contains(chara)) {
                party.RemoveMember(chara);
            }

            if (chara.parent is Zone) {
                zone.RemoveCard(chara);
            }
        }

        EmpLog.Information("Took over zone {ZoneFullName}, uid range from {UidRangeStart}, from the host {WithHost}",
            zone.ZoneFullName, grant.UidRangeStart, withHost);
        EmpPop.Information("emp_travel_handoff".lang());
    }

    /// <summary>
    ///     The host leaves us the map we stand on: its copy is the reference, not what stands on our screen (one
    ///     message missed earlier would become true for everyone: the others load our copy, and so does the host
    ///     when it comes back). Same numbers: nothing to do. Otherwise its copy is loaded under our feet
    /// </summary>
    /// <param name="from">who kept the map until now, for the journal: the host, or the player we were visiting
    ///     (its copy went to the host, which sent it on, see ElinNetHost.HandOverZone)</param>
    /// <returns>true when the map was loaded again</returns>
    private bool AdoptHostCopy(Zone zone, ZoneLeaseGrant grant, string from)
    {
        if (grant.Map is null || grant.MapSums is not { } theirs) {
            return false;
        }

        var ours = ZoneLeaseState.Sums(_map);
        if (ours.SequenceEqual(theirs)) {
            EmpLog.Information("Taking over {ZoneFullName} from {From}: our copy is the same, kept ({Sums})",
                zone.ZoneFullName, from, ZoneLeaseState.TellSums(ours));
            return false;
        }

        if (ZoneLeaseState.SameFloor(ours, theirs)) {
            // only what the things on the floor hold: told, not a reason to load the map under the player
            EmpLog.Information("Taking over {ZoneFullName} from {From}: the content of a container differs, our copy is kept (here {Local} | there {Host})",
                zone.ZoneFullName, from, ZoneLeaseState.TellSums(ours), ZoneLeaseState.TellSums(theirs));
            return false;
        }

        // ponytail: what we did in the last round trip and the keeper never saw is not in its copy. A thing put
        // down then is lost (one picked up is taken off the floor below). Fix when seen: wait for an ack as a
        // leaving player does
        EmpLog.Warning("Taking over {ZoneFullName} from {From}: our copy differs, replaced by theirs (here {Local} | there {Host})",
            zone.ZoneFullName, from, ZoneLeaseState.TellSums(ours), ZoneLeaseState.TellSums(theirs));

        var stood = pc.pos.Copy();
        var carried = pc.things.Flatten().Select(t => t.uid).ToHashSet();

        // we stay on our tile: the game only moves a character that walks in, see Zone.AddGlobalCharasOnActivate
        pc.global.transition = null;

        // close the windows of the player before the map goes (a bag or a chest open on it): Scene.Init, which
        // player.MoveZone runs, does the same but after the unload. Not waited for: a fight going on or a
        // menu the player is in is cut short (known limit, the reload is not delayed until the player is idle)
        ui.RemoveLayers();

        try {
            // as OnZoneActivateResponse reloads the active map
            zone.Deactivate();

            // the game puts the artifacts lying here in our bag on the way out (Zone.Deactivate): they come back
            // with the host's map
            foreach (var thing in pc.things.Flatten().Where(t => !carried.Contains(t.uid)).ToList()) {
                thing.parentCard?.RemoveCard(thing);
            }

            zone.UnloadMap();
            ZoneLeaseState.WriteMap(zone, grant.Map);
        } catch (Exception ex) {
            // no map to stand on (disk, truncated map): back to the host, which sends us its own
            EmpLog.Warning(ex, "Taking over {ZoneFullName} from {From}: their copy could not be loaded, returning to the host",
                zone.ZoneFullName, from);

            // a visitor that no longer holds a good map must not hand it back as the zone's: it is a guest again
            if (Session.IsAway) {
                Session.IsGuest = true;
            }

            SendRejoin();
            return false;
        }

        player.MoveZone(zone);

        if (pc.isDead) {
            PutHimRightEr(stood);
        }

        // what we carry cannot lie on the floor too: picked up in the last round trip, that copy never saw it.
        // Kept where a player alone would have it, in the bag
        // Known limits, not handled (the uid is what is compared): a pick-up merged into a stack of the bag (the
        // floor thing is gone from our bag, the floor copy stays: doubled), a partial pick-up of a stack (same),
        // a thing taken from a chest of the received map (it is back in the chest)
        var doubled = _map.things.Where(t => carried.Contains(t.uid)).ToList();
        foreach (var thing in doubled) {
            zone.RemoveCard(thing);
            if (pc.things.Flatten().FirstOrDefault(t => t.uid == thing.uid) is { } kept) {
                CardCache.Set(kept);
            }
        }

        if (doubled.Count > 0) {
            EmpLog.Warning("Taking over {ZoneFullName}: {Count} thing(s) of that copy are in our bag already, taken off the floor: {Uids}",
                zone.ZoneFullName, doubled.Count, doubled.Select(t => t.uid));
        }

        return true;
    }

    /// <summary>
    ///     The host leaves the map we stand on and another player takes it over: we stay, as its guest.
    ///     Same path as asking to join a zone someone simulates, see OnZoneLeaseDepart
    /// </summary>
    private void StayAsGuest(ZoneLeaseGrant grant)
    {
        if (_pendingTravel is not null || _rejoining) {
            return;
        }

        WorldStateDeltaProcess();

        _pendingTravel = (_zone, new ZoneTransition());
        _pendingGrant = grant;
        StopWorldStateUpdate();

        // our game reloads this very map from the one keeping it: we stay on our tile
        Host.Send(new ZoneLeaseAck {
            ZoneUid = grant.ZoneUid,
            Stood = pc.pos,
        });
    }

    /// <summary>
    ///     Net event: another player joins the zone we simulate, host it in a zone session
    /// </summary>
    private void OnZoneGuestRequest(ZoneGuestRequest request)
    {
        var accepted = Session.IsZoneAuthority && Session.AwayZone?.uid == request.ZoneUid &&
                       _zone == Session.AwayZone && _pendingTravel is null && !_rejoining;

        if (accepted) {
            var zoneHost = Session.ZoneSession as ElinNetHost ?? StartZoneSession();
            zoneHost.RegisterGuest(request);
        }

        EmpLog.Information("Guest {RemoteIdentity} for zone {ZoneUid}: {Accepted}",
            request.GuestUser, request.ZoneUid, accepted);

        Host.Send(new ZoneGuestReady {
            ZoneUid = request.ZoneUid,
            GuestUser = request.GuestUser,
            Accepted = accepted,
            Port = ElinNetHost.ZoneSessionPort,
        });
    }

    private ElinNetHost StartZoneSession()
    {
        var zoneHost = Session.InitializeZoneSession<ElinNetHost>();
        zoneHost.StartZoneServer(IsLocalConnection);
        return zoneHost;
    }

    /// <summary>
    ///     Net event: Travel refused, stay where we are
    /// </summary>
    private void OnZoneLeaseDenied(ZoneLeaseDenied denied)
    {
        EmpLog.Information("Zone lease {ZoneUid} denied: {Reason}",
            denied.ZoneUid, denied.Reason);

        // not a refusal: the host comes along to our quest and runs its zone itself. The zone made here takes
        // the number of the host's (as a leased one does) and is the one its map lands in when we follow the
        // host there: it knows the quest, who gave it and where to go back to. What the host put in it comes
        // with the map, and with OnQuestZoneState
        if (denied.Reason == QuestFollowDelta.Coming) {
            if (_pendingTravel is { } asked && asked.Zone.IsInstance && _pendingGrant is null) {
                _pendingTravel = null;
                AdoptHostUid(asked.Zone, denied.ZoneUid);
            }

            return;
        }

        // out of the zone of a quest there is no staying: it is over, back to the host
        if (Session.IsAway && pc.currentZone?.IsInstance == true && _pendingGrant is null && !_rejoining) {
            // where the game sends it back to: the place the zone was entered from, in the town of the quest.
            // Only if the host stands in that town: elsewhere nothing tells where
            var back = _pendingTravel is { } refused ? ZoneArrival.Create(refused.Zone.uid, refused.Transition) : null;
            _pendingTravel = null;
            SendRejoin(back);
            return;
        }

        // the host stands there (we had not heard yet that it moved): going there is rejoining it
        if (denied.Reason == "emp_travel_host_zone" && Session.IsAway && _pendingGrant is null && !_rejoining) {
            var hostZoneUid = denied.ZoneUid;

            // host rule SoftRecall, slice 2: a zone we made ourselves (the floor the host already went down to,
            // made after our last copy of the world) takes the number the host knows it by, as a leased one does:
            // the map that comes without a world lands in it, and the way in is noted under the right number
            if (Session.Rules.SoftRecall && denied.HostZoneUid > 0 && _pendingTravel is { } made) {
                hostZoneUid = denied.HostZoneUid;

                // not when another zone of ours holds that number (ours are as old as our world): the map then
                // builds its own zone from what comes with it (OnZoneDataResponse)
                if (game.spatials.Find(hostZoneUid) is not { } other || other == made.Zone ||
                    other.ZoneFullName == made.Zone.ZoneFullName) {
                    AdoptHostUid(made.Zone, hostZoneUid);
                }
            }

            var arrival = _pendingTravel is { } going ? ZoneArrival.Create(hostZoneUid, going.Transition) : null;
            _pendingTravel = null;
            _hostZoneUid = hostZoneUid;
            SendRejoin(arrival);
            EmpPop.Debug("emp_travel_returning".lang());
            return;
        }

        _pendingTravel = null;
        EmpPop.Pop(denied.Reason.lang());

        // refused after leaving our place to join another player: back to the host
        if (_pendingGrant is not null) {
            _pendingGrant = null;
            SendRejoin();
        } else if (!Session.IsAway && _hostZoneAfterDeparture is not null && !_rejoining) {
            // the host changed map while we asked to go elsewhere (we did not follow it): we stay with it, on its map
            _hostZoneAfterDeparture = null;
            SendRejoin();
        }
    }

    /// <summary>
    ///     Net event: the host runs the zone of our quest, this is what its quest events hold there. Shown here
    ///     (how many monsters are left), and run from here if the host goes back alone and leaves us the zone
    /// </summary>
    internal void OnQuestZoneState(int zoneUid, LZ4Bytes? events)
    {
        if (events is null || game.spatials.Find(zoneUid) is not { IsInstance: true } zone) {
            return;
        }

        zone.events.list.RemoveAll(e => e is ZoneEventQuest);
        foreach (var zoneEvent in events.Decompress<List<ZoneEvent>>()) {
            // as a loaded one: not ZoneEventManager.Add, which starts it anew
            zoneEvent.zone = zone;
            zone.events.list.Add(zoneEvent);
        }
    }

    /// <summary>
    ///     Net event: The host wants to enter our zone, hand it back and rejoin the host
    /// </summary>
    private void OnZoneLeaseRecall(ZoneLeaseRecall recall)
    {
        if (Session.AwayZone?.uid != recall.ZoneUid) {
            // already handed back, or never held here: say so, the host would wait at the door of that map for good
            // (a release already on its way made the host forget the lease, this answer then does nothing)
            if (_pendingTravel is null && _pendingGrant is null) {
                Host.Send(new ZoneLeaseDecline {
                    ZoneUid = recall.ZoneUid,
                });
            }

            return;
        }

        if (_pendingTravel is not null) {
            // leaving already, the zone is released as soon as the next one is granted
            return;
        }

        // host rule SoftRecall: we keep our game and wait for ZoneSoftRejoin instead of the world, see
        // ElinNetClientSoftRejoin. Anything else going on (dead, visitors, not on that map): as before
        if (recall.Soft && CanRejoinSoftly()) {
            _softRejoinDeadline = Time.realtimeSinceStartup + SoftRejoinWaitSeconds;
        }

        EmpLog.Information("Host recalls zone {ZoneFullName}, rejoining (in place {Soft})",
            Session.AwayZone.ZoneFullName, IsAwaitingSoftRejoin);

        SendRejoin();
        // the screen reloads when the host answers: say who is coming and why
        EmpPop.Information("emp_travel_recalled_by".lang(), Host);
    }

    /// <summary>
    ///     Host zone change while away, rejoin if the host arrived in our zone anyway
    /// </summary>
    private void OnHostZoneChangedWhileAway(int zoneUid)
    {
        _hostZoneUid = zoneUid;

        // the host is already out of the zone it asked us along to
        if (zoneUid != _questInviteZone) {
            CloseQuestInvite();
        }

        if (Session.AwayZone?.uid != zoneUid || _pendingTravel is not null) {
            return;
        }

        // the world map is never recalled: everyone walks its own copy (ElinNetHost.CanEnterNow), the host
        // stepping on its own is no reason to drop ours and load the whole world again, each time it leaves a
        // town. We keep walking where we are; to travel with the host, a player joins it from a map
        if (Session.AwayZone.IsRegion) {
            EmpLog.Debug("Host walks the world map too, we stay on our copy");
            return;
        }

        EmpLog.Warning("Host entered away zone {ZoneFullName} without recall, rejoining",
            Session.AwayZone.ZoneFullName);

        SendRejoin();
    }

    /// <summary>
    ///     How long the question "come along?" stays open, no answer is a no
    /// </summary>
    private const float QuestInviteSeconds = 15f;

    private Dialog? _questInvite;
    private float _questInviteDeadline;
    private int _questInviteZone = -1;

    /// <summary>
    ///     Net event: the host entered the zone of a quest it took and left us in town, it asks us along
    /// </summary>
    internal void OnQuestFollowInvite(QuestFollowDelta invite)
    {
        // only a player away in that town (keeping it or visiting it), with nothing else going on: no quest zone
        // of its own, no trade
        if (!PersonalQuests.InstancesEnabled || !Session.IsAway || IsInTransfer || _pendingTravel is not null ||
            _questInviteDeadline > 0 || invite.ZoneUid != _hostZoneUid ||
            game.quests.list.Any(q => q.UseInstanceZone && PersonalQuests.IsPersonal(q)) ||
            PlayerTrade.View is { Phase: PlayerTrade.Invited or PlayerTrade.Open }) {
            EmpLog.Debug("Not asking to follow the host to quest zone {ZoneUid}", invite.ZoneUid);
            return;
        }

        _questInviteZone = invite.ZoneUid;
        _questInviteDeadline = Time.realtimeSinceStartup + QuestInviteSeconds;
        _questInvite = Dialog.YesNo("emp_quest_follow_ask".Loc(invite.Name),
            FollowHost,
            () => AnswerQuestInvite(QuestFollowDelta.Declined));
    }

    private void UpdateQuestInvite()
    {
        if (_questInviteDeadline <= 0) {
            return;
        }

        // we moved on meanwhile (recalled, travelling): the question is void
        if (!Session.IsAway || IsInTransfer || _pendingTravel is not null) {
            CloseQuestInvite();
            return;
        }

        // closed without a click
        if (_questInvite == null) {
            AnswerQuestInvite(QuestFollowDelta.Declined);
            return;
        }

        if (Time.realtimeSinceStartup >= _questInviteDeadline) {
            CloseQuestInvite();
            AnswerQuestInvite(QuestFollowDelta.NoAnswer);
        }
    }

    private void AnswerQuestInvite(int kind)
    {
        _questInviteDeadline = 0;
        _questInvite = null;

        SendWhileAway(new QuestFollowDelta {
            Kind = kind,
            Name = pc.Name,
        });
    }

    private void CloseQuestInvite()
    {
        if (_questInviteDeadline <= 0) {
            return;
        }

        _questInviteDeadline = 0;
        if (_questInvite != null) {
            _questInvite.Close();
        }

        _questInvite = null;
    }

    /// <summary>
    ///     Join the host where it is now (the zone of its quest): the map we kept goes back to it, then the same
    ///     way in as coming back to the host's map. The quest stays the host's, see QuestZoneVisitorPatch
    /// </summary>
    internal void FollowHost()
    {
        _questInviteDeadline = 0;
        _questInvite = null;

        // a visitor leaves the same way as when the host recalls it (OnZoneLeaseRecall)
        if (!Session.IsAway || _pendingTravel is not null || IsInTransfer) {
            return;
        }

        EmpLog.Information("Following the host from {AwayZone} to its zone {ZoneUid}",
            Session.AwayZone!.ZoneFullName, _hostZoneUid);

        SendRejoin();
        EmpPop.Debug("emp_travel_returning".lang());
    }

    /// <summary>
    ///     Hand the away zone back and return to the host <br />
    ///     The host answers with a save probe, which rebuilds the game in the host zone
    /// </summary>
    /// <param name="arrival">we walk into the host's map: the way in. Not when the host is the one coming to us</param>
    private void SendRejoin(ZoneArrival? arrival = null)
    {
        if (_rejoining) {
            return;
        }

        // host rule SoftRecall, slice 2: we walk into the host's map and keep our game. The release is marked
        // Soft (CreateLeaseRelease) and the host answers ZoneSoftRejoin then that one map, or the world as before
        if (arrival is not null && CanReturnSoftly(arrival.ZoneUid)) {
            _softReturnZone = arrival.ZoneUid;
            _softRejoinDeadline = Time.realtimeSinceStartup + SoftRejoinWaitSeconds;
        }

        _rejoining = true;
        HandBackAwayZone(true, arrival);
    }

    /// <summary>
    ///     Leaving the zone: hand it back (our guests stay, the host gives it to one of them, see HandOverZone)
    /// </summary>
    private const float TransferLockTimeout = 30f;

    private float _transferLockedAt;
    private bool _transferLocked;

    /// <summary>
    ///     Between the moment our character and bag were handed over (leaving the host map, coming back to it)
    ///     and the moment the next world is here. What is done in that window is known to nobody: an item
    ///     picked up would exist twice, one dropped would be lost
    /// </summary>
    /// <remarks>
    ///     Also while a guest waits to hear what becomes of the map its owner just left: if the host recalls it,
    ///     the copy the owner handed back is the one kept
    /// </remarks>
    internal bool IsInTransfer => _pendingGrant is not null || _rejoining || _handoffDeadline > 0 || _softReturnZone != 0;

    /// <summary>
    ///     No input during a transfer, it lasts a round trip
    /// </summary>
    private void UpdateTransferLock()
    {
        if (!IsInTransfer) {
            if (_transferLocked) {
                _transferLocked = false;
                EInput.haltInput = false;
            }

            return;
        }

        if (!_transferLocked) {
            _transferLocked = true;
            _transferLockedAt = Time.realtimeSinceStartup;
        }

        // never for good: a transfer that does not end is a bug, not a reason to freeze the player
        if (Time.realtimeSinceStartup - _transferLockedAt > TransferLockTimeout) {
            EInput.haltInput = false;
            return;
        }

        EInput.haltInput = true;
    }

    private void HandBackAwayZone(bool rejoin, ZoneArrival? arrival = null)
    {
        Host.Send(CreateLeaseRelease(rejoin, arrival: arrival));

        if (Session.ZoneSession is ElinNetHost) {
            Session.RemoveZoneSession();
        }
    }

    private void EnterAway(Zone zone)
    {
        var wasAway = Session.IsAway;

        // from now on the game runs as single player, see NetSession.Connection
        Session.AwayZone = zone;

        if (wasAway) {
            return;
        }

        _skippedAway.Clear();
        _awaySince = Time.realtimeSinceStartup;

        // the host may already have told us where it went (it left the map we stay on)
        _hostZoneUid = _hostZoneAfterDeparture ?? Session.CurrentZone?.uid ?? -1;
        _hostZoneAfterDeparture = null;
        _nextCheckpoint = Time.realtimeSinceStartup + Session.Rules.TravelCheckpointSeconds;
        StopWorldStateUpdate();
        Delta.ClearOut();
        Delta.ClearIn();
        EmptyWorldContainers();

        // other players stay with the host (party lists may hold empty slots)
        foreach (var member in pc.party?.members.ToList() ?? []) {
            if (member is not null && member != pc && Session.CurrentPlayers.Any(p => p?.CharaUid == member.uid)) {
                pc.party!.RemoveMember(member);
            }
        }
    }

    private void FlushDeltasNow()
    {
        Delta.RefreshBuffer();

        // second pass sends what was deferred by the first
        for (var i = 0; i < 3 && Delta.HasPendingOut; i++) {
            if (Delta.FlushOutBuffer() is { Count: > 0 } deltaList) {
                Host.Send(new WorldStateDeltaList {
                    DeltaList = deltaList,
                });
            }
        }
    }

    private ZoneLeaseRelease CreateLeaseRelease(bool rejoin, bool checkpoint = false, ZoneArrival? arrival = null)
    {
        // only a map we really stand on: on our way to one, our tile is still the one of the map we left
        var stoodIn = Session.AwayZone is { } away && _zone == away ? away.uid : -1;

        if (!Session.IsZoneAuthority || Session.AwayZone is not { } zone) {
            // a guest (or a player refused on its way) holds no zone, it only brings its character
            return new() {
                ZoneUid = -1,
                ZoneState = [],
                Chara = LZ4Bytes.Create(pc),
                Companions = CollectCompanions(),
                Residents = Patches.AwayResidents.Collect(!checkpoint),
                UidNext = game.cards.uidNext,
                Rejoin = rejoin,
                Checkpoint = checkpoint,
                StoodZoneUid = stoodIn,
                Arrival = arrival,
            };
        }

        // the world map is a local copy for everyone; the zone of a quest is gone once left
        var map = zone.IsRegion || zone.IsInstance ? null : ZoneLeaseState.CollectMap(zone);

        return new() {
            ZoneUid = zone.uid,
            ZoneState = ZoneLeaseState.GetState(zone),
            IdCurrentSubset = zone.idCurrentSubset,
            Map = map,
            // leaving for good: a visitor who takes the zone over compares its copy with this one
            MapSums = map is null || checkpoint || zone.map is null ? null : ZoneLeaseState.Sums(zone.map),
            Chara = LZ4Bytes.Create(pc),
            Companions = CollectCompanions(),
            Residents = Patches.AwayResidents.Collect(!checkpoint),
            UidNext = game.cards.uidNext,
            Rejoin = rejoin,
            Checkpoint = checkpoint,
            StoodZoneUid = stoodIn,
            Arrival = arrival,
            Soft = rejoin && !checkpoint && IsAwaitingSoftRejoin,
            GuestCharas = (Session.ZoneSession as ElinNetHost)?.CollectGuestCharas(),
            GuestCompanions = (Session.ZoneSession as ElinNetHost)?.CollectGuestCompanions(),
        };
    }

    private static List<LZ4Bytes> CollectCompanions()
    {
        return CompanionHelper.TravellingWith(pc).Select(c => LZ4Bytes.Create(c)).ToList();
    }

    internal void SendChatWhileAway(MsgSayDelta delta)
    {
        SendWhileAway(delta);
    }

    /// <summary>
    ///     What an away player still shares with the world: chat, the quest log
    /// </summary>
    internal void SendWhileAway(ElinDelta delta)
    {
        Host.Send(new WorldStateDeltaList {
            DeltaList = [delta],
        });
    }

    /// <summary>
    ///     What a player away from the host map takes of its deltas (the host sends it nothing else, see
    ///     ElinNetHost.SendByMap)
    /// </summary>
    internal static bool ReachesAwayPlayer(ElinDelta delta)
    {
        return delta is MsgSayDelta or WhisperDelta or GateItemAnswer or GateZoneDelta or MarriageDelta or QuestStartDelta or QuestCompleteDelta or QuestChangePhaseDelta or DialogFlagDelta or QuestFailDelta or QuestUpdateDelta or PersonalStateDelta or PlayerStandingDelta or WorldDateAdvanceDelta or WorldMapTogetherDelta or PlayerLeadDelta or WeatherDelta or DayDataDelta or QuestFollowDelta or SleepReadyDelta or SleepStartDelta or CharaSleepDelta or BillPayDelta or CodexDelta or AddRecipeDelta or BossWinDelta or ExtraTaxDelta or QuestJournalDelta or TentZoneDelta or TeamsDelta or ModerationDelta or AdventurerBoutDelta;
    }

    /// <summary>
    ///     Delta lists reaching an away client are about the host map, except chat
    /// </summary>
    private void ApplyChatWhileAway(WorldStateDeltaList response)
    {
        foreach (var delta in response.DeltaList) {
            // chat and the quest log are the world's, the rest is about the host map
            if (ReachesAwayPlayer(delta)) {
                // the regular delta loop does not run while away, see CoreSynchronizationContext
                delta.Apply(this);
            } else {
                var name = delta.GetType().Name;
                _skippedAway[name] = _skippedAway.GetValueOrDefault(name) + 1;
            }
        }
    }

    // measure only (council 11): what the host said while this player was away and that its game did not take,
    // by kind, and since when. Read at the world copy that brings it back, see ReportReturn
    private readonly Dictionary<string, int> _skippedAway = [];
    private float _awaySince;

    /// <summary>
    ///     One line a return: how long away, how big the world copy, what was not taken meanwhile. The figures a
    ///     return without a world copy needs before it is written (how often nothing of the world moved)
    /// </summary>
    private void ReportReturn(int worldBytes)
    {
        var kinds = string.Join(", ", _skippedAway.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key} {k.Value}"));
        EmpLog.Information("Back after {Seconds:F0}s away: world copy of {Bytes} bytes, {Skipped} host deltas not taken meanwhile ({Kinds})",
            _awaySince > 0 ? Time.realtimeSinceStartup - _awaySince : 0f, worldBytes, _skippedAway.Values.Sum(), kinds);
        _skippedAway.Clear();
        _awaySince = 0;
    }

    /// <summary>
    ///     Regular progress save while away, so a disconnect only loses what happened since
    /// </summary>
    private void UpdateTravelCheckpoint()
    {
        var interval = Session.Rules.TravelCheckpointSeconds;
        if (!Session.IsZoneAuthority || interval <= 0 || _pendingTravel is not null || _rejoining ||
            !core.IsGameStarted || _zone != Session.AwayZone) {
            return;
        }

        if (Time.realtimeSinceStartup < _nextCheckpoint) {
            return;
        }

        _nextCheckpoint = Time.realtimeSinceStartup + interval;
        SendTravelCheckpoint();
    }

    internal void SendTravelCheckpoint()
    {
        Host.Send(CreateLeaseRelease(false, true));
        EmpLog.Debug("Sent checkpoint of zone {ZoneFullName}", Session.AwayZone!.ZoneFullName);
    }

    /// <summary>
    ///     Only lease traffic and session control reach an away client, the rest is about the host map
    /// </summary>
    private static bool ShouldReceiveWhileAway(object packet)
    {
        return packet is ZoneLeaseGrant or
            WorldStateDeltaList or
            ZoneLeaseDepart or
            ZoneLeaseDenied or
            ZoneLeaseRecall or
            ZoneSoftRejoin or
            ZoneGuestRequest or
            ShippingPayout or
            ZoneDataResponse or
            SaveDataProbe or
            NetSessionRules or
            WorldCopyManifest or
            WorldCopyPiece or
            HostLeaving or
            NetIntegrityRejected;
        // not SessionPlayersSnapshot: the players of a zone session are ours, the host list is about its map
    }
}
