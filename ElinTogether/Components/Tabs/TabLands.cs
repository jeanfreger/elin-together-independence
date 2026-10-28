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
    public override void OnLayout()
    {
        if (!TabTeam.TeamsOn) {
            TabTeam.BuildOff(this, "emp_ui_tab_lands");
            return;
        }

        var me = EClass.pc.uid;
        var mine = Teams.TeamOf(me);
        if (mine == Teams.Wanderers) {
            this.Card("emp_ui_tab_lands".lang()).EmptyText("emp_ui_land_noteam".lang());
            BuildBoxes(mine);
            return;
        }

        var lands = Teams.LandsOf(mine).ToList();
        if (lands.Count == 0) {
            this.Card("emp_ui_tab_lands".lang()).EmptyText("emp_ui_land_none".lang());
        }

        foreach (var land in lands) {
            BuildLand(land, Teams.LeaderOf(mine) == me);
        }

        BuildBoxes(mine);
    }

    private static string Level(int level) => $"emp_ui_team_rule_{level}".lang();

    /// <summary>
    ///     Where a land is: the zone it lies in, its world map otherwise
    /// </summary>
    private static string Where(int land)
    {
        var spatial = EClass.game.spatials.Find(land);
        return spatial?.parent is { } parent && parent.Name is { Length: > 0 } name ? "emp_ui_land_where".Loc(name) : "";
    }

    private void BuildLand(int land, bool leads)
    {
        var card = this.Card(Teams.ZoneName(land), EmpUi.Icon("icon_LayerHome"));
        if (Where(land) is { Length: > 0 } where) {
            card.TextSmall(where, FontColor.Gray);
        }

        if (!Teams.LandRulesOn) {
            card.TextSmall("emp_ui_land_rules_off".lang(), FontColor.Gray);
        } else {
            var rule = Teams.RuleOf(land);
            var (mineLevel, others) = (Teams.MineLevel(rule), Teams.OthersLevel(rule));
            // (the team keeps at least Visit: a land closed to its own team would shut its owners out)
            var own = new List<string> { Level(Teams.Visit), Level(Teams.Use), Level(Teams.Build) };
            var dropdown = card.DropdownRow("emp_ui_team_rule_mine".lang(), own,
                i => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(i + 1, others, Teams.OfflineClosed(rule)), land), mineLevel - 1);
            dropdown.interactable = leads;
            var all = new List<string> { Level(Teams.Closed), Level(Teams.Visit), Level(Teams.Use), Level(Teams.Build) };
            dropdown = card.DropdownRow("emp_ui_team_rule_others".lang(), all,
                i => Teams.Ask(TeamAsk.LandRule, Teams.PackRule(mineLevel, i, Teams.OfflineClosed(rule)), land), others);
            dropdown.interactable = leads;
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
    private void BuildBoxes(int mine)
    {
        var bank = mine == Teams.Wanderers ? null : TeamBoxes.TeamBankBox();
        var delivery = TeamBoxes.DeliveryBox();
        if (bank is null && delivery is null) {
            return;
        }

        var card = this.Card("emp_ui_land_boxes".lang(), EmpUi.Icon("icon_container"));
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
        var row = card.Horizontal();
        row.Layout.childForceExpandWidth = false;
        row.Layout.childAlignment = TextAnchor.MiddleLeft;
        row.Layout.spacing = 12f;
        row.CardIcon(box);
        var col = row.Vertical();
        col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        col.Layout.spacing = 0f;
        col.LayoutElement().flexibleWidth = 1f;
        col.Text(title).SetSize(UIHelper.LabelSize);
        col.TextSmall(detail, FontColor.Gray);
    }
}
