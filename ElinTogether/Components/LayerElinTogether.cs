using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

internal class LayerElinTogether : YKLayer<LayerCreationData>
{
    // First opening: a little right and down of the centre, so the window clears the debug box that the game's popup
    // stack draws at the top left (it hid the first tab and the caption); later openings keep where the player left it
    private static Vector2 _browsedPosition = new(120f, -30f);
    private static string _lastOpenedTab = "";
    private readonly bool _resetHyp = Lang.setting.hyphenation;

    private readonly List<TabEmpBase> _tabs = [];

    public override string Title => "emp_ui_title".lang();
    public override Rect Bound => FixedWindow();

    public static LayerElinTogether? Instance { get; private set; }

    public override void OnLayout()
    {
        Instance = this;

        Resources.Load<UIButton>("UI/Element/Button/ButtonNote");

        // the mod's logo, two putits (package/Texture/emp_logo.png); the HUD button uses it too
        Window.setting.spriteCaption = MultiplayerHubPatch.Logo ?? MultiplayerHubPatch.Icon;

        if (_resetHyp) {
            Lang.setting.hyphenation = false;
        }

        // Play and Players: always there, the same for everyone
        _tabs.Add(CreateTab<TabPlay>("emp_ui_tab_play", TabPlay.TabName));
        DecorateLastTab("icon_LayerTravel", "emp_tip_tab_play");
        _tabs.Add(CreateTab<TabPlayers>("emp_ui_tab_players", TabPlayers.TabName));
        DecorateLastTab("icon_LayerPeople", "emp_tip_tab_players");

        // the teams and their lands (council 2026-10-28): always there, saying so when teams are off
        _tabs.Add(CreateTab<TabTeam>("emp_ui_tab_team", "emp_tab_team"));
        DecorateLastTab("icon_factions", "emp_tip_tab_team");
        _tabs.Add(CreateTab<TabLands>("emp_ui_tab_lands", "emp_tab_lands"));
        DecorateLastTab("icon_LayerHome", "emp_tip_tab_lands");

        // journal and rankings (PLAN_hub_features): one tab with two sub-tabs, so the strip keeps the game's own label size
        _tabs.Add(CreateTab<TabCommunity>("emp_ui_tab_community", TabCommunity.TabName));
        DecorateLastTab("icon_medal", "emp_tip_tab_community");

        // the world's rules: always there. The host edits them (also at the title screen, before hosting), a guest
        // reads the host's (the tab's own guest view)
        _tabs.Add(CreateTab<TabServerConfiguration>("emp_ui_tab_server", "emp_tab_server"));
        DecorateLastTab("icon_policy", "emp_tip_tab_server");

        _tabs.Add(CreateTab<TabServerInfo>("emp_ui_tab_info", "emp_tab_info"));
        DecorateLastTab("icon_book", "emp_tip_tab_info");
        _tabs.Add(CreateTab<TabClientConfiguration>("emp_ui_tab_client", "emp_tab_client"));
        DecorateLastTab("icon_m_system", "emp_tip_tab_client");

        foreach (var tab in _tabs) {
            UIHelper.WrapTexts(tab);
            tab.RebuildLayout(true);
        }
    }

    public override void OnAfterAddLayer()
    {
        base.OnAfterAddLayer();

        if (!string.IsNullOrEmpty(Data.StartingTab)) {
            _lastOpenedTab = Data.StartingTab;
        }

        Window.SwitchContent(_lastOpenedTab);

        Window.transform.localPosition = _browsedPosition;
        try {
            MoveTabsToLeft();
        }
        catch (System.Exception e) {
            EmpLog.Debug(e, "The tab column could not be built");
        }
        Decorate();
#if DEBUG
        // the debug box (Ping/Delta/Snap/Dump) sits over the window: hidden while the menu is open
        if (NetSession.Instance.Transport is { IsDebugGuiActive: true } net) {
            net.StopDebugGui();
            _debugHidden = true;
        }
#endif
    }

#if DEBUG
    private bool _debugHidden;
#endif

    /// <summary>
    ///     The character sheet's window art on this window: the ornament at the top right and the faint drawing in the
    ///     bottom right corner, copied from its prefab (behind the pages, over the parchment)
    /// </summary>
    private void Decorate()
    {
        try {
            var bg = Window.transform.Find("BG") ?? Window.transform;
            if (EmpUiParts.CharaBackground("Image") is { } art) {
                // (anchored to the bottom right corner in the sheet already)
                var copy = Instantiate(art, bg, false);
                copy.name = "EmpArt";
                if (bg == Window.transform) {
                    copy.SetAsFirstSibling();
                }
            }

            if (EmpUiParts.CharaBackground("Image (3)") is { } ornament) {
                // in the sheet it is placed from the left edge of its 920 wide window: here from the right edge
                var width = ornament.parent is RectTransform parent && parent.rect.width > 1f ? parent.rect.width : 962f;
                var copy = (RectTransform)Instantiate(ornament, bg, false);
                copy.name = "EmpOrnament";
                var x = ornament.anchoredPosition.x + ornament.anchorMin.x * width - width;
                copy.anchorMin = copy.anchorMax = Vector2.one;
                copy.anchoredPosition = new(x, ornament.anchoredPosition.y);
            }
        }
        catch (System.Exception e) {
            EmpLog.Debug(e, "The window art of the character sheet could not be copied");
        }

        // (the two putits keep their shape in the caption's square)
        if (Window.iconCaption != null) {
            var icon = Window.iconCaption;
            icon.preserveAspect = true;
            // as tall as the caption's text, like the game's caption icons: the sprite's own size covered the
            // window's corner and the caption's first letters
            var height = Mathf.Clamp(Window.textCaption != null ? Window.textCaption.Rect().rect.height : 16f, 12f, 16f);
            var aspect = icon.sprite != null && icon.sprite.rect.height > 0f ? icon.sprite.rect.width / icon.sprite.rect.height : 1f;
            icon.rectTransform.sizeDelta = new(height * aspect, height);
            var element = icon.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? icon.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minWidth = element.preferredWidth = height * aspect;
            element.minHeight = element.preferredHeight = height;
        }
    }

    public override void OnKill()
    {
        if (Window != null) {
            _browsedPosition = Window.transform.localPosition;

            if (Window.CurrentContent != null) {
                _lastOpenedTab = Window.CurrentContent.name;
            }
        }

        if (_resetHyp) {
            Lang.setting.hyphenation = true;
        }

#if DEBUG
        if (_debugHidden && NetSession.Instance.Transport is { IsDebugGuiActive: false } net) {
            net.StartDebugGui();
        }
#endif
        Instance = null;
    }

    public void Reopen()
    {
        ui.RemoveLayer(this);
        OpenPanelSesame(_lastOpenedTab);
    }

    [ElinContextMenuEntry("emp_ui_title")]
    private static void OpenInternal()
    {
        OpenPanelSesame();
    }

    public static void OpenPanelSesame(string targetTab = "")
    {
        YK.CreateLayer<LayerElinTogether, LayerCreationData>(new(TabCommunity.Redirect(targetTab)));
    }

    /// <summary>
    ///     The game's own tab buttons (same prefab, same selected look through the window's group) stacked in a column
    ///     on the left edge, all one size (the longest label at the game's font size); the pages sit to their right.
    ///     Tab ids, the group and SwitchContent are untouched: only the strip's container changes
    /// </summary>
    private void MoveTabsToLeft()
    {
        var strip = Window.rectTab;
        var view = Window.view != null ? Window.view.transform as RectTransform : null;
        if (strip == null || view == null || view.parent == null) {
            return;
        }

        // a body row in the view's place: [tab column][pages]
        var body = new GameObject("EmpBody", typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
        var bodyRect = (RectTransform)body.transform;
        bodyRect.SetParent(view.parent, false);
        bodyRect.SetSiblingIndex(view.GetSiblingIndex());
        var bodyElement = body.GetComponent<UnityEngine.UI.LayoutElement>();
        bodyElement.flexibleWidth = bodyElement.flexibleHeight = 1f;
        // (the view's own size hints carry over to the row, so the window gives it the same room)
        if (view.GetComponent<UnityEngine.UI.LayoutElement>() is { } old) {
            bodyElement.minHeight = old.minHeight;
            bodyElement.preferredHeight = old.preferredHeight;
        }

        // the old horizontal strip: its layout and fitter go, so nothing is left of the top strip
        foreach (var fitter in strip.GetComponents<UnityEngine.UI.ContentSizeFitter>()) {
            DestroyImmediate(fitter);
        }

        foreach (var group in strip.GetComponents<UnityEngine.UI.LayoutGroup>()) {
            DestroyImmediate(group);
        }

        // sizes first: the column's width is the widest label (game font size) plus the icon and margins
        float width = 0f;
        var buttons = new List<UIButton>();
        foreach (var tab in Window.setting.tabs) {
            if (tab.button == null) {
                continue;
            }

            buttons.Add(tab.button);
            var text = tab.button.mainText;
            width = Mathf.Max(width, (text != null ? text.preferredWidth : 0f) + 26f + 28f);
        }

        const float Gap = 4f, Margin = 10f, ButtonHeight = 30f;
        strip.SetParent(bodyRect, false);
        strip.SetAsFirstSibling();
        strip.anchorMin = new(0f, 0f);
        strip.anchorMax = new(0f, 1f);
        strip.pivot = new(0f, 1f);
        strip.offsetMin = new(0f, 0f);
        strip.offsetMax = new(width, 0f);
        strip.gameObject.SetActive(true);
        var column = strip.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        column.childAlignment = TextAnchor.UpperLeft;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        column.spacing = Gap;

        // the pages start to the right of the column: nothing under the tabs
        view.SetParent(bodyRect, false);
        foreach (var ignore in view.GetComponents<UnityEngine.UI.LayoutElement>()) {
            ignore.ignoreLayout = true;
        }

        view.anchorMin = Vector2.zero;
        view.anchorMax = Vector2.one;
        view.pivot = new(0f, 1f);
        view.offsetMin = new(width + Margin, 0f);
        view.offsetMax = Vector2.zero;

        foreach (var button in buttons) {
            button.transform.SetParent(strip, false);
            var element = button.GetComponent<UnityEngine.UI.LayoutElement>() ?? button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = ButtonHeight;
            element.flexibleWidth = element.flexibleHeight = 0f;
            if (button.icon != null) {
                // icon at the left edge, label to its right
                var ir = button.icon.rectTransform;
                ir.anchorMin = ir.anchorMax = new(0f, 0.5f);
                ir.pivot = new(0f, 0.5f);
                ir.anchoredPosition = new(6f, 0f);
                ir.sizeDelta = new(22f, 22f);
                button.icon.preserveAspect = true;
            }

            if (button.mainText != null) {
                if (button.mainText.TryGetComponent<UnityEngine.UI.ContentSizeFitter>(out var f)) {
                    DestroyImmediate(f);
                }

                button.mainText.horizontalOverflow = HorizontalWrapMode.Overflow;
                button.mainText.alignment = TextAnchor.MiddleLeft;
                var tr = button.mainText.rectTransform;
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = new(34f, 0f);
                tr.offsetMax = new(-4f, 0f);
            }
        }

        // (the window shows the strip only for 2+ tabs: always the case here)
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(bodyRect);
    }

    /// <summary>One game sprite and a tooltip for the tab CreateTab just added (it takes neither).</summary>
    private void DecorateLastTab(string spriteId, string tipId)
    {
        var tab = Window.setting.tabs[^1];
        tab.sprite = SpriteSheet.Get(spriteId);
        tab.langTooltip = tipId;
    }

    /// <summary>A vanilla-like fixed window (the character sheet's footprint), shrunk only on a small screen.</summary>
    private static Rect FixedWindow()
    {
        var scaler = ui.canvasScaler.scaleFactor;
        var screen = new Vector2(Screen.width, Screen.height) / scaler;
        return new(Vector2.zero, new(Mathf.Min(1180f, screen.x * 0.95f), Mathf.Min(620f, screen.y * 0.85f)));
    }
}