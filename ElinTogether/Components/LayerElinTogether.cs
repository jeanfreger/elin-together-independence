using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

internal class LayerElinTogether : YKLayer<LayerCreationData>
{
    // First opening: centred on the screen; later openings keep where the player left it
    private static Vector2 _browsedPosition = Vector2.zero;
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

        // the Journal's tab buttons (its window's tab mold), set before Window.Init builds the tabs; tabAfterCaption
        // as in the Journal, so the skin never moves the tab strip back over the pages
        if (JournalWindow() is { } journal && JournalTabMold(journal) is { } mold) {
            Window.moldTab = mold;
        }

        Window.setting.tabAfterCaption = true;

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
            JournalTabsOnTheLeft();
        }
        catch (System.Exception e) {
            EmpLog.Debug(e, "The Journal-style tabs could not be placed");
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

    /// <summary>The Journal layer's window in its prefab (looked up like Layer.Create does, never instantiated).</summary>
    private static Window? JournalWindow()
    {
        foreach (var folder in Layer.searchPath) {
            var layer = Resources.Load<Layer>($"UI/{folder}/LayerJournal");
            if (layer == null) {
                layer = Resources.Load<Layer>($"UI/{folder}/LayerJournal/LayerJournal");
            }

            if (layer != null) {
                return layer.windows.Count > 0 ? layer.windows[0] : layer.GetComponentInChildren<Window>(true);
            }
        }

        return null;
    }

    /// <summary>The "ButtonTab Journal" button: the window's tab mold, else a tab's custom button, else one in its strip.</summary>
    private static UIButton? JournalTabMold(Window journal)
    {
        if (journal.moldTab != null) {
            return journal.moldTab;
        }

        foreach (var tab in journal.setting.tabs) {
            if (tab.customTab != null) {
                return tab.customTab;
            }
        }

        return journal.rectTab != null ? journal.rectTab.GetComponentInChildren<UIButton>(true) : null;
    }

    // the Journal's strip sits at the window's top right, (-38, -84.8), growing out of the frame; mirrored here
    private const float JournalTabInset = 38f, JournalTabTop = -84.8f, JournalTabHeight = 54f;

    /// <summary>
    ///     The Journal's side tabs mirrored to the left: the window's own strip (same buttons, group and SwitchContent)
    ///     taken out of the window's layout, anchored at the top left and stacked downwards like bookmarks sticking out
    ///     of the frame. Every button gets the widest label's width and the Journal's 54 height; the button is flipped
    ///     so its bookmark art points left, its label (and the icon inside it) flipped back so they read normally
    /// </summary>
    private void JournalTabsOnTheLeft()
    {
        var strip = Window.rectTab;
        if (strip == null) {
            return;
        }

        var journal = JournalWindow();
        var journalStrip = journal != null ? journal.rectTab : null;

        if (strip.parent != Window.transform) {
            strip.SetParent(Window.transform, false);
        }

        var stripElement = strip.GetComponent<UnityEngine.UI.LayoutElement>() ?? strip.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        stripElement.ignoreLayout = true;

        // the horizontal strip's row and fitter go; the Journal's column takes their place
        foreach (var fitter in strip.GetComponents<UnityEngine.UI.ContentSizeFitter>()) {
            DestroyImmediate(fitter);
        }

        foreach (var group in strip.GetComponents<UnityEngine.UI.HorizontalLayoutGroup>()) {
            DestroyImmediate(group);
        }

        var column = strip.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? strip.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (journalStrip != null && journalStrip.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() is { } model) {
            column.spacing = model.spacing;
            column.padding = new(model.padding.right, model.padding.left, model.padding.top, model.padding.bottom);
        }

        // right aligned: the bookmarks' inner ends line up under the frame, mirroring the Journal's left alignment
        column.childAlignment = TextAnchor.UpperRight;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = column.childForceExpandHeight = false;

        // the strip's own background as in the Journal
        if (journalStrip != null && journalStrip.GetComponent<UnityEngine.UI.Image>() is { } journalBg
            && strip.GetComponent<UnityEngine.UI.Image>() is { } bg) {
            bg.sprite = journalBg.sprite;
            bg.color = journalBg.color;
            bg.type = journalBg.type;
            bg.enabled = journalBg.enabled;
        }

        // the widest label decides every button's width (each button's own fitted size, at the game's font size)
        var buttons = new List<UIButton>();
        var width = 0f;
        foreach (var tab in Window.setting.tabs) {
            if (tab.button == null) {
                continue;
            }

            var rect = (RectTransform)tab.button.transform;
            if (rect.GetComponent<UnityEngine.UI.LayoutElement>() is { } own) {
                own.preferredWidth = own.minWidth = -1f;
            }

            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            width = Mathf.Max(width, UnityEngine.UI.LayoutUtility.GetPreferredWidth(rect), rect.rect.width);
            buttons.Add(tab.button);
        }

        foreach (var button in buttons) {
            var rect = (RectTransform)button.transform;
            foreach (var fitter in button.GetComponents<UnityEngine.UI.ContentSizeFitter>()) {
                fitter.horizontalFit = fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            }

            var element = button.GetComponent<UnityEngine.UI.LayoutElement>() ?? button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = JournalTabHeight;
            element.flexibleWidth = element.flexibleHeight = 0f;
            rect.sizeDelta = new(width, JournalTabHeight);

            // the bookmark art faces the other way: the button flips around its centre, its label flips back
            // (UIButton only ever scales its icon, which lives inside the label)
            rect.pivot = new(0.5f, rect.pivot.y);
            rect.localScale = new(-1f, 1f, 1f);
            if (button.mainText != null) {
                var text = button.mainText.rectTransform;
                text.pivot = new(0.5f, text.pivot.y);
                text.localScale = new(-1f, 1f, 1f);
                if (button.icon != null && !button.icon.transform.IsChildOf(text)) {
                    button.icon.rectTransform.localScale = new(-1f, 1f, 1f);
                }
            }
        }

        strip.anchorMin = strip.anchorMax = new(0f, 1f);
        strip.pivot = new(1f, 1f);
        strip.anchoredPosition = new(JournalTabInset, JournalTabTop);
        strip.sizeDelta = new(width + column.padding.horizontal, strip.sizeDelta.y);
        var fit = strip.GetComponent<UnityEngine.UI.ContentSizeFitter>() ?? strip.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        strip.SetAsLastSibling();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)Window.transform);
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
        return new(Vector2.zero, new(Mathf.Min(860f, screen.x * 0.9f), Mathf.Min(620f, screen.y * 0.85f)));
    }
}