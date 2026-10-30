using System.Collections.Generic;
using System.Linq;
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

    // the report's draft, kept while the window is closed and opened again, cleared once sent
    private static string _doing = "";
    private static string _happened = "";
    private static string _expected = "";
    private static int _again = 2;

    internal static ReportAnswers Draft => new(_doing, _happened, _expected, _again);

    // something went wrong: one zip to send with the game's own log in it, or a GitHub issue with the player's answers
    // (a guided form, user 2026-10-30: reports came as one title, "fortune drum never changes", nothing else)
    private static void BuildReport(YKVertical page)
    {
        var card = page.Card("emp_help_report".lang());
        // (the video stops while this page is shown: it keeps the 30 seconds before the report was opened)
        card.gameObject.AddComponent<ReplayBuffer.Hold>();
        card.Text("emp_ui_report_what".lang());
        card.TextSmall("emp_ui_report_send_desc".lang(), FontColor.Gray);

        UIButton? send = null;
        UIText? hint = null;
        var boxes = new List<UIInputText>();

        void Refresh()
        {
            var missing = Draft.Missing();
            send?.interactable = missing is null;
            hint?.SetText(missing is null ? "" : "emp_ui_report_need".Loc(ReportAnswers.MinLength, string.Join(", ", missing.Select(id => id.lang()))));
        }

        void Box(string label, string value, System.Action<string> keep)
        {
            card.Text(label.lang());
            var input = card.InputText(value);
            boxes.Add(input);
            input.field.characterLimit = ReportAnswers.MaxLength;
            input.AllowAnyText();
            // three or four lines each, the text from the top left, wrapped, Enter a new line
            TextBox(input, 96f);
            input.field.onValueChanged.AddListener(text => {
                keep(text);
                Refresh();
            });
        }

        Box("emp_ui_report_doing", _doing, t => _doing = t);
        Box("emp_ui_report_happened", _happened, t => _happened = t);
        Box("emp_ui_report_expected", _expected, t => _expected = t);
        card.DropdownRow("emp_ui_report_again".lang(),
            [.."yes,no,untried".Split(',').Select(o => $"emp_ui_report_again_{o}".lang())], i => _again = i, _again);

        var buttons = card.ButtonRow();
        send = buttons.RowButton("emp_ui_report_send".lang(), () => {
            var answers = Draft;
            if (answers.Missing() is not null) {
                return;
            }

            // (each box's change clears its part of the draft)
            foreach (var box in boxes) {
                box.Text = "";
            }

            _doing = _happened = _expected = "";
            _again = 2;
            EmpPop.Information("emp_ui_report_sending".lang());
            BugReport.Send(answers, url => {
                if (url is null) {
                    EmpPop.Information("emp_ui_report_page".lang());
                    return;
                }

                Dialog.YesNo("emp_ui_report_sent".Loc(url), () => Application.OpenURL(url));
            });
        });
        buttons.RowButton("emp_ui_report".lang(), () => {
            EmpPop.Information("emp_ui_report_preparing".lang());
            BugReport.Prepare(Draft, path => {
                if (path is not null) {
                    EmpPop.Information("emp_ui_report_done".Loc(path));
                    Util.ShowExplorer(path);
                } else {
                    EmpPop.Information("emp_ui_report_failed".lang());
                }
            });
        });
        hint = card.TextSmall("", FontColor.Gray);
        Refresh();
        card.TextSmall("emp_ui_report_desc".lang(), FontColor.Gray);

        // the last seconds of the screen sent with the report (ReplayBuffer)
        var replay = EmpConfig.Client.RecordReplay;
        card.ToggleRow("emp_ui_cl_record_replay".lang(), null, replay.Value, v => replay.Value = v)
            .SetTooltipLang(replay.Description.Description);
    }

    /// <summary>
    ///     The game's one-line input field made a box of several lines: its height, its text wrapped from the top left
    ///     (it showed one centred line that ran out sideways, whatever was typed)
    /// </summary>
    internal static void TextBox(UIInputText input, float height)
    {
        var field = input.field;
        field.lineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
        var element = input.LayoutElement();
        element.minHeight = element.preferredHeight = height;
        element.flexibleWidth = 1f;
        ((RectTransform)input.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        if (field.transform != input.transform) {
            var rect = (RectTransform)field.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        foreach (var text in new[] { field.textComponent, field.placeholder as UnityEngine.UI.Text }) {
            if (text is null) {
                continue;
            }

            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new(10, 8);
            rt.offsetMax = new(-10, -8);
        }
    }
}
