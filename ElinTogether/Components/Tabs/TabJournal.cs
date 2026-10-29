using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Journal page (PLAN_hub_features B): the world's activity journal, newest first. Filters: All, my Team, Mine
/// </summary>
internal static class TabJournal
{
    private static readonly string[] Icons = ["icon_book", "icon_medal"];

    // 0 all, 1 my team, 2 mine
    private static int _filter;

    public static void Build(YKVertical page)
    {
        var card = page.Card("emp_ui_tab_journal".lang());
        if (NetSession.Instance is not { HasActiveConnection: true, Rules.ActivityJournal: true }) {
            TabTeam.EmptyPage(card, Icons, "emp_ui_journal_off".lang());
            return;
        }

        var me = EClass.pc.uid;
        var team = Teams.TeamOf(me);
        card.DropdownRow("emp_ui_journal_filter".lang(),
            new List<string> { "emp_ui_journal_f_all".lang(), "emp_ui_journal_f_team".lang(), "emp_ui_journal_f_mine".lang() },
            i => {
                _filter = i;
                LayerElinTogether.Instance?.Window.SwitchContent(TabCommunity.TabName);
            }, _filter);

        var entries = ActivityJournal.Entries()
            .Where(e => ActivityJournal.Visible(e, me) && _filter switch {
                1 => e.Team == team,
                2 => e.Actor == me,
                _ => true,
            })
            .Reverse()
            .ToList();
        if (entries.Count == 0) {
            TabTeam.EmptyPage(card, Icons, "emp_ui_journal_empty".lang());
            return;
        }

        foreach (var entry in entries) {
            card.Text(ActivityJournal.Line(entry));
        }
    }
}
