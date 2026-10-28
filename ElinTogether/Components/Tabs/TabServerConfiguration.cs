using ElinTogether.LangMod;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Net.Steam;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The rules page ("World rules", step 6 of dev/PLAN_multiplayer_hub.md), the same page for everyone: the host
///     edits, a friend reads what the host told (SettingsTable.FromRules, as before). A banner says who decides,
///     three preset cards, a search box, then a collapsible group per card of SettingsTable with each row as title,
///     grey line, control; the advanced rows are folded per group. Changes apply at once. Searching, folding and
///     jumping only show or hide what is built (no redraw), so the typed text stays
/// </summary>
internal class TabServerConfiguration : TabEmpBase
{
    /// <summary>
    ///     Advanced rows shown on the game's mod config page (ModConfigPage): a view of that page only, never stored
    /// </summary>
    internal static bool ShowAdvanced;

    // the view of this page, kept over redraws (a change redraws the layer), never stored
    private static string _search = "";
    private static readonly HashSet<string> Collapsed = [];
    private static readonly HashSet<string> Expanded = [];
    private static bool _allDiffs;
    private static TabServerConfiguration? _instance;
    private static float _savedY;
    private static int _restore;

    private sealed class Line(SettingRow row, GameObject go, string hay, bool changed)
    {
        internal SettingRow Row => row;
        internal GameObject Go => go;
        internal string Hay => hay;
        internal bool Changed => changed;
    }

    private sealed class Group(string id)
    {
        internal string Id => id;
        internal GameObject Card = null!, Body = null!, More = null!, Dot = null!;
        internal UIButton Header = null!, MoreButton = null!;
        internal string Title = "";
        internal readonly List<Line> Lines = [];
        internal int AdvancedCount => Lines.Count(l => l.Row.Advanced);
    }

    private readonly List<Group> _groups = [];
    private UIInputText? _searchInput;
    private UIText? _noMatch;
    private bool _guest;
    private RuleSource _src = RuleSource.Current;
    private RuleSource _target = RuleSource.Defaults;

    // what a greyed row needs, by row id; "!" in front: that row must be off. (Requires only holds the host's own
    // answer, a friend's page must work from what it was told)
    private static readonly Dictionary<string, string[]> Needs = new() {
        ["export_chara"] = ["import_chara"],
        ["player_clock"] = ["combat_time"],
        ["step_pace"] = ["combat_time"],
        ["turn_combat"] = ["!combat_time"],
        ["duel_arena"] = ["duels"],
        ["duel_bets"] = ["duels"],
        ["team_bases"] = ["teams"],
        ["team_approval"] = ["teams"],
        ["team_deed"] = ["teams"],
        ["team_leaders"] = ["teams"],
        ["team_personal"] = ["teams"],
        ["team_pvp"] = ["teams"],
        ["team_bills"] = ["teams"],
        ["team_story"] = ["teams"],
        ["team_boxes"] = ["teams"],
        ["land_rules"] = ["teams", "team_bases"],
        ["sibling_lands"] = ["teams", "team_bases"],
        ["land_abandon"] = ["teams", "team_bases"],
        ["lands_per_team"] = ["teams", "team_bases"],
        ["abandon_days"] = ["teams", "team_bases", "land_abandon"],
        ["team_max_size"] = ["teams", "team_leaders"],
        ["team_found_cooldown"] = ["teams", "team_leaders"],
        ["spawn_zone"] = ["teams", "independent_travel"],
        ["team_own_guilds"] = ["team_own_story"],
        ["night_passes"] = ["own_sleep"],
        ["living_world"] = ["world_time"],
        ["absent_quests_wait"] = ["personal_quests"],
        ["town_boards"] = ["personal_quests"],
        ["world_map_lead"] = ["world_map_choice"],
        ["look_mods_own"] = ["publish_mods"],
    };

    // (on the official server these are fixed: see SettingRow.Description)
    private static readonly string[] OfficialFixed = ["import_chara", "export_chara", "team_knowledge", "team_boxes", "team_own_story"];

    public override void OnLayout()
    {
        _instance = this;
        _guest = NetSession.Instance.Transport is ElinNetClient;
        _src = _guest ? SettingsTable.FromRules(NetSession.Instance.Rules) : RuleSource.Current;

        var rows = SettingsTable.Rows().Where(r => !_guest || Told(r)).ToList();
        var preset = Preset(rows);
        _target = preset == 1 ? SettingsTable.Classic : RuleSource.Defaults;

        BuildBanner();
        BuildPresetCards(preset);
        if (preset == 2) {
            BuildDifferences(rows);
        }

        BuildSearch();
        foreach (var id in SettingsTable.Cards) {
            var inCard = rows.Where(r => r.Card == id).ToList();
            if (inCard.Count > 0) {
                BuildGroup(id, inCard);
            }
        }

        _noMatch = Vertical(this).TextSmall("emp_ui_sv_nomatch".lang(), FontColor.Gray);
        ApplyFilter();
    }

    // (a vertical group sizes its height to its content only with this fitter)
    private static YKVertical Vertical(YKLayout parent, bool card = false)
    {
        var box = card ? parent.MakeCard() : parent.Vertical();
        box.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return box;
    }

    /// <summary>
    ///     A friend is told only some rules: the others are not shown (a row it cannot read would say "off")
    /// </summary>
    private bool Told(SettingRow row) => row.Choice is { } c ? c.Entries.Any(e => _src.Raw(e) is not null)
        : row.Entry is { } e && _src.Raw(e) is not null;

    private static bool IsChanged(SettingRow row, RuleSource src, RuleSource target)
    {
        // (the hidden server is the player's own choice, not a rule of the world; see HostRules)
        if (row.Id is "hidden_server") {
            return false;
        }

        if (row.Choice is { } c) {
            var known = c.Entries.Count(e => src.Raw(e) is not null);
            return known > 0 && (known == c.Entries.Length || c.Read(src) >= 0) && c.Read(src) != c.Read(target);
        }

        return row.Entry is { } e && src.Get(e) is { } v && v != target.Get(e);
    }

    private int Preset(List<SettingRow> rows)
    {
        var top = rows.Where(r => r.Under is null).ToList();
        return !top.Any(r => IsChanged(r, _src, RuleSource.Defaults)) ? 0
            : !top.Any(r => IsChanged(r, _src, SettingsTable.Classic)) ? 1 : 2;
    }

    private void BuildBanner()
    {
        var card = Vertical(this, true);
        var host = NetSession.Instance.CurrentPlayers.FirstOrDefault(p => p.Index == 0)?.User.Name ?? "";
        card.HeaderSmall(_guest ? "emp_ui_sv_banner_guest".Loc(host) : "emp_ui_sv_banner_host".lang());
        card.TextSmall(_guest ? "emp_ui_rules_note".lang() : "emp_ui_sv_note_live".lang(), FontColor.Gray);
        if (_guest && SettingsTable.LastNotice is { } notice) {
            card.TextSmall(notice);
        }
    }

    /// <summary>
    ///     Recommended, Classic and Custom as three cards side by side; the current one is tinted and says so. A
    ///     friend sees them as a read-only answer
    /// </summary>
    private void BuildPresetCards(int preset)
    {
        var row = Vertical(this).Horizontal();
        row.Layout.childForceExpandWidth = true;
        row.Layout.spacing = UIHelper.RowSpacing * 1.5f;
        row.Layout.childAlignment = TextAnchor.UpperLeft;
        for (var i = 0; i < SettingsTable.Presets.Length; i++) {
            var index = i;
            var card = Vertical(row, true);
            card.LayoutElement().flexibleWidth = 1f;
            card.LayoutElement().preferredWidth = 0f;
            var selected = i == preset;
            if (selected && card.Layout.TryGetComponent<Image>(out var bg)) {
                bg.color = new(1f, 0.9f, 0.62f);
            }

            var title = card.Button($"emp_ui_sv_opt_preset_{SettingsTable.Presets[i]}".lang(), () => {
                if (index == 2) {
                    // Custom writes nothing: it opens every group's folded rows to edit by hand
                    foreach (var g in _groups) {
                        Expanded.Add(g.Id);
                    }
                    ApplyFilter();
                    return;
                }

                Capture();
                SettingsTable.ApplyPreset(index);
            });
            title.interactable = !_guest;
            card.TextSmall(selected ? "emp_ui_sv_preset_current".lang() : " ", FontColor.Good);
            card.TextSmall($"emp_ui_sv_desc_preset_{SettingsTable.Presets[i]}".lang(), FontColor.Gray);
        }

        if (!_guest && SettingsTable.CanUndo) {
            Vertical(this).ButtonRow().RowButton("emp_ui_sv_undo".lang(), () => {
                Capture();
                SettingsTable.Undo();
            });
        }
    }

    /// <summary>
    ///     What makes the world Custom, as a short list of the rows that differ from the recommended rules; a click
    ///     jumps to the row
    /// </summary>
    private void BuildDifferences(List<SettingRow> rows)
    {
        var diff = rows.Where(r => r.Under is null && IsChanged(r, _src, RuleSource.Defaults)).ToList();
        var card = Vertical(this, true);
        card.HeaderSmall("emp_ui_sv_diff_title".Loc(diff.Count));
        const int shown = 5;
        foreach (var row in diff.Take(_allDiffs ? diff.Count : shown)) {
            var id = row.Id;
            var button = card.ButtonRow().RowButton(row.Label, () => Jump(id));
            button.LayoutElement().preferredWidth = UIHelper.ControlWidth * 1.6f;
        }

        if (diff.Count > shown) {
            card.ButtonRow().RowButton((_allDiffs ? "emp_ui_sv_fewer" : "emp_ui_sv_diff_all").Loc(diff.Count), () => {
                _allDiffs = !_allDiffs;
                LayerElinTogether.Instance?.Reopen();
            });
        }
    }

    private void BuildSearch()
    {
        // the game's own input field (YKLayout.InputText: UIInputText over an InputField)
        var input = Vertical(this).InputText(_search);
        input.LayoutElement().flexibleWidth = 1f;
        if (input.field.placeholder is Text placeholder) {
            placeholder.text = "emp_ui_sv_search".lang();
        }
        input.field.onValueChanged.AddListener(text => {
            _search = text;
            ApplyFilter();
        });
        _searchInput = input;
    }

    private void BuildGroup(string id, List<SettingRow> rows)
    {
        var group = new Group(id) { Title = $"emp_ui_sv_sec_{id}".lang() };
        var card = Vertical(this, true);
        group.Card = card.gameObject;

        var head = card.Horizontal();
        head.Layout.childForceExpandWidth = false;
        head.Layout.childAlignment = TextAnchor.MiddleLeft;
        head.Layout.spacing = 10f;
        group.Header = head.Button(group.Title, () => {
            if (!Collapsed.Add(id)) {
                Collapsed.Remove(id);
            }
            ApplyFilter();
        });
        group.Header.LayoutElement().flexibleWidth = 1f;
        group.Dot = MakeDot(head);

        var body = Vertical(card);
        body.Layout.spacing = UIHelper.RowSpacing * 2;
        group.Body = body.gameObject;

        foreach (var row in rows) {
            if (BuildRow(body, row) is { } line) {
                group.Lines.Add(line);
            }
        }

        group.Dot.SetActive(group.Lines.Any(l => l.Changed));

        var more = body.Horizontal();
        more.Layout.childAlignment = TextAnchor.MiddleLeft;
        group.More = more.gameObject;
        group.MoreButton = more.Button("", () => {
            if (!Expanded.Add(id)) {
                Expanded.Remove(id);
            }
            ApplyFilter();
        }).FixedWidth();
        _groups.Add(group);
    }

    /// <summary>
    ///     One row: title and a grey line on the left (or what it needs), a dot and Revert when it differs from the
    ///     preset, the control on the right
    /// </summary>
    private Line? BuildRow(YKVertical parent, SettingRow row)
    {
        if (row.Id is "checks" or "bills") {
            return BuildSpecial(parent, row);
        }

        var changed = IsChanged(row, _src, _target);
        var line = parent.Horizontal();
        line.Layout.childForceExpandWidth = false;
        line.Layout.childAlignment = TextAnchor.MiddleLeft;
        line.Layout.spacing = 10f;
        if (row.Under is not null) {
            line.Layout.padding = new(UIHelper.SubIndent, 0, 0, 0);
        }

        var left = line.Vertical();
        left.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        left.Layout.spacing = 2f;
        left.LayoutElement().flexibleWidth = 1f;
        left.Text(row.Label).SetSize(UIHelper.LabelSize);
        foreach (var text in row.Description.Split('\n')) {
            left.TextSmall(text, FontColor.Gray);
        }

        var needs = NeedsText(row);
        if (needs is not null) {
            left.TextSmall(needs, FontColor.Warning);
        }

        var enabled = !_guest && needs is null;
        if (changed) {
            MakeDot(line);
            if (!_guest) {
                var revert = line.Button("emp_ui_sv_revert".lang(), () => Revert(row));
                revert.LayoutElement().preferredWidth = 110f;
                revert.LayoutElement().flexibleWidth = 0f;
            }
        }

        switch (row) {
            case { Choice: { } choice }:
                var current = choice.Read(_src);
                var options = Enumerable.Range(0, choice.Options.Length).Select(choice.OptionLabel).ToList();
                // (boxes that match no option: shown as such, never rewritten until the host picks one)
                if (current < 0) {
                    options.Add(choice.OptionLabel(-1));
                }
                var dropdown = line.Dropdown(options, i => {
                    Capture();
                    SettingsTable.Write(choice.WriteFor(i));
                }, current < 0 ? options.Count - 1 : current);
                dropdown.LayoutElement().preferredWidth = UIHelper.ControlWidth * 0.7f;
                dropdown.LayoutElement().flexibleWidth = 0f;
                dropdown.Rect().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, UIHelper.RowHeight);
                dropdown.interactable = enabled;
                break;
            case { Entry: { } entry }:
                var toggle = line.Toggle("", _src.Get(entry) ?? false, value => {
                    Capture();
                    SettingsTable.Write([(entry, value)]);
                });
                toggle.mainText.text = "";
                toggle.LayoutElement().preferredWidth = 50f;
                toggle.LayoutElement().flexibleWidth = 0f;
                toggle.AsRow();
                toggle.interactable = enabled;
                break;
        }

        return new(row, line.gameObject, $"{row.Label} {row.Description}", changed);
    }

    /// <summary>
    ///     The joining checks and the tax bills have their own controls (the host only, the page of a friend has no
    ///     line for them)
    /// </summary>
    private Line? BuildSpecial(YKVertical parent, SettingRow row)
    {
        if (_guest) {
            return null;
        }

        var box = Vertical(parent);
        box.Layout.padding = new(row.Under is not null ? UIHelper.SubIndent : 0, 0, 0, 0);
        box.Layout.spacing = UIHelper.RowSpacing;
        if (row.Id == "checks") {
            BuildValidationSets(box);
        } else if (EClass.core.IsGameStarted && NetSession.Instance.Transport is ElinNetHost) {
            // the count of unpaid tax bills against the bills that exist (ElinNetHost.RepairBillCount), in game
            var (count, found) = (EClass.player.taxBills, ElinNetHost.CountTaxBills());
            box.ButtonRow().RowButton("emp_ui_sv_bills".Loc(count, found), () => {
                var off = ElinNetHost.RepairBillCount(true);
                EmpPop.Information((off > 0 ? "emp_ui_sv_bills_done" : "emp_ui_sv_bills_none").Loc(off));
                LayerElinTogether.Instance?.Reopen();
            });
            box.TextSmall("emp_ui_sv_bills_desc".lang(), FontColor.Gray);
        } else {
            Destroy(box.gameObject);
            return null;
        }

        return new(row, box.gameObject, $"{row.Label} {row.Description}", false);
    }

    /// <summary>
    ///     Why a row is greyed ("Needs X"), or null when it is on. Read from what this page shows (a friend's rules
    ///     are the host's), the host's own Requires only for the rows this table does not name
    /// </summary>
    private string? NeedsText(SettingRow row)
    {
        if (!_guest && row.Requires is { } requires && requires()) {
            return null;
        }

        if (!_guest && OfficialServer.Hosting && OfficialFixed.Contains(row.Id)) {
            return "emp_ui_sv_needs_notofficial".lang();
        }

        if (!Needs.TryGetValue(row.Id, out var ids)) {
            return !_guest && row.Requires is not null ? "emp_ui_sv_needs_other".lang() : null;
        }

        var all = SettingsTable.Rows().ToDictionary(r => r.Id);
        foreach (var raw in ids) {
            var (id, off) = raw.StartsWith('!') ? (raw[1..], true) : (raw, false);
            var met = id == "world_map_choice" ? _src.Raw(EmpConfig.Server.WorldMapTime) is Net.WorldMapTime.PersonalPace
                : all[id].Entry is not { } e || _src.Get(e) is not { } v || v != off;
            if (!met) {
                return (off ? "emp_ui_sv_needs_off" : "emp_ui_sv_needs").Loc(all[id].Label);
            }
        }

        // (a guest is not told what the host's own check says: nothing greyed)
        return _guest ? null : "emp_ui_sv_needs_other".lang();
    }

    private void Revert(SettingRow row)
    {
        var changes = row.Choice is { } c ? c.Entries.Select(e => (e, _target.Raw(e)!)).ToList()
            : row.Entry is { } entry ? [((ConfigEntryBase)entry, _target.Raw(entry)!)] : [];
        Capture();
        SettingsTable.Write(changes);
    }

    /// <summary>
    ///     Searching, folding and the folded rows: shows and hides what is built, then lays the page out again
    /// </summary>
    private void ApplyFilter()
    {
        var query = _search.Trim();
        var any = false;
        foreach (var g in _groups) {
            var hits = 0;
            foreach (var line in g.Lines) {
                var show = query.Length > 0
                    ? $"{g.Title} {line.Hay}".Contains(query, System.StringComparison.OrdinalIgnoreCase)
                    : !line.Row.Advanced || Expanded.Contains(g.Id);
                line.Go.SetActive(show);
                hits += show ? 1 : 0;
            }

            var open = query.Length > 0 ? hits > 0 : !Collapsed.Contains(g.Id);
            g.Card.SetActive(query.Length == 0 || hits > 0);
            g.Body.SetActive(open);
            any |= g.Card.activeSelf;

            var advanced = g.AdvancedCount;
            g.More.SetActive(query.Length == 0 && advanced > 0);
            g.MoreButton.mainText.text = Expanded.Contains(g.Id) ? "emp_ui_sv_fewer".lang() : "emp_ui_sv_more".Loc(advanced);
            g.Header.mainText.text = (query.Length == 0 && Collapsed.Contains(g.Id) ? "> " : "v ") + g.Title;
        }

        _noMatch?.gameObject.SetActive(!any);
        this.RebuildLayout(true);
    }

    /// <summary>
    ///     From the list of differences: opens the group and the folded rows, clears the search, scrolls to the row
    /// </summary>
    private void Jump(string rowId)
    {
        var group = _groups.FirstOrDefault(g => g.Lines.Any(l => l.Row.Id == rowId));
        if (group is null) {
            return;
        }

        var line = group.Lines.First(l => l.Row.Id == rowId);
        _search = "";
        if (_searchInput != null) {
            _searchInput.Text = "";
        }
        Collapsed.Remove(group.Id);
        if (line.Row.Advanced) {
            Expanded.Add(group.Id);
        }

        ApplyFilter();
        if (GetComponentInParent<ScrollRect>() is not { content: { } content } scroll) {
            return;
        }

        Canvas.ForceUpdateCanvases();
        var target = line.Go.GetComponent<RectTransform>();
        var offset = (Vector2)scroll.transform.InverseTransformPoint(content.position) - (Vector2)scroll.transform.InverseTransformPoint(target.position);
        content.anchoredPosition = new(content.anchoredPosition.x, Mathf.Clamp(offset.y - UIHelper.RowHeight, 0f, MaxScroll(scroll)));
    }

    private static float MaxScroll(ScrollRect scroll) => Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);

    private static GameObject MakeDot(YKLayout parent)
    {
        var go = new GameObject("changed", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent.transform, false);
        var image = go.GetComponent<Image>();
        // (the engine's round UI sprite; a plain square where it is not loaded)
        image.sprite = UIHelper.FindSprite("Knob");
        image.color = new(0.8f, 0.36f, 0.1f);
        image.raycastTarget = false;
        go.GetComponent<RectTransform>().sizeDelta = new(12f, 12f);
        var element = go.GetComponent<LayoutElement>();
        (element.preferredWidth, element.preferredHeight, element.flexibleWidth) = (12f, 12f, 0f);
        return go;
    }

    /// <summary>
    ///     A change draws the layer again (SettingsTable.Update): keep where the page was scrolled to
    /// </summary>
    private static void Capture()
    {
        if (_instance != null && _instance.GetComponentInParent<ScrollRect>() is { content: { } content }) {
            (_savedY, _restore) = (content.anchoredPosition.y, 6);
        }
    }

    /// <summary>
    ///     Every frame (SettingsTable.Update): puts the page back where Capture saw it once the layer is drawn again
    /// </summary>
    internal static void Tick()
    {
        if (_restore == 0 || _instance == null || !_instance.isActiveAndEnabled ||
            _instance.GetComponentInParent<ScrollRect>() is not { content: { } content } scroll) {
            return;
        }

        if (--_restore == 0) {
            content.anchoredPosition = new(content.anchoredPosition.x, Mathf.Clamp(_savedY, 0f, MaxScroll(scroll)));
        }
    }

    /// <summary>
    ///     What the preset means, or for Custom how many rows differ from the recommended rules and which (shared
    ///     with ModConfigPage)
    /// </summary>
    internal static string PresetText(int preset)
    {
        if (preset != 2) {
            return $"emp_ui_sv_desc_preset_{SettingsTable.Presets[preset]}".lang();
        }

        var rows = SettingsTable.Rows().ToDictionary(r => r.Id);
        var diff = SettingsTable.Differences(RuleSource.Current, RuleSource.Defaults);
        return "emp_ui_sv_diff".Loc(diff.Count, string.Join(", ", diff.Select(id => rows[id].Label)));
    }

    private static void BuildValidationSets(YKVertical sets)
    {
        sets.TextSmall("emp_ui_sv_desc_validation".lang());

        var list = sets.Grid()
            .WithConstraintCount(4);
        list.Fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        list.Layout.cellSize = FitCell(4);

        var options = new List<string> {
            "none",
            "sources",
            "plugins",
            "all",
        };

        foreach (var option in options) {
            list.Toggle($"emp_validation_{option}", EmpConfig.Server.SourceValidationSet.Value.Contains(option),
                value => SettingsTable.Write([(EmpConfig.Server.SourceValidationSet, SettingsTable.ToggleCheck(EmpConfig.Server.SourceValidationSet.Value, option, value))]));
        }
    }
}
