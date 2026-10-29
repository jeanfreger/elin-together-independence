using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The "Help" page (hub step 7): short "how it works" cards, About (version, the GitHub page) and the bug report,
///     which used to hide at the bottom of the client settings
/// </summary>
internal static class TabHelp
{
    private const string Repo = "https://github.com/jeanfreger/elin-together-independence";

    // topic, the tab where it is done (tab name, its label id)
    private static readonly (string Id, string Tab, string Label)[] Topics = [
        ("hosting", TabPlay.TabName, "emp_ui_tab_play"),
        ("joining", TabPlay.TabName, "emp_ui_tab_play"),
        ("teams", "emp_tab_team", "emp_ui_tab_team"),
        ("lands", "emp_tab_lands", "emp_ui_tab_lands"),
        ("trading", TabPlayers.TabName, "emp_ui_tab_players"),
        ("duels", "emp_tab_server", "emp_ui_tab_server"),
    ];

    /// <summary>The Help pages, shown as sub-tabs of the Server info tab</summary>
    public static List<(string, System.Action<YKVertical>)> Pages()
    {
        return [
            ("emp_ui_sub_how".lang(), BuildHow),
            ("emp_help_about".lang(), BuildAbout),
            ("emp_help_report".lang(), BuildReport),
        ];
    }

    // the topic open on the right page
    private static int _topic;

    /// <summary>
    ///     The journal's two pages: the topics on the left (a click opens one), the picked topic's words and the button
    ///     to its page on the right (six cards one under the other ran under the window's edge)
    /// </summary>
    private static void BuildHow(YKVertical page)
    {
        var (left, right) = page.Book();
        var list = left.Card("emp_ui_sub_how".lang());
        var text = right.Vertical();
        text.Fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        text.Layout.spacing = UIHelper.RowSpacing;
        var rows = new List<YKHorizontal>();

        void Show(int index, bool relayout = true)
        {
            _topic = index;
            foreach (Transform child in text.transform) {
                Object.Destroy(child.gameObject);
            }

            var (id, tab, label) = Topics[index];
            var card = text.Card($"emp_help_{id}_t".lang());
            card.Text($"emp_help_{id}".lang());
            card.ButtonRow().RowButton("emp_ui_help_open".Loc(label.lang()),
                () => LayerElinTogether.Instance?.Window.SwitchContent(tab));
            for (var i = 0; i < rows.Count; i++) {
                if (rows[i].GetComponent<UnityEngine.UI.Image>() is { } shade) {
                    shade.color = i == index ? new(0.28f, 0.22f, 0.13f, 0.14f) : new(0f, 0f, 0f, 0.001f);
                }
            }

            UIHelper.WrapTexts(text);
            if (relayout) {
                page.GetComponentInParent<TabEmpBase>()?.Relayout();
            }
        }

        for (var i = 0; i < Topics.Length; i++) {
            var index = i;
            rows.Add(list.PickLine($"emp_help_{Topics[i].Id}_t".lang(), null, i == _topic, () => Show(index)));
        }

        Show(Mathf.Clamp(_topic, 0, Topics.Length - 1), false);
    }

    private static void BuildAbout(YKVertical page)
    {
        var card = page.Card("emp_help_about".lang());
        card.Text("emp_help_version".Loc(""));
        card.KeyValue("emp_ui_kv_version".lang(), ModInfo.BuildVersion);
        card.TextSmall("emp_help_about_desc".lang(), FontColor.Gray);
        card.ButtonRow().RowButton("emp_help_github".lang(), () => Application.OpenURL(Repo));
    }

    // something went wrong: one zip to send with the game's own log in it, or a GitHub issue with the player's words
    private static void BuildReport(YKVertical page)
    {
        var card = page.Card("emp_help_report".lang());
        card.Text("emp_ui_report_what".lang());
        card.TextSmall("emp_ui_report_send_desc".lang(), FontColor.Gray);

        var input = card.InputText("");
        input.field.characterLimit = 3000;
        input.AllowAnyText();
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
