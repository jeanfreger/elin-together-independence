using System.Collections.Generic;
using System.Linq;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-25, host options "Teams" and "Team ownership of bases" (dev/PLAN_teams.md). Not Elin's
///     factions: everyone stays in the one player faction, a team is a label the host keeps in its save
///     (<see cref="ElinNetHost.TeamList" />) and tells every game (<see cref="TeamsDelta" />). Team 0, the
///     Wanderers, is everyone in no team: an old world is all Wanderers and nothing is blocked
/// </summary>
[HarmonyPatch]
internal static class Teams
{
    internal const int Wanderers = 0;

    // a guest's copy of the host's book
    private static Dictionary<int, int[]> _teams = [];
    private static Dictionary<int, string> _names = [];
    private static Dictionary<int, int> _zones = [];
    private static Dictionary<int, int> _owners = [];
    private static Dictionary<int, int> _requests = [];
    private static bool _told;

    // the game that keeps the book: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    private static Dictionary<int, int[]> TeamList => KeepsBook ? ElinNetHost.TeamList : _teams;
    private static Dictionary<int, string> Names => KeepsBook ? ElinNetHost.TeamNames : _names;
    private static Dictionary<int, int> Zones => KeepsBook ? ElinNetHost.ZoneTeams : _zones;
    private static Dictionary<int, int> Owners => KeepsBook ? ElinNetHost.CardOwners : _owners;
    private static Dictionary<int, int> Requests => KeepsBook ? ElinNetHost.TeamRequests : _requests;

    [ElinPreLoad]
    private static void Forget(GameIOContext context)
    {
        _teams = [];
        _names = [];
        _zones = [];
        _owners = [];
        _requests = [];
        _told = false;
        ElinNetHost.TeamRequests.Clear();
    }

    /// <summary>
    ///     A guest learns the host's book
    /// </summary>
    internal static void Learn(TeamsDelta delta)
    {
        var (team, asked, known) = (TeamOf(EClass.pc?.uid ?? 0), Requests.Keys.ToArray(), _told);
        _told = true;
        _teams = new(delta.TeamList);
        _names = new(delta.Names);
        _zones = new(delta.Zones);
        _owners = new(delta.Owners);
        _requests = new(delta.Requests ?? []);
        // (not the book told on joining: nothing changed then)
        if (known) {
            Notice(team, asked);
        }
    }

    /// <summary>
    ///     Says in the log what changed for the local player: its team, a player who asks to join the team it leads
    /// </summary>
    private static void Notice(int teamBefore, int[] askedBefore)
    {
        if (!EClass.core.IsGameStarted || EClass.pc is not { } pc) {
            return;
        }

        if (TeamOf(pc.uid) is var team && team != teamBefore) {
            Msg.Say("emp_team_now".Loc(NameOf(team)));
        }

        foreach (var (uid, id) in Requests) {
            if (!askedBefore.Contains(uid) && LeaderOf(id) == pc.uid) {
                Msg.Say("emp_team_asks".Loc(PlayerName(uid), NameOf(id)));
            }
        }
    }

    private static IEnumerable<int> MembersOf(int[] team) => team.Skip(2);

    /// <summary>
    ///     The team of that player character, the Wanderers when none
    /// </summary>
    internal static int TeamOf(int charaUid)
    {
        foreach (var (id, team) in TeamList) {
            if (MembersOf(team).Contains(charaUid)) {
                return id;
            }
        }

        return Wanderers;
    }

    internal static string NameOf(int teamId)
    {
        return Names.TryGetValue(teamId, out var name) && !string.IsNullOrEmpty(name) ? name : "emp_team_wanderers".lang();
    }

    internal static int LeaderOf(int teamId)
    {
        return TeamList.TryGetValue(teamId, out var team) ? team[0] : 0;
    }

    internal static string PlayerName(int charaUid)
    {
        return EClass.game.cards.globalCharas.Find(charaUid)?.NameSimple ?? "?";
    }

    internal static int TeamOfZone(Zone? zone)
    {
        return zone is null ? Wanderers : Zones.GetValueOrDefault(zone.uid);
    }

    /// <summary>
    ///     The team holds a land (the Wanderers hold none)
    /// </summary>
    internal static bool HasBase(int teamId)
    {
        return teamId != Wanderers && Zones.ContainsValue(teamId);
    }

    /// <summary>
    ///     The host's spawn zone: the meadow where a new game starts, none for "the host's map", or a zone by id
    /// </summary>
    internal static Zone? SpawnZone()
    {
        return EmpConfig.Server.SpawnZone.Value switch {
            "host" => null,
            "start" or "" => EClass.game.StartZone,
            var id => EClass.game.spatials.Find(id),
        };
    }

    internal static int OwnerOf(Card card)
    {
        return Owners.GetValueOrDefault(card.uid);
    }

    private static bool BasesOwned => NetSession.Instance.Rules is { UseTeams: true, TeamBases: true };

    private static bool PersonalOwned => NetSession.Instance.Rules is { UseTeams: true, TeamPersonal: true };

    /// <summary>
    ///     The player other than that one who owns this card of the map, or the installed card it lies in (personal
    ///     ownership inside a team); 0 when none. What a character carries is its own business
    /// </summary>
    private static int OtherOwner(Chara chara, Card card)
    {
        if (!PersonalOwned || !chara.IsPlayer || card.GetRootCard() is not { } root || root is Chara) {
            return 0;
        }

        var owner = OwnerOf(card) is var own and not 0 ? own : OwnerOf(root);
        return owner == chara.uid ? 0 : owner;
    }

    /// <summary>
    ///     That character may use this land: a land of no team or of its team. Only players are asked, residents and
    ///     companions go on as before
    /// </summary>
    internal static bool CanTouch(Chara chara, Zone? zone)
    {
        if (!BasesOwned || !chara.IsPlayer) {
            return true;
        }

        var team = TeamOfZone(zone);
        return team == Wanderers || team == TeamOf(chara.uid);
    }

    /// <summary>
    ///     That character may take or open this card of the map (on the ground, installed, in a chest): its own card,
    ///     or the land allows it. What a character carries is not the land's
    /// </summary>
    internal static bool CanTake(Chara chara, Card card)
    {
        if (card.parent is null || OwnerOf(card) == chara.uid) {
            return true;
        }

        // a teammate's own chest, door or bed, and what lies in that chest
        if (OtherOwner(chara, card) != 0) {
            return false;
        }

        return !BasesOwned || card.GetRootCard() is Chara || CanTouch(chara, EClass._zone);
    }

    /// <summary>
    ///     That character may open this chest of the map: the land allows it. A teammate's own chest opens, what
    ///     is inside stays (<see cref="CanTake" />)
    /// </summary>
    internal static bool CanOpen(Chara chara, Card card)
    {
        return !BasesOwned || card.parent is null || OwnerOf(card) == chara.uid || card.GetRootCard() is Chara ||
               CanTouch(chara, EClass._zone);
    }

    /// <summary>
    ///     "This belongs to (team).", or "to (player)." for a card a player owns, in the log
    /// </summary>
    internal static void SayBelongs(Card? card = null)
    {
        SE.Beep();
        var owner = card is null ? 0 : OtherOwner(EClass.pc, card);
        Msg.Say("emp_team_belongs".Loc(owner != 0 ? PlayerName(owner) : NameOf(TeamOfZone(EClass._zone))));
    }

    /// <summary>
    ///     A container, a door or a bed a player installs on its team's land is that player's (personal ownership):
    ///     written in the game that keeps the book, asked of it by a player who builds on a map it holds
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TaskBuild), nameof(TaskBuild.OnProgressComplete))]
    private static void OnBuilt(TaskBuild __instance)
    {
        if (!PersonalOwned || !__instance.useHeld || __instance.owner is not { IsPlayer: true } builder ||
            __instance.target is not Thing { IsInstalled: true } thing || thing.trait is not (TraitContainer or TraitDoor or TraitBed)) {
            return;
        }

        if (KeepsBook) {
            Own(builder, thing.uid, EClass._zone.uid);
        } else if (builder.IsPC && NetSession.Instance.IsAway) {
            // (on the host's map the host builds it and writes it itself)
            Ask(TeamAsk.Own, thing.uid, EClass._zone.uid);
        }
    }

    private static void Own(Chara chara, int cardUid, int zoneUid)
    {
        var team = TeamOf(chara.uid);
        if (team == Wanderers || Zones.GetValueOrDefault(zoneUid) != team || Owners.ContainsKey(cardUid)) {
            return;
        }

        Owners[cardUid] = chara.uid;
        EmpLog.Information("Card {CardUid} on land {ZoneUid} is player {CharaUid}'s", cardUid, zoneUid, chara.uid);
        Tell();
    }

    /// <summary>
    ///     "Share with team" in the full menu of a card the local player owns: it is the team's again
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActPlan), nameof(ActPlan._Update))]
    private static void OnUpdatePlan(ActPlan __instance)
    {
        if (!PersonalOwned || __instance.input != ActInput.AllAction) {
            return;
        }

        foreach (var card in __instance.list.Select(i => i.tc).OfType<Thing>().Where(t => t.IsInstalled && OwnerOf(t) == EClass.pc.uid)
                     .Distinct().ToArray()) {
            __instance.TrySetAct("emp_team_share", () => {
                Ask(TeamAsk.Share, card.uid);
                SE.Click();
                return false;
            }, card);
        }
    }

    /// <summary>
    ///     A door a teammate owns: the local player does not step through, the bump says whose it is
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.TryMove))]
    private static bool OnTryMove(Chara __instance, Point newPoint, ref Card.MoveResult __result)
    {
        if (!PersonalOwned || !__instance.IsPC || !newPoint.IsValid || !newPoint.cell.hasDoor) {
            return true;
        }

        foreach (var thing in newPoint.Things) {
            if (thing.trait is TraitDoor && thing.IsInstalled && OtherOwner(__instance, thing) != 0) {
                SayBelongs(thing);
                __result = Card.MoveResult.Fail;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     A chest of another team's land: no open action (nor the lock to pick), the click says whose it is
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitContainer), nameof(TraitContainer.TrySetAct))]
    private static bool OnContainerAct(TraitContainer __instance, ActPlan p)
    {
        var owner = __instance.owner;
        if (owner.parent is not Zone || CanOpen(EClass.pc, owner)) {
            return true;
        }

        p.TrySetAct("actContainer", () => {
            SayBelongs(owner);
            return false;
        }, owner, CursorSystem.Container);
        return false;
    }

    /// <summary>
    ///     On the host: the player of that connection stands on another team's land (what it asks to build or
    ///     manage there is refused)
    /// </summary>
    internal static bool RefusesPeer(ElinNetHost host, int peerId)
    {
        return host.ActiveRemoteCharas.TryGetValue(peerId, out var chara) && !CanTouch(chara, EClass._zone);
    }

    /// <summary>
    ///     Build mode on another team's land: not offered (the menu button, the hotbar)...
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Zone), nameof(Zone.CanEnterBuildMode), MethodType.Getter)]
    private static void OnCanEnterBuildMode(Zone __instance, ref bool __result)
    {
        __result = __result && CanTouch(EClass.pc, __instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Zone), nameof(Zone.CanEnterBuildModeAnywhere), MethodType.Getter)]
    private static void OnCanEnterBuildModeAnywhere(Zone __instance, ref bool __result)
    {
        __result = __result && CanTouch(EClass.pc, __instance);
    }

    /// <summary>
    ///     ...and its key says whose land it is
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ActionMode), nameof(ActionMode.DoFunc), typeof(CoreConfig.GameFunc))]
    private static bool OnDoFunc(CoreConfig.GameFunc func)
    {
        if (func != CoreConfig.GameFunc.ToggleBuild || EClass.scene.actionMode.IsBuildMode || CanTouch(EClass.pc, EClass._zone)) {
            return true;
        }

        SayBelongs();
        return false;
    }

    /// <summary>
    ///     The base's board of another team's land: the click says whose it is instead of opening the base's menu
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitHomeBoard), nameof(TraitHomeBoard.TrySetAct))]
    private static bool OnHomeBoardAct(TraitHomeBoard __instance, ActPlan p)
    {
        if (!EClass._zone.IsPCFaction || CanTouch(EClass.pc, EClass._zone)) {
            return true;
        }

        p.TrySetAct("actLayerHome", () => {
            SayBelongs();
            return false;
        }, __instance.owner);
        return false;
    }

    /// <summary>
    ///     What the bench and the console read: every team, its leader and members, and the teams of the lands
    /// </summary>
    internal static string Describe()
    {
        var lines = TeamList.Select(t =>
            $"{t.Key} {NameOf(t.Key)} leader {t.Value.ElementAtOrDefault(0)} open {t.Value.ElementAtOrDefault(1)} members [{string.Join(",", MembersOf(t.Value))}]");
        var zones = Zones.Select(z => $"zone {z.Key} -> team {z.Value}");
        return string.Join("\n", lines.Concat(zones).DefaultIfEmpty("no teams"));
    }

    /// <summary>
    ///     That player founds a team named so and leads it, leaving its former team. Only in the game that keeps the
    ///     book; 0 when it does not
    /// </summary>
    internal static int Found(Chara chara, string name)
    {
        if (!KeepsBook || string.IsNullOrWhiteSpace(name)) {
            return Wanderers;
        }

        Remove(chara.uid);
        var id = TeamList.Keys.DefaultIfEmpty(Wanderers).Max() + 1;
        // open: anyone joins at once; closed: its leader answers who asks (the host's rule when it is founded)
        TeamList[id] = [chara.uid, EmpConfig.Server.TeamApproval.Value ? 0 : 1, chara.uid];
        Names[id] = name.Trim();
        EmpLog.Information("Team {TeamId} {TeamName} founded by {CharaUid}", id, name, chara.uid);
        GiveDeed(chara, id);
        Tell();
        return id;
    }

    /// <summary>
    ///     A new team's founder receives one land deed (host option), once per team and once per player: founding
    ///     again after leaving gives none
    /// </summary>
    private static void GiveDeed(Chara chara, int teamId)
    {
        if (!NetSession.Instance.Rules.UseTeams || !EmpConfig.Server.TeamDeed.Value || ElinNetHost.TeamDeeds.ContainsKey(teamId) ||
            ElinNetHost.TeamDeeds.ContainsValue(chara.uid)) {
            return;
        }

        ElinNetHost.TeamDeeds[teamId] = chara.uid;
        var deed = ThingGen.Create("deed");
        if (chara.IsPC) {
            chara.Pick(deed);
        } else if (chara.IsAliveInCurrentZone && EClass._map.charas.Contains(chara) && !chara.things.IsFull(deed)) {
            // in its bag the way any change the host makes to a player's things reaches that player
            using (ElinDelta.Simulate()) {
                chara.AddThing(deed);
            }
        } else {
            var parcel = ThingGen.CreateParcel(null, deed);
            parcel.c_idRefName = "emp_team_deed_mail".Loc(chara.NameSimple);
            EClass.world.SendPackage(parcel);
        }

        EmpLog.Information("Team {TeamId}: a land deed for its founder {CharaUid}", teamId, chara.uid);
    }

    /// <summary>
    ///     That player joins the team, leaving its former one. It leads a team left without members
    /// </summary>
    internal static bool Join(Chara chara, int teamId)
    {
        if (!KeepsBook || !TeamList.ContainsKey(teamId)) {
            return false;
        }

        Remove(chara.uid);
        Requests.Remove(chara.uid);
        var team = TeamList[teamId];
        TeamList[teamId] = [team[0] == 0 ? chara.uid : team[0], ..team.Skip(1), chara.uid];
        EmpLog.Information("Chara {CharaUid} joined team {TeamId}", chara.uid, teamId);
        Tell();
        return true;
    }

    /// <summary>
    ///     That player is a Wanderer again. Its team and the team's lands stay, even without members
    /// </summary>
    internal static bool Leave(Chara chara)
    {
        if (!KeepsBook || !Remove(chara.uid)) {
            return false;
        }

        Requests.Remove(chara.uid);

        EmpLog.Information("Chara {CharaUid} left its team", chara.uid);
        Tell();
        return true;
    }

    /// <summary>
    ///     The local player's choice in a team menu: answered here in the game that keeps the book, asked of the
    ///     host otherwise (wherever this player is)
    /// </summary>
    internal static void Ask(TeamAsk kind, int id = 0, int zoneUid = 0)
    {
        if (KeepsBook) {
            Answer(EClass.pc, kind, id, zoneUid);
        } else {
            PersonalQuests.TellHost(new TeamRequestDelta { Kind = kind, Id = id, ZoneUid = zoneUid });
        }
    }

    /// <summary>
    ///     In the game that keeps the book: what that player asks, read against the book as it is now
    /// </summary>
    internal static void Answer(Chara chara, TeamAsk kind, int id, int zoneUid)
    {
        if (!KeepsBook || !NetSession.Instance.Rules.UseTeams) {
            return;
        }

        var (team, asked) = (TeamOf(EClass.pc.uid), Requests.Keys.ToArray());
        switch (kind) {
            case TeamAsk.Found:
                Found(chara, chara.NameSimple);
                break;
            case TeamAsk.Join when TeamList.TryGetValue(id, out var wanted) && TeamOf(chara.uid) != id:
                // at once into an open team, a team without a leader, or when the host does not ask the leaders
                if (wanted[1] == 1 || wanted[0] == 0 || !EmpConfig.Server.TeamApproval.Value) {
                    Join(chara, id);
                } else {
                    Requests[chara.uid] = id;
                    Tell();
                }
                break;
            case TeamAsk.Leave:
                Leave(chara);
                break;
            case TeamAsk.Own when PersonalOwned:
                Own(chara, id, zoneUid);
                break;
            case TeamAsk.Share when Owners.TryGetValue(id, out var owner) && owner == chara.uid:
                Owners.Remove(id);
                EmpLog.Information("Card {CardUid} is shared with the team again", id);
                Tell();
                break;
            // only the leader of the team asked answers
            case TeamAsk.Accept or TeamAsk.Refuse when Requests.TryGetValue(id, out var to) && LeaderOf(to) == chara.uid:
                Requests.Remove(id);
                if (kind == TeamAsk.Accept && EClass.game.cards.globalCharas.Find(id) is { } joiner) {
                    Join(joiner, to);
                } else {
                    Tell();
                }
                break;
        }

        Notice(team, asked);
    }

    /// <summary>
    ///     The team entries of a player's row in the list of players: on one's own row found, leave, join any
    ///     other team and the leader's answers; on another player's row, join its team
    /// </summary>
    internal static IEnumerable<(string Label, System.Action Act)> MenuFor(Chara chara)
    {
        if (!NetSession.Instance.Rules.UseTeams) {
            yield break;
        }

        var mine = TeamOf(EClass.pc.uid);
        if (!chara.IsPC) {
            if (TeamOf(chara.uid) is var theirs and not Wanderers && theirs != mine) {
                yield return ("emp_team_join".Loc(NameOf(theirs)), () => Ask(TeamAsk.Join, theirs));
            }
            yield break;
        }

        if (mine == Wanderers) {
            yield return ("emp_team_found".lang(), () => Ask(TeamAsk.Found));
        } else {
            yield return ("emp_team_leave".Loc(NameOf(mine)), () => Ask(TeamAsk.Leave));
        }

        foreach (var id in TeamList.Keys.Where(id => id != mine).ToArray()) {
            yield return ("emp_team_join".Loc(NameOf(id)), () => Ask(TeamAsk.Join, id));
        }

        if (mine != Wanderers && LeaderOf(mine) == EClass.pc.uid) {
            foreach (var uid in Requests.Where(r => r.Value == mine).Select(r => r.Key).ToArray()) {
                yield return ("emp_team_accept".Loc(PlayerName(uid)), () => Ask(TeamAsk.Accept, uid));
                yield return ("emp_team_refuse".Loc(PlayerName(uid)), () => Ask(TeamAsk.Refuse, uid));
            }
        }
    }

    /// <summary>
    ///     The player a claim run on the host is for: a guest's yes to a deed (<see cref="ClaimZoneDelta" />)
    /// </summary>
    internal static Chara? Claimer { get; set; }

    /// <summary>
    ///     A land was claimed in the game that keeps the book: it is the claimer's team's, a Wanderer's is nobody's
    /// </summary>
    internal static void OnClaimed(Zone zone)
    {
        // teams off: a claim is nobody's, as before (it stays so when the box is ticked later)
        if (!KeepsBook || !NetSession.Instance.Rules.UseTeams) {
            return;
        }

        var claimer = Claimer ?? EClass.pc;
        var team = TeamOf(claimer.uid);
        if (team == Wanderers) {
            Zones.Remove(zone.uid);
        } else {
            Zones[zone.uid] = team;
        }

        EmpLog.Information("Land {ZoneUid} claimed by {CharaUid}, team {TeamId}", zone.uid, claimer.uid, team);
        Tell();
    }

    private static bool Remove(int charaUid)
    {
        var id = TeamOf(charaUid);
        if (id == Wanderers) {
            return false;
        }

        var team = TeamList[id];
        var members = MembersOf(team).Where(uid => uid != charaUid).ToArray();
        // the leader who leaves hands the team to the next member
        var leader = team[0] == charaUid ? members.FirstOrDefault() : team[0];
        TeamList[id] = [leader, team[1], ..members];
        return true;
    }

    private static void Tell()
    {
        (NetSession.Instance.Transport as ElinNetHost)?.TellTeams();
    }
}
