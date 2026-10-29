using ElinTogether.LangMod;
using ElinTogether.Net;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Journal page (PLAN_hub_features B): the world's activity journal. Stub: the entries come with the box's work
///     package
/// </summary>
internal class TabJournal : TabEmpBase
{
    public const string TabName = "emp_tab_journal";

    private static readonly string[] Icons = ["icon_book", "icon_medal"];

    public override void OnLayout()
    {
        var card = this.Card("emp_ui_tab_journal".lang());
        if (NetSession.Instance is not { HasActiveConnection: true, Rules.ActivityJournal: true }) {
            TabTeam.EmptyPage(card, Icons, "emp_ui_journal_off".lang());
            return;
        }

        // TODO(PLAN_hub_features WP-B): list the entries of Helper/ActivityJournal.cs
        TabTeam.EmptyPage(card, Icons, "emp_ui_journal_empty".lang());
    }
}
