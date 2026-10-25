using ElinTogether.LangMod;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using ElinTogether.Helper;
using ElinTogether.Net;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The host's rules ("World rules"), drawn from SettingsTable: the preset line, then the choices a new host
///     makes before friends join; "Show advanced options" adds every box (council 2026-10-23)
/// </summary>
internal class TabServerConfiguration : TabEmpBase
{
    /// <summary>
    ///     Advanced rows shown: a view of this page only, never stored
    /// </summary>
    internal static bool ShowAdvanced;

    public override void OnLayout()
    {
        // (in a group that sets its width: straight on the page the line ran past the edge, see UIHelper.WrapTexts)
        Vertical().TextSmall("emp_ui_sv_note_live".lang());

        // how we play together: the preset these rules make, what differs from the recommended ones
        var top = Section("preset");
        var preset = SettingsTable.Preset(RuleSource.Current);
        var line = top.Horizontal();
        line.Layout.childForceExpandWidth = true;
        line.Text("emp_ui_sv_cfg_preset".lang());
        line.Dropdown([..SettingsTable.Presets.Select(p => $"emp_ui_sv_opt_preset_{p}".lang())], SettingsTable.ApplyPreset, preset)
            .LayoutElement().preferredWidth = 360f;
        top.TextSmall(PresetText(preset));

        var buttons = top.Horizontal();
        buttons.Layout.childForceExpandWidth = true;
        if (preset != 0) {
            buttons.Button("emp_ui_sv_reset".lang(), () => SettingsTable.ApplyPreset(0));
        }
        if (SettingsTable.CanUndo) {
            buttons.Button("emp_ui_sv_undo".lang(), SettingsTable.Undo);
        }

        top.Toggle("emp_ui_sv_advanced", ShowAdvanced, value => {
            ShowAdvanced = value;
            LayerElinTogether.Instance?.Reopen();
        });

        var rows = SettingsTable.Rows().Where(r => ShowAdvanced || !r.Advanced).ToList();
        foreach (var id in SettingsTable.Cards) {
            var inCard = rows.Where(r => r.Card == id).ToList();
            if (inCard.Count == 0) {
                continue;
            }

            var card = Section(id);
            foreach (var row in inCard) {
                Draw(row.Under is null ? card : Indented(card), row);
            }
        }
    }

    /// <summary>
    ///     What the preset means, or for Custom how many rows differ from the recommended rules and which (shared
    ///     with ModConfigPage)
    /// </summary>
    internal static string PresetText(int preset)
    {
        if (preset != 2) {
            return $"emp_ui_sv_desc_preset_{SettingsTable.Presets[preset]}".lang();
        }

        var rows = SettingsTable.Rows().ToDictionary(r => r.Id);
        var diff = SettingsTable.Differences(RuleSource.Current, RuleSource.Defaults);
        return "emp_ui_sv_diff".Loc(diff.Count, string.Join(", ", diff.Select(id => rows[id].Label)));
    }

    private YKVertical Section(string id)
    {
        var card = this.MakeCard();
        card.Layout.padding = new(20, 20, 8, 10);
        card.HeaderSmall($"emp_ui_sv_sec_{id}".lang());
        return card;
    }

    // the boxes of a choice, under it
    private static YKVertical Indented(YKVertical card)
    {
        var sub = card.Vertical();
        sub.Layout.padding = new(40, 0, 0, 0);
        return sub;
    }

    /// <summary>
    ///     A row and, under it, what it does in one line (a tooltip needs a mouse: on a controller or a Steam Deck
    ///     the line under the row is what tells)
    /// </summary>
    private static void Draw(YKVertical card, SettingRow row)
    {
        switch (row) {
            case { Choice: { } choice }:
                var line = card.Horizontal();
                line.Layout.childForceExpandWidth = true;
                line.Text(row.Label);
                var current = choice.Read(RuleSource.Current);
                var options = Enumerable.Range(0, choice.Options.Length).Select(choice.OptionLabel).ToList();
                // (boxes that match no option: shown as such, never rewritten until the host picks one)
                if (current < 0) {
                    options.Add(choice.OptionLabel(-1));
                }
                var dropdown = line.Dropdown(options, i => SettingsTable.Write(choice.WriteFor(i)), current < 0 ? options.Count - 1 : current);
                dropdown.LayoutElement().preferredWidth = 360f;
                break;
            case { Entry: { } entry }:
                var toggle = card.Toggle($"emp_ui_sv_cfg_{row.Id}", entry.Value, value => SettingsTable.Write([(entry, value)]));
                toggle.SetTooltipLang($"emp_ui_sv_desc_{row.Id}");
                // greyed while what it needs is off (the page is drawn again when that changes)
                if (row.Requires is { } requires) {
                    toggle.interactable = requires();
                }
                break;
            case { Id: "checks" }:
                BuildValidationSets(card);
                return;
            case { Id: "bills" }:
                // the count of unpaid tax bills against the bills that exist (ElinNetHost.RepairBillCount), in game
                if (EClass.core.IsGameStarted && NetSession.Instance.Transport is ElinNetHost) {
                    var (count, found) = (EClass.player.taxBills, ElinNetHost.CountTaxBills());
                    card.Button("emp_ui_sv_bills".Loc(count, found), () => {
                        var off = ElinNetHost.RepairBillCount(true);
                        EmpPop.Information((off > 0 ? "emp_ui_sv_bills_done" : "emp_ui_sv_bills_none").Loc(off));
                        LayerElinTogether.Instance?.Reopen();
                    });
                    card.TextSmall("emp_ui_sv_bills_desc".lang());
                }
                return;
        }

        card.TextSmall(row.Description);
    }

    private static void BuildValidationSets(YKVertical sets)
    {
        sets.TextSmall("emp_ui_sv_desc_validation".lang());

        var list = sets.Grid()
            .WithConstraintCount(4);
        list.Fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        list.Layout.cellSize = FitCell(4);

        var options = new List<string> {
            "none",
            "sources",
            "plugins",
            "all",
        };

        foreach (var option in options) {
            list.Toggle($"emp_validation_{option}", EmpConfig.Server.SourceValidationSet.Value.Contains(option),
                value => SettingsTable.Write([(EmpConfig.Server.SourceValidationSet, SettingsTable.ToggleCheck(EmpConfig.Server.SourceValidationSet.Value, option, value))]));
        }
    }
}
