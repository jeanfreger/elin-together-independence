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

    private static int _team = -1;
    private static Emblem _draft = new(0, 0, []);
    private static int _selected;
    private static bool _picking;
    private static readonly List<Emblem> _undo = [];

    // the slider drag being one undo step, not one per value
    private static bool _sliding;

    private static YKLayout? _root;
    private static Image? _preview;

    internal static void Build(YKLayout card, int teamId)
    {
        if (_team != teamId) {
            _team = teamId;
            _draft = Saved(teamId);
            _selected = 0;
            _undo.Clear();
            _picking = false;
        }

        var root = card.Vertical();
        root.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        root.Layout.spacing = 6f;
        _root = root;
        Fill();
    }

    private static Emblem Saved(int teamId) =>
        TeamBanner.EmblemOf(teamId) ?? new(TeamBanner.Of(teamId)?.Color ?? 0, 0, []);

    private static void Fill()
    {
        if (_root == null) {
            return;
        }

        foreach (Transform child in _root.transform) {
            Object.Destroy(child.gameObject);
        }

        _sliding = false;
        var root = _root;
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _draft.Layers.Count - 1));

        // the preview, then the layers beside it
        var top = root.Horizontal();
        top.Layout.spacing = 12f;
        top.Layout.childForceExpandWidth = false;
        _preview = TeamEmblemBaker.Place(top.transform, TeamEmblemBaker.Get(_draft), PreviewSize);
        var layers = top.Vertical();
        layers.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        layers.LayoutElement().flexibleWidth = 1f;
        layers.TextSmall("emp_ui_emblem_layers".Loc(_draft.Layers.Count, TeamEmblem.MaxLayers), FontColor.Gray);
        for (var i = 0; i < _draft.Layers.Count; i++) {
            LayerRow(layers, i);
        }

        var add = layers.ButtonRow().RowButton((_picking ? "emp_ui_emblem_add_close" : "emp_ui_emblem_add").lang(), () => {
            _picking = !_picking;
            Fill();
        });
        add.interactable = _picking || _draft.Layers.Count < TeamEmblem.MaxLayers;

        if (_picking) {
            TeamBanner.BuildThingPicker(root, TeamBanner.ColorOf(_draft.Background), thing => {
                if (_draft.Layers.Count >= TeamEmblem.MaxLayers) {
                    return;
                }

                Change(_draft with { Layers = [.._draft.Layers, TeamEmblem.Layer(thing)] });
                _selected = _draft.Layers.Count - 1;
                _picking = false;
                Fill();
            });
        }

        if (_draft.Layers.Count > 0) {
            LayerTools(root);
        }

        // the background
        root.TextSmall("emp_ui_emblem_background".lang(), FontColor.Gray);
        var colours = root.Row();
        for (var c = 0; c < TeamBanner.ColorCount; c++) {
            var index = c;
            TeamBanner.Swatch(colours, TeamBanner.ColorOf(c), 24f, 24f, c == _draft.Background, () => {
                Change(_draft with { Background = index });
                Fill();
            });
        }

        TeamBanner.Pills(root, ["emp_ui_emblem_square".lang(), "emp_ui_emblem_round".lang(), "emp_ui_emblem_shield".lang()],
            _draft.Shape, i => {
                if (i != _draft.Shape) {
                    Change(_draft with { Shape = i });
                    Refresh();
                }
            });

        var bar = root.ButtonRow();
        bar.RowButton("emp_ui_emblem_undo".lang(), () => {
            if (_undo.Count == 0) {
                return;
            }

            _draft = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Fill();
        }).interactable = _undo.Count > 0;
        bar.RowButton("emp_ui_emblem_reset".lang(), () => {
            Change(Saved(_team));
            Fill();
        });
        bar.RowButton("emp_ui_emblem_save".lang(), () => {
            Teams.Ask(TeamAsk.Banner, 0, _draft.Background, TeamEmblem.Format(_draft));
        }).interactable = _draft.Layers.Count > 0;
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
        Small(row, "^", () => Move(index, -1)).interactable = index > 0;
        Small(row, "v", () => Move(index, 1)).interactable = index < _draft.Layers.Count - 1;
        Small(row, "x", () => {
            Change(_draft with { Layers = _draft.Layers.Where((_, i) => i != index).ToList() });
            if (_selected >= index && _selected > 0) {
                _selected--;
            }
            Fill();
        });
    }

    /// <summary>The selected layer: nudges, size, quarter turn, flips, tint</summary>
    private static void LayerTools(YKLayout root)
    {
        var layer = _draft.Layers[_selected];
        root.TextSmall("emp_ui_emblem_selected".Loc(NameOf(layer.Thing)), FontColor.Gray);

        var move = root.ButtonRow();
        move.Text("emp_ui_emblem_move".lang());
        Small(move, "<", () => Edit(l => l with { X = Mathf.Clamp(l.X - Step, 0, TeamEmblem.Grid) }));
        Small(move, ">", () => Edit(l => l with { X = Mathf.Clamp(l.X + Step, 0, TeamEmblem.Grid) }));
        Small(move, "^", () => Edit(l => l with { Y = Mathf.Clamp(l.Y - Step, 0, TeamEmblem.Grid) }));
        Small(move, "v", () => Edit(l => l with { Y = Mathf.Clamp(l.Y + Step, 0, TeamEmblem.Grid) }));

        var size = root.ButtonRow();
        size.Text("emp_ui_emblem_size".lang());
        Slider? slider = null;
        Small(size, "-", () => {
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
        Small(size, "+", () => {
            Edit(l => l with { Size = Mathf.Clamp(l.Size + 10, TeamEmblem.MinSize, TeamEmblem.MaxSize) });
            slider.SetValueWithoutNotify(_draft.Layers[_selected].Size);
        });

        var turn = root.ButtonRow();
        turn.RowButton("emp_ui_emblem_rotate".lang(), () => Edit(l => l with { Rotation = (l.Rotation + 1) % 4 }));
        turn.RowButton("emp_ui_emblem_flip_h".lang(), () => Edit(l => l with { Flip = l.Flip ^ 1 }));
        turn.RowButton("emp_ui_emblem_flip_v".lang(), () => Edit(l => l with { Flip = l.Flip ^ 2 }));

        root.TextSmall("emp_ui_emblem_tint".lang(), FontColor.Gray);
        var tints = root.Row();
        for (var c = 0; c < TeamBanner.ColorCount; c++) {
            var index = c;
            TeamBanner.Swatch(tints, TeamBanner.ColorOf(c), 24f, 24f, layer.Tint == c, () => {
                Edit(l => l with { Tint = index });
                Fill();
            });
        }

        Small(tints, "emp_ui_emblem_tint_none".lang(), () => {
            Edit(l => l with { Tint = TeamEmblem.OwnColours });
            Fill();
        }, 0f).interactable = layer.Tint != TeamEmblem.OwnColours;
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

    /// <summary>A narrow game button for one sign (or a short word when <paramref name="width" /> is 0)</summary>
    private static UIButton Small(YKLayout row, string label, System.Action act, float width = 44f)
    {
        var button = row.Button(label, act);
        var element = button.LayoutElement();
        element.preferredWidth = width > 0f ? width : (button.mainText != null ? button.mainText.preferredWidth + 32f : 80f);
        element.flexibleWidth = 0f;
        return button;
    }
}
