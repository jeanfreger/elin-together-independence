using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The team page (council 2026-10-28): the same in every game, the host's own included. The player's team, its
///     members and lands, and the choices the game's player list already offered (found, join, leave, accept,
///     refuse), asked of the game that keeps the book (<see cref="Teams.Ask" />)
/// </summary>
internal class TabTeam : TabEmpBase
{
    public override void OnLayout()
    {
        if (EClass.pc is not { } pc) {
            return;
        }

        var mine = Teams.TeamOf(pc.uid);
        BuildInvitation(pc.uid);
        if (mine == Teams.Wanderers) {
            BuildFound();
        } else {
            BuildOwnTeam(mine, pc.uid);
        }

        BuildOtherTeams(mine);
        BuildTeamLines();
    }

    private static string Level(int level) => $"emp_ui_team_rule_{level}".lang();

    /// <summary>
    ///     A land and, when lands have rules, what its team and everyone else may do there; the leader changes them
    /// </summary>
    private static void BuildLand(YKVertical card, int land, bool leads)
    {
        if (!Teams.LandRulesOn) {
            card.Text(Teams.ZoneName(land));
            return;
        }

        var rule = Teams.RuleOf(land);
        var (mine, others) = (Teams.MineLevel(rule), Teams.OthersLevel(rule));
        if (!leads) {
            card.Text("emp_ui_team_land_line".Loc(Teams.ZoneName(land), Level(mine), Level(others)) +
                      (Teams.OfflineClosed(rule) ? " " + "emp_ui_team_rule_offline_tag".lang() : ""));
            return;
        }

        card.Text(Teams.ZoneName(land));
        // (the team keeps at least Visit: a land closed to its own team would shut its owners out)
        var own = new List<string> { Level(Teams.Visit), Level(Teams.Use), Level(Teams.Build) };
        card.DropdownRow("emp_ui_team_rule_mine".lang(), own,
            i => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(i + 1, others, Teams.OfflineClosed(rule)), land), mine - 1);
        var all = new List<string> { Level(Teams.Closed), Level(Teams.Visit), Level(Teams.Use), Level(Teams.Build) };
        card.DropdownRow("emp_ui_team_rule_others".lang(), all,
            i => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(mine, i, Teams.OfflineClosed(rule)), land), others);
        // offline protection: while nobody of the team is in the game, the land is closed to everyone else
        card.Toggle("emp_ui_team_rule_offline", Teams.OfflineClosed(rule),
            on => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(mine, others, on), land)).AsRow();
    }

    /// <summary>
    ///     A team invited this player: a line and the two answers (no popup)
    /// </summary>
    private void BuildInvitation(int me)
    {
        var team = Teams.InviteOf(me);
        if (team == Teams.Wanderers) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_team_invite_title".lang());
        card.Text("emp_ui_team_invite_line".Loc(Teams.NameOf(team), Teams.PlayerName(Teams.LeaderOf(team))));
        var row = card.ButtonRow();
        row.RowButton("emp_ui_team_invite_accept".lang(), () => Teams.Ask(TeamAsk.AcceptInvite, team));
        row.RowButton("emp_ui_team_invite_refuse".lang(), () => Teams.Ask(TeamAsk.RefuseInvite, team));
    }

    private void BuildFound()
    {
        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_team_title".lang());
        card.Text("emp_ui_team_none".lang());
        card.ButtonRow().RowButton("emp_team_found".lang(), () => {
            Dialog.InputName("emp_ui_team_name_ask", EClass.pc.NameSimple, (cancel, text) => {
                if (!cancel) {
                    Teams.Ask(TeamAsk.Found, text: text);
                }
            }).input.field.characterLimit = Teams.NameMax;
        });
    }

    private void BuildOwnTeam(int mine, int me)
    {
        var leader = Teams.LeaderOf(mine);
        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_team_mine".Loc(Teams.NameOf(mine)));
        card.Text("emp_ui_team_leader".Loc(Teams.PlayerName(leader), (Teams.IsOpen(mine) ? "emp_ui_team_open" : "emp_ui_team_closed").lang()));
        var manages = leader == me && Teams.LeadersManage;
        foreach (var member in Teams.MembersOfTeam(mine)) {
            var line = Teams.PlayerName(member) + "  " + (Teams.IsOnline(member) ? "emp_ui_team_online" : "emp_ui_team_offline").lang();
            card.Text(member == leader ? line + "  " + "emp_ui_team_leader_tag".lang() : line);
            if (manages && member != me) {
                var row = card.ButtonRow();
                row.RowButton("emp_ui_team_promote".lang(), () => Teams.Ask(TeamAsk.Promote, member));
                row.RowButton("emp_ui_team_kick".lang(), () => Teams.Ask(TeamAsk.Kick, member));
            }
        }

        var lands = Teams.LandsOf(mine).ToList();
        card.HeaderSmall("emp_ui_team_lands".lang());
        if (lands.Count == 0) {
            card.Text("emp_ui_team_no_land".lang());
        }
        foreach (var land in lands) {
            BuildLand(card, land, leader == me);
        }

        if (TeamBills.On) {
            card.Text("emp_ui_team_bills".Loc(TeamBills.UnpaidOf(mine)));
        }
        if (TeamStory.On) {
            card.Text("emp_ui_team_story".Loc(TeamStory.ChapterOf(mine)));
        }
        if (TeamBoxes.TeamBankGold() is { } bank) {
            card.Text("emp_ui_team_bank".Loc(bank));
        }

        // the leader answers who asks (the choices of the player list moved here)
        if (leader == me) {
            foreach (var asker in Teams.Asking(mine).ToList()) {
                card.Text("emp_team_asks".Loc(Teams.PlayerName(asker), Teams.NameOf(mine)));
                var row = card.ButtonRow();
                row.RowButton("emp_team_accept".Loc(Teams.PlayerName(asker)), () => {
                    Teams.Ask(TeamAsk.Accept, asker);
                });
                row.RowButton("emp_team_refuse".Loc(Teams.PlayerName(asker)), () => {
                    Teams.Ask(TeamAsk.Refuse, asker);
                });
            }
        }

        if (manages) {
            BuildManagement(mine);
        }

        card.ButtonRow().RowButton("emp_team_leave".Loc(Teams.NameOf(mine)), () => {
            Teams.Ask(TeamAsk.Leave);
        });
    }

    /// <summary>
    ///     The leader's choices (host box "Team leaders manage their team"): rename, invite the players in the game who
    ///     have no team, end the team
    /// </summary>
    private void BuildManagement(int mine)
    {
        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_team_manage".lang());
        card.ButtonRow().RowButton("emp_ui_team_rename".lang(), () => {
            Dialog.InputName("emp_ui_team_rename_ask", Teams.NameOf(mine), (cancel, text) => {
                if (!cancel) {
                    Teams.Ask(TeamAsk.Rename, text: text);
                }
            }).input.field.characterLimit = Teams.NameMax;
        });

        foreach (var invited in Teams.InvitedBy(mine)) {
            card.TextSmall("emp_ui_team_invited_line".Loc(Teams.PlayerName(invited)));
        }

        // (a player in the game, in no team and not yet asked: the host checks the same again)
        foreach (var player in NetSession.Instance.CurrentPlayers.Select(p => p.CharaUid).Distinct().ToList()) {
            if (Teams.TeamOf(player) == mine || Teams.InviteOf(player) != Teams.Wanderers) {
                continue;
            }

            var target = player;
            card.ButtonRow().RowButton("emp_ui_team_invite".Loc(Teams.PlayerName(player)), () => Teams.Ask(TeamAsk.Invite, target));
        }

        card.ButtonRow().RowButton("emp_ui_team_disband".lang(), () => {
            Dialog.YesNo("emp_ui_team_disband_confirm".Loc(Teams.NameOf(mine)), () => Teams.Ask(TeamAsk.Disband));
        });
    }

    private void BuildOtherTeams(int mine)
    {
        var others = Teams.Ids().Where(id => id != Teams.Wanderers && id != mine).ToList();
        if (others.Count == 0) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_team_others".lang());
        foreach (var team in others) {
            card.Text("emp_ui_team_line".Loc(Teams.NameOf(team), Teams.PlayerName(Teams.LeaderOf(team)), Teams.MembersOfTeam(team).Count(),
                (Teams.IsOpen(team) ? "emp_ui_team_open" : "emp_ui_team_closed").lang()));
            card.ButtonRow().RowButton("emp_team_join".Loc(Teams.NameOf(team)), () => {
                Teams.Ask(TeamAsk.Join, team);
            });
        }
    }

    /// <summary>
    ///     The bills and story chapter of every team (host options "Teams pay their own land" and "Teams keep their own
    ///     story journal"), moved here from the session page
    /// </summary>
    private void BuildTeamLines()
    {
        if (!TeamBills.On && !TeamStory.On) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_teams_title".lang());
        foreach (var team in Teams.Ids()) {
            var line = Teams.NameOf(team);
            if (TeamBills.On) {
                line += "  " + "emp_ui_team_bills".Loc(TeamBills.UnpaidOf(team));
            }
            if (TeamStory.On) {
                line += "  " + "emp_ui_team_story".Loc(TeamStory.ChapterOf(team));
            }
            card.Text(line);
        }
    }
}
