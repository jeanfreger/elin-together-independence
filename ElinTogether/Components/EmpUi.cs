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
    private const float PortraitW = 48f;
    private const float PortraitH = 56f;
    private const float SpriteW = 40f;
    private const float BadgeSize = 20f;
    private const float MarkSlotWidth = 64f;

    private static readonly Color Lit = new(0.45f, 0.68f, 0.32f);
    private static readonly Color Mid = new(0.85f, 0.62f, 0.2f);
    private static readonly Color Bad = new(0.78f, 0.3f, 0.25f);
    private static readonly Color Unlit = new(0.3f, 0.25f, 0.18f, 0.35f);
    private static readonly Color Dot = new(0.85f, 0.55f, 0.1f);

    private static Portrait? _portraitPrefab;
    private static bool _portraitLooked;
    private static Material? _iconMaterial;
    private static bool _iconMaterialLooked;

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
        // (as wide as its label: a whole-width bar beside a short title in a half-width box left no room for it)
        var button = row.Button(buttonText, act);
        var element = button.LayoutElement();
        element.preferredWidth = Mathf.Max(110f, (button.mainText != null ? button.mainText.preferredWidth : 0f) + 48f);
        element.flexibleWidth = 0f;
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
    ///     A section as the character sheet and the journal draw one: the game's header (round grey bullet, title,
    ///     thin underline) over its lines, no box around them. Returns the body (the icon is kept for the callers:
    ///     the game's header has no icon slot, so there is never an empty gap before the title)
    /// </summary>
    public static YKVertical Card(this YKLayout parent, string title, Sprite? icon = null)
    {
        var section = parent.Vertical();
        section.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        section.LayoutElement().flexibleWidth = 1f;
        section.Layout.padding = new(2, 2, 2, 12);
        section.Layout.spacing = 2f;
        section.SectionHeader(title);

        // (the lines start under the title, as the character sheet's do)
        var body = section.Vertical();
        body.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        body.Layout.padding = new(22, 8, 0, 0);
        body.Layout.spacing = UIHelper.RowSpacing;
        return body;
    }

    /// <summary>
    ///     The character sheet's section header (WindowChara.moldHeader) on a line of its own, at its own width (its
    ///     underline follows the title); the game's HeaderInner, or a plain header, where the sheet has none
    /// </summary>
    public static void SectionHeader(this YKLayout parent, string title)
    {
        var line = Line(parent, spacing: 0f);
        line.Layout.childControlWidth = false;
        line.Layout.childControlHeight = false;
        var element = line.LayoutElement();
        element.minHeight = element.preferredHeight = 38f;

        if (EmpUiParts.SectionHeader is { } mold) {
            var header = UnityEngine.Object.Instantiate(mold, line.transform, false);
            header.gameObject.SetActive(true);
            header.SetText(title);
            return;
        }

        var item = ((UINote)line).AddHeader("HeaderInner", title);
        item.transform.SetParent(line.transform, false);
        foreach (var image in item.GetComponentsInChildren<Image>(true)) {
            image.gameObject.SetActive(true);
        }
    }

    // ---------------------------------------------------------------- (h) Book

    /// <summary>
    ///     The journal's two pages: a list on the left page, what is picked on the right page, a thin fold between
    /// </summary>
    public static (YKVertical Left, YKVertical Right) Book(this YKLayout parent)
    {
        var spread = parent.Horizontal();
        spread.Layout.childForceExpandWidth = false;
        spread.Layout.childControlWidth = true;
        spread.Layout.childControlHeight = true;
        spread.Layout.childForceExpandHeight = true;
        spread.Layout.childAlignment = TextAnchor.UpperLeft;
        spread.Layout.spacing = 0f;
        spread.LayoutElement().flexibleWidth = 1f;
        // (a page as tall as the window: the fold runs down the whole book even when a page is short)
        spread.LayoutElement().minHeight = 420f;

        YKVertical Page(int padLeft, int padRight)
        {
            var page = spread.Vertical();
            page.Fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            page.Layout.spacing = UIHelper.RowSpacing;
            page.Layout.childAlignment = TextAnchor.UpperLeft;
            page.Layout.padding = new(padLeft, padRight, 0, 8);
            var element = page.LayoutElement();
            element.flexibleWidth = 1f;
            element.preferredWidth = 0f;
            element.minWidth = 0f;
            return page;
        }

        var left = Page(0, 14);
        var fold = NewBox(spread.transform, "Fold", 2f, 1f);
        fold.GetComponent<LayoutElement>().flexibleHeight = 1f;
        Stretch(NewImage(fold, "Line", new(0.28f, 0.22f, 0.13f, 0.22f)).rectTransform);
        var right = Page(16, 0);
        return (left, right);
    }

    /// <summary>
    ///     A row of the left page: a player row that is picked by a click, the picked one shaded as the journal's
    /// </summary>
    public static YKHorizontal PickRow(this YKLayout parent, PlayerRowInfo info, bool picked, Action onPick,
        IReadOnlyList<(string Label, Action Act)>? actions = null)
    {
        var row = parent.PlayerRow(info, actions ?? []);
        var shade = row.gameObject.AddComponent<Image>();
        shade.color = picked ? new(0.28f, 0.22f, 0.13f, 0.14f) : new(0f, 0f, 0f, 0.001f);
        var button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = shade;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => {
            SE.ClickGeneral();
            onPick();
        });
        return row;
    }

    // ---------------------------------------------------------------- (g) KeyValue

    /// <summary>
    ///     A line of the character sheet's facts ("Faction  Noyel"): the game's TopicDefault, its small brown label
    ///     right-aligned in its column, then the value ("-" when there is none, as the sheet shows it)
    /// </summary>
    public static UIItem KeyValue(this YKLayout parent, string label, string? value)
    {
        var item = parent.Topic(label, string.IsNullOrEmpty(value) ? "-" : value);
        if (item.text1 != null) {
            item.text1.alignment = TextAnchor.MiddleRight;
        }
        return item;
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
        // (the portrait in a column of its own, the text starting after it: 14 of air, not the usual 10)
        var row = Line(parent, spacing: 14f);
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

    /// <summary>
    ///     Just the portrait, in its own fixed-width column (a character sheet's header: the facts go beside it)
    /// </summary>
    public static void Portrait(this YKHorizontal row, Chara? chara)
    {
        AddPortrait(row, chara);
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
            // (the game's Portrait also draws the pixel sprite over the face: that one goes in a column of its own)
            if (portrait.imageChara != null) {
                portrait.imageChara.gameObject.SetActive(false);
            }

            var spriteHolder = NewBox(row.transform, "SpriteBox", SpriteW, PortraitH);
            CharaSprite(spriteHolder, chara);
            return;
        }

        // no Portrait prefab found: the character's own sprite, as the game's people lists show it
        CharaSprite(holder, chara);
    }

    private static void CharaSprite(Transform holder, Chara chara)
    {
        var image = NewImage(holder, "CharaImage", Color.white);
        Stretch(image.rectTransform);
        image.preserveAspect = true;
        chara.SetImage(image);
        Stretch(image.rectTransform);
        // Card.SetImage packs the card's colour into image.color (neutral = 0.5 grey) for the material of the game's
        // item icons ("UI obj", which decodes it); under the default UI material the sprite came out half as bright
        if (!_iconMaterialLooked) {
            _iconMaterialLooked = true;
            _iconMaterial = Resources.LoadAll<ButtonGrid>("UI").Select(b => b.icon != null ? b.icon.material : null)
                .FirstOrDefault(m => m != null);
        }

        if (_iconMaterial != null) {
            image.material = _iconMaterial;
        } else {
            // (what the game's Portrait does with its pixel sprite: the plain sprite, untinted)
            image.color = Color.white;
        }
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

    // ---------------------------------------------------------------- (f) Box and columns (the options window)

    /// <summary>
    ///     A framed group as the game's options window draws one ("Language", "User Interface"): its frame and its
    ///     small title, copied from LayerConfig's Group prefab; the rows inside. Without a title the frame alone
    /// </summary>
    public static YKVertical Box(this YKLayout parent, string? title = null)
    {
        var box = parent.Vertical();
        box.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        box.LayoutElement().flexibleWidth = 1f;
        box.Layout.padding = new(20, 20, title is null ? 16 : 44, 16);
        box.Layout.spacing = UIHelper.RowSpacing;

        var mold = EmpUiParts.ConfigGroup;
        if (mold != null && mold.Find("BG") is { } frame) {
            var bg = UnityEngine.Object.Instantiate(frame, box.transform, false);
            bg.GetOrCreate<LayoutElement>().ignoreLayout = true;
            Stretch((RectTransform)bg);
            bg.SetAsFirstSibling();
        } else {
            // (no options window to copy from: the parchment frame of the mod's old cards)
            var image = box.gameObject.AddComponent<Image>();
            image.sprite = UIHelper.FindSprite("buttonBig");
            image.type = Image.Type.Sliced;
        }

        if (title is null) {
            return box;
        }

        if (mold != null && mold.Find("Text topic") is { } topic) {
            var copy = UnityEngine.Object.Instantiate(topic, box.transform, false);
            copy.GetOrCreate<LayoutElement>().ignoreLayout = true;
            if (copy.GetComponent<UIText>() is { } text) {
                // (the prefab's own lang id would come back on a language change)
                text.lang = null;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.SetText(title);
            }
        } else {
            var head = box.TextSmall(title, FontColor.Topic);
            head.LayoutElement().ignoreLayout = true;
            var rt = head.Rect();
            rt.anchorMin = rt.anchorMax = rt.pivot = new(0f, 1f);
            rt.anchoredPosition = new(24f, -14f);
        }

        return box;
    }

    /// <summary>
    ///     Two columns side by side, the same width, as the options window lays its groups out
    /// </summary>
    public static (YKVertical Left, YKVertical Right) Columns(this YKLayout parent)
    {
        var row = parent.Horizontal();
        row.Layout.childForceExpandWidth = true;
        row.Layout.childControlWidth = true;
        row.Layout.childAlignment = TextAnchor.UpperLeft;
        row.Layout.spacing = 14f;
        row.LayoutElement().flexibleWidth = 1f;

        YKVertical Column()
        {
            var col = row.Vertical();
            col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            col.Layout.spacing = 12f;
            var element = col.LayoutElement();
            element.flexibleWidth = 1f;
            element.preferredWidth = 0f;
            element.minWidth = 0f;
            return col;
        }

        return (Column(), Column());
    }

    // ---------------------------------------------------------------- (b) SettingRow

    /// <summary>
    ///     The "changed" mark of a setting: a dot and the revert arrow, hidden (but keeping its width, so nothing
    ///     jumps) while the value is the reference one
    /// </summary>
    private sealed record Marks(Action<bool> ShowChanged, Action<Action> SetRevert);

    private static Marks AddMarks(YKHorizontal row)
    {
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
        return new(changed => {
            group.alpha = changed ? 1f : 0f;
            group.blocksRaycasts = changed;
        }, act => onRevert = act);
    }

    /// <summary>
    ///     The words of a setting: its title (with "set by the host" when the player only reads it) and a grey line
    /// </summary>
    private static void SettingWords(YKVertical col, string title, string? description, bool readOnly, string setByHostText)
    {
        if (readOnly && setByHostText.Length > 0) {
            var titleLine = Line(col, spacing: 8f);
            titleLine.Text(title).SetSize(UIHelper.LabelSize);
            titleLine.Chip(setByHostText);
        } else {
            col.Text(title).SetSize(UIHelper.LabelSize);
        }

        if (!string.IsNullOrEmpty(description)) {
            GreyLine(col, description!);
        }
    }

    private static YKVertical WordsColumn(YKHorizontal row)
    {
        var col = row.Vertical();
        col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        col.Layout.spacing = 0f;
        var element = col.LayoutElement();
        element.flexibleWidth = 1f;
        element.minWidth = 0f;
        return col;
    }

    private static void Lock(Selectable control, bool readOnly)
    {
        control.interactable = !readOnly;
    }

    /// <summary>
    ///     A box setting as the options window has them: the game's checkbox first, then the title and a grey line.
    ///     With a reference value, a changed value shows the dot and the revert arrow
    /// </summary>
    public static UIButton ToggleRow(this YKLayout parent, string title, string? description, bool value,
        Action<bool> onChange, bool? reference = null, bool readOnly = false, string setByHostText = "")
    {
        var row = Line(parent, TextAnchor.UpperLeft, 6f);
        row.LayoutElement().minHeight = UIHelper.RowHeight;
        GrowWithText(row);

        var current = value;
        Marks? marks = null;
        void Refresh() => marks?.ShowChanged(reference.HasValue && current != reference.Value);
        // (only the box: the title is the row's own text, so the label here stays empty)
        var toggle = row.Toggle("", value, v => {
            current = v;
            onChange(v);
            Refresh();
        });
        toggle.mainText.text = "";
        var element = toggle.LayoutElement();
        element.minWidth = element.preferredWidth = 40f;
        element.minHeight = element.preferredHeight = UIHelper.RowHeight;
        element.flexibleWidth = 0f;
        toggle.AsRow();

        SettingWords(WordsColumn(row), title, description, readOnly, setByHostText);
        marks = AddMarks(row);
        Lock(toggle, readOnly);
        marks.SetRevert(() => {
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
    ///     A control under its title, the whole width of the box (the options window's sliders and font choice)
    /// </summary>
    private static (YKVertical Group, Marks Marks) StackedSetting(YKLayout parent, string title, bool readOnly,
        string setByHostText)
    {
        var group = parent.Vertical();
        group.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        group.Layout.spacing = 2f;
        var titleLine = Line(group, TextAnchor.UpperLeft, 6f);
        GrowWithText(titleLine);
        // (the grey line goes under the control, added by the caller)
        SettingWords(WordsColumn(titleLine), title, null, readOnly, setByHostText);
        return (group, AddMarks(titleLine));
    }

    /// <summary>
    ///     A choice setting: title, the game's dropdown under it, a grey line (the reference is an option index)
    /// </summary>
    public static UIDropdown DropdownSettingRow(this YKLayout parent, string title, string? description,
        List<string> options, int value, Action<int> onPick, int? reference = null, bool readOnly = false,
        string setByHostText = "")
    {
        var (group, marks) = StackedSetting(parent, title, readOnly, setByHostText);
        var current = value;
        void Refresh() => marks.ShowChanged(reference.HasValue && current != reference.Value);
        var dropdown = group.Dropdown(options, i => {
            current = i;
            onPick(i);
            Refresh();
        }, value);
        var element = dropdown.LayoutElement();
        element.flexibleWidth = 1f;
        element.preferredHeight = UIHelper.RowHeight;
        dropdown.Rect().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, UIHelper.RowHeight);
        if (!string.IsNullOrEmpty(description)) {
            GreyLine(group, description!);
        }
        Lock(dropdown, readOnly);
        marks.SetRevert(() => {
            if (reference.HasValue) {
                dropdown.value = reference.Value;
            }
        });
        Refresh();
        return dropdown;
    }

    /// <summary>
    ///     A number setting on the options window's own slider (whole numbers, as YKF's slider is), under its title
    /// </summary>
    public static Slider SliderRow(this YKLayout parent, string title, string? description, int value, int min,
        int max, Action<int> onChange, Func<int, string>? label = null, int? reference = null, bool readOnly = false,
        string setByHostText = "")
    {
        var (group, marks) = StackedSetting(parent, title, readOnly, setByHostText);
        var current = value;
        void Refresh() => marks.ShowChanged(reference.HasValue && current != reference.Value);
        var slider = group.Slider(value, v => {
            current = Mathf.RoundToInt(v);
            onChange(current);
            Refresh();
        }, min, max, v => label?.Invoke(Mathf.RoundToInt(v)) ?? Mathf.RoundToInt(v).ToString());
        slider.LayoutElement().flexibleWidth = 1f;
        if (!string.IsNullOrEmpty(description)) {
            GreyLine(group, description!);
        }
        Lock(slider, readOnly);
        marks.SetRevert(() => {
            if (reference.HasValue) {
                slider.value = reference.Value;
            }
        });
        Refresh();
        return slider;
    }
}
