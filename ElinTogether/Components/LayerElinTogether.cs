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

        // the world's rules: always there. The host edits them (also at the title screen, before hosting), a guest
        // reads the host's (the tab's own guest view)
        _tabs.Add(CreateTab<TabServerConfiguration>("emp_ui_tab_server", "emp_tab_server"));
        DecorateLastTab("icon_policy", "emp_tip_tab_server");

        _tabs.Add(CreateTab<TabServerInfo>("emp_ui_tab_info", "emp_tab_info"));
        DecorateLastTab("icon_book", "emp_tip_tab_info");
        _tabs.Add(CreateTab<TabClientConfiguration>("emp_ui_tab_client", "emp_tab_client"));
        DecorateLastTab("icon_m_system", "emp_tip_tab_client");
        _tabs.Add(CreateTab<TabHelp>("emp_ui_tab_help", "emp_tab_help"));
        DecorateLastTab("icon_LayerHelp", "emp_tip_tab_help");

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
        Decorate();
    }

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
        YK.CreateLayer<LayerElinTogether, LayerCreationData>(new(targetTab));
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