using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
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
        if (mine == Teams.Wanderers) {
            BuildFound();
        } else {
            BuildOwnTeam(mine, pc.uid);
        }

        BuildOtherTeams(mine);
        BuildTeamLines();
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
        foreach (var uid in Teams.MembersOfTeam(mine)) {
            var line = Teams.PlayerName(uid) + "  " + (Teams.IsOnline(uid) ? "emp_ui_team_online" : "emp_ui_team_offline").lang();
            card.Text(uid == leader ? line + "  " + "emp_ui_team_leader_tag".lang() : line);
        }

        var lands = Teams.LandsOf(mine).ToList();
        card.HeaderSmall("emp_ui_team_lands".lang());
        card.Text(lands.Count == 0 ? "emp_ui_team_no_land".lang() : string.Join(", ", lands.Select(Teams.ZoneName)));

        if (TeamBills.On) {
            card.Text("emp_ui_team_bills".Loc(TeamBills.UnpaidOf(mine)));
        }
        if (TeamStory.On) {
            card.Text("emp_ui_team_story".Loc(TeamStory.ChapterOf(mine)));
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

        card.ButtonRow().RowButton("emp_team_leave".Loc(Teams.NameOf(mine)), () => {
            Teams.Ask(TeamAsk.Leave);
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
