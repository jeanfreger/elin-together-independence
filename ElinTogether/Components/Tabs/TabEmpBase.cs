using System.Collections;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

internal abstract class TabEmpBase : YKLayout<LayerCreationData>
{
    internal static Vector2 FitCell(int constraint)
    {
        var scaler = EMono.ui.canvasScaler.scaleFactor;
        // (a share of the screen that the settings panel always holds: at 1/1.7 the row was wider than the panel at
        // 1280x800 and ran under the scroll bar, 2026-10-25)
        return new Vector2(Screen.width / 2f / constraint, 45f) / scaler;
    }

    public override void OnSwitchContent(int idTab)
    {
        base.OnSwitchContent(idTab);
        Relayout();
    }

    /// <summary>
    ///     Lays the page out again, twice and once more on the next frame. One pass of the game's RebuildLayout sizes
    ///     each text before its column has its width, so a description that wraps to two lines kept the height of one
    ///     and ran under the next row (My settings, 2026-10-28); the second pass measures it at its real width
    /// </summary>
    internal void Relayout()
    {
        for (var i = 0; i < 2; i++) {
            this.RebuildLayout(true);
            this.RebuildLayoutTo<UIContent>();
        }

        if (isActiveAndEnabled) {
            StartCoroutine(RelayoutLater());
        }
    }

    private IEnumerator RelayoutLater()
    {
        yield return null;
        this.RebuildLayout(true);
        this.RebuildLayoutTo<UIContent>();
    }
}
