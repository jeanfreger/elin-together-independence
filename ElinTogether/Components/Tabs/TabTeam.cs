using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The team page (hub plan step 5, council 2026-10-28): the same in every game, the host's own included, and its
///     tab is always there. The player's team and its members, the leader's tools, the requests and invitations, and
///     the choices the game's player list already offered (found, join, leave, accept, refuse), asked of the game that
///     keeps the book (<see cref="Teams.Ask" />). Its lands are on the next page (<see cref="TabLands" />)
/// </summary>
internal class TabTeam : TabEmpBase
{
    /// <summary>
    ///     Teams exist in this game: a connected game whose rules have them on
    /// </summary>
    internal static bool TeamsOn => NetSession.Instance is { HasActiveConnection: true, Rules.UseTeams: true } && EClass.pc != null;

    /// <summary>
    ///     "Teams are off in this world": the empty state both pages show. Only the host gets the button that opens the
    ///     World rules page at the Teams row
    /// </summary>
    internal static void BuildOff(YKLayout page, string titleId)
    {
        var card = page.Card(titleId.lang());
        if (!NetSession.Instance.HasActiveConnection) {
            card.EmptyState("emp_ui_team_unplugged".lang(), "emp_ui_players_go_play".lang(),
                () => LayerElinTogether.Instance?.Window.SwitchContent(TabPlay.TabName));
        } else if (NetSession.Instance.Transport is ElinNetHost) {
            card.EmptyState("emp_ui_team_off".lang(), "emp_ui_team_turn_on".lang(), () => {
                LayerElinTogether.Instance?.Window.SwitchContent("emp_tab_server");
                TabServerConfiguration.ShowRule("teams");
            });
        } else {
            card.EmptyText("emp_ui_team_off_guest".lang());
        }
    }

    public override void OnLayout()
    {
        if (!TeamsOn) {
            BuildOff(this, "emp_ui_team_title");
            return;
        }

        // the journal's two pages: the team and its members on the left, the picked member and the leader's tools
        // on the right (without a team: founding and invitations on the left, the other teams on the right)
        var me = EClass.pc.uid;
        var mine = Teams.TeamOf(me);
        var (left, right) = this.Book();
        BuildInvitation(left, me);
        if (mine == Teams.Wanderers) {
            BuildFound(left);
            BuildOtherTeams(right, mine);
            BuildTeamLines(right);
        } else {
            BuildOwnTeam(left, right, mine, me);
            BuildOtherTeams(left, mine);
            BuildTeamLines(left);
        }
    }

    // the member whose page is open on the right, kept over reopenings
    private static int _picked;

    private static Chara? CharaOf(int uid) => EClass.game.cards.globalCharas.Find(uid);

    /// <summary>
    ///     A line with a text and the two answers of a request or an invitation
    /// </summary>
    private static void AnswerRow(YKLayout card, string text, System.Action accept, System.Action refuse)
    {
        var row = card.Row();
        row.Text(text).LayoutElement().flexibleWidth = 1f;
        foreach (var (label, act) in new[] { ("emp_ui_team_invite_accept".lang(), accept), ("emp_ui_team_invite_refuse".lang(), refuse) }) {
            var element = row.Button(label, act).LayoutElement();
            element.preferredWidth = 130f;
            element.flexibleWidth = 0f;
        }
    }

    /// <summary>
    ///     A team invited this player: a row with the two answers (no popup)
    /// </summary>
    private static void BuildInvitation(YKLayout page, int me)
    {
        var team = Teams.InviteOf(me);
        if (team == Teams.Wanderers) {
            return;
        }

        var card = page.Card("emp_ui_team_invite_title".lang());
        AnswerRow(card, "emp_ui_team_invite_line".Loc(Teams.NameOf(team), Teams.PlayerName(Teams.LeaderOf(team))),
            () => Teams.Ask(TeamAsk.AcceptInvite, team), () => Teams.Ask(TeamAsk.RefuseInvite, team));
    }

    private static void BuildFound(YKLayout page)
    {
        var card = page.Card("emp_ui_team_title".lang());
        card.EmptyState("emp_ui_team_none".lang(), "emp_team_found".lang(), () => {
            Dialog.InputName("emp_ui_team_name_ask", EClass.pc.NameSimple, (cancel, text) => {
                if (!cancel) {
                    Teams.Ask(TeamAsk.Found, text: text);
                }
            }).input.field.characterLimit = Teams.NameMax;
        });
    }

    private static void BuildOwnTeam(YKVertical left, YKVertical right, int mine, int me)
    {
        var leader = Teams.LeaderOf(mine);
        var leads = leader == me;
        var manages = leads && Teams.LeadersManage;
        var members = Teams.MembersOfTeam(mine).ToList();

        // the header: the team's name, then its facts as the character sheet lists them
        var card = left.Card(Teams.NameOf(mine));
        card.KeyValue("emp_ui_kv_leader".lang(), Teams.PlayerName(leader));
        card.KeyValue("emp_ui_kv_members".lang(), members.Count.ToString());
        card.KeyValue("emp_ui_kv_joining".lang(), (Teams.IsOpen(mine) ? "emp_ui_team_chip_open" : "emp_ui_team_chip_closed").lang());
        if (TeamBoxes.TeamBankGold() is { } bank) {
            card.KeyValue("emp_ui_kv_bank".lang(), bank.ToString());
        }
        if (TeamBills.On) {
            card.KeyValue("emp_ui_kv_bills".lang(), TeamBills.UnpaidOf(mine).ToString());
        }
        if (TeamStory.On) {
            card.KeyValue("emp_ui_kv_story".lang(), TeamStory.ChapterOf(mine).ToString());
        }

        // the members: a row each; a click opens the member's page on the right
        var list = left.Card("emp_ui_kv_members".lang());
        var picked = members.Contains(_picked) ? _picked : me;
        foreach (var member in members) {
            var chara = CharaOf(member);
            var target = member;
            // the "..." of a member the leader manages: the same two choices as the member's page, one click nearer
            IReadOnlyList<(string, System.Action)> actions = manages && member != me
                ? [("emp_ui_team_promote".lang(), () => Teams.Ask(TeamAsk.Promote, target)),
                    ("emp_ui_team_kick".lang(), () => Teams.Ask(TeamAsk.Kick, target))]
                : [];
            var row = list.PickRow(new PlayerRowInfo(Teams.PlayerName(member), MemberLine(chara, member), chara), member == picked, () => {
                _picked = target;
                LayerElinTogether.Instance?.Reopen();
            }, actions);
            if (member == leader) {
                AddCrown(row);
            }
        }

        list.ButtonRow().RowButton("emp_team_leave".Loc(Teams.NameOf(mine)), () => Teams.Ask(TeamAsk.Leave));

        BuildMemberPage(right, picked, leader, me, manages);

        // the leader answers who asks (the choices of the player list moved here)
        if (leads) {
            var asking = Teams.Asking(mine).ToList();
            if (asking.Count > 0) {
                var requests = left.Card("emp_ui_team_requests".lang());
                foreach (var asker in asking) {
                    var target = asker;
                    AnswerRow(requests, "emp_ui_team_asks_row".Loc(Teams.PlayerName(asker)),
                        () => Teams.Ask(TeamAsk.Accept, target), () => Teams.Ask(TeamAsk.Refuse, target));
                }
            }
        }

        BuildTools(right, mine, leads, manages);
    }

    /// <summary>
    ///     The right page: the picked member's facts and, for a leader who manages the team, promote and remove
    /// </summary>
    private static void BuildMemberPage(YKVertical page, int member, int leader, int me, bool manages)
    {
        var chara = CharaOf(member);
        var card = page.Card(Teams.PlayerName(member));
        var head = card.Row();
        if (chara != null) {
            // (the portrait alone: a whole player row here kept an empty flexible name column between it and the facts)
            head.Portrait(chara);
        }
        var lines = head.Vertical();
        lines.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        lines.LayoutElement().flexibleWidth = 1f;
        lines.KeyValue("emp_ui_kv_character".lang(), chara?.Name);
        lines.KeyValue("emp_ui_kv_level".lang(), chara?.LV.ToString());
        lines.KeyValue("emp_ui_kv_state".lang(), (Teams.IsOnline(member) ? "emp_ui_team_online" : "emp_ui_team_offline").lang());
        lines.KeyValue("emp_ui_kv_role".lang(), member == leader ? "emp_ui_team_crown".lang() : "emp_ui_role_player".lang());

        if (manages && member != me) {
            var buttons = card.ButtonRow();
            buttons.RowButton("emp_ui_team_promote".lang(), () => Teams.Ask(TeamAsk.Promote, member));
            buttons.RowButton("emp_ui_team_kick".lang(), () => Teams.Ask(TeamAsk.Kick, member));
        }
    }

    private static string MemberLine(Chara? chara, int member)
    {
        var state = (Teams.IsOnline(member) ? "emp_ui_team_online" : "emp_ui_team_offline").lang();
        return chara != null ? "emp_ui_team_detail".Loc(chara.LV, state) : state;
    }

    /// <summary>
    ///     A crown by the leader's name: the game's own sprite when it has one, a "Leader" chip otherwise
    /// </summary>
    private static void AddCrown(YKHorizontal row)
    {
        var before = row.transform.childCount;
        if (!row.SpriteIcon(22f, "icon_medal")) {
            row.Chip("emp_ui_team_crown".lang());
        }

        // (before the "..." button, after the name)
        if (row.transform.childCount > before) {
            row.transform.GetChild(row.transform.childCount - 1).SetSiblingIndex(2);
        }
    }

    /// <summary>
    ///     The leader's tools in one card (host box "Team leaders manage their team"): rename, who may join, invite the
    ///     players in the game who have no team, end the team. Grey, with one line saying why, for anyone else
    /// </summary>
    private static void BuildTools(YKLayout page, int mine, bool leads, bool manages)
    {
        var card = page.Card("emp_ui_team_manage".lang());
        if (!manages) {
            card.TextSmall((leads ? "emp_ui_team_tools_host_off" : "emp_ui_team_tools_leader_only").lang(), FontColor.Gray);
        }

        void Tool(UIButton button) => button.interactable = manages;

        Tool(card.ButtonRow().RowButton("emp_ui_team_rename".lang(), () => {
            Dialog.InputName("emp_ui_team_rename_ask", Teams.NameOf(mine), (cancel, text) => {
                if (!cancel) {
                    Teams.Ask(TeamAsk.Rename, text: text);
                }
            }).input.field.characterLimit = Teams.NameMax;
        }));

        card.ToggleRow("emp_ui_team_joining".lang(), "emp_ui_team_joining_desc".lang(), Teams.IsOpen(mine),
            on => Teams.Ask(TeamAsk.SetOpen, on ? 1 : 0), readOnly: !manages, setByHostText: "emp_ui_team_leader_only_chip".lang());

        if (manages) {
            foreach (var invited in Teams.InvitedBy(mine)) {
                card.TextSmall("emp_ui_team_invited_line".Loc(Teams.PlayerName(invited)), FontColor.Gray);
            }

            // (a player in the game, in no team and not yet asked: the host checks the same again)
            var open = NetSession.Instance.CurrentPlayers.Select(p => p.CharaUid).Distinct()
                .Where(uid => Teams.TeamOf(uid) != mine && Teams.InviteOf(uid) == Teams.Wanderers).ToList();
            if (open.Count == 0) {
                card.TextSmall("emp_ui_team_invite_none".lang(), FontColor.Gray);
            }

            foreach (var player in open) {
                var target = player;
                card.ButtonRow().RowButton("emp_ui_team_invite".Loc(Teams.PlayerName(player)), () => Teams.Ask(TeamAsk.Invite, target));
            }
        } else {
            Tool(card.ButtonRow().RowButton("emp_ui_team_invite_any".lang(), () => { }));
        }

        Tool(card.ButtonRow().RowButton("emp_ui_team_disband".lang(), () => {
            Dialog.YesNo("emp_ui_team_disband_confirm".Loc(Teams.NameOf(mine)), () => Teams.Ask(TeamAsk.Disband));
        }));
    }

    private static void BuildOtherTeams(YKLayout page, int mine)
    {
        var others = Teams.Ids().Where(id => id != Teams.Wanderers && id != mine).ToList();
        if (others.Count == 0) {
            return;
        }

        var card = page.Card("emp_ui_team_others".lang());
        foreach (var team in others) {
            var row = card.Row();
            row.TextColumn(Teams.NameOf(team),
                "emp_ui_team_leader_line".Loc(Teams.PlayerName(Teams.LeaderOf(team)), Teams.MembersOfTeam(team).Count()));
            row.Chip((Teams.IsOpen(team) ? "emp_ui_team_chip_open" : "emp_ui_team_chip_closed").lang());
            var join = row.Button("emp_team_join".Loc(Teams.NameOf(team)), () => Teams.Ask(TeamAsk.Join, team)).LayoutElement();
            join.preferredWidth = 200f;
            join.flexibleWidth = 0f;
        }
    }

    /// <summary>
    ///     The bills and story chapter of every team (host options "Teams pay their own land" and "Teams keep their own
    ///     story journal"), moved here from the session page
    /// </summary>
    private static void BuildTeamLines(YKLayout page)
    {
        if (!TeamBills.On && !TeamStory.On) {
            return;
        }

        var card = page.Card("emp_ui_teams_title".lang());
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
