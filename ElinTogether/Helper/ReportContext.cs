using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     What a bug report says by itself about the moment (user 2026-10-30: reports were a title alone, and we never
///     knew what the player was aiming at): the player's last 20 actions, kept in memory only (ReportContextPatch
///     notes them), the open windows, what was under the mouse, the zone and the position
/// </summary>
internal static class ReportContext
{
    private const int Kept = 20;

    private static readonly LinkedList<string> Actions = new();
    private static string _last = "";
    private static int _repeat;

    // what the mouse was on last, sampled twice a second (over the report window the game's own target is empty)
    private static string? _mouse;
    private static float _mouseAt;
    private static string? _hovered;
    private static float _hoveredAt;
    private static float _nextSample;

    // the AI types that are not an action of the player (walking, waiting)
    private static readonly Dictionary<Type, FieldInfo?> TargetFields = [];

    internal static void Note(string what)
    {
        try {
            if (what == _last && Actions.Count > 0) {
                _repeat++;
                Actions.Last.Value = $"{Stamp()} {what} (x{_repeat + 1})";
                return;
            }

            _last = what;
            _repeat = 0;
            Actions.AddLast($"{Stamp()} {what}");
            while (Actions.Count > Kept) {
                Actions.RemoveFirst();
            }
        } catch (Exception) {
            // (a note is never worth an exception in the game's own code)
        }
    }

    internal static void NoteAi(AIAct ai)
    {
        var type = ai.GetType();
        if (type.Name.StartsWith("Goal", StringComparison.Ordinal) || type.Name is "NoGoal" or "AI_Idle" or "AI_Goto" or "AI_Wait") {
            return;
        }

        if (!TargetFields.TryGetValue(type, out var field)) {
            field = type.GetField("target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { } f &&
                    typeof(Card).IsAssignableFrom(f.FieldType)
                ? f
                : null;
            TargetFields[type] = field;
        }

        var target = field?.GetValue(ai) as Card;
        Note(target is null ? $"started {type.Name}" : $"started {type.Name} on {Describe(target)}");
    }

    internal static string Describe(Card card)
    {
        return $"{card.Name} ({card.id} #{card.uid})";
    }

    // the real time (UTC, as in the mod's log) and the game's
    private static string Stamp()
    {
        var date = EClass.core?.IsGameStarted == true ? EClass.world?.date : null;
        return date is null
            ? $"{DateTime.UtcNow:HH:mm:ss}"
            : $"{DateTime.UtcNow:HH:mm:ss} (day {date.day} {date.hour:00}:{date.min:00})";
    }

    /// <summary>Twice a second: what the mouse is on in the world and in the inventories</summary>
    internal static void Sample()
    {
        var now = Time.unscaledTime;
        if (now < _nextSample) {
            return;
        }

        _nextSample = now + 0.5f;
        try {
            if (EClass.scene?.mouseTarget?.card is { } card) {
                _mouse = Describe(card);
                _mouseAt = now;
            } else if (EClass.scene?.mouseTarget is { isValid: true } target && target.pos is { IsValid: true } pos && !EClass.ui.isPointerOverUI) {
                _mouse = $"the ground at ({pos.x}, {pos.z})";
                _mouseAt = now;
            }

            if (InputModuleEX.GetComponentOf<ButtonGrid>()?.card is { } item) {
                _hovered = Describe(item);
                _hoveredAt = now;
            }
        } catch (Exception) {
            // noexcept
        }
    }

    /// <summary>The block written at the top of a report, in plain words</summary>
    internal static string Text()
    {
        var text = new StringBuilder();
        try {
            if (EClass.core?.IsGameStarted == true && EClass._zone is { } zone) {
                var pos = EClass.pc?.pos;
                text.AppendLine($"Zone: {zone.Name} ({zone.id} #{zone.uid}), position ({pos?.x}, {pos?.z})");
            } else {
                text.AppendLine("Zone: no game started");
            }

            var now = Time.unscaledTime;
            text.AppendLine(_mouse is null ? "Under the mouse: nothing seen" : $"Under the mouse ({now - _mouseAt:F0} s ago): {_mouse}");
            if (_hovered is not null) {
                text.AppendLine($"Item under the mouse ({now - _hoveredAt:F0} s ago): {_hovered}");
            }

            text.AppendLine($"Open windows: {Windows()}");
        } catch (Exception ex) {
            text.AppendLine($"(context incomplete: {ex.GetType().Name})");
        }

        text.AppendLine(Actions.Count == 0 ? "Last actions: none noted (they are noted during a multiplayer session)" : "Last actions (newest last):");
        foreach (var action in Actions) {
            text.AppendLine($"- {action}");
        }

        return text.ToString();
    }

    private static string Windows()
    {
        var names = new List<string>();
        foreach (var layers in new[] { EClass.ui?.layers, EClass.ui?.layerFloat?.layers }) {
            if (layers is null) {
                continue;
            }

            // (the mod's own window is where the report is written: always there)
            names.AddRange(layers.Where(l => l != null && l.GetType().Name != "LayerElinTogether").Select(l => l.GetType().Name));
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
