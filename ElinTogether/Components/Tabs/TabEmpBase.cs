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
}