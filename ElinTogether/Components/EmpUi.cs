using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     What a player row shows (the multiplayer hub, dev/PLAN_multiplayer_hub.md step 3)
/// </summary>
[Flags]
internal enum PlayerBadge
{
    None = 0,
    Idle = 1,
    Admin = 2,
    Moderator = 4,
    Frozen = 8,
}

/// <param name="Name">Already coloured by the caller (PeerColorizer) when it wants to be</param>
/// <param name="Detail">The grey line under the name: "character, level"</param>
/// <param name="PingBars">0-4 lit bars, -1 for none (the host's own line has no ping)</param>
internal record PlayerRowInfo(
    string Name,
    string Detail,
    Chara? Chara = null,
    string? Team = null,
    int PingBars = -1,
    PlayerBadge Badges = PlayerBadge.None);

/// <summary>
///     Vanilla-looking pieces for the Elin Together layer: they reuse the game's own prefabs (UINote texts, headers,
///     buttons, the "Tag General" chip, Portrait) and sprites, and only draw what the game has no part for (ping
///     bars, the "changed" mark). Built on YKF like the existing tabs, so a tab can mix them with the old calls.
/// </summary>
internal static class EmpUi
{
    private const float PortraitW = 42f;
    private const float PortraitH = 56f;
    private const float BadgeSize = 20f;
    private const float MarkSlotWidth = 64f;

    private static readonly Color Lit = new(0.45f, 0.68f, 0.32f);
    private static readonly Color Mid = new(0.85f, 0.62f, 0.2f);
    private static readonly Color Bad = new(0.78f, 0.3f, 0.25f);
    private static readonly Color Unlit = new(0.3f, 0.25f, 0.18f, 0.35f);
    private static readonly Color Dot = new(0.85f, 0.55f, 0.1f);

    private static Portrait? _portraitPrefab;
    private static bool _portraitLooked;

    // ---------------------------------------------------------------- helpers

    /// <summary>
    ///     The first sprite the game has under one of these names (its sheet, then any loaded sprite)
    /// </summary>
    public static Sprite? Icon(params string[] names)
    {
        foreach (var name in names) {
            var sprite = SpriteSheet.Get(name);
            if (sprite == null) {
                sprite = UIHelper.FindSprite(name);
            }
            if (sprite != null) {
                return sprite;
            }
        }
        return null;
    }

    /// <summary>
    ///     4 bars from a ping in ms (0 or less: none known)
    /// </summary>
    public static int PingBarsFor(float ms)
    {
        return ms <= 0f ? -1 : ms < 60f ? 4 : ms < 120f ? 3 : ms < 220f ? 2 : 1;
    }

    private static RectTransform NewBox(Transform parent, string name, float w, float h, bool flexible = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = new(w, h);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = w;
        le.preferredWidth = w;
        le.minHeight = h;
        le.preferredHeight = h;
        le.flexibleWidth = flexible ? 1f : 0f;
        return rt;
    }

    private static Image NewImage(Transform parent, string name, Color color, bool raycast = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private static YKHorizontal Line(YKLayout parent, TextAnchor align = TextAnchor.MiddleLeft, float spacing = 10f)
    {
        var row = parent.Horizontal();
        row.Layout.childForceExpandWidth = false;
        row.Layout.childAlignment = align;
        row.Layout.spacing = spacing;
        return row;
    }

    /// <summary>
    ///     A row of a card: the one height, spacing and alignment every page uses, so the controls line up
    /// </summary>
    public static YKHorizontal Row(this YKLayout parent)
    {
        var row = Line(parent);
        row.LayoutElement().minHeight = UIHelper.RowHeight;
        GrowWithText(row);
        return row;
    }

    /// <summary>
    ///     A row takes the height of its tallest child (a description that wraps to two lines) instead of keeping
    ///     its minimum and letting the text run into the row below
    /// </summary>
    private static void GrowWithText(YKHorizontal row)
    {
        row.Layout.childControlHeight = true;
        row.Layout.childForceExpandHeight = false;
    }

    /// <summary>
    ///     The left part of a row: a title and, under it, a grey line; it takes the width the controls leave
    /// </summary>
    public static YKVertical TextColumn(this YKHorizontal row, string title, string? description = null)
    {
        var col = row.Vertical();
        col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        col.Layout.spacing = 0f;
        col.LayoutElement().flexibleWidth = 1f;
        col.Text(title).SetSize(UIHelper.LabelSize);
        if (!string.IsNullOrEmpty(description)) {
            GreyLine(col, description!);
        }
        return col;
    }

    /// <summary>
    ///     A setting that is a button rather than a box: title, grey line, the button on the right
    /// </summary>
    public static void ButtonSetting(this YKLayout parent, string title, string? description, string buttonText,
        Action act, string? tooltip = null)
    {
        var row = parent.Row();
        row.TextColumn(title, description);
        var button = row.Button(buttonText, act).FixedWidth();
        if (tooltip != null) {
            button.SetTooltipLang(tooltip);
        }
    }

    /// <summary>
    ///     A thin line between rows of a card, in the card's own ink
    /// </summary>
    public static void Divider(this YKLayout parent)
    {
        var box = NewBox(parent.transform, "Divider", 1f, 1f, true);
        box.GetComponent<LayoutElement>().minWidth = 0f;
        box.GetComponent<LayoutElement>().preferredWidth = -1f;
        Stretch(NewImage(box, "Line", new(0.28f, 0.22f, 0.13f, 0.18f)).rectTransform);
    }

    private static UIText GreyLine(YKLayout parent, string text)
    {
        return parent.TextSmall(text, FontColor.Gray);
    }

    // ---------------------------------------------------------------- (c) Card

    /// <summary>
    ///     A card: the game's parchment box, its brown header (with an optional icon) and the mod's padding
    /// </summary>
    public static YKVertical Card(this YKLayout parent, string title, Sprite? icon = null)
    {
        var card = parent.MakeCard();
        var header = card.HeaderCard(title, icon);
        // the header prefab carries a small placeholder picture of its own: a card without a real sprite must draw
        // nothing there (never the placeholder), and one with a sprite draws only that one
        foreach (var image in header.GetComponentsInChildren<Image>(true)) {
            if (image.gameObject != header.gameObject && image != header.image1) {
                image.gameObject.SetActive(false);
            }
        }
        if (header.image1 != null && header.image1.sprite == null) {
            header.image1.gameObject.SetActive(false);
        }
        return card;
    }

    // ---------------------------------------------------------------- (e) Chip

    /// <summary>
    ///     A small label such as "Open to all": the game's own tag chip ("Tag General", as on a character's job)
    /// </summary>
    public static void Chip(this YKLayout parent, string text, FontColor color = FontColor.Default)
    {
        Chip(parent.transform, parent, text, color);
    }

    private static void Chip(Transform into, YKLayout fallback, string text, FontColor color)
    {
        if (Util.Instantiate<HintIcon>("UI/Element/Item/Tag General", into) is not { } chip) {
            fallback.TextSmall(text, color);
            return;
        }

        chip.text.SetText(text, color);
        chip.transform.RebuildLayout(true);
    }

    // ---------------------------------------------------------------- (d) EmptyState

    /// <summary>
    ///     "Nothing here yet": one line saying what to do next and one button for it
    /// </summary>
    public static YKVertical EmptyState(this YKLayout parent, string message, string buttonText, Action onClick)
    {
        var box = parent.Vertical();
        box.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        box.Layout.childAlignment = TextAnchor.MiddleCenter;
        box.Layout.padding = new(16, 16, 4, 4);
        box.Layout.spacing = UIHelper.RowSpacing;

        var text = box.Text(message, FontColor.Gray);
        text.alignment = TextAnchor.MiddleCenter;

        var row = Line(box, TextAnchor.MiddleCenter);
        row.Button(buttonText, onClick).FixedWidth();
        return box;
    }

    // ---------------------------------------------------------------- (a) PlayerRow

    /// <summary>
    ///     A player: portrait, name, "character, level", team chip, ping bars, small badges and a "..." button that
    ///     opens the game's context menu with these actions (label, what it does)
    /// </summary>
    public static YKHorizontal PlayerRow(this YKLayout parent, PlayerRowInfo info,
        IReadOnlyList<(string Label, Action Act)> actions)
    {
        var row = Line(parent);
        row.LayoutElement().minHeight = PortraitH + 4f;

        AddPortrait(row, info.Chara);

        var col = row.Vertical();
        col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        col.Layout.spacing = 0f;
        col.LayoutElement().flexibleWidth = 1f;
        col.Text(info.Name).SetSize(UIHelper.LabelSize);
        if (info.Detail.Length > 0) {
            GreyLine(col, info.Detail);
        }

        if (!string.IsNullOrEmpty(info.Team)) {
            row.Chip(info.Team!);
        }

        foreach (var badge in new[] { PlayerBadge.Idle, PlayerBadge.Admin, PlayerBadge.Moderator, PlayerBadge.Frozen }) {
            if (info.Badges.HasFlag(badge)) {
                AddBadge(row, badge);
            }
        }

        if (info.PingBars >= 0) {
            AddPingBars(row.transform, info.PingBars);
        }

        if (actions.Count > 0) {
            AddMoreButton(row, actions);
        }

        return row;
    }

    private static void AddPortrait(YKHorizontal row, Chara? chara)
    {
        var holder = NewBox(row.transform, "PortraitBox", PortraitW, PortraitH);
        if (chara is null) {
            return;
        }

        if (!_portraitLooked) {
            _portraitLooked = true;
            // the prefab of the character lists (ButtonChara) carries the game's Portrait; found by type, not by path
            _portraitPrefab = Resources.LoadAll<ButtonChara>("UI").Select(b => b.portrait).FirstOrDefault(p => p != null)
                              ?? Resources.LoadAll<Portrait>("UI").FirstOrDefault();
        }

        if (_portraitPrefab != null) {
            var portrait = UnityEngine.Object.Instantiate(_portraitPrefab, holder, false);
            Stretch(portrait.Rect());
            portrait.SetChara(chara);
            return;
        }

        // no Portrait prefab found: the character's own sprite, as the game's people lists show it
        var image = NewImage(holder, "CharaImage", Color.white);
        Stretch(image.rectTransform);
        image.preserveAspect = true;
        chara.SetImage(image);
        Stretch(image.rectTransform);
    }

    private static void AddBadge(YKHorizontal row, PlayerBadge badge)
    {
        var (sprite, letter, color) = badge switch {
            PlayerBadge.Idle => (Icon("icon_hotSleep"), "z", FontColor.Passive),
            PlayerBadge.Admin => (Icon("icon_admin"), "A", FontColor.Warning),
            PlayerBadge.Moderator => (Icon("icon_influence"), "M", FontColor.Good),
            _ => (Icon("icon_speed0"), "!", FontColor.Bad),
        };

        if (sprite == null) {
            // a sprite the game does not have under that name: a letter in the badge's colour
            var text = row.TextSmall(letter, color);
            text.LayoutElement().minWidth = 14f;
            return;
        }

        var box = NewBox(row.transform, "Badge" + badge, BadgeSize, BadgeSize);
        var image = NewImage(box, "Icon", Color.white);
        image.sprite = sprite;
        image.preserveAspect = true;
        Stretch(image.rectTransform);
    }

    private static void AddPingBars(Transform parent, int lit)
    {
        var box = NewBox(parent, "Ping", 26f, 18f);
        var color = lit >= 3 ? Lit : lit == 2 ? Mid : Bad;
        for (var i = 0; i < 4; i++) {
            var bar = NewImage(box, "Bar" + i, i < lit ? color : Unlit).rectTransform;
            bar.anchorMin = bar.anchorMax = Vector2.zero;
            bar.pivot = Vector2.zero;
            bar.sizeDelta = new(4f, 4f + i * 4.5f);
            bar.anchoredPosition = new(i * 6f, 0f);
        }
    }

    private static void AddMoreButton(YKHorizontal row, IReadOnlyList<(string Label, Action Act)> actions)
    {
        UIButton? button = null;
        button = row.Button("...", () => {
            var menu = EClass.ui.CreateContextMenuInteraction().SetHighlightTarget(button);
            foreach (var (label, act) in actions) {
                var action = act;
                menu.AddButton(label, () => action());
            }
            menu.Show();
        });
        var element = button.LayoutElement();
        element.minWidth = 40f;
        element.preferredWidth = 40f;
        element.flexibleWidth = 0f;
    }

    // ---------------------------------------------------------------- (b) SettingRow

    /// <summary>
    ///     The shared skeleton of a setting: [title / grey description] [changed dot + revert] [control slot]. Every
    ///     control slot has the same width and sits on the right, so the controls of a card line up. Returns the slot
    /// </summary>
    private sealed record SettingParts(YKHorizontal Slot, Action<bool> ShowChanged, Action<Action> SetRevert);

    private static SettingParts SettingSkeleton(YKLayout parent, string title, string? description, bool readOnly,
        string setByHostText)
    {
        var row = Line(parent);
        row.LayoutElement().minHeight = UIHelper.RowHeight;
        GrowWithText(row);

        var col = row.Vertical();
        col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        col.Layout.spacing = 0f;
        col.LayoutElement().flexibleWidth = 1f;

        if (readOnly) {
            var titleLine = Line(col, spacing: 8f);
            titleLine.Text(title).SetSize(UIHelper.LabelSize);
            titleLine.Chip(setByHostText);
        } else {
            col.Text(title).SetSize(UIHelper.LabelSize);
        }

        if (!string.IsNullOrEmpty(description)) {
            GreyLine(col, description!);
        }

        // the mark slot keeps its width when nothing changed, so the controls never jump
        var marks = NewBox(row.transform, "Marks", MarkSlotWidth, UIHelper.RowHeight);
        var group = marks.gameObject.AddComponent<CanvasGroup>();
        var dot = NewImage(marks, "ChangedDot", Dot).rectTransform;
        dot.anchorMin = dot.anchorMax = new(0f, 0.5f);
        dot.sizeDelta = new(9f, 9f);
        dot.anchoredPosition = new(10f, 0f);
        dot.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // the arrow is a game button made by the row, then moved into the mark slot
        Action? onRevert = null;
        var revertButton = row.Button("<", () => onRevert?.Invoke());
        revertButton.transform.SetParent(marks, false);
        revertButton.LayoutElement().ignoreLayout = true;
        var rrt = revertButton.Rect();
        rrt.anchorMin = rrt.anchorMax = new(1f, 0.5f);
        rrt.pivot = new(1f, 0.5f);
        rrt.sizeDelta = new(34f, UIHelper.RowHeight - 6f);
        rrt.anchoredPosition = Vector2.zero;

        group.alpha = 0f;
        group.blocksRaycasts = false;

        var slot = Line(row, TextAnchor.MiddleRight, 0f);
        var slotElement = slot.LayoutElement();
        slotElement.preferredWidth = UIHelper.ControlWidth;
        slotElement.minWidth = UIHelper.ControlWidth;
        slotElement.flexibleWidth = 0f;
        return new(slot, changed => {
            group.alpha = changed ? 1f : 0f;
            group.blocksRaycasts = changed;
        }, act => onRevert = act);
    }

    private static void Lock(Selectable control, bool readOnly)
    {
        control.interactable = !readOnly;
    }

    /// <summary>
    ///     A box setting. With a reference value, a changed value shows the dot and the revert arrow
    /// </summary>
    public static UIButton ToggleRow(this YKLayout parent, string title, string? description, bool value,
        Action<bool> onChange, bool? reference = null, bool readOnly = false, string setByHostText = "")
    {
        var parts = SettingSkeleton(parent, title, description, readOnly, setByHostText);
        var slot = parts.Slot;
        var showChanged = parts.ShowChanged;
        var current = value;
        // (only the box: the title is the row's own text, so the label here stays empty)
        UIButton? toggle = null;
        void Refresh() => showChanged(reference.HasValue && current != reference.Value);
        toggle = slot.Toggle("", value, v => {
            current = v;
            onChange(v);
            Refresh();
        });
        var element = toggle.LayoutElement();
        element.minWidth = 44f;
        element.preferredWidth = 44f;
        element.flexibleWidth = 0f;
        toggle.AsRow();
        Lock(toggle, readOnly);
        parts.SetRevert(() => {
            if (!reference.HasValue) {
                return;
            }
            current = reference.Value;
            toggle.SetCheck(current);
            onChange(current);
            Refresh();
        });
        Refresh();
        return toggle;
    }

    /// <summary>
    ///     A choice setting (the reference is an option index)
    /// </summary>
    public static UIDropdown DropdownSettingRow(this YKLayout parent, string title, string? description,
        List<string> options, int value, Action<int> onPick, int? reference = null, bool readOnly = false,
        string setByHostText = "")
    {
        var parts = SettingSkeleton(parent, title, description, readOnly, setByHostText);
        var slot = parts.Slot;
        var showChanged = parts.ShowChanged;
        var current = value;
        UIDropdown? dropdown = null;
        void Refresh() => showChanged(reference.HasValue && current != reference.Value);
        dropdown = slot.Dropdown(options, i => {
            current = i;
            onPick(i);
            Refresh();
        }, value);
        var element = dropdown.LayoutElement();
        element.preferredWidth = UIHelper.ControlWidth;
        element.flexibleWidth = 1f;
        dropdown.Rect().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, UIHelper.RowHeight);
        Lock(dropdown, readOnly);
        parts.SetRevert(() => {
            if (reference.HasValue) {
                dropdown.value = reference.Value;
            }
        });
        Refresh();
        return dropdown;
    }

    /// <summary>
    ///     A number setting on the game's own slider (whole numbers, as YKF's slider is)
    /// </summary>
    public static Slider SliderRow(this YKLayout parent, string title, string? description, int value, int min,
        int max, Action<int> onChange, Func<int, string>? label = null, int? reference = null, bool readOnly = false,
        string setByHostText = "")
    {
        var parts = SettingSkeleton(parent, title, description, readOnly, setByHostText);
        var slot = parts.Slot;
        var showChanged = parts.ShowChanged;
        var current = value;
        Slider? slider = null;
        void Refresh() => showChanged(reference.HasValue && current != reference.Value);
        slider = slot.Slider(value, v => {
            current = Mathf.RoundToInt(v);
            onChange(current);
            Refresh();
        }, min, max, v => label?.Invoke(Mathf.RoundToInt(v)) ?? Mathf.RoundToInt(v).ToString());
        var element = slider.LayoutElement();
        element.preferredWidth = UIHelper.ControlWidth;
        element.flexibleWidth = 1f;
        Lock(slider, readOnly);
        parts.SetRevert(() => {
            if (reference.HasValue) {
                slider.value = reference.Value;
            }
        });
        Refresh();
        return slider;
    }
}
