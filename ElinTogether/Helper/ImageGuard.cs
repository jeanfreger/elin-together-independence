using System;

namespace ElinTogether.Helper;

/// <summary>
///     A picture another player sends is read for its size before the game turns it into a texture: the size is in
///     the first bytes, and Unity allocates width * height * 4 for it whatever the file weighs
/// </summary>
internal static class ImageGuard
{
    // a canvas or a portrait is a few hundred pixels a side
    internal const int MaxSide = 4096;

    /// <summary>
    ///     The PNG or JPEG is no bigger than MaxSide a side. Anything else (other format, cut header) is refused
    /// </summary>
    internal static bool IsSafe(byte[]? data)
    {
        if (data is null || !TrySize(data, out var w, out var h)) {
            EmpLog.Warning("A picture of an unknown format or with a cut header was refused");
            return false;
        }

        if (w is < 1 or > MaxSide || h is < 1 or > MaxSide) {
            EmpLog.Warning("A picture of {W}x{H} was refused (at most {Max} a side)", w, h, MaxSide);
            return false;
        }

        return true;
    }

    private static bool TrySize(byte[] d, out int w, out int h)
    {
        w = h = 0;
        // PNG: signature, then the IHDR chunk holds the width and the height as big-endian ints
        if (d.Length >= 24 && d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G' && d[12] == 'I' && d[13] == 'H' &&
            d[14] == 'D' && d[15] == 'R') {
            w = Be32(d, 16);
            h = Be32(d, 20);
            return true;
        }

        // JPEG: segments until a start-of-frame one (SOF0-SOF15 but DHT, JPG, DAC), which holds height then width
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) {
            return false;
        }

        var i = 2;
        while (i + 4 <= d.Length) {
            if (d[i] != 0xFF) {
                return false;
            }

            var marker = d[i + 1];
            if (marker == 0xFF) {
                i++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) {
                i += 2;
                continue;
            }

            var len = (d[i + 2] << 8) | d[i + 3];
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) {
                if (i + 9 > d.Length) {
                    return false;
                }

                h = (d[i + 5] << 8) | d[i + 6];
                w = (d[i + 7] << 8) | d[i + 8];
                return true;
            }

            if (marker == 0xD9 || len < 2) {
                return false;
            }

            i += 2 + len;
        }

        return false;
    }

    private static int Be32(byte[] d, int at)
    {
        var v = ((uint)d[at] << 24) | ((uint)d[at + 1] << 16) | ((uint)d[at + 2] << 8) | d[at + 3];
        return v > int.MaxValue ? int.MaxValue : (int)v;
    }
}
