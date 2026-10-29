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
        var body = new GameObject("EmpBody", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
        var bodyRect = (RectTransform)body.transform;
        bodyRect.SetParent(view.parent, false);
        bodyRect.SetSiblingIndex(view.GetSiblingIndex());
        var row = body.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = true;
        row.spacing = 8f;
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

        strip.SetParent(bodyRect, false);
        strip.SetAsFirstSibling();
        strip.anchorMin = Vector2.zero;
        strip.anchorMax = Vector2.one;
        strip.pivot = new(0f, 1f);
        strip.anchoredPosition = Vector2.zero;
        strip.gameObject.SetActive(true);
        var column = strip.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        column.childAlignment = TextAnchor.UpperLeft;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = column.childForceExpandHeight = false;
        column.spacing = 4f;

        view.SetParent(bodyRect, false);
        var viewElement = view.GetComponent<UnityEngine.UI.LayoutElement>() ?? view.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        viewElement.flexibleWidth = viewElement.flexibleHeight = 1f;

        // every button the same size: the widest label (game font size, never shrunk) plus the icon and the margins
        float width = 0f, height = 0f;
        var buttons = new List<UIButton>();
        foreach (var tab in Window.setting.tabs) {
            if (tab.button == null) {
                continue;
            }

            buttons.Add(tab.button);
            var text = tab.button.mainText;
            var iconWidth = tab.button.icon != null ? Mathf.Max(tab.button.icon.rectTransform.rect.width, 24f) : 0f;
            width = Mathf.Max(width, (text != null ? text.preferredWidth : 0f) + iconWidth + 40f);
            height = Mathf.Max(height, tab.button.Rect().rect.height, text != null ? text.preferredHeight + 16f : 0f);
        }

        height = Mathf.Max(height, 40f);
        foreach (var button in buttons) {
            button.transform.SetParent(strip, false);
            var element = button.GetComponent<UnityEngine.UI.LayoutElement>() ?? button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = height;
            element.flexibleWidth = element.flexibleHeight = 0f;
            if (button.mainText != null) {
                button.mainText.horizontalOverflow = HorizontalWrapMode.Overflow;
                if (button.mainText.TryGetComponent<UnityEngine.UI.ContentSizeFitter>(out var f)) {
                    f.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
                }
            }
        }

        var stripElement = strip.GetComponent<UnityEngine.UI.LayoutElement>() ?? strip.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        stripElement.minWidth = stripElement.preferredWidth = width;
        stripElement.flexibleWidth = 0f;
        stripElement.flexibleHeight = 1f;
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
        return new(Vector2.zero, new(Mathf.Min(1040f, screen.x * 0.95f), Mathf.Min(620f, screen.y * 0.85f)));
    }
}