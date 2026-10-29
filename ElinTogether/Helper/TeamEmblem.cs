using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ElinTogether.Helper;

/// <summary>
///     A team emblem made of several sprites (PLAN_team_emblem): a background in a shape and a pattern of two colours,
///     then up to <see cref="TeamEmblem.MaxLayers" /> layers drawn in order. Kept as text in the team's thing id
///     (<see cref="Net.ElinNetHost.TeamThings" />). Two forms, the older one written whenever it can hold the emblem so
///     games of an older version still draw it:
///     <c>e1|bg|shape|L;L;...</c>, L = <c>kind:id,x,y,s,q,f,c</c> (q quarter turns, c 0..11 or 12 own colours), and
///     <c>e2|bg|shape|pattern|bg2|L;L;...</c>, L = <c>kind:id,x,y,s,r,f,c,o,e</c> (r eighths of a turn, c 0..23 or 24
///     own colours, o opacity %, e outline 0/1)
/// </summary>
/// <param name="Thing">The typed source id: "t:sword" (a thing) or "c:putty" (a monster)</param>
/// <param name="X">Centre on the 64 grid, 0..64 from the left</param>
/// <param name="Y">Centre on the 64 grid, 0..64 from the top</param>
/// <param name="Size">The longer side, in % of the emblem, 10..200</param>
/// <param name="Rotation">Eighths of a turn clockwise (45 degrees each), 0..7</param>
/// <param name="Flip">1 horizontal, 2 vertical</param>
/// <param name="Tint">A palette colour 0..23, or <see cref="TeamEmblem.OwnColours" /> for the sprite's own colours</param>
/// <param name="Opacity">10..100 %</param>
/// <param name="Outline">1: a dark border of one pixel around the layer</param>
internal readonly record struct EmblemLayer(
    string Thing,
    int X,
    int Y,
    int Size,
    int Rotation,
    int Flip,
    int Tint,
    int Opacity = 100,
    int Outline = 0);

/// <param name="Background">Palette colour 0..23</param>
/// <param name="Shape">0 square, 1 round, 2 shield, 3 banner, 4 diamond, 5 hexagon</param>
/// <param name="Pattern">0 plain, 1 halves (vertical), 2 halves (horizontal), 3 quarters, 4 diagonal, 5 stripes, 6 chevron, 7 border</param>
/// <param name="Background2">The pattern's second colour 0..23</param>
internal sealed record Emblem(int Background, int Shape, IReadOnlyList<EmblemLayer> Layers, int Pattern = 0, int Background2 = 0);

internal static class TeamEmblem
{
    internal const string Prefix = "e1|";
    internal const string Prefix2 = "e2|";
    internal const int MaxText = 800;
    internal const int MaxLayers = 12;
    internal const int Grid = 64;
    internal const int MinSize = 10;
    internal const int MaxSize = 200;
    internal const int MinOpacity = 10;

    /// <summary>The colours of the emblem palette (<see cref="TeamBanner.EmblemColorCount" />)</summary>
    internal const int Colours = 24;

    /// <summary>The tint that keeps the sprite's own colours</summary>
    internal const int OwnColours = Colours;

    internal const int ShapeCount = 6;
    internal const int PatternCount = 8;

    // what the first form can hold
    private const int OldLayers = 8;
    private const int OldColours = 12;
    private const int OldShapes = 3;

    /// <summary>One layer at the centre, full size, own colours: how an old single-thing banner draws</summary>
    internal static EmblemLayer Layer(string thing) => new(thing, Grid / 2, Grid / 2, 100, 0, 0, OwnColours);

    /// <summary>
    ///     The emblem of a stored value: an <c>e1|</c> or <c>e2|</c> text, or an old value (t:id, c:id, bare id) as one
    ///     layer on <paramref name="background" />. Null when the text cannot be read. Ids are not checked here
    /// </summary>
    internal static Emblem? Parse(string? text, int background = 0)
    {
        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        var second = text!.StartsWith(Prefix2);
        if (!second && !text.StartsWith(Prefix)) {
            // legacy: one thing, typed or not
            if (text.IndexOfAny(['|', ';', ',']) >= 0) {
                return null;
            }

            var typed = text.StartsWith("t:") || text.StartsWith("c:") ? text : "t:" + text;
            return new(background, 0, [Layer(typed)]);
        }

        var parts = text.Split('|');
        if (parts.Length != (second ? 6 : 4) || !Int(parts[1], out var bg) || !Int(parts[2], out var shape)) {
            return null;
        }

        var (pattern, bg2) = (0, bg);
        if (second && (!Int(parts[3], out pattern) || !Int(parts[4], out bg2))) {
            return null;
        }

        var fields = second ? 8 : 6;
        var layers = new List<EmblemLayer>();
        var list = parts[parts.Length - 1];
        if (list.Length > 0) {
            foreach (var chunk in list.Split(';')) {
                var f = chunk.Split(',');
                if (f.Length != fields + 1 || f[0].Length < 3 || (!f[0].StartsWith("t:") && !f[0].StartsWith("c:"))) {
                    return null;
                }

                var n = new int[fields];
                for (var i = 0; i < fields; i++) {
                    if (!Int(f[i + 1], out n[i])) {
                        return null;
                    }
                }

                layers.Add(second
                    ? new(f[0], n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7])
                    // (the first form: quarter turns, and 12 for the sprite's own colours)
                    : new(f[0], n[0], n[1], n[2], n[3] * 2, n[4], n[5] == OldColours ? OwnColours : n[5]));
            }
        }

        return new(bg, shape, layers, pattern, bg2);
    }

    /// <summary>The first form holds it: what an older game can read</summary>
    private static bool FitsFirstForm(Emblem e) =>
        e.Pattern == 0 && e.Background < OldColours && e.Shape < OldShapes && e.Layers.Count <= OldLayers &&
        e.Layers.All(l => l.Rotation % 2 == 0 && (l.Tint < OldColours || l.Tint == OwnColours) && l.Opacity >= 100 && l.Outline == 0);

    private static string S(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The canonical text of an emblem (what the host stores and tells), in the first form when it fits</summary>
    internal static string Format(Emblem emblem)
    {
        if (FitsFirstForm(emblem)) {
            var old = emblem.Layers.Select(l => string.Join(",", l.Thing, S(l.X), S(l.Y), S(l.Size), S(l.Rotation / 2),
                S(l.Flip), S(l.Tint == OwnColours ? OldColours : l.Tint)));
            return Prefix + S(emblem.Background) + "|" + S(emblem.Shape) + "|" + string.Join(";", old);
        }

        var layers = emblem.Layers.Select(l => string.Join(",", l.Thing, S(l.X), S(l.Y), S(l.Size), S(l.Rotation),
            S(l.Flip), S(l.Tint), S(l.Opacity), S(l.Outline)));
        return Prefix2 + S(emblem.Background) + "|" + S(emblem.Shape) + "|" + S(emblem.Pattern) + "|" +
               S(emblem.Background2) + "|" + string.Join(";", layers);
    }

    /// <summary>
    ///     The emblem with every field in range and every id typed and known (<paramref name="normalize" /> gives the
    ///     typed id or null), or null: 1..12 layers, x/y 0..64, size 10..200, rotation 0..7, flip 0..3, tint 0..24,
    ///     opacity 10..100, outline 0..1, colours 0..23, shape 0..5, pattern 0..7
    /// </summary>
    internal static Emblem? Validate(Emblem? emblem, Func<string, string?> normalize)
    {
        if (emblem is null || emblem.Layers.Count is < 1 or > MaxLayers ||
            emblem.Background is < 0 or >= Colours || emblem.Background2 is < 0 or >= Colours ||
            emblem.Shape is < 0 or >= ShapeCount || emblem.Pattern is < 0 or >= PatternCount) {
            return null;
        }

        var layers = new List<EmblemLayer>();
        foreach (var l in emblem.Layers) {
            if (l.X is < 0 or > Grid || l.Y is < 0 or > Grid || l.Size is < MinSize or > MaxSize ||
                l.Rotation is < 0 or > 7 || l.Flip is < 0 or > 3 || l.Tint is < 0 or > OwnColours ||
                l.Opacity is < MinOpacity or > 100 || l.Outline is < 0 or > 1 ||
                normalize(l.Thing) is not { } typed) {
                return null;
            }

            layers.Add(l with { Thing = typed });
        }

        return emblem with { Layers = layers };
    }

    /// <summary>
    ///     What the host keeps for a leader's text: the canonical string of a valid emblem (an old single thing
    ///     upgraded on <paramref name="background" />), else null
    /// </summary>
    internal static string? Canonical(string text, int background, Func<string, string?> normalize)
    {
        if (text.Length is 0 or > MaxText) {
            return null;
        }

        return Validate(Parse(text, background), normalize) is { } emblem ? Format(emblem) : null;
    }

    private static bool Int(string s, out int value) =>
        int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
