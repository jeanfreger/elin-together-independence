using YKF;

namespace ElinTogether.Components;

/// <summary>
///     Report a bug, a tab of its own (user 2026-10-30: it was a sub-page of Server info, hard to find)
/// </summary>
internal class TabReport : TabEmpBase
{
    public const string TabName = "emp_tab_report";

    public override void OnLayout()
    {
        if (Waits()) {
            return;
        }

        var page = Vertical();
        page.Fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        page.LayoutElement().flexibleWidth = 1f;
        TabHelp.BuildReport(page);
    }
}
