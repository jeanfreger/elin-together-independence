using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using UnityEngine;
using UnityEngine.UI;
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

    // ---------------------------------------------------------------- sub-tabs

    // the sub-tab each page was left on, kept over reopenings while the game runs (never stored)
    private static readonly Dictionary<string, int> LastSubTab = [];
    private const float PillRowHeight = 40f;

    private readonly List<GameObject> _subPages = [];
    private string _subKey = "";

    /// <summary>
    ///     The sub-tab shown now (0 when the page has none)
    /// </summary>
    protected int SubTab => LastSubTab.TryGetValue(_subKey, out var i) && i < _subPages.Count ? i : 0;

    /// <summary>
    ///     Pages of this tab behind a row of the character sheet's tab pills along the bottom of the window (the
    ///     current one highlighted, the choice remembered per tab). Each page is built at once and only shown or
    ///     hidden, so a typed text or an open dropdown survives a switch. With one page there is no row
    /// </summary>
    protected List<YKVertical> SubTabs(string key, IReadOnlyList<(string Label, Action<YKVertical> Build)> pages)
    {
        _subKey = key;
        var built = new List<YKVertical>();
        foreach (var (_, build) in pages) {
            var page = Vertical();
            page.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            page.LayoutElement().flexibleWidth = 1f;
            page.Layout.spacing = UIHelper.RowSpacing;
            build(page);
            _subPages.Add(page.gameObject);
            built.Add(page);
        }

        if (pages.Count > 1) {
            BuildPills(pages.Select(p => p.Label).ToList());
        }

        ShowSubTab(SubTab, false);
        return built;
    }

    /// <summary>
    ///     Shows one page (scrolled to its top) and highlights its pill
    /// </summary>
    protected void ShowSubTab(int index, bool relayout = true)
    {
        if (_subPages.Count == 0) {
            return;
        }

        index = Mathf.Clamp(index, 0, _subPages.Count - 1);
        LastSubTab[_subKey] = index;
        for (var i = 0; i < _subPages.Count; i++) {
            _subPages[i].SetActive(i == index);
        }

        if (_pills != null && index < _pills.list.Count) {
            _pills.Select(_pills.list[index]);
        }

        OnSubTabShown(index);
        if (!relayout) {
            return;
        }

        Relayout();
        if (GetComponentInParent<ScrollRect>() is { } scroll) {
            scroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>
    ///     After a sub-tab is shown (a page that filters its rows does it again here)
    /// </summary>
    protected virtual void OnSubTabShown(int index)
    {
    }

    /// <summary>
    ///     Every page shown at once (a search runs through all of them), or back to the current one
    /// </summary>
    protected void ShowAllSubTabs(bool all)
    {
        for (var i = 0; i < _subPages.Count; i++) {
            _subPages[i].SetActive(all || i == SubTab);
        }
    }

    private UISelectableGroup? _pills;

    private void BuildPills(List<string> labels)
    {
        // (the row hangs at the bottom of this tab's scroll view, outside the scrolled content, as the character
        // sheet's row hangs under its pages; the view is shortened by the row's height)
        if (GetComponentInParent<ScrollRect>() is not { } scroll || EmpUiParts.Pill is not { } mold) {
            return;
        }

        var holder = new GameObject("SubTabs", typeof(RectTransform));
        holder.SetActive(false);
        var rt = (RectTransform)holder.transform;
        rt.SetParent(scroll.transform, false);
        rt.anchorMin = new(0f, 0f);
        rt.anchorMax = new(1f, 0f);
        rt.pivot = new(0.5f, 0f);
        var column = holder.AddComponent<VerticalLayoutGroup>();
        column.childAlignment = TextAnchor.LowerCenter;
        column.childControlWidth = true;
        column.childControlHeight = false;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        var group = holder.AddComponent<UISelectableGroup>();
        group.autoInitialize = false;

        // the pills flow onto a second line when they do not fit in one (World rules has nine)
        // (the view is not laid out yet when the page is built: the window's own width)
        var width = Mathf.Max(scroll.GetComponent<RectTransform>().rect.width, Layer != null ? Layer.Bound.width : 0f, 400f) - 60f;
        HorizontalLayoutGroup? line = null;
        var used = 0f;
        var lines = 0;
        foreach (var label in labels) {
            var pill = Util.Instantiate(mold, holder.transform);
            pill.mainText.SetText(label);
            if (pill.icon != null) {
                pill.icon.gameObject.SetActive(false);
            }
            pill.tooltip.enable = false;
            pill.RebuildLayout(true);
            var w = pill.Rect().rect.width > 1f ? pill.Rect().rect.width : pill.mainText.preferredWidth + 40f;

            if (line == null || used + w > width) {
                line = new GameObject("Line", typeof(RectTransform)).AddComponent<HorizontalLayoutGroup>();
                line.transform.SetParent(holder.transform, false);
                line.childAlignment = TextAnchor.MiddleCenter;
                line.childControlWidth = false;
                line.childControlHeight = false;
                line.childForceExpandWidth = false;
                line.spacing = 2f;
                line.GetComponent<RectTransform>().sizeDelta = new(0f, PillRowHeight);
                used = 0f;
                lines++;
            }

            pill.transform.SetParent(line.transform, false);
            used += w + 2f;
        }

        var height = lines * PillRowHeight + 4f;
        rt.sizeDelta = new(0f, height);
        rt.anchoredPosition = new(0f, 2f);
        if (scroll.viewport != null) {
            scroll.viewport.offsetMin = new(scroll.viewport.offsetMin.x, scroll.viewport.offsetMin.y + height);
        }
        if (scroll.verticalScrollbar != null && scroll.verticalScrollbar.transform is RectTransform bar) {
            bar.offsetMin = new(bar.offsetMin.x, bar.offsetMin.y + height);
        }

        holder.SetActive(true);
        group.Init(SubTab, i => ShowSubTab(i));
        _pills = group;
    }

    public override void OnSwitchContent(int idTab)
    {
        base.OnSwitchContent(idTab);
        Relayout();
    }

    /// <summary>
    ///     Lays the page out again, twice and once more on the next frame. One pass of the game's RebuildLayout sizes
    ///     each text before its column has its width, so a description that wraps to two lines kept the height of one
    ///     and ran under the next row (My settings, 2026-10-28); the second pass measures it at its real width
    /// </summary>
    internal void Relayout()
    {
        for (var i = 0; i < 2; i++) {
            this.RebuildLayout(true);
            this.RebuildLayoutTo<UIContent>();
        }

        if (isActiveAndEnabled) {
            StartCoroutine(RelayoutLater());
        }
    }

    private IEnumerator RelayoutLater()
    {
        yield return null;
        this.RebuildLayout(true);
        this.RebuildLayoutTo<UIContent>();
    }
}
