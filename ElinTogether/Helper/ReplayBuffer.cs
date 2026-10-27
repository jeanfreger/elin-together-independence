using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     The last seconds of the screen, for the bug report (user, 2026-10-27): one small picture a second, the last
///     thirty kept in memory, sent with the logs. Players rarely record when the bug happens; this did it for them.
///     The picture is made small on the graphics card (one blit, one read of 640x360), off with Client.RecordReplay
/// </summary>
internal static class ReplayBuffer
{
    private const int Frames = 30;
    private const float Every = 1f;
    private const int Width = 640;
    private const int Quality = 60;

    private static readonly Queue<(DateTime At, byte[] Jpg)> _frames = new();
    private static RenderTexture? _small;
    private static Texture2D? _read;

    internal static IEnumerator Run()
    {
        var endOfFrame = new WaitForEndOfFrame();
        var wait = new WaitForSecondsRealtime(Every);
        while (true) {
            yield return wait;
            if (!EmpConfig.Client.RecordReplay.Value || !EClass.core.IsGameStarted) {
                continue;
            }

            yield return endOfFrame;
            try {
                Capture();
            } catch (Exception ex) {
                // (never the game's frame for a picture: one failure is told, and it stops)
                EmpLog.Warning(ex, "The replay for bug reports could not take a picture, it stops");
                yield break;
            }
        }
    }

    private static void Capture()
    {
        var height = Mathf.Max(1, Width * Screen.height / Mathf.Max(1, Screen.width));
        var full = RenderTexture.GetTemporary(Screen.width, Screen.height, 0);
        try {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
            if (_small is null || _small.height != height) {
                if (_small is not null) {
                    _small.Release();
                }

                _small = new(Width, height, 0);
                _read = new(Width, height, TextureFormat.RGB24, false);
            }

            // (the capture comes upside down on Direct3D: flipped in the same blit)
            Graphics.Blit(full, _small, new Vector2(1, -1), new Vector2(0, 1));
        } finally {
            RenderTexture.ReleaseTemporary(full);
        }

        var before = RenderTexture.active;
        RenderTexture.active = _small;
        _read!.ReadPixels(new(0, 0, Width, height), 0, 0, false);
        RenderTexture.active = before;

        _frames.Enqueue((DateTime.Now, _read.EncodeToJPG(Quality)));
        while (_frames.Count > Frames) {
            _frames.Dequeue();
        }
    }

    /// <summary>
    ///     The pictures kept, as one animated GIF (twice as fast as played), null without pictures. Decoded here on the
    ///     main thread, made smaller (480 wide) for the page that shows it
    /// </summary>
    private static (DateTime Last, byte[]? Gif) _made;

    internal static byte[]? Gif()
    {
        if (_frames.Count == 0) {
            return null;
        }

        // (asked for the zip and for the report's own files: made once for the same pictures)
        var last = _frames.Last().At;
        if (_made.Last == last) {
            return _made.Gif;
        }

        const int width = 480;
        var frames = new List<byte[]>();
        var height = 0;
        var texture = new Texture2D(2, 2);
        try {
            foreach (var (_, jpg) in _frames) {
                if (!texture.LoadImage(jpg)) {
                    continue;
                }

                height = width * texture.height / texture.width;
                var pixels = texture.GetPixels32();
                var rgb = new byte[width * height * 3];
                for (var y = 0; y < height; y++) {
                    // (the texture's first row is the bottom one)
                    var sy = (texture.height - 1 - y * texture.height / height) * texture.width;
                    for (var x = 0; x < width; x++) {
                        var c = pixels[sy + x * texture.width / width];
                        var i = (y * width + x) * 3;
                        rgb[i] = c.r;
                        rgb[i + 1] = c.g;
                        rgb[i + 2] = c.b;
                    }
                }

                frames.Add(rgb);
            }
        } finally {
            UnityEngine.Object.Destroy(texture);
        }

        _made = (last, frames.Count == 0 ? null : GifWriter.Write(width, height, frames, 50));
        return _made.Gif;
    }

    /// <summary>
    ///     The pictures kept, oldest first, named by their time
    /// </summary>
    internal static List<(string Name, byte[] Jpg)> Snapshot()
    {
        var list = new List<(string, byte[])>();
        foreach (var (at, jpg) in _frames) {
            list.Add(($"replay/{at:HH-mm-ss}.jpg", jpg));
        }

        return list;
    }
}
