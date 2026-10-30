using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The leader's emblem creator on the Team tab's Emblem page (PLAN_team_emblem): a 3x preview where a click picks
///     the layer under the pointer and a drag moves it; the background's two colours, its shape and its pattern; undo,
///     reset, save, a random emblem and a few starters. On the right page the layers (select, up, down, remove, add
///     from the Items/Monsters picker) and the selected layer's tools: the move pad, 45 degree turns, flips, size,
///     opacity, outline, duplicate and tint. The draft stays in this game until Save asks the host
/// </summary>
internal static class EmblemEditor
{
    private const int UndoMax = 20;
    private const int Step = 2;
    private const float PreviewSize = TeamEmblem.Grid * 3f;
    // (twelve swatches and their gaps in a page's width: the palette in two lines)
    private const float SwatchSize = 20f;
    private const int PerLine = 12;

    private static int _team = -1;
    private static Emblem _draft = new(0, 0, []);
    private static int _selected;
    private static bool _picking;
    private static readonly List<Emblem> _undo = [];

    // the background colour the swatches set: 0 the first, 1 the pattern's second
    private static int _colourTarget;

    // a drag (a slider's, the preview's) being one undo step, not one per value
    private static string? _dragKey;

    // the preview's pointer: the layer moved and where it was taken from it, and a selection to redraw on release
    private static bool _moving;
    private static Vector2 _grab;
    private static bool _reselect;

    private static YKLayout? _left;
    private static YKLayout? _right;
    private static bool _leads;
    private static Image? _preview;

    /// <summary>
    ///     The Emblem page of the Team tab: the big preview, the background and the buttons on the left page; the
    ///     layers and the selected layer's tools (or the picker while it is open) on the right page. Anyone but the
    ///     leader sees the emblem and its layers only
    /// </summary>
    internal static void Build(YKLayout left, YKLayout right, int teamId, bool leads)
    {
        if (_team != teamId) {
            _team = teamId;
            _draft = Saved(teamId);
            _selected = 0;
            _undo.Clear();
            _picking = false;
        }

        _left = left;
        _right = right;
        _leads = leads;
        if (!leads) {
            _draft = Saved(teamId);
        }

        Fill();
    }

    private static Emblem Saved(int teamId) =>
        TeamBanner.EmblemOf(teamId) ?? new(TeamBanner.Of(teamId)?.Color ?? 0, 0, []);

    private static void Clear(YKLayout page)
    {
        foreach (Transform child in page.transform) {
            Object.Destroy(child.gameObject);
        }
    }

    private static void Fill()
    {
        if (_left == null || _right == null) {
            return;
        }

        Clear(_left);
        Clear(_right);
        _dragKey = null;
        _moving = false;
        var left = _left;
        var right = _right;
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _draft.Layers.Count - 1));

        // the left page: the preview, centred, with air above it
        var view = left.Horizontal();
        view.Layout.childAlignment = TextAnchor.MiddleCenter;
        view.Layout.childForceExpandWidth = false;
        view.Layout.childControlHeight = false;
        view.Layout.padding = new(0, 0, 10, 6);
        view.LayoutElement().minHeight = view.LayoutElement().preferredHeight = PreviewSize + 16f;
        view.LayoutElement().flexibleWidth = 1f;
        _preview = TeamEmblemBaker.Place(view.transform, TeamEmblemBaker.Get(_draft), PreviewSize);

        if (!_leads) {
            right.TextSmall("emp_ui_emblem_layers".Loc(_draft.Layers.Count, TeamEmblem.MaxLayers), FontColor.Gray);
            var names = right.ListArea(_draft.Layers.Count, 10, 26f);
            foreach (var layer in _draft.Layers) {
                names.Text(NameOf(layer.Thing));
            }

            right.TextSmall("emp_ui_team_tools_leader_only".lang(), FontColor.Gray);
            return;
        }

        // a click on the preview picks the layer under the pointer, a drag moves it
        _preview.raycastTarget = true;
        var input = _preview.gameObject.AddComponent<EmblemPreviewInput>();
        input.Down = PointerDown;
        input.Drag = PointerDrag;
        input.Up = PointerUp;

        BuildBackground(left);

        // (three buttons in the page's width: each a third of it, widened only by a longer label)
        var bar = left.ButtonRow();
        bar.Layout.childAlignment = TextAnchor.MiddleCenter;
        Part(bar.RowButton("emp_ui_emblem_undo".lang(), () => {
            if (_undo.Count == 0) {
                return;
            }

            _draft = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Fill();
        }), 3).interactable = _undo.Count > 0;
        Part(bar.RowButton("emp_ui_emblem_reset".lang(), () => {
            Change(Saved(_team));
            Fill();
        }), 3);
        Part(bar.RowButton("emp_ui_emblem_save".lang(), () => {
            // (the team's colour: one of the twelve team colours; the host keeps the emblem's own background)
            var colour = _draft.Background < TeamBanner.ColorCount ? _draft.Background : TeamBanner.Of(_team)?.Color ?? 0;
            Teams.Ask(TeamAsk.Banner, 0, colour, TeamEmblem.Format(_draft));
        }), 3).interactable = _draft.Layers.Count > 0;

        var more = left.ButtonRow();
        more.Layout.childAlignment = TextAnchor.MiddleCenter;
        Part(more.RowButton("emp_ui_emblem_random".lang(), () => {
            Change(RandomEmblem());
            _selected = 0;
            Fill();
        }), 2);
        UIButton? starters = null;
        starters = Part(more.RowButton("emp_ui_emblem_templates".lang(), () => ShowStarters(starters)), 2);

        // the right page: the layers, then the picker or the selected layer's tools
        if (_picking) {
            right.ButtonRow().RowButton("emp_ui_emblem_add_close".lang(), () => {
                _picking = false;
                Fill();
            });
            TeamBanner.BuildThingPicker(right, TeamBanner.ColorOf(_draft.Background), thing => {
                if (_draft.Layers.Count >= TeamEmblem.MaxLayers) {
                    return;
                }

                Change(_draft with { Layers = [.._draft.Layers, TeamEmblem.Layer(thing)] });
                _selected = _draft.Layers.Count - 1;
                _picking = false;
                Fill();
            });
        } else {
            // the count and Add on one line, the layers under it (scrolled past three)
            var head = right.Row();
            head.TextSmall("emp_ui_emblem_layers".Loc(_draft.Layers.Count, TeamEmblem.MaxLayers), FontColor.Gray)
                .LayoutElement().flexibleWidth = 1f;
            var add = head.RowButton("emp_ui_emblem_add".lang(), () => {
                _picking = true;
                Fill();
            });
            add.interactable = _draft.Layers.Count < TeamEmblem.MaxLayers;
            var layers = right.ListArea(_draft.Layers.Count, 3, UIHelper.RowHeight);
            for (var i = 0; i < _draft.Layers.Count; i++) {
                LayerRow(layers, i);
            }

            if (_draft.Layers.Count > 0) {
                LayerTools(right);
            } else {
                // (an empty list said nothing: how to start, user 2026-10-30 "on peut faire beaucoup mieux")
                right.EmptyText("emp_ui_emblem_start".lang());
            }
        }

        if (left.transform.parent is RectTransform spread) {
            LayoutRebuilder.MarkLayoutForRebuild(spread);
        }
    }

    /// <summary>A button a share of the page's width (widened only by a longer label)</summary>
    private static UIButton Part(UIButton button, int of)
    {
        button.LayoutElement().preferredWidth = (EmpUi.PageInner - 10f * (of - 1)) / of;
        return button;
    }

    /// <summary>
    ///     The background: which of its two colours the swatches set, the palette in two lines, then the shape and the
    ///     pattern as the game's dropdowns side by side
    /// </summary>
    private static void BuildBackground(YKLayout left)
    {
        TeamBanner.Pills(left, ["emp_ui_emblem_colour1".lang(), "emp_ui_emblem_colour2".lang()], _colourTarget, i => {
            _colourTarget = i;
            Fill();
        });

        var current = _colourTarget == 0 ? _draft.Background : _draft.Background2;
        Palette(left, current, index => {
            Change(_colourTarget == 0 ? _draft with { Background = index } : _draft with { Background2 = index });
            Fill();
        });

        var choices = left.Horizontal();
        choices.Layout.childForceExpandWidth = true;
        choices.Layout.childControlWidth = true;
        choices.Layout.spacing = 10f;
        Choice(choices, "emp_ui_emblem_shape",
            ["emp_ui_emblem_square", "emp_ui_emblem_round", "emp_ui_emblem_shield", "emp_ui_emblem_banner", "emp_ui_emblem_diamond", "emp_ui_emblem_hexagon"],
            _draft.Shape, i => _draft with { Shape = i });
        Choice(choices, "emp_ui_emblem_pattern",
            Enumerable.Range(0, TeamEmblem.PatternCount).Select(i => $"emp_ui_emblem_pattern_{i}").ToArray(),
            _draft.Pattern, i => _draft with { Pattern = i });
    }

    /// <summary>A title in grey over one of the game's dropdowns, half the page wide</summary>
    private static void Choice(YKLayout row, string titleId, string[] optionIds, int value, System.Func<int, Emblem> change)
    {
        var col = row.Vertical();
        col.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        col.Layout.spacing = 0f;
        col.LayoutElement().flexibleWidth = 1f;
        col.LayoutElement().preferredWidth = 0f;
        col.TextSmall(titleId.lang(), FontColor.Gray);
        var dropdown = col.Dropdown(optionIds.Select(id => id.lang()).ToList(), i => {
            Change(change(i));
            Refresh();
        }, value);
        dropdown.LayoutElement().flexibleWidth = 1f;
        dropdown.LayoutElement().preferredHeight = UIHelper.RowHeight;
        dropdown.Rect().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, UIHelper.RowHeight);
    }

    /// <summary>The 24 colours as small swatches, twelve to a line, the current one framed</summary>
    private static void Palette(YKLayout parent, int current, System.Action<int> pick)
    {
        YKHorizontal? line = null;
        for (var c = 0; c < TeamBanner.EmblemColorCount; c++) {
            if (c % PerLine == 0) {
                line = parent.Row();
                line.Layout.spacing = 4f;
                line.LayoutElement().minHeight = SwatchSize + 4f;
            }

            var index = c;
            TeamBanner.Swatch(line!, TeamBanner.ColorOf(c), SwatchSize, SwatchSize, c == current, () => pick(index));
        }
    }

    /// <summary>A layer: its thumbnail and name (a click selects it), then up, down and remove</summary>
    private static void LayerRow(YKLayout parent, int index)
    {
        var layer = _draft.Layers[index];
        var row = parent.Row();
        TeamBanner.Frame(row, TeamBanner.ColorOf(_draft.Background), TeamBanner.SpriteOf(layer.Thing), 24f,
            index == _selected, () => {
                _selected = index;
                Fill();
            });
        var name = NameOf(layer.Thing);
        row.Text(index == _selected ? "> " + name : name).LayoutElement().flexibleWidth = 1f;
        // the game's own small list buttons (its playlist's up and down, the inventory's trash)
        var icons = EmpUi.GameIcons;
        row.IconButton(icons?.up, "^", () => Move(index, -1), "emp_ui_emblem_up", index > 0, size: 26f);
        row.IconButton(icons?.down, "v", () => Move(index, 1), "emp_ui_emblem_down", index < _draft.Layers.Count - 1, size: 26f);
        row.IconButton(icons?.trash, "x", () => {
            Change(_draft with { Layers = _draft.Layers.Where((_, i) => i != index).ToList() });
            if (_selected >= index && _selected > 0) {
                _selected--;
            }
            Fill();
        }, "emp_ui_emblem_remove", size: 26f);
    }

    /// <summary>The move pad: the four nudges as a 3x3 cross of small arrows</summary>
    private static void MovePad(YKLayout parent)
    {
        var icons = EmpUi.GameIcons;
        var pad = parent.Vertical();
        pad.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        pad.Layout.spacing = 2f;
        pad.Layout.childForceExpandWidth = false;
        pad.LayoutElement().flexibleWidth = 0f;
        // (the game's up arrow turned: 90 points left, -90 right)
        var cells = new (string Sign, float Angle, int Dx, int Dy)?[] {
            null, ("^", 0f, 0, -1), null,
            ("<", 90f, -1, 0), null, (">", -90f, 1, 0),
            null, ("v", 180f, 0, 1), null,
        };
        YKHorizontal? line = null;
        for (var i = 0; i < cells.Length; i++) {
            if (i % 3 == 0) {
                line = pad.Horizontal();
                line.Layout.spacing = 2f;
                line.Layout.childForceExpandWidth = false;
                line.Layout.childControlWidth = line.Layout.childControlHeight = true;
                line.LayoutElement().minHeight = line.LayoutElement().preferredHeight = 28f;
            }

            if (cells[i] is not { } cell) {
                var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(line!.transform, false);
                var le = gap.GetComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = 28f;
                continue;
            }

            var (dx, dy) = (cell.Dx, cell.Dy);
            line!.IconButton(icons?.up, cell.Sign, () => Edit(l => l with {
                X = Mathf.Clamp(l.X + dx * Step, 0, TeamEmblem.Grid),
                Y = Mathf.Clamp(l.Y + dy * Step, 0, TeamEmblem.Grid),
            }), null, true, cell.Angle, 28f);
        }
    }

    /// <summary>The selected layer: nudges, turn, flips, size, opacity, outline, duplicate, tint</summary>
    private static void LayerTools(YKLayout root)
    {
        var layer = _draft.Layers[_selected];

        // the move pad beside the turn and the flips (one block instead of three rows of wide buttons)
        var block = root.Horizontal();
        block.Layout.childForceExpandWidth = false;
        block.Layout.childAlignment = TextAnchor.MiddleLeft;
        block.Layout.spacing = 12f;
        MovePad(block);
        var turn = block.Vertical();
        turn.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        turn.Layout.spacing = 2f;
        turn.Layout.childForceExpandWidth = false;
        foreach (var (label, change) in new (string, System.Func<EmblemLayer, EmblemLayer>)[] {
                     ("emp_ui_emblem_rotate", l => l with { Rotation = (l.Rotation + 1) % 8 }),
                     ("emp_ui_emblem_flip_h", l => l with { Flip = l.Flip ^ 1 }),
                     ("emp_ui_emblem_flip_v", l => l with { Flip = l.Flip ^ 2 }),
                 }) {
            var edit = change;
            var button = turn.Button(label.lang(), () => Edit(edit));
            var element = button.LayoutElement();
            element.minHeight = element.preferredHeight = 28f;
            element.preferredWidth = UIHelper.ButtonWidth;
            element.flexibleWidth = 0f;
        }

        Stepper(root, "emp_ui_emblem_size", layer.Size, TeamEmblem.MinSize, TeamEmblem.MaxSize, 10,
            (l, v) => l with { Size = v }, l => l.Size);
        Stepper(root, "emp_ui_emblem_opacity", layer.Opacity, TeamEmblem.MinOpacity, 100, 10,
            (l, v) => l with { Opacity = v }, l => l.Opacity);

        // the outline and duplicate on one line
        var line = root.Row();
        var outline = line.Toggle("emp_ui_emblem_outline".lang(), layer.Outline != 0, on => Edit(l => l with { Outline = on ? 1 : 0 }));
        outline.LayoutElement().flexibleWidth = 1f;
        outline.AsRow();
        line.RowButton("emp_ui_emblem_duplicate".lang(), Duplicate).interactable = _draft.Layers.Count < TeamEmblem.MaxLayers;

        // the tint: "None" on the title's line, the swatches under it
        var tintHead = root.Row();
        tintHead.TextSmall("emp_ui_emblem_tint".lang(), FontColor.Gray).LayoutElement().flexibleWidth = 1f;
        var none = tintHead.Button("emp_ui_emblem_tint_none".lang(), () => {
            Edit(l => l with { Tint = TeamEmblem.OwnColours });
            Fill();
        });
        none.LayoutElement().preferredWidth = 90f;
        none.LayoutElement().flexibleWidth = 0f;
        none.interactable = layer.Tint != TeamEmblem.OwnColours;
        Palette(root, layer.Tint, index => {
            Edit(l => l with { Tint = index });
            Fill();
        });
    }

    /// <summary>A number of the selected layer: its title, "-", the game's slider, "+"</summary>
    private static void Stepper(YKLayout root, string titleId, int value, int min, int max, int step,
        System.Func<EmblemLayer, int, EmblemLayer> set, System.Func<EmblemLayer, int> get)
    {
        var row = root.Row();
        row.Text(titleId.lang());
        Slider? slider = null;
        row.SignButton("-", () => {
            Edit(l => set(l, Mathf.Clamp(get(l) - step, min, max)));
            slider?.SetValueWithoutNotify(get(_draft.Layers[_selected]));
        });
        slider = row.Slider(value, v => {
            var rounded = Mathf.RoundToInt(v);
            if (rounded == get(_draft.Layers[_selected])) {
                return;
            }

            // (one undo step for the whole drag)
            Edit(l => set(l, rounded), titleId);
        }, min, max, v => Mathf.RoundToInt(v) + " %");
        slider.LayoutElement().flexibleWidth = 1f;
        row.SignButton("+", () => {
            Edit(l => set(l, Mathf.Clamp(get(l) + step, min, max)));
            slider.SetValueWithoutNotify(get(_draft.Layers[_selected]));
        });
    }

    private static void Duplicate()
    {
        if (_draft.Layers.Count >= TeamEmblem.MaxLayers || _selected >= _draft.Layers.Count) {
            return;
        }

        // the copy just above the original, a little aside so it shows
        var copy = _draft.Layers[_selected];
        copy = copy with { X = Mathf.Clamp(copy.X + 4, 0, TeamEmblem.Grid), Y = Mathf.Clamp(copy.Y + 4, 0, TeamEmblem.Grid) };
        var list = _draft.Layers.ToList();
        list.Insert(_selected + 1, copy);
        Change(_draft with { Layers = list });
        _selected++;
        Fill();
    }

    private static void Move(int index, int by)
    {
        var to = index + by;
        if (to < 0 || to >= _draft.Layers.Count) {
            return;
        }

        var list = _draft.Layers.ToList();
        (list[index], list[to]) = (list[to], list[index]);
        Change(_draft with { Layers = list });
        if (_selected == index) {
            _selected = to;
        } else if (_selected == to) {
            _selected = index;
        }

        Fill();
    }

    // ---------------------------------------------------------------- the preview's pointer

    /// <summary>The topmost layer whose square holds this point of the grid, or -1</summary>
    private static int LayerAt(Vector2 p)
    {
        for (var i = _draft.Layers.Count - 1; i >= 0; i--) {
            var l = _draft.Layers[i];
            var half = TeamEmblem.Grid * l.Size / 200f;
            if (Mathf.Abs(p.x - l.X) <= half && Mathf.Abs(p.y - l.Y) <= half) {
                return i;
            }
        }

        return -1;
    }

    private static void PointerDown(Vector2 p)
    {
        // (the selected layer keeps the pointer while it is under it, even under another one)
        var selected = _selected < _draft.Layers.Count ? _draft.Layers[_selected] : default;
        var half = TeamEmblem.Grid * selected.Size / 200f;
        var keep = _selected < _draft.Layers.Count && Mathf.Abs(p.x - selected.X) <= half && Mathf.Abs(p.y - selected.Y) <= half;
        var hit = keep ? _selected : LayerAt(p);
        _moving = hit >= 0;
        if (!_moving) {
            return;
        }

        if (hit != _selected) {
            _selected = hit;
            _reselect = true;
        }

        var layer = _draft.Layers[hit];
        _grab = p - new Vector2(layer.X, layer.Y);
    }

    private static void PointerDrag(Vector2 p)
    {
        if (!_moving) {
            return;
        }

        var x = Mathf.Clamp(Mathf.RoundToInt(p.x - _grab.x), 0, TeamEmblem.Grid);
        var y = Mathf.Clamp(Mathf.RoundToInt(p.y - _grab.y), 0, TeamEmblem.Grid);
        if (_draft.Layers[_selected] is var l && l.X == x && l.Y == y) {
            return;
        }

        Edit(layer => layer with { X = x, Y = y }, "preview");
    }

    private static void PointerUp()
    {
        _moving = false;
        _dragKey = null;
        if (_reselect) {
            _reselect = false;
            // (the layer list and the tools show the layer picked on the preview)
            Fill();
        }
    }

    // ---------------------------------------------------------------- random and starters

    /// <summary>
    ///     A random emblem: a shape, a pattern of two colours and one or two things of the game, some turned, tinted or
    ///     outlined
    /// </summary>
    private static Emblem RandomEmblem()
    {
        var rng = new System.Random();
        var bg = rng.Next(TeamBanner.EmblemColorCount);
        var bg2 = (bg + 1 + rng.Next(TeamBanner.EmblemColorCount - 1)) % TeamBanner.EmblemColorCount;
        var layers = new List<EmblemLayer>();
        var rows = EClass.sources.things.rows;
        for (var n = 1 + rng.Next(2); layers.Count < n;) {
            string? thing = null;
            for (var tries = 0; tries < 30 && thing == null; tries++) {
                var row = rows[rng.Next(rows.Count)];
                if (!string.IsNullOrEmpty(row.id) && TeamBanner.Normalize("t:" + row.id) is { } typed && TeamBanner.SpriteOf(typed) != null) {
                    thing = typed;
                }
            }

            if (thing == null) {
                break;
            }

            var first = layers.Count == 0;
            layers.Add(new(thing, first ? 32 : 20 + rng.Next(24), first ? 32 : 20 + rng.Next(24), first ? 70 + rng.Next(40) : 40 + rng.Next(30),
                rng.Next(3) == 0 ? rng.Next(8) : 0, rng.Next(4) == 0 ? 1 : 0,
                rng.Next(2) == 0 ? TeamEmblem.OwnColours : rng.Next(TeamBanner.EmblemColorCount), 100, rng.Next(2)));
        }

        return new(bg, rng.Next(TeamEmblem.ShapeCount), layers, rng.Next(TeamEmblem.PatternCount), bg2);
    }

    /// <summary>A few ready emblems of the game's own things and monsters (one the game does not have is left out)</summary>
    private static readonly Emblem[] Starters = [
        new(19, 1, [new("c:putty", 32, 34, 90, 0, 0, TeamEmblem.OwnColours, 100, 1)], 0, 19),
        new(13, 2, [new("t:mathammer", 32, 30, 110, 1, 0, 18, 100, 1)], 1, 16),
        new(12, 3, [new("t:torch", 32, 28, 100, 0, 0, TeamEmblem.OwnColours, 100, 1)], 5, 0),
        new(7, 4, [new("t:crystal_mana", 32, 32, 80, 0, 0, TeamEmblem.OwnColours, 100, 0)], 7, 18),
        new(14, 5, [new("c:hound", 32, 34, 90, 0, 0, TeamEmblem.OwnColours, 100, 1)], 6, 6),
        new(16, 0, [new("t:crystal_sun", 22, 22, 60, 0, 0, 18, 100, 0), new("t:crystal_earth", 42, 42, 60, 0, 0, 2, 100, 0)], 3, 17),
    ];

    /// <summary>The starters in the game's context menu under the button, by the name of their main thing</summary>
    private static void ShowStarters(UIButton? anchor)
    {
        var menu = EClass.ui.CreateContextMenuInteraction();
        if (anchor != null) {
            menu.SetHighlightTarget(anchor);
        }

        foreach (var starter in Starters) {
            var layers = starter.Layers.Where(l => TeamBanner.Normalize(l.Thing) != null).ToList();
            if (layers.Count == 0) {
                continue;
            }

            var pick = starter with { Layers = layers };
            menu.AddButton(NameOf(layers[0].Thing), () => {
                Change(pick);
                _selected = 0;
                Fill();
            });
        }

        menu.Show();
    }

    // ---------------------------------------------------------------- the draft

    /// <summary>
    ///     The selected layer changed: the draft and the preview only (the buttons stay as they are). A
    ///     <paramref name="drag" /> key makes its repeated changes one undo step
    /// </summary>
    private static void Edit(System.Func<EmblemLayer, EmblemLayer> change, string? drag = null)
    {
        var undoStep = drag == null || drag != _dragKey;
        _dragKey = drag;
        if (_selected >= _draft.Layers.Count) {
            return;
        }

        var list = _draft.Layers.ToList();
        list[_selected] = change(list[_selected]);
        Change(_draft with { Layers = list }, undoStep);
        Refresh();
    }

    private static void Change(Emblem next, bool undoStep = true)
    {
        if (undoStep) {
            _undo.Add(_draft);
            if (_undo.Count > UndoMax) {
                _undo.RemoveAt(0);
            }
        }

        _draft = next;
    }

    private static void Refresh()
    {
        if (_preview != null) {
            _preview.sprite = TeamEmblemBaker.Get(_draft);
        }
    }

    private static string NameOf(string thing)
    {
        var id = thing.Length > 2 ? thing.Substring(2) : thing;
        if (thing.StartsWith("c:")) {
            return EClass.sources.charas.map.TryGetValue(id, out var chara) ? chara.GetName() ?? id : id;
        }

        return EClass.sources.things.map.TryGetValue(id, out var row) ? row.GetName() ?? id : id;
    }
}

/// <summary>
///     The emblem preview's pointer, in points of the 64 grid from the top left: pressed, dragged, released
/// </summary>
internal sealed class EmblemPreviewInput : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
{
    internal System.Action<Vector2>? Down;
    internal System.Action<Vector2>? Drag;
    internal System.Action? Up;

    private Vector2 OnGrid(PointerEventData data)
    {
        var rt = (RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, data.position, data.pressEventCamera, out var p);
        var r = rt.rect;
        return new((p.x - r.xMin) / Mathf.Max(1f, r.width) * TeamEmblem.Grid, (r.yMax - p.y) / Mathf.Max(1f, r.height) * TeamEmblem.Grid);
    }

    public void OnPointerDown(PointerEventData data) => Down?.Invoke(OnGrid(data));

    public void OnBeginDrag(PointerEventData data)
    {
    }

    public void OnDrag(PointerEventData data) => Drag?.Invoke(OnGrid(data));

    public void OnPointerUp(PointerEventData data) => Up?.Invoke();
}
