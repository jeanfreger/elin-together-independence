using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Helper;

public static class UIHelper
{
    private static readonly Dictionary<string, Sprite> _lookup = [];

    // the settings charter (dev/PLAN_settings_charter.md, rules 9-12): one card, one row, one button for every tab
    public const float RowHeight = 32f;
    public const float RowSpacing = 6f;
    public const float ControlWidth = 320f;
    public const float ButtonWidth = 220f;
    public const int SubIndent = 32;

    public static RectOffset CardPadding => new(20, 20, 12, 12);

    private static int? _labelSize;

    /// <summary>
    ///     The size of the game's own box labels (ButtonToggle, as on its option screens): dropdown labels use it too
    /// </summary>
    public static int LabelSize => _labelSize ??= Resources.Load<UIButton>("UI/Element/Button/ButtonToggle")?.mainText?.size ?? 0;

    /// <summary>
    ///     A box row: the same height as a dropdown row
    /// </summary>
    public static UIButton AsRow(this UIButton toggle)
    {
        toggle.Rect().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RowHeight);
        return toggle;
    }

    /// <summary>
    ///     A button at its normal width, never a full-width bar
    /// </summary>
    public static UIButton FixedWidth(this UIButton button)
    {
        var element = button.LayoutElement();
        // the normal width, wider when the label needs it (a long label ran out of its button)
        var wanted = button.mainText != null ? button.mainText.preferredWidth + 48f : 0f;
        element.preferredWidth = Mathf.Max(ButtonWidth, wanted);
        element.flexibleWidth = 0f;
        return button;
    }

    /// <summary>
    ///     A button row on a page of the game's own (UINote): the game's buttons, moved into it, side by side
    /// </summary>
    public static YKHorizontal NoteButtonRow(UINote note)
    {
        var row = YK.Create<YKHorizontal>();
        row.transform.SetParent(note.layout.transform, false);
        row.OnLayout();
        SetUpButtonRow(row);
        return row;
    }

    public static UIButton InRow(this UIButton button, YKHorizontal row)
    {
        button.transform.SetParent(row.transform, false);
        return button.FixedWidth();
    }

    private static void SetUpButtonRow(YKHorizontal row)
    {
        row.Layout.childForceExpandWidth = false;
        row.Layout.childAlignment = TextAnchor.MiddleLeft;
        row.Layout.spacing = 10f;
        // (the game's note texts start a few pixels left of their box: the buttons start where the words do)
        row.Layout.padding = new(-5, 0, 2, 2);
    }

    /// <summary>
    ///     YK Framework's TextSmall/TextMedium come from game prefabs whose ContentSizeFitter sizes the width to the
    ///     unwrapped line, over the card's layout group: long lines were cut at the card's edge (JP, CN, and English at
    ///     1280x800, 2026-10-25). Under this root the group sets the width and the fitter only the height
    /// </summary>
    public static void WrapTexts(Component root)
    {
        foreach (var fitter in root.GetComponentsInChildren<ContentSizeFitter>(true)) {
            if (fitter.horizontalFit != ContentSizeFitter.FitMode.PreferredSize ||
                // (a section header keeps its one line: wrapped, its first words hid above the line)
                fitter.GetComponentInParent<UIHeader>() != null ||
                !fitter.TryGetComponent<Text>(out var text) || text.horizontalOverflow != HorizontalWrapMode.Wrap ||
                fitter.transform.parent?.GetComponent<HorizontalOrVerticalLayoutGroup>() is not { childControlWidth: true }) {
                continue;
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    public static Sprite FindSprite(string name)
    {
        if (_lookup.TryGetValue(name, out var sprite)) {
            return sprite;
        }

        sprite = Resources.FindObjectsOfTypeAll<Sprite>()
            .LastOrDefault(s => s.name == name);

        return _lookup[name] = sprite!;
    }

    extension(YKLayout layout)
    {
        public UIInputText AddPair(string idLang, string text)
        {
            var pair = layout.Horizontal();
            pair.Layout.childForceExpandWidth = true;

            pair.Text(idLang);
            var input = pair.InputText(text);

            input.type = UIInputText.Type.Name;
            input.field.characterLimit = 150;
            input.field.contentType = InputField.ContentType.Standard;
            input.field.inputType = InputField.InputType.Standard;
            input.field.characterValidation = InputField.CharacterValidation.None;

            input.Text = text;

            return input;
        }

        public YKVertical MakeCard()
        {
            var card = layout.Vertical();
            card.LayoutElement().flexibleWidth = 1f;
            card.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.Layout.padding = CardPadding;
            card.Layout.spacing = RowSpacing;

            var cardBg = card.Layout.gameObject.AddComponent<Image>();
            cardBg.sprite = FindSprite("buttonBig");
            cardBg.type = Image.Type.Sliced;

            return card;
        }

        /// <summary>
        ///     One setting: its row and the lines under it, close together (rows are RowSpacing apart in the card);
        ///     indented under the choice it belongs to
        /// </summary>
        public YKVertical RowGroup(bool indented = false)
        {
            var group = layout.Vertical();
            group.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (indented) {
                group.Layout.padding = new(SubIndent, 0, 0, 0);
            }
            return group;
        }

        /// <summary>
        ///     A dropdown row: the label takes the free width, the dropdown a fixed width on the right, so that every
        ///     dropdown of a card starts at the same place
        /// </summary>
        public UIDropdown DropdownRow(string label, List<string> options, Action<int> onPick, int value)
        {
            var row = layout.Horizontal();
            row.Layout.childForceExpandWidth = false;
            row.Layout.childAlignment = TextAnchor.MiddleLeft;
            row.Layout.spacing = 10f;

            var text = row.Text(label);
            text.SetSize(LabelSize);
            text.LayoutElement().flexibleWidth = 1f;

            var dropdown = row.Dropdown(options, onPick, value);
            var element = dropdown.LayoutElement();
            element.preferredWidth = ControlWidth;
            element.flexibleWidth = 0f;
            dropdown.Rect().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RowHeight);
            return dropdown;
        }

        /// <summary>
        ///     Buttons side by side, left-aligned, each FixedWidth (danger buttons last)
        /// </summary>
        public YKHorizontal ButtonRow()
        {
            var row = layout.Horizontal();
            SetUpButtonRow(row);
            return row;
        }

        public UIButton RowButton(string text, Action action)
        {
            return layout.Button(text, action).FixedWidth();
        }

        public UIItem AddImageCard(Component parent, Sprite sprite)
        {
            var item = Util.Instantiate<UIItem>("UI/Element/Deco/ImageNote", parent);
            var bg = item.image1;
            var rt = bg.rectTransform;

            bg.sprite = sprite;
            if (bg.sprite != null) {
                bg.SetNativeSize();
                (rt.parent as RectTransform)?.sizeDelta = rt.sizeDelta;
            }
            return item;
        }
    }
}