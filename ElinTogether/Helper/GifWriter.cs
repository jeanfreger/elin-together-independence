using System.Collections.Generic;
using System.IO;

namespace ElinTogether.Helper;

/// <summary>
///     An animated GIF from the replay's pictures (user, 2026-10-27: "a video" in the bug report; GitHub shows a GIF
///     moving, right in the page). One fixed palette of 6x7x6 colours (no per-frame quantising: fast, good enough for
///     a game screen), LZW as the format wants. Pure code, no Unity: runs off the main thread
/// </summary>
internal static class GifWriter
{
    private const int Levels = 6 * 7 * 6;

    /// <param name="frames">each frame: width*height RGB bytes, top row first</param>
    /// <param name="delay">hundredths of a second per frame</param>
    internal static byte[] Write(int width, int height, IReadOnlyList<byte[]> frames, int delay)
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("GIF89a".ToCharArray());
        w.Write((ushort)width);
        w.Write((ushort)height);
        // global colour table of 256 entries (7 bits size)
        w.Write((byte)0xF7);
        w.Write((byte)0);
        w.Write((byte)0);
        for (var i = 0; i < 256; i++) {
            if (i < Levels) {
                w.Write((byte)(i / 42 * 51));
                w.Write((byte)(i / 6 % 7 * 255 / 6));
                w.Write((byte)(i % 6 * 51));
            } else {
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((byte)0);
            }
        }

        // loop forever
        w.Write(new byte[] { 0x21, 0xFF, 0x0B });
        w.Write("NETSCAPE2.0".ToCharArray());
        w.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

        var indices = new byte[width * height];
        foreach (var rgb in frames) {
            for (int p = 0, i = 0; p < indices.Length; p++, i += 3) {
                indices[p] = (byte)((rgb[i] * 6 >> 8) * 42 + (rgb[i + 1] * 7 >> 8) * 6 + (rgb[i + 2] * 6 >> 8));
            }

            // graphic control (delay), image descriptor, image data
            w.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00 });
            w.Write((ushort)delay);
            w.Write(new byte[] { 0x00, 0x00, 0x2C, 0, 0, 0, 0 });
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)0);
            w.Write((byte)8);
            Lzw(w, indices);
        }

        w.Write((byte)0x3B);
        w.Flush();
        return stream.ToArray();
    }

    private static void Lzw(BinaryWriter w, byte[] pixels)
    {
        const int clear = 256, end = 257, maxCode = 4095;
        var table = new Dictionary<int, int>();
        var next = 258;
        var size = 9;
        var block = new List<byte>(255);
        int bits = 0, count = 0;

        void Emit(int code)
        {
            bits |= code << count;
            count += size;
            while (count >= 8) {
                block.Add((byte)bits);
                bits >>= 8;
                count -= 8;
                if (block.Count == 255) {
                    w.Write((byte)255);
                    w.Write(block.ToArray());
                    block.Clear();
                }
            }
        }

        Emit(clear);
        int prefix = pixels[0];
        for (var i = 1; i < pixels.Length; i++) {
            var k = pixels[i];
            var key = prefix << 8 | k;
            if (table.TryGetValue(key, out var code)) {
                prefix = code;
                continue;
            }

            Emit(prefix);
            if (next <= maxCode) {
                table[key] = next++;
                // (the decoder adds its entry one code late: the codes widen once the table has passed 2^size)
                if (next > 1 << size && size < 12) {
                    size++;
                }
            } else {
                Emit(clear);
                table.Clear();
                next = 258;
                size = 9;
            }

            prefix = k;
        }

        Emit(prefix);
        Emit(end);
        if (count > 0) {
            block.Add((byte)bits);
        }

        if (block.Count > 0) {
            w.Write((byte)block.Count);
            w.Write(block.ToArray());
        }

        w.Write((byte)0);
    }
}
