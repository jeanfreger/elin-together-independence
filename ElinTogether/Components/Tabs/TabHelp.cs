using ElinTogether.Helper;
using ElinTogether.LangMod;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The "Help" page (hub step 7): short "how it works" cards, About (version, the GitHub page) and the bug report,
///     which used to hide at the bottom of the client settings
/// </summary>
internal class TabHelp : TabEmpBase
{
    private const string Repo = "https://github.com/jeanfreger/elin-together-independence";

    private static readonly (string Id, string Icon)[] Topics = [
        ("hosting", "icon_LayerTravel"),
        ("joining", "icon_goto"),
        ("teams", "icon_factions"),
        ("lands", "icon_LayerHome"),
        ("trading", "icon_money"),
        ("duels", "icon_hp"),
    ];

    public override void OnLayout()
    {
        foreach (var (id, icon) in Topics) {
            var card = this.Card($"emp_help_{id}_t".lang(), EmpUi.Icon(icon));
            card.Text($"emp_help_{id}".lang());
        }

        BuildAbout();
        BuildReport();
    }

    private void BuildAbout()
    {
        var card = this.Card("emp_help_about".lang(), EmpUi.Icon("icon_hint"));
        card.Text("emp_help_version".Loc(ModInfo.BuildVersion));
        card.TextSmall("emp_help_about_desc".lang(), FontColor.Gray);
        card.ButtonRow().RowButton("emp_help_github".lang(), () => Application.OpenURL(Repo));
    }

    // something went wrong: one zip to send with the game's own log in it, or a GitHub issue with the player's words
    private void BuildReport()
    {
        var card = this.Card("emp_help_report".lang(), EmpUi.Icon("icon_Inspect"));
        card.Text("emp_ui_report_what".lang());
        card.TextSmall("emp_ui_report_send_desc".lang(), FontColor.Gray);

        var input = card.InputText("");
        input.field.characterLimit = 3000;
        input.field.lineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
        input.LayoutElement().minHeight = 120f;
        input.LayoutElement().preferredHeight = 120f;

        var buttons = card.ButtonRow();
        buttons.RowButton("emp_ui_report_send".lang(), () => {
            var words = input.Text.Trim();
            if (words.Length < 3) {
                return;
            }

            input.Text = "";
            EmpPop.Information("emp_ui_report_sending".lang());
            BugReport.Send(words, url => {
                if (url is null) {
                    EmpPop.Information("emp_ui_report_page".lang());
                    return;
                }

                Dialog.YesNo("emp_ui_report_sent".Loc(url), () => Application.OpenURL(url));
            });
        });
        buttons.RowButton("emp_ui_report".lang(), () => {
            if (BugReport.Make() is { } path) {
                EmpPop.Information("emp_ui_report_done".Loc(path));
                Util.ShowExplorer(path);
            } else {
                EmpPop.Information("emp_ui_report_failed".lang());
            }
        });
        card.TextSmall("emp_ui_report_desc".lang(), FontColor.Gray);

        // the last seconds of the screen sent with the report (ReplayBuffer)
        var replay = EmpConfig.Client.RecordReplay;
        card.ToggleRow("emp_ui_cl_record_replay".lang(), null, replay.Value, v => replay.Value = v)
            .SetTooltipLang(replay.Description.Description);
    }
}
