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
            EmptyPage(card, TeamIcons, "emp_ui_team_unplugged".lang(), "emp_ui_players_go_play".lang(),
                () => LayerElinTogether.Instance?.Window.SwitchContent(TabPlay.TabName));
        } else if (NetSession.Instance.Transport is ElinNetHost) {
            EmptyPage(card, TeamIcons, "emp_ui_team_off".lang(), "emp_ui_team_turn_on".lang(), () => {
                LayerElinTogether.Instance?.Window.SwitchContent("emp_tab_server");
                TabServerConfiguration.ShowRule("teams");
            });
        } else {
            EmptyPage(card, TeamIcons, "emp_ui_team_off_guest".lang());
        }
    }


    internal static readonly string[] TeamIcons = ["icon_factions", "icon_medal"];

    /// <summary>
    ///     An empty page: a game icon, a two-line explanation and, when there is something to do, one button as wide as
    ///     its label
    /// </summary>
    internal static void EmptyPage(YKLayout card, string[] icons, string message, string? buttonText = null, System.Action? act = null)
    {
        var box = card.Vertical();
        box.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        box.Layout.childAlignment = TextAnchor.MiddleCenter;
        box.Layout.padding = new(16, 16, 10, 10);
        box.Layout.spacing = UIHelper.RowSpacing;

        var iconRow = box.Horizontal();
        iconRow.Layout.childForceExpandWidth = false;
        iconRow.Layout.childAlignment = TextAnchor.MiddleCenter;
        iconRow.SpriteIcon(48f, icons);

        box.Text(message, FontColor.Gray).alignment = TextAnchor.MiddleCenter;
        if (buttonText == null || act == null) {
            return;
        }

        var row = box.Horizontal();
        row.Layout.childForceExpandWidth = false;
        row.Layout.childAlignment = TextAnchor.MiddleCenter;
        var button = row.Button(buttonText, act);
        var element = button.LayoutElement();
        element.preferredWidth = Mathf.Max(160f, (button.mainText != null ? button.mainText.preferredWidth : 0f) + 48f);
        element.flexibleWidth = 0f;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "\u2026";

    private static string Cap(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text.Substring(1);

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

    private static Chara? CharaOf(int uid) => Teams.FindChara(uid);

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
        EmptyPage(card, TeamIcons, "emp_ui_team_none".lang(), "emp_team_found".lang(), () => {
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
        if (TeamBanner.Of(mine) != null) {
            var banner = card.Row();
            TeamBanner.Draw(banner, mine);
            banner.Text(Teams.NameOf(mine)).LayoutElement().flexibleWidth = 1f;
        }
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
            var actions = new List<(string, System.Action)>();
            if (TeamSheet.On) {
                actions.Add(("emp_ui_team_sheet".lang(), () => TeamSheet.Ask(target)));
            }
            if (manages && member != me) {
                actions.Add(("emp_ui_team_promote".lang(), () => Teams.Ask(TeamAsk.Promote, target)));
                actions.Add(("emp_ui_team_kick".lang(), () => Teams.Ask(TeamAsk.Kick, target)));
            }
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
        if (TeamBanner.On) {
            BuildBannerCard(right, mine, leads);
        }
    }

    // the banner picker is open (kept over reopenings)
    private static bool _bannerPicker;

    /// <summary>
    ///     Host box "Team banners": the leader's "Banner" tool and its picker, the map of the team's lands and the
    ///     team journal. The host checks that the asker leads the team
    /// </summary>
    private static void BuildBannerCard(YKLayout page, int mine, bool leads)
    {
        var card = page.Card("emp_ui_team_banner".lang());
        var tool = card.ButtonRow().RowButton("emp_ui_team_banner_tool".lang(), () => {
            _bannerPicker = !_bannerPicker;
            LayerElinTogether.Instance?.Reopen();
        });
        tool.interactable = leads;
        if (!leads) {
            card.TextSmall("emp_ui_team_tools_leader_only".lang(), FontColor.Gray);
        } else if (_bannerPicker) {
            TeamBanner.BuildPicker(card, mine);
        }

        var lands = page.Card("emp_ui_team_lands_map".lang());
        TeamBanner.BuildLandMap(lands, mine);

        var journal = page.Card("emp_ui_team_journal".lang());
        // the activity journal's entries of this team, newest first (box "Activity journal")
        var lines = ActivityJournal.On
            ? ActivityJournal.Entries().Where(e => e.Team == mine).Reverse().Take(20).ToList()
            : [];
        if (lines.Count == 0) {
            journal.TextSmall("emp_ui_team_journal_none".lang(), FontColor.Gray);
        }

        foreach (var entry in lines) {
            journal.TextSmall(ActivityJournal.Line(entry));
        }
    }

    /// <summary>
    ///     The right page: the picked member's facts and, for a leader who manages the team, promote and remove
    /// </summary>
    private static void BuildMemberPage(YKVertical page, int member, int leader, int me, bool manages)
    {
        var chara = CharaOf(member);
        var card = page.Card(Teams.PlayerName(member));
        if (chara != null) {
            // the portrait on top, the facts under it across the page, as the character sheet lays them out
            card.Row().Portrait(chara);
        }
        card.KeyValue("emp_ui_kv_character".lang(), chara?.Name);
        card.KeyValue("emp_ui_kv_level".lang(), chara?.LV.ToString());
        card.KeyValue("emp_ui_kv_state".lang(), Cap((Teams.IsOnline(member) ? "emp_ui_team_online" : "emp_ui_team_offline").lang()));
        card.KeyValue("emp_ui_kv_role".lang(), member == leader ? "emp_ui_team_crown".lang() : "emp_ui_role_player".lang());

        if (TeamSheet.On) {
            card.ButtonRow().RowButton("emp_ui_team_sheet".lang(), () => TeamSheet.Ask(member));
        }

        if (manages && member != me) {
            card.Divider();
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
            on => Teams.Ask(TeamAsk.SetOpen, on ? 1 : 0), readOnly: !manages, setByHostText: "emp_ui_team_leader_only_chip".lang(), ownLines: true);

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
        // more than a few teams: a frame of a fixed height that scrolls, not a page that grows
        YKLayout list = others.Count > MaxTeamRows ? ScrollFrame(card, MaxTeamRows * TeamRowHeight) : card;
        foreach (var team in others) {
            var row = list.Row();
            var element = row.LayoutElement();
            element.minHeight = element.preferredHeight = TeamRowHeight;
            TeamBanner.Draw(row, team, 22f);
            row.TextColumn(Clip(Teams.NameOf(team), 24),
                "emp_ui_team_leader_short".Loc(Clip(Teams.PlayerName(Teams.LeaderOf(team)), 16), Teams.MembersOfTeam(team).Count()));
            var id = team;
            // (the joining rule is the button's hover text, said once instead of on every row)
            var join = row.Button("emp_ui_team_join_short".lang(), () => Teams.Ask(TeamAsk.Join, id));
            join.SetTooltipLang(Teams.IsOpen(team) ? "emp_ui_team_chip_open" : "emp_ui_team_chip_closed");
            var le = join.LayoutElement();
            le.preferredWidth = 90f;
            le.flexibleWidth = 0f;
        }
    }

    private const int MaxTeamRows = 5;
    private const float TeamRowHeight = 54f;

    /// <summary>
    ///     A clipped frame of a fixed height holding a list that scrolls by wheel or drag; returns the list to fill
    /// </summary>
    private static YKVertical ScrollFrame(YKLayout parent, float height)
    {
        var frame = new GameObject("TeamScroll", typeof(RectTransform), typeof(LayoutElement), typeof(RectMask2D), typeof(Image), typeof(ScrollRect));
        frame.transform.SetParent(parent.transform, false);
        var element = frame.GetComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        element.flexibleWidth = 1f;
        frame.GetComponent<Image>().color = new(0f, 0f, 0f, 0.001f);

        var content = parent.Vertical();
        content.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        content.Layout.spacing = UIHelper.RowSpacing;
        content.transform.SetParent(frame.transform, false);
        var rt = (RectTransform)content.transform;
        rt.anchorMin = new(0f, 1f);
        rt.anchorMax = new(1f, 1f);
        rt.pivot = new(0.5f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var scroll = frame.GetComponent<ScrollRect>();
        scroll.content = rt;
        scroll.viewport = (RectTransform)frame.transform;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return content;
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
