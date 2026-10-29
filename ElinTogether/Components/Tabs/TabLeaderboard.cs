using ElinTogether.LangMod;
using ElinTogether.Net;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Leaderboards page (PLAN_hub_features D): rankings of players and teams. Stub: the boards come with the
///     box's work package
/// </summary>
internal class TabLeaderboard : TabEmpBase
{
    public const string TabName = "emp_tab_leaderboard";

    private static readonly string[] Icons = ["icon_medal", "icon_influence"];

    public override void OnLayout()
    {
        var card = this.Card("emp_ui_tab_leaderboard".lang());
        if (NetSession.Instance is not { HasActiveConnection: true, Rules.Leaderboards: true }) {
            TabTeam.EmptyPage(card, Icons, "emp_ui_leaderboard_off".lang());
            return;
        }

        // TODO(PLAN_hub_features WP-D): show the boards of Helper/Leaderboards.cs
        TabTeam.EmptyPage(card, Icons, "emp_ui_leaderboard_empty".lang());
    }
}
