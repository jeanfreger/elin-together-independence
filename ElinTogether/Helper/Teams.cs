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

    // the game that keeps the book: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    private static Dictionary<int, int[]> TeamList => KeepsBook ? ElinNetHost.TeamList : _teams;
    private static Dictionary<int, string> Names => KeepsBook ? ElinNetHost.TeamNames : _names;
    private static Dictionary<int, int> Zones => KeepsBook ? ElinNetHost.ZoneTeams : _zones;
    private static Dictionary<int, int> Owners => KeepsBook ? ElinNetHost.CardOwners : _owners;

    [ElinPreLoad]
    private static void Forget(GameIOContext context)
    {
        _teams = [];
        _names = [];
        _zones = [];
        _owners = [];
    }

    /// <summary>
    ///     A guest learns the host's book
    /// </summary>
    internal static void Learn(TeamsDelta delta)
    {
        _teams = new(delta.TeamList);
        _names = new(delta.Names);
        _zones = new(delta.Zones);
        _owners = new(delta.Owners);
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

    internal static int TeamOfZone(Zone? zone)
    {
        return zone is null ? Wanderers : Zones.GetValueOrDefault(zone.uid);
    }

    internal static int OwnerOf(Card card)
    {
        return Owners.GetValueOrDefault(card.uid);
    }

    private static bool BasesOwned => NetSession.Instance.Rules is { UseTeams: true, TeamBases: true };

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
        if (!BasesOwned || card.parent is null || OwnerOf(card) == chara.uid) {
            return true;
        }

        return card.GetRootCard() is Chara || CanTouch(chara, EClass._zone);
    }

    /// <summary>
    ///     "This belongs to (team).", in the log
    /// </summary>
    internal static void SayBelongs()
    {
        SE.Beep();
        Msg.Say("emp_team_belongs".Loc(NameOf(TeamOfZone(EClass._zone))));
    }

    /// <summary>
    ///     A chest of another team's land: no open action (nor the lock to pick), the click says whose it is
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitContainer), nameof(TraitContainer.TrySetAct))]
    private static bool OnContainerAct(TraitContainer __instance, ActPlan p)
    {
        var owner = __instance.owner;
        if (owner.parent is not Zone || CanTake(EClass.pc, owner)) {
            return true;
        }

        p.TrySetAct("actContainer", () => {
            SayBelongs();
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
        TeamList[id] = [chara.uid, 1, chara.uid];
        Names[id] = name.Trim();
        EmpLog.Information("Team {TeamId} {TeamName} founded by {CharaUid}", id, name, chara.uid);
        Tell();
        return id;
    }

    /// <summary>
    ///     That player joins the team, leaving its former one (the leader's approval is a later step)
    /// </summary>
    internal static bool Join(Chara chara, int teamId)
    {
        if (!KeepsBook || !TeamList.ContainsKey(teamId)) {
            return false;
        }

        Remove(chara.uid);
        TeamList[teamId] = [..TeamList[teamId], chara.uid];
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

        EmpLog.Information("Chara {CharaUid} left its team", chara.uid);
        Tell();
        return true;
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
