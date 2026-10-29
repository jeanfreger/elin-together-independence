using ElinTogether.Helper;
using ElinTogether.LangMod;

namespace ElinTogether.Components;

/// <summary>
///     The "Community" page: the world's journal and the leaderboards as two sub-tabs (one tab of the strip instead of
///     two, so the tab labels keep the game's own size)
/// </summary>
internal class TabCommunity : TabEmpBase
{
    public const string TabName = "emp_tab_community";

    private const string SubKey = "community";

    /// <summary>
    ///     The tab (and sub-tab) an older tab name stands for: the bench scripts still open the Journal and Leaderboards
    ///     by their former ids
    /// </summary>
    internal static string Redirect(string tab)
    {
        switch (tab) {
            case "emp_tab_journal":
                SetSubTab(SubKey, 0);
                return TabName;
            case "emp_tab_leaderboard":
                SetSubTab(SubKey, 1);
                return TabName;
            default:
                return tab;
        }
    }

    public override void OnLayout()
    {
        if (Waits()) {
            return;
        }

        SubTabs(SubKey, [
            ("emp_ui_tab_journal".lang(), TabJournal.Build),
            ("emp_ui_tab_leaderboard".lang(), page => TabLeaderboard.Build(page, Relayout)),
        ]);
    }

    protected override void OnSubTabShown(int index)
    {
        if (index == 1 && TabLeaderboard.Enabled) {
            Leaderboards.Ask(TabLeaderboard.Board);
        }
    }

    private void OnDestroy()
    {
        TabLeaderboard.Release();
    }
}
