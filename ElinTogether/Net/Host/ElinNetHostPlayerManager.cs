using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net.Steam;
using UnityEngine;

namespace ElinTogether.Net;

internal partial class ElinNetHost
{
    public readonly Dictionary<int, Chara> ActiveRemoteCharas = [];

    /// <summary>
    ///     A combination of all remote chara's act states
    /// </summary>
    public int SharedActState => States.Values.Sum(s => s.LastAct);

    /// <summary>
    ///     Shared speed of all players
    /// </summary>
    // the players that told their speed (a 0 is one that did not yet: it slowed everyone, and no player at all
    // threw), upstream 6f0fda1
    public int SharedSpeed => (int)States.Values.Where(s => s.Speed > 0).Select(s => s.Speed).DefaultIfEmpty(0).Average();

    [ElinGameIOProperty("remote_chara")]
    private static Dictionary<ulong, int> SavedRemoteCharas
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Every character a player made in this world. <see cref="SavedRemoteCharas" /> holds the one it plays
    /// </summary>
    [ElinGameIOProperty("remote_chara_roster")]
    private static Dictionary<ulong, List<int>> PlayerRosters
    {
        get => field ??= [];
        set;
    }

    public static void RemoveRemoteChara(Chara remoteChara, bool broadcast = true)
    {
        if (!core.IsGameStarted) {
            return;
        }

        remoteChara.SetNoGoal();

        pc.party?.RemoveMember(remoteChara);

        // it stays a resident of the base: taking it out made every return go through the whole FactionBranch.AddMemeber
        // again, which destroys the duplicates of a god's artifact among the people of the base (the other players)

        // on this map whatever zone it says it is in: left in the list it kept standing, and acting, there
        if (_map.charas.Contains(remoteChara)) {
            _zone.RemoveCard(remoteChara);
        } else {
            // a player on another map keeps its position there, which may not exist on this one
            remoteChara.parent = null;
            remoteChara.currentZone = null;
        }

        if (broadcast && NetSession.Instance.Connection is ElinNetHost host) {
            host.Delta.AddRemote(new CharaRemoveFromGameDelta {
                Owner = remoteChara,
            });
        }
    }

    /// <summary>
    ///     The character of a player nobody plays right now is never on a map: it waits for its player with what
    ///     it carries. Wherever the game runs: the host, a player hosting its zone, a player alone on a map
    ///     (its world is the copy it left with, and nothing told it who stopped playing since)
    /// </summary>
    internal static void RemoveUnplayedCharas()
    {
        // a client on the map of its host is told who is there, see WorldStateSnapshot.RemoveLeftOverCharas
        if (!core.IsGameStarted || _map is null || Session.Connection is ElinNetClient) {
            return;
        }

        var played = (Session.Connection as ElinNetHost)?.ActiveRemoteCharas;

        foreach (var chara in _map.charas.ToArray()) {
            if (chara != pc && chara.GetBool("remote_chara") && played?.ContainsValue(chara) != true) {
                RemoveRemoteChara(chara);
            }
        }
    }

    /// <summary>
    ///     PeerConnect -> Prepare -> MoveZone -> SaveProbe
    /// </summary>
    public void PreparePlayerJoin(ISteamNetPeer peer, string? notice = null)
    {
        EmpLog.Information("Preparing player {@Peer} for joining",
            peer);

        if (!IsZoneSession) {
            GiveOrphanTo(peer.User);
        }

        var roster = RosterOf(peer.User);
        var import = EmpConfig.Server.ImportCharacter.Value;
        var known = SavedRemoteCharas.TryGetValue(peer.User, out var played) && game.cards.globalCharas.Find(played) is not null;
        // no question at every connection: a player who comes back gets the character it played last. Asked only
        // when nothing says which one (several characters, none of them played last), when it has nobody here
        // and may bring someone, or when the host wants every joining player asked
        var ask = EmpConfig.Server.ChooseCharacter.Value
            ? roster.Count > 0 || import
            : !known && (roster.Count > 1 || (roster.Count == 0 && import));
        // not for the guests of a zone hosted by a player: they come with the character they are playing
        if (!IsZoneSession && ask) {
            // the player picks who to play, brings someone from its own saves, or makes someone new
            peer.Send(new SessionCharaSelectRequest {
                AllowImport = import,
                AllowDelete = EmpConfig.Server.DeleteCharacter.Value,
                Notice = notice,
                Charas = roster
                    .Select(c => new SessionCharaEntry {
                        Uid = c.uid,
                        Label = $"{c.Name} - {c.race.GetName()} {c.job.GetName()}, Lv {c.LV}",
                    })
                    .ToList(),
            });
            return;
        }

        // its only character here
        if (!IsZoneSession && !known && roster.Count == 1) {
            SavedRemoteCharas[peer.User] = roster[0].uid;
        }

        if (!SavedRemoteCharas.TryGetValue(peer.User, out var charaUid) ||
            game.cards.globalCharas.Find(charaUid) is not { } chara) {
            EmpLog.Debug("Remote character does not exist, request for new character generation");
            peer.Send(new SessionNewPlayerRequest());
        } else {
            // remote character exists
            SendSaveProbe(chara, peer);
            SendToSpawnZone(chara, peer, false);
        }
    }

    /// <summary>
    ///     The characters of a player in this world nobody is playing right now
    /// </summary>
    private List<Chara> RosterOf(ulong user)
    {
        if (!PlayerRosters.TryGetValue(user, out var uids)) {
            uids = PlayerRosters[user] = [];
        }

        // worlds from before the roster only know the one character
        if (SavedRemoteCharas.TryGetValue(user, out var current) && !uids.Contains(current)) {
            uids.Add(current);
        }

        return uids
            .Select(uid => game.cards.globalCharas.Find(uid))
            .Where(c => c is not null && !ActiveRemoteCharas.ContainsValue(c))
            .ToList();
    }

    /// <summary>
    ///     Net event: the player picked a character, or asks for a new one
    /// </summary>
    private void OnSessionCharaSelectResponse(SessionCharaSelectResponse response, ISteamNetPeer peer)
    {
        if (ActiveRemoteCharas.ContainsKey(peer.Id)) {
            return;
        }

        if (response.Uid == 0) {
            EmpLog.Debug("Player {@Peer} asks for a new character", peer);
            peer.Send(new SessionNewPlayerRequest());
            return;
        }

        if (RosterOf(peer.User).Find(c => c.uid == response.Uid) is not { } chara) {
            EmpLog.Warning("Player {@Peer} picked chara {Uid} which is not one of its own", peer, response.Uid);
            PreparePlayerJoin(peer);
            return;
        }

        if (response.Delete) {
            if (EmpConfig.Server.DeleteCharacter.Value) {
                DeleteCharacter(peer.User, chara);
            }

            PreparePlayerJoin(peer, EmpConfig.Server.DeleteCharacter.Value ? "emp_ui_chara_deleted" : null);
            return;
        }

        SavedRemoteCharas[peer.User] = chara.uid;
        SendSaveProbe(chara, peer);
        SendToSpawnZone(chara, peer, false);
    }

    /// <summary>
    ///     A player deletes one of its characters in this world, not played right now (player request,
    ///     2026-10-14: a guest who made several had no way to clear them). It goes with what it carries; its
    ///     companions stay in the world, following nobody
    /// </summary>
    private void DeleteCharacter(ulong user, Chara chara)
    {
        if (PlayerRosters.TryGetValue(user, out var roster)) {
            roster.Remove(chara.uid);
        }

        if (SavedRemoteCharas.TryGetValue(user, out var played) && played == chara.uid) {
            SavedRemoteCharas.Remove(user);
        }

        PlayerStandings.Remove(chara.uid);
        foreach (var companion in game.cards.globalCharas.Values.Where(c => c is not null && c.CompanionOwnerUid == chara.uid).ToList()) {
            companion.SetCompanionOwner(null);
            companion.party?.RemoveMember(companion);
        }

        EmpLog.Information("Player character {Uid} ({Name}, Lv {Level}) deleted by its player", chara.uid, chara.Name, chara.LV);
        chara.Destroy();
    }

    /// <summary>
    ///     Net event: the player brings the character of one of its own saves
    /// </summary>
    private void OnSessionCharaImportResponse(SessionCharaImportResponse response, ISteamNetPeer peer)
    {
        if (ActiveRemoteCharas.ContainsKey(peer.Id)) {
            return;
        }

        if (IsZoneSession || !EmpConfig.Server.ImportCharacter.Value) {
            EmpLog.Warning("Player {@Peer} sent a character while bringing one is not allowed", peer);
            PreparePlayerJoin(peer);
            return;
        }

        // one copy of a save's character per player: a second one would be the same items twice
        RosterOf(peer.User);
        if (PlayerRosters[peer.User].Any(uid =>
                game.cards.globalCharas.Find(uid)?.GetStr(CharaImport.SourceKey) == response.Source)) {
            EmpLog.Information("Player {@Peer} already brought the character of {Source}", peer, response.Source);
            PreparePlayerJoin(peer, "emp_ui_chara_import_twice");
            return;
        }

        Chara chara;
        try {
            chara = response.Chara.Decompress<Chara>();
            CharaImport.Adopt(chara, response.Source);
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not take in the character brought by player {@Peer}", peer);
            PreparePlayerJoin(peer, "emp_ui_chara_import_fail");
            return;
        }

        EmpLog.Information("Player {@Peer} brings {Name} (Lv {Level}) from its save {Source}",
            peer, chara.Name, chara.LV, response.Source);

        // before the save probe, which hands a missing standing the one of a new player
        PlayerStandings[chara.uid] = [response.Fame, response.Karma];
        SavedRemoteCharas[peer.User] = chara.uid;
        // adds the character now played to the roster
        RosterOf(peer.User);

        SendSaveProbe(chara, peer);
        SendToSpawnZone(chara, peer, true);
    }

    /// <summary>
    ///     The character is played by that peer from now on: in the party, on the host's map, in the player list.
    ///     Shared by the copy of the world (<see cref="SendSaveProbe" />) and the return without one
    ///     (RegisterSoftRejoin)
    /// </summary>
    private void RegisterPlayer(Chara chara, ISteamNetPeer peer)
    {
        // register before any SetAI
        ActiveRemoteCharas[peer.Id] = chara;

        chara.MakeAlly();
        chara.MoveZone(pc.currentZone);
        chara.SetBool("remote_chara", true);
        GiveAxeToRemotePlayer(chara);

        var state = States[peer.Id] = new() {
            Index = peer.Id,
            User = peer.User,
            CharaUid = chara.uid,
        };

        CardCache.Add(chara);
        CardCache.CacheContainer(chara.things);

        Session.CurrentPlayers.Add(state);
    }

    /// <summary>
    ///     Send a save snapshot for replication
    /// </summary>
    public void SendSaveProbe(Chara chara, ISteamNetPeer peer)
    {
        EmpLog.Information("Sending save probe to player {@Peer} for replication",
            peer);

        RegisterPlayer(chara, peer);

        // what is already done is in the copies below: told to everyone before them, this player would
        // otherwise replay it on top of a world that has it
        Delta.RefreshBuffer();
        WorldStateDeltaUpdate();

        peer.Send(NetSession.Instance.Rules);
        peer.Send(SaveDataProbe.Create(chara.uid));

        // the map in the same frame as the world, not when the player asks for it once the world is loaded:
        // nothing happens between the two copies, and what happens after both is kept there until the map is
        // loaded (ElinDeltaManager.HoldForIncomingMap). The characters of the players are in the world only:
        // what they picked up or put on between the two copies never reached this player
        if (_zone is { } zone) {
            EmpLog.Information("World and map {ZoneFullName} sent together to player {@Peer}",
                zone.ZoneFullName, peer);
            PropagateZoneChangeState(zone, peer);
        } else {
            EmpLog.Warning("No active map to send with the world to player {@Peer}", peer);
        }

        TellMarriages();
        TellTeams();
    }

    /// <summary>
    ///     Net event: Client finished character generation and is ready for save probe
    /// </summary>
    private void OnSessionNewPlayerResponse(SessionNewPlayerResponse response, ISteamNetPeer peer)
    {
        EmpLog.Information("Received remote chara creation from player {@Peer}",
            peer);

        // sent twice: the character it plays would stay on the map with nobody playing it
        if (ActiveRemoteCharas.ContainsKey(peer.Id)) {
            return;
        }

        var chara = response.Chara.Decompress<Chara>();
        AdoptNewPlayerChara(chara);

        SavedRemoteCharas[peer.User] = chara.uid;
        // adds the character now played to the roster
        RosterOf(peer.User);

        SendSaveProbe(chara, peer);
        SendToSpawnZone(chara, peer, true);
    }

    /// <summary>
    ///     A character just made on the creation screen of another game becomes a character of this world, with what
    ///     a new game gives its player
    /// </summary>
    private static void AdoptNewPlayerChara(Chara chara)
    {
        chara.SetBool(CINT.IsPC, false);
        chara.SetBool("emp_creating", true);
        game.cards.AssignUID(chara);
        // its 90 days without a death penalty start now, not with the world (CharaReviveDelta.IsProtected)
        chara.SetInt(CharaReviveDelta.BornKey, player.stats.days + 1);

        var host = pc;
        try {
            player.chara = chara;

            chara.hp = chara.MaxHP;
            chara.SetFaith(game.religions.list[0]);
            chara.elements.SetBase(SKILL.strategy, 1);
            chara.elements.SetBase(ABILITY.AI_Meditate, 1);
            chara.elements.SetBase(ABILITY.ActQuickCraft, 1);
            chara.elements.SetBase(ABILITY.AI_SelfHarm, 1);
            chara.elements.SetBase(ABILITY.ActPray, 1);
            foreach (var e in chara.elements.dict.Values.ToList()) {
                if (e.Value == 0) {
                    chara.elements.Remove(e.id);
                    continue;
                }
                if (e.HasTag("primary")) {
                    e.vTempPotential = Mathf.Max(30, (e.ValueWithoutLink - 8) * 7);
                }
            }

            foreach (var slot in chara.body.slots) {
                chara.body.Unequip(slot);
            }
            chara.things.DestroyAll();
            chara.elements.CheckSkillActions();
            chara.hunger.value = 30;
            chara.CalculateMaxStamina();
            chara.stamina.Set(chara.stamina.max / 2);
            chara.Refresh();

            // as a new game does, and nothing more: a purse was added here, which a player who starts a world
            // does not have
            player.CreateEquip();
        } finally {
            chara.SetBool("emp_creating", false);
            player.chara = host;
        }
    }

    /// <summary>
    ///     Request a specific client to reconnect for full synchronization
    /// </summary>
    public void RequestClientReconnect(int peerIndex)
    {
        if (!States.ContainsKey(peerIndex)) {
            EmpLog.Warning("Cannot request reconnect: peer {PeerIndex} not found",
                peerIndex);
            return;
        }

        foreach (var peer in Socket.Peers) {
            if (peer.Id == peerIndex) {
                EmpLog.Information("Requesting reconnect for peer {@Peer}",
                    peer);
                peer.Send(SessionReconnectRequest.Current);
                return;
            }
        }

        EmpLog.Warning("Cannot request reconnect: peer {PeerIndex} not connected",
            peerIndex);
    }

    private static void GiveAxeToRemotePlayer(Chara chara)
    {
        if (!chara.GetBool("remote_axe_given")) {
            try {
                chara.SetBool("emp_creating", true);
                chara.AddThing("axe");
            } finally {
                chara.SetBool("emp_creating", false);
            }
            chara.SetBool("remote_axe_given", true);
        }
    }

    [ElinPostLoad]
    private static void RemoveLeftOverCharas(GameIOContext? context)
    {
        // a world taken over from another player: our own character first, then the game starts again from that save.
        // One frame later: the tables of the save (who plays whom) are read after this hook, here they are still
        // those of the game played before. Never on a server nobody plays at, never for a client
        if (Session.Transport is null && !EmpServer.Requested) {
            var loaded = game;
            core.actionsNextFrame.Add(() => {
                if (core.game != loaded || Session.Transport is not null) {
                    return;
                }

                // opened from the copy a guest kept of it: one handover more than that copy, written by the save
                // below, or by the first save of the session when no character is exchanged
                var takenOver = WorldTakeover.Opened(out var handover);
                if (takenOver) {
                    WorldHandoverCount[0] = System.Math.Max(HandoverNumber, handover) + 1;
                    EmpLog.Information("World taken over from a copy: handover {Handover}", HandoverNumber);
                }

                if (!TakeOverPc()) {
                    // (not when the creation screen of a new player just took the place of the game)
                    if (takenOver && scene.mode != Scene.Mode.Title) {
                        game.Save(false, true);
                    }

                    // the right character is in place: the session opens without a click on "Start Server"
                    EmpAutoHost.OpenSession();
                    return;
                }

                // not saved: played as it is, loading again would exchange again
                var (id, cloud) = (Game.id, game.isCloud);
                if (game.Save(false, true)) {
                    core.actionsNextFrame.Add(() => Game.Load(id, cloud));
                }
            });
        }

        IEnumerable<Chara> excluded = Session.Connection is ElinNetHost host
            ? host.ActiveRemoteCharas.Values
            : [];

        // not ourselves: a client hosting a zone session is a remote chara in the world it copied
        var currentRemoteCharas = game.cards.globalCharas.Values
            .Where(c => c != pc && c.GetBool("remote_chara"));

        foreach (var chara in currentRemoteCharas.Except(excluded)) {
            RemoveRemoteChara(chara);
        }

        pc.party?.members.RemoveAll(c => c is null);
        pc.party?.uidMembers.RemoveAll(uid => pc.party?.members.Find(c => c.uid == uid) is null);
    }
}