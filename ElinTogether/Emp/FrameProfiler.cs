#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ElinTogether.Emp;

/// <summary>
///     Debug build only: time spent in chosen methods, for the bench's eval (the 110-player host ran at 3-5 fps with
///     rendering off and the mod's own jobs at a few % of the frame, 2026-10-28).
///     <c>ElinTogether.Emp.FrameProfiler.Start("Chara:Tick", "Scene:OnUpdate")</c>, then <c>Report()</c> and <c>Stop()</c>.
///     Nested methods count in both (a method's time includes what it calls)
/// </summary>
public static class FrameProfiler
{
    private const string Id = "emp.frameprofiler";
    private static readonly Harmony _harmony = new(Id);
    private static readonly Dictionary<MethodBase, (long Ticks, long Calls)> _spent = [];
    private static readonly List<MethodBase> _patched = [];
    private static readonly Stopwatch _since = new();

    public static string Start(params string[] methods)
    {
        Stop();
        var told = new List<string>();
        foreach (var name in methods) {
            var parts = name.Split(':');
            var type = AccessTools.TypeByName(parts[0]);
            var found = type is null ? [] : AccessTools.GetDeclaredMethods(type).Where(m => m.Name == parts[1] && !m.IsAbstract && !m.IsGenericMethodDefinition).ToList();
            if (found.Count == 0) {
                told.Add(name + " not found");
                continue;
            }

            foreach (var m in found) {
                _harmony.Patch(m, prefix: new(typeof(FrameProfiler), nameof(Before)) { priority = Priority.First },
                    postfix: new(typeof(FrameProfiler), nameof(After)) { priority = Priority.Last });
                _patched.Add(m);
            }
        }

        _since.Restart();
        return $"{_patched.Count} method(s) timed" + (told.Count > 0 ? "; " + string.Join(", ", told) : "");
    }

    public static string Report()
    {
        var wall = _since.Elapsed.TotalMilliseconds;
        var lines = _spent.OrderByDescending(kv => kv.Value.Ticks).Select(kv =>
            $"{kv.Value.Ticks * 1000.0 / Stopwatch.Frequency,9:F0} ms {kv.Value.Ticks * 100000.0 / Stopwatch.Frequency / wall,5:F1}% {kv.Value.Calls,8} calls  {kv.Key.DeclaringType?.Name}.{kv.Key.Name}");
        return $"over {wall / 1000:F1} s:\n" + string.Join("\n", lines);
    }

    public static void Stop()
    {
        Harmony.UnpatchID(Id);
        _patched.Clear();
        _spent.Clear();
    }

    private static void Before(out long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    private static void After(MethodBase __originalMethod, long __state)
    {
        var took = Stopwatch.GetTimestamp() - __state;
        var was = _spent.GetValueOrDefault(__originalMethod);
        _spent[__originalMethod] = (was.Ticks + took, was.Calls + 1);
    }
}
#endif
