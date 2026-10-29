using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ElinTogether.Helper;

/// <summary>
///     Draws a <see cref="Emblem" /> once into a 64x64 point-filtered texture (PLAN_team_emblem): the background colour
///     in its shape, then each layer from the cropped sprite of its thing, nearest inverse map (size, quarter turns,
///     flips), tinted, alpha over, cut to the shape. Kept by canonical text (LRU of 256) so every view shares the sprite
/// </summary>
internal static class TeamEmblemBaker
{
    private const int N = TeamEmblem.Grid;
    private const int Capacity = 256;

    private static readonly Dictionary<string, LinkedListNode<(string Key, Sprite Sprite)>> _baked = [];
    private static readonly LinkedList<(string Key, Sprite Sprite)> _order = new();

    // typed thing id -> its opaque pixels (the sprite cut to its alpha box), null when it cannot be read
    private static readonly Dictionary<string, (Color32[] Pixels, int W, int H)?> _sources = [];

    internal static Sprite Get(Emblem emblem)
    {
        var key = TeamEmblem.Format(emblem);
        if (_baked.TryGetValue(key, out var node)) {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Sprite;
        }

        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "emp_emblem",
        };
        tex.SetPixels32(Bake(emblem));
        tex.Apply(false, false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _baked[key] = _order.AddFirst((key, sprite));
        while (_order.Count > Capacity) {
            var last = _order.Last!.Value;
            _order.RemoveLast();
            _baked.Remove(last.Key);
            Object.Destroy(last.Sprite.texture);
            Object.Destroy(last.Sprite);
        }

        return sprite;
    }

    /// <summary>A short hash of the baked pixels of a stored text (the bench compares it between games)</summary>
    internal static string PixelHash(Emblem emblem)
    {
        unchecked {
            var h = 2166136261u;
            foreach (var c in Get(emblem).texture.GetPixels32()) {
                h = (h ^ c.r) * 16777619u;
                h = (h ^ c.g) * 16777619u;
                h = (h ^ c.b) * 16777619u;
                h = (h ^ c.a) * 16777619u;
            }

            return h.ToString("x8");
        }
    }

    internal static void Clear()
    {
        foreach (var (_, sprite) in _order) {
            Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }

        _order.Clear();
        _baked.Clear();
        _sources.Clear();
    }

    /// <summary>The baked sprite as an image of <paramref name="size" /> in a layout row</summary>
    internal static Image Place(Transform parent, Sprite sprite, float size)
    {
        var go = new GameObject("Emblem", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        go.transform.SetParent(parent, false);
        // (also sized by hand: some rows do not control their children's height)
        ((RectTransform)go.transform).sizeDelta = new(size, size);
        var element = go.GetComponent<LayoutElement>();
        (element.minWidth, element.preferredWidth, element.minHeight, element.preferredHeight, element.flexibleWidth) = (size, size, size, size, 0f);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>The 64x64 pixels, rows bottom-up as a texture holds them</summary>
    internal static Color32[] Bake(Emblem emblem)
    {
        // (top-down while drawing) the background: its pattern of the two colours
        var px = new Color[N * N];
        var bg = TeamBanner.ColorOf(emblem.Background);
        var bg2 = TeamBanner.ColorOf(emblem.Background2);
        for (var y = 0; y < N; y++) {
            for (var x = 0; x < N; x++) {
                px[y * N + x] = Second(emblem.Pattern, emblem.Shape, x, y) ? bg2 : bg;
            }
        }

        var cover = new float[N * N];
        var colour = new Color[N * N];
        foreach (var layer in emblem.Layers) {
            if (Source(layer.Thing) is not { } src) {
                continue;
            }

            System.Array.Clear(cover, 0, cover.Length);
            Color? tint = layer.Tint < TeamEmblem.OwnColours ? TeamBanner.ColorOf(layer.Tint) : null;
            var opacity = Mathf.Clamp(layer.Opacity, TeamEmblem.MinOpacity, 100) / 100f;
            var scale = N * layer.Size / 100f / Mathf.Max(src.W, src.H, 1);
            // undo the clockwise turn (eighths of a turn), then the flips
            var angle = layer.Rotation * Mathf.PI / 4f;
            var (cos, sin) = (Mathf.Cos(angle), Mathf.Sin(angle));
            if (layer.Rotation % 2 == 0) {
                // (quarter turns stay exact: the same pixels as the first form drew)
                (cos, sin) = (Mathf.Round(cos), Mathf.Round(sin));
            }

            for (var dy = 0; dy < N; dy++) {
                for (var dx = 0; dx < N; dx++) {
                    float x0 = dx + 0.5f - layer.X, y0 = dy + 0.5f - layer.Y;
                    var u = x0 * cos + y0 * sin;
                    var v = -x0 * sin + y0 * cos;

                    if ((layer.Flip & 1) != 0) {
                        u = -u;
                    }

                    if ((layer.Flip & 2) != 0) {
                        v = -v;
                    }

                    var sx = Mathf.FloorToInt(u / scale + src.W / 2f);
                    var syTop = Mathf.FloorToInt(v / scale + src.H / 2f);
                    if (sx < 0 || sx >= src.W || syTop < 0 || syTop >= src.H) {
                        continue;
                    }

                    var c = (Color)src.Pixels[(src.H - 1 - syTop) * src.W + sx];
                    if (c.a <= 0f) {
                        continue;
                    }

                    if (tint is { } t) {
                        var l = 0.3f + 0.7f * (0.299f * c.r + 0.587f * c.g + 0.114f * c.b);
                        c = new(t.r * l, t.g * l, t.b * l, c.a);
                    }

                    var i = dy * N + dx;
                    cover[i] = c.a;
                    colour[i] = c;
                }
            }

            // the outline first, on the pixels the layer leaves bare beside the ones it covers; then the layer
            for (var i = 0; i < px.Length; i++) {
                if (cover[i] > 0f) {
                    px[i] = Color.Lerp(px[i], new(colour[i].r, colour[i].g, colour[i].b, 1f), cover[i] * opacity);
                } else if (layer.Outline != 0 && Borders(cover, i)) {
                    px[i] = Color.Lerp(px[i], Ink, opacity);
                }
            }
        }

        var output = new Color32[N * N];
        for (var dy = 0; dy < N; dy++) {
            for (var dx = 0; dx < N; dx++) {
                var c = (Color32)px[dy * N + dx];
                c.a = Inside(emblem.Shape, dx, dy) ? (byte)255 : (byte)0;
                output[(N - 1 - dy) * N + dx] = c;
            }
        }

        return output;
    }

    // the outline's dark ink
    private static readonly Color Ink = new(0.1f, 0.08f, 0.06f, 1f);

    /// <summary>A bare pixel next to one the layer covers at least half (its four neighbours)</summary>
    private static bool Borders(float[] cover, int i)
    {
        var (x, y) = (i % N, i / N);
        return (x > 0 && cover[i - 1] >= 0.5f) || (x < N - 1 && cover[i + 1] >= 0.5f) ||
               (y > 0 && cover[i - N] >= 0.5f) || (y < N - 1 && cover[i + N] >= 0.5f);
    }

    /// <summary>
    ///     The pixel (top-down) takes the pattern's second colour: halves, quarters, a diagonal, stripes, a chevron, or a
    ///     border along the shape's edge
    /// </summary>
    private static bool Second(int pattern, int shape, int x, int y)
    {
        const int half = N / 2;
        switch (pattern) {
            case 1:
                return x >= half;
            case 2:
                return y >= half;
            case 3:
                return x >= half ^ y >= half;
            case 4:
                return x > y;
            case 5:
                return x / 8 % 2 == 1;
            case 6:
                // an upturned V across the middle, 12 pixels thick
                var top = N * 0.3f + Mathf.Abs(x + 0.5f - half) * 0.9f;
                return y + 0.5f >= top && y + 0.5f < top + 12f;
            case 7:
                const int w = 6;
                return !Inside(shape, x - w, y) || !Inside(shape, x + w, y) || !Inside(shape, x, y - w) || !Inside(shape, x, y + w);
            default:
                return false;
        }
    }

    /// <summary>
    ///     The pixel (top-down) is inside the shape: square, round, a shield pointed at the bottom, a banner with a
    ///     swallowtail, a diamond, a hexagon
    /// </summary>
    private static bool Inside(int shape, int x, int y)
    {
        if (x < 0 || y < 0 || x >= N || y >= N) {
            return false;
        }

        var dx = Mathf.Abs(x + 0.5f - N / 2f);
        var py = y + 0.5f;
        var dy = Mathf.Abs(py - N / 2f);
        const float r = N / 2f;
        switch (shape) {
            case 1:
                return dx * dx + dy * dy <= r * r;
            case 2:
                if (py < N / 2f) {
                    return true;
                }

                var t = (py - N / 2f) / (N / 2f);
                return dx <= N / 2f * Mathf.Pow(Mathf.Max(0f, 1f - t), 0.6f);
            case 3:
                // a hanging banner, a little narrower than the square, its bottom cut in a V
                const float notch = 16f;
                return dx <= r * 0.8f && !(py > N - notch && dx < py - (N - notch));
            case 4:
                return dx + dy <= r;
            case 5:
                // pointed at the top and the bottom, its sides upright
                return dx <= r * 0.866f && dx * 0.57735f + dy <= r;
            default:
                return true;
        }
    }

    private static (Color32[] Pixels, int W, int H)? Source(string thing)
    {
        if (_sources.TryGetValue(thing, out var known)) {
            return known;
        }

        (Color32[] Pixels, int W, int H)? result = null;
        try {
            if (TeamBanner.SpriteOf(thing) is { } sprite) {
                var src = Patches.MultiplayerHubPatch.Read(sprite);
                var box = Patches.MultiplayerHubPatch.AlphaBox(src.Pixels, src.W, src.H);
                var cut = new Color32[box.width * box.height];
                for (var y = 0; y < box.height; y++) {
                    System.Array.Copy(src.Pixels, (box.y + y) * src.W + box.x, cut, y * box.width, box.width);
                }

                result = (cut, box.width, box.height);
            }
        } catch (System.Exception e) {
            EmpLog.Debug(e, "Team emblem: the sprite of {Thing} could not be read", thing);
        }

        if (_sources.Count > 1024) {
            _sources.Clear();
        }

        return _sources[thing] = result;
    }
}
