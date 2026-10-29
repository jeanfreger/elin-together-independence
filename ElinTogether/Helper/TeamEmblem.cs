using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ElinTogether.Helper;

/// <summary>
///     A team emblem made of several sprites (PLAN_team_emblem): a background colour and shape, then up to
///     <see cref="TeamEmblem.MaxLayers" /> layers drawn in order. Kept as text in the team's thing id
///     (<see cref="Net.ElinNetHost.TeamThings" />): <c>e1|bg|shape|L;L;...</c>, L = <c>kind:id,x,y,s,r,f,c</c>
/// </summary>
/// <param name="Thing">The typed source id: "t:sword" (a thing) or "c:putty" (a monster)</param>
/// <param name="X">Centre on the 64 grid, 0..64 from the left</param>
/// <param name="Y">Centre on the 64 grid, 0..64 from the top</param>
/// <param name="Size">The longer side, in % of the emblem, 10..200</param>
/// <param name="Rotation">Quarter turns clockwise, 0..3</param>
/// <param name="Flip">1 horizontal, 2 vertical</param>
/// <param name="Tint">A palette colour 0..11, or 12 for the sprite's own colours</param>
internal readonly record struct EmblemLayer(string Thing, int X, int Y, int Size, int Rotation, int Flip, int Tint);

/// <param name="Background">Palette colour 0..11</param>
/// <param name="Shape">0 square, 1 round, 2 shield</param>
internal sealed record Emblem(int Background, int Shape, IReadOnlyList<EmblemLayer> Layers);

internal static class TeamEmblem
{
    internal const string Prefix = "e1|";
    internal const int MaxText = 400;
    internal const int MaxLayers = 8;
    internal const int Grid = 64;
    internal const int MinSize = 10;
    internal const int MaxSize = 200;
    internal const int OwnColours = 12;
    internal const int ShapeCount = 3;

    /// <summary>One layer at the centre, full size, own colours: how an old single-thing banner draws</summary>
    internal static EmblemLayer Layer(string thing) => new(thing, Grid / 2, Grid / 2, 100, 0, 0, OwnColours);

    /// <summary>
    ///     The emblem of a stored value: an <c>e1|</c> text, or an old value (t:id, c:id, bare id) as one layer on
    ///     <paramref name="background" />. Null when the text cannot be read. Ids are not checked here
    /// </summary>
    internal static Emblem? Parse(string? text, int background = 0)
    {
        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        if (!text!.StartsWith(Prefix)) {
            // legacy: one thing, typed or not
            if (text.IndexOfAny(['|', ';', ',']) >= 0) {
                return null;
            }

            var typed = text.StartsWith("t:") || text.StartsWith("c:") ? text : "t:" + text;
            return new(background, 0, [Layer(typed)]);
        }

        var parts = text.Split('|');
        if (parts.Length != 4 || !Int(parts[1], out var bg) || !Int(parts[2], out var shape)) {
            return null;
        }

        var layers = new List<EmblemLayer>();
        if (parts[3].Length > 0) {
            foreach (var chunk in parts[3].Split(';')) {
                var f = chunk.Split(',');
                if (f.Length != 7 || f[0].Length < 3 || (!f[0].StartsWith("t:") && !f[0].StartsWith("c:"))) {
                    return null;
                }

                var n = new int[6];
                for (var i = 0; i < 6; i++) {
                    if (!Int(f[i + 1], out n[i])) {
                        return null;
                    }
                }

                layers.Add(new(f[0], n[0], n[1], n[2], n[3], n[4], n[5]));
            }
        }

        return new(bg, shape, layers);
    }

    /// <summary>The canonical text of an emblem (what the host stores and tells)</summary>
    internal static string Format(Emblem emblem)
    {
        var layers = emblem.Layers.Select(l => string.Join(",", l.Thing,
            l.X.ToString(CultureInfo.InvariantCulture), l.Y.ToString(CultureInfo.InvariantCulture),
            l.Size.ToString(CultureInfo.InvariantCulture), l.Rotation.ToString(CultureInfo.InvariantCulture),
            l.Flip.ToString(CultureInfo.InvariantCulture), l.Tint.ToString(CultureInfo.InvariantCulture)));
        return Prefix + emblem.Background.ToString(CultureInfo.InvariantCulture) + "|" +
               emblem.Shape.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(";", layers);
    }

    /// <summary>
    ///     The emblem with every field in range and every id typed and known (<paramref name="normalize" /> gives the
    ///     typed id or null), or null: 1..8 layers, x/y 0..64, size 10..200, rotation 0..3, flip 0..3, tint 0..12,
    ///     background 0..11, shape 0..2
    /// </summary>
    internal static Emblem? Validate(Emblem? emblem, Func<string, string?> normalize)
    {
        if (emblem is null || emblem.Layers.Count is < 1 or > MaxLayers ||
            emblem.Background is < 0 or >= TeamBanner.ColorCount || emblem.Shape is < 0 or >= ShapeCount) {
            return null;
        }

        var layers = new List<EmblemLayer>();
        foreach (var l in emblem.Layers) {
            if (l.X is < 0 or > Grid || l.Y is < 0 or > Grid || l.Size is < MinSize or > MaxSize ||
                l.Rotation is < 0 or > 3 || l.Flip is < 0 or > 3 || l.Tint is < 0 or > OwnColours ||
                normalize(l.Thing) is not { } typed) {
                return null;
            }

            layers.Add(l with { Thing = typed });
        }

        return emblem with { Layers = layers };
    }

    /// <summary>
    ///     What the host keeps for a leader's text: the canonical <c>e1|</c> string of a valid emblem (an old single
    ///     thing upgraded on <paramref name="background" />), else null
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
