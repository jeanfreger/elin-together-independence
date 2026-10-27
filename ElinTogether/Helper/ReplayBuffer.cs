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
    // (sharp enough to read the game's messages and windows: the reports are read by the developers, and by Claude)
    private const int Width = 960;
    private const int Quality = 70;

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
    ///     All the pictures on one sheet, 6 across and 5 down, oldest top left, read like a page: the whole half
    ///     minute at one glance, then the sharp pictures of the zip for the seconds that matter. Null without pictures
    /// </summary>
    internal static byte[]? Sheet()
    {
        if (_frames.Count == 0) {
            return null;
        }

        const int columns = 6, cell = 320;
        var rows = (_frames.Count + columns - 1) / columns;
        var picture = new Texture2D(2, 2);
        Texture2D? sheet = null;
        try {
            var index = 0;
            foreach (var (_, jpg) in _frames) {
                if (!picture.LoadImage(jpg)) {
                    index++;
                    continue;
                }

                var height = cell * picture.height / picture.width;
                if (sheet is null) {
                    sheet = new(cell * columns, height * rows, TextureFormat.RGB24, false);
                    sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
                }

                var pixels = picture.GetPixels32();
                var small = new Color32[cell * height];
                for (var y = 0; y < height; y++) {
                    var sy = y * picture.height / height * picture.width;
                    for (var x = 0; x < cell; x++) {
                        small[y * cell + x] = pixels[sy + x * picture.width / cell];
                    }
                }

                // (the texture's rows go up: the first picture's row is the top one)
                var (col, row) = (index % columns, index / columns);
                sheet.SetPixels32(col * cell, (rows - 1 - row) * height, cell, height, small);
                index++;
            }

            return sheet?.EncodeToJPG(80);
        } finally {
            UnityEngine.Object.Destroy(picture);
            if (sheet is not null) {
                UnityEngine.Object.Destroy(sheet);
            }
        }
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
