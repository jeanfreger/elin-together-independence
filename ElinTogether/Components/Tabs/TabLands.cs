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
///     The lands page (hub plan step 5): a card per land of the player's team, with its place, what the team and
///     everyone else may do there, its offline protection and its container log, then the team bank and the player's
///     delivery box. The leader changes the rules; the others see the same drop-downs, greyed
/// </summary>
internal class TabLands : TabEmpBase
{
    private static readonly string[] LandIcons = ["icon_container", "icon_influence"];

    public override void OnLayout()
    {
        if (Waits()) {
            return;
        }

        if (!TabTeam.TeamsOn) {
            TabTeam.BuildOff(this, "emp_ui_tab_lands");
            return;
        }

        var me = EClass.pc.uid;
        var mine = Teams.TeamOf(me);
        if (mine == Teams.Wanderers) {
            TabTeam.EmptyPage(this.Card("emp_ui_tab_lands".lang()), LandIcons, "emp_ui_land_noteam".lang(), "emp_ui_land_go_team".lang(),
                () => LayerElinTogether.Instance?.Window.SwitchContent("emp_tab_team"));
            BuildBoxes(this, mine);
            return;
        }

        var lands = Teams.LandsOf(mine).ToList();
        if (lands.Count == 0) {
            TabTeam.EmptyPage(this.Card("emp_ui_tab_lands".lang()), LandIcons, "emp_ui_land_none".lang());
            BuildBoxes(this, mine);
            return;
        }

        // the journal's two pages: the lands (scrolled past five) and the boxes on the left, the picked land's rules
        // on the right (a card per land ran under the window's bottom edge)
        var (left, right) = this.Book();
        var picked = lands.Contains(_picked) ? _picked : lands[0];
        var list = left.Card("emp_ui_tab_lands".lang()).ListArea(lands.Count, 5, 44f);
        foreach (var land in lands) {
            var target = land;
            list.PickLine(Teams.ZoneName(land), Where(land), land == picked, () => {
                _picked = target;
                LayerElinTogether.Instance?.Reopen();
            });
        }

        BuildBoxes(left, mine);
        BuildLand(right, picked, Teams.LeaderOf(mine) == me);
    }

    // the land whose rules are open on the right, kept over reopenings
    private static int _picked;

    private static string Level(int level) => $"emp_ui_team_rule_{level}".lang();

    /// <summary>
    ///     Where a land is: the zone it lies in, its world map otherwise
    /// </summary>
    private static string Where(int land)
    {
        var spatial = EClass.game.spatials.Find(land);
        return spatial?.parent is { } parent && parent.Name is { Length: > 0 } name ? "emp_ui_land_where".Loc(name) : "";
    }

    private static void BuildLand(YKLayout page, int land, bool leads)
    {
        var card = page.Card(Teams.ZoneName(land));
        if (Where(land) is { Length: > 0 } where) {
            card.TextSmall(where, FontColor.Gray);
        }

        if (!Teams.LandRulesOn) {
            card.TextSmall("emp_ui_land_rules_off".lang(), FontColor.Gray);
        } else {
            var rule = Teams.RuleOf(land);
            var (mineLevel, others) = (Teams.MineLevel(rule), Teams.OthersLevel(rule));
            // (each dropdown under its title, the page's width: beside it, a fixed-width dropdown ran off the page)
            // (the team keeps at least Visit: a land closed to its own team would shut its owners out)
            var own = new List<string> { Level(Teams.Visit), Level(Teams.Use), Level(Teams.Build) };
            card.DropdownSettingRow("emp_ui_team_rule_mine".lang(), null, own, mineLevel - 1,
                i => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(i + 1, others, Teams.OfflineClosed(rule)), land), readOnly: !leads);
            var all = new List<string> { Level(Teams.Closed), Level(Teams.Visit), Level(Teams.Use), Level(Teams.Build) };
            card.DropdownSettingRow("emp_ui_team_rule_others".lang(), null, all, others,
                i => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(mineLevel, i, Teams.OfflineClosed(rule)), land), readOnly: !leads);
            // offline protection: while nobody of the team is in the game, the land is closed to everyone else
            card.ToggleRow("emp_ui_team_rule_offline".lang(), null, Teams.OfflineClosed(rule),
                on => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(mineLevel, others, on), land),
                readOnly: !leads, setByHostText: "emp_ui_team_leader_only_chip".lang());
            if (!leads) {
                card.TextSmall("emp_ui_land_leader_decides".lang(), FontColor.Gray);
            }
        }

        // who opened what on the land (host box "Container access log"); the host checks who may read it
        if (ContainerLog.On) {
            card.ButtonRow().RowButton("emp_ui_container_logs".lang(), () => ContainerLog.Ask(0));
        }
    }

    /// <summary>
    ///     The team bank and the player's delivery box, with the game's own icons for them
    /// </summary>
    private static void BuildBoxes(YKLayout page, int mine)
    {
        var bank = mine == Teams.Wanderers ? null : TeamBoxes.TeamBankBox();
        var delivery = TeamBoxes.DeliveryBox();
        if (bank is null && delivery is null) {
            return;
        }

        // (the header says what is shown: both boxes, or the one there is)
        var header = bank is not null && delivery is not null ? "emp_ui_land_boxes" : bank is not null ? "emp_ui_land_team_bank" : "emp_ui_land_delivery";
        var card = page.Card(header.lang(), EmpUi.Icon("icon_container"));
        if (bank is not null) {
            BoxRow(card, bank, "emp_ui_land_team_bank".lang(), "emp_ui_land_bank_line".Loc(bank.GetCurrency(), bank.things.Count));
        }

        if (delivery is not null) {
            if (bank is not null) {
                card.Divider();
            }

            BoxRow(card, delivery, "emp_ui_land_delivery".lang(), "emp_ui_land_items".Loc(delivery.things.Count));
        }
    }

    private static void BoxRow(YKLayout card, Thing box, string title, string detail)
    {
        var row = card.Row();
        row.CardIcon(box);
        row.TextColumn(title, detail);
    }
}
