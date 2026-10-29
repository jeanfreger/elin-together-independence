using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The leader's emblem creator in the Team page's banner card (PLAN_team_emblem): a 3x preview, the layers
///     (select, up, down, remove, add from the Items/Monsters picker), the selected layer's position, size, turn, flips
///     and tint, the background colour and shape. The draft stays in this game until Save asks the host
/// </summary>
internal static class EmblemEditor
{
    private const int UndoMax = 20;
    private const int Step = 2;
    private const float PreviewSize = TeamEmblem.Grid * 3f;
    // (twelve swatches and their gaps in a page's width)
    private const float SwatchSize = 20f;

    private static int _team = -1;
    private static Emblem _draft = new(0, 0, []);
    private static int _selected;
    private static bool _picking;
    private static readonly List<Emblem> _undo = [];

    // the slider drag being one undo step, not one per value
    private static bool _sliding;

    private static YKLayout? _left;
    private static YKLayout? _right;
    private static bool _leads;
    private static Image? _preview;

    /// <summary>
    ///     The Emblem page of the Team tab: the big preview, the background, the shape and the buttons on the left page;
    ///     the layers and the selected layer's tools (or the picker while it is open) on the right page. Anyone but the
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
        _sliding = false;
        var left = _left;
        var right = _right;
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _draft.Layers.Count - 1));

        // the left page: the preview, centred, with air above it (it touched the page's top edge)
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
            foreach (var layer in _draft.Layers) {
                right.Text(NameOf(layer.Thing));
            }

            right.TextSmall("emp_ui_team_tools_leader_only".lang(), FontColor.Gray);
            return;
        }

        // the background
        left.TextSmall("emp_ui_emblem_background".lang(), FontColor.Gray);
        var colours = Swatches(left);
        for (var c = 0; c < TeamBanner.ColorCount; c++) {
            var index = c;
            TeamBanner.Swatch(colours, TeamBanner.ColorOf(c), SwatchSize, SwatchSize, c == _draft.Background, () => {
                Change(_draft with { Background = index });
                Fill();
            });
        }

        TeamBanner.Pills(left, ["emp_ui_emblem_square".lang(), "emp_ui_emblem_round".lang(), "emp_ui_emblem_shield".lang()],
            _draft.Shape, i => {
                if (i != _draft.Shape) {
                    Change(_draft with { Shape = i });
                    Refresh();
                }
            });

        // (three buttons in the page's width: each a third of it, widened only by a longer label)
        var bar = left.ButtonRow();
        bar.Layout.childAlignment = TextAnchor.MiddleCenter;
        UIButton Third(UIButton button)
        {
            button.LayoutElement().preferredWidth = (EmpUi.PageInner - 20f) / 3f;
            return button;
        }

        Third(bar.RowButton("emp_ui_emblem_undo".lang(), () => {
            if (_undo.Count == 0) {
                return;
            }

            _draft = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Fill();
        })).interactable = _undo.Count > 0;
        Third(bar.RowButton("emp_ui_emblem_reset".lang(), () => {
            Change(Saved(_team));
            Fill();
        }));
        Third(bar.RowButton("emp_ui_emblem_save".lang(), () => {
            Teams.Ask(TeamAsk.Banner, 0, _draft.Background, TeamEmblem.Format(_draft));
        })).interactable = _draft.Layers.Count > 0;

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
            // the count and Add on one line, the layers under it (scrolled past four)
            var head = right.Row();
            head.TextSmall("emp_ui_emblem_layers".Loc(_draft.Layers.Count, TeamEmblem.MaxLayers), FontColor.Gray)
                .LayoutElement().flexibleWidth = 1f;
            var add = head.RowButton("emp_ui_emblem_add".lang(), () => {
                _picking = true;
                Fill();
            });
            add.interactable = _draft.Layers.Count < TeamEmblem.MaxLayers;
            var layers = right.ListArea(_draft.Layers.Count, 4, UIHelper.RowHeight);
            for (var i = 0; i < _draft.Layers.Count; i++) {
                LayerRow(layers, i);
            }

            if (_draft.Layers.Count > 0) {
                LayerTools(right);
            }
        }

        if (left.transform.parent is RectTransform spread) {
            LayoutRebuilder.MarkLayoutForRebuild(spread);
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

    /// <summary>A line of small swatches, packed close so twelve fit in a page</summary>
    private static YKHorizontal Swatches(YKLayout parent)
    {
        var row = parent.Row();
        row.Layout.spacing = 4f;
        return row;
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

    /// <summary>The selected layer: nudges, size, quarter turn, flips, tint</summary>
    private static void LayerTools(YKLayout root)
    {
        var layer = _draft.Layers[_selected];
        root.TextSmall("emp_ui_emblem_selected".Loc(NameOf(layer.Thing)), FontColor.Gray);

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
                     ("emp_ui_emblem_rotate", l => l with { Rotation = (l.Rotation + 1) % 4 }),
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

        var size = root.Row();
        size.Text("emp_ui_emblem_size".lang());
        Slider? slider = null;
        size.SignButton("-", () => {
            Edit(l => l with { Size = Mathf.Clamp(l.Size - 10, TeamEmblem.MinSize, TeamEmblem.MaxSize) });
            slider?.SetValueWithoutNotify(_draft.Layers[_selected].Size);
        });
        slider = size.Slider(layer.Size, v => {
            var value = Mathf.RoundToInt(v);
            if (value == _draft.Layers[_selected].Size) {
                return;
            }

            // (one undo step for the whole drag)
            Edit(l => l with { Size = value }, true);
        }, TeamEmblem.MinSize, TeamEmblem.MaxSize, v => Mathf.RoundToInt(v) + " %");
        slider.LayoutElement().flexibleWidth = 1f;
        size.SignButton("+", () => {
            Edit(l => l with { Size = Mathf.Clamp(l.Size + 10, TeamEmblem.MinSize, TeamEmblem.MaxSize) });
            slider.SetValueWithoutNotify(_draft.Layers[_selected].Size);
        });

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
        var tints = Swatches(root);
        for (var c = 0; c < TeamBanner.ColorCount; c++) {
            var index = c;
            TeamBanner.Swatch(tints, TeamBanner.ColorOf(c), SwatchSize, SwatchSize, layer.Tint == c, () => {
                Edit(l => l with { Tint = index });
                Fill();
            });
        }
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

    /// <summary>The selected layer changed: the draft and the preview only (the buttons stay as they are)</summary>
    private static void Edit(System.Func<EmblemLayer, EmblemLayer> change, bool fromSlider = false)
    {
        var undoStep = !(fromSlider && _sliding);
        _sliding = fromSlider;
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
