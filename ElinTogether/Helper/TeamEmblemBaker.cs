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
        // (top-down while drawing)
        var px = new Color[N * N];
        var bg = TeamBanner.ColorOf(emblem.Background);
        for (var i = 0; i < px.Length; i++) {
            px[i] = bg;
        }

        foreach (var layer in emblem.Layers) {
            if (Source(layer.Thing) is not { } src) {
                continue;
            }

            Color? tint = layer.Tint < TeamEmblem.OwnColours ? TeamBanner.ColorOf(layer.Tint) : null;
            var scale = N * layer.Size / 100f / Mathf.Max(src.W, src.H, 1);
            for (var dy = 0; dy < N; dy++) {
                for (var dx = 0; dx < N; dx++) {
                    float u = dx + 0.5f - layer.X, v = dy + 0.5f - layer.Y;
                    // undo the clockwise quarter turns, then the flips
                    for (var r = 0; r < layer.Rotation; r++) {
                        (u, v) = (v, -u);
                    }

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
                    px[i] = Color.Lerp(px[i], new(c.r, c.g, c.b, 1f), c.a);
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

    /// <summary>The pixel (top-down) is inside the shape: square, round, or a shield pointed at the bottom</summary>
    private static bool Inside(int shape, int x, int y)
    {
        var dx = Mathf.Abs(x + 0.5f - N / 2f);
        var py = y + 0.5f;
        switch (shape) {
            case 1:
                var dy = py - N / 2f;
                return dx * dx + dy * dy <= N * N / 4f;
            case 2:
                if (py < N / 2f) {
                    return true;
                }

                var t = (py - N / 2f) / (N / 2f);
                return dx <= N / 2f * Mathf.Pow(Mathf.Max(0f, 1f - t), 0.6f);
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
