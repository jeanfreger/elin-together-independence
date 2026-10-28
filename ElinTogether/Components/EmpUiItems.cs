using ElinTogether.Helper;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     Game item icons for the team and land pages: a card's own sprite, drawn the way the game's inventory lists do
/// </summary>
internal static class EmpUiItems
{
    private const float IconBox = 44f;

    /// <summary>
    ///     A box of a fixed size holding the card's sprite (a chest, a bank, a delivery box); empty when it has none
    /// </summary>
    public static Image CardIcon(this YKLayout parent, Card? card, float size = IconBox)
    {
        var go = new GameObject("CardIcon", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, false);
        var element = go.GetComponent<LayoutElement>();
        (element.minWidth, element.preferredWidth, element.minHeight, element.preferredHeight, element.flexibleWidth) =
            (size, size, size, size, 0f);

        var image = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(go.transform, false);
        image.raycastTarget = false;
        image.preserveAspect = true;
        card?.SetImage(image);

        // (SetImage sizes the image natively: the box keeps it inside its square)
        var rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        image.enabled = card is not null;
        return image;
    }

    /// <summary>
    ///     A sprite of the game in a box of a fixed size, left out when the game has none under those names
    /// </summary>
    public static bool SpriteIcon(this YKLayout parent, float size, params string[] names)
    {
        if (EmpUi.Icon(names) is not { } sprite) {
            return false;
        }

        var image = parent.CardIcon(null, size);
        image.sprite = sprite;
        image.enabled = true;
        return true;
    }

    /// <summary>
    ///     "Nothing here" without a button: one grey line, centred
    /// </summary>
    public static void EmptyText(this YKLayout parent, string message)
    {
        // (a short centred box: the line sits in the middle of it, not under a tall empty area)
        var box = parent.Vertical();
        box.Fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        box.Layout.childAlignment = TextAnchor.MiddleCenter;
        box.Layout.padding = new(16, 16, 4, 4);
        var text = box.Text(message, FontColor.Gray);
        text.alignment = TextAnchor.MiddleCenter;
    }
}
