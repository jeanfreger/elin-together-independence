using UnityEngine;

namespace ElinTogether.Components;

/// <summary>
///     The game's own UI parts the hub copies, taken from the prefabs of its windows (read from the game's assets,
///     2026-10-28): the character sheet (LayerChara) gives the section header ("List of Equipment", WindowChara's
///     moldHeader), its bottom tab pills, its top-right ornament and its faint background art; the options window
///     (LayerConfig) gives its framed group. Loaded once; null when the game has moved or renamed them, and every
///     caller then falls back to a plainer part of its own
/// </summary>
internal static class EmpUiParts
{
    private static GameObject? _charaSheet;
    private static bool _charaLooked;
    private static Transform? _configGroup;
    private static bool _groupLooked;

    private static GameObject? CharaSheet
    {
        get {
            if (!_charaLooked) {
                _charaLooked = true;
                _charaSheet = Resources.Load<GameObject>("UI/Layer/LayerChara");
            }
            return _charaSheet;
        }
    }

    /// <summary>
    ///     The character sheet's section header: a small round grey bullet, the title, a thin underline
    /// </summary>
    internal static UIHeader? SectionHeader => CharaSheet != null ? CharaSheet.GetComponentInChildren<WindowChara>(true)?.moldHeader : null;

    /// <summary>
    ///     The character sheet's tab pill (Profile / Skill / Trait...), the current one highlighted by its group
    /// </summary>
    internal static UIButton? Pill
    {
        get {
            var mold = CharaSheet != null ? CharaSheet.GetComponentInChildren<Window>(true)?.moldTab : null;
            return mold != null ? mold : Resources.Load<UIButton>("UI/Window/Base/Element/ButtonTab Parchment");
        }
    }

    /// <summary>
    ///     The options window's framed group ("Language", "User Interface"): its frame and its small title
    /// </summary>
    internal static Transform? ConfigGroup
    {
        get {
            if (!_groupLooked) {
                _groupLooked = true;
                _configGroup = Resources.Load<Transform>("UI/Layer/LayerConfig/Group");
            }
            return _configGroup;
        }
    }

    /// <summary>
    ///     A child of the character sheet's window background, by name ("Image (3)": the ornament at the top right,
    ///     "Image": the faint drawing at the bottom right)
    /// </summary>
    internal static RectTransform? CharaBackground(string name)
    {
        var window = CharaSheet != null ? CharaSheet.GetComponentInChildren<Window>(true) : null;
        return window != null ? window.transform.Find("BG/" + name) as RectTransform : null;
    }
}
