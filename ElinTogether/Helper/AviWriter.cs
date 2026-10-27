using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ElinTogether.Helper;

/// <summary>
///     A real video of the replay (user, 2026-10-27: "a video", not a GIF nor a sheet): the JPEG pictures as they are,
///     in an AVI file (Motion JPEG), which Windows' player and VLC open. No encoding: the container only
/// </summary>
internal static class AviWriter
{
    internal static byte[] Write(int width, int height, int fps, IReadOnlyList<byte[]> jpegs)
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream, Encoding.ASCII);
        var biggest = 0;
        foreach (var jpg in jpegs) {
            biggest = System.Math.Max(biggest, jpg.Length);
        }

        w.Write("RIFF".ToCharArray());
        var riffSize = Mark(w);
        w.Write("AVI ".ToCharArray());

        // header list: main header, one video stream
        w.Write("LIST".ToCharArray());
        var hdrlSize = Mark(w);
        w.Write("hdrl".ToCharArray());

        w.Write("avih".ToCharArray());
        w.Write(56);
        w.Write(1000000 / fps); // microseconds per frame
        w.Write(biggest * fps); // max bytes per second
        w.Write(0); // padding granularity
        w.Write(0x10); // flags: has an index
        w.Write(jpegs.Count);
        w.Write(0); // initial frames
        w.Write(1); // streams
        w.Write(biggest);
        w.Write(width);
        w.Write(height);
        w.Write(new byte[16]);

        w.Write("LIST".ToCharArray());
        var strlSize = Mark(w);
        w.Write("strl".ToCharArray());

        w.Write("strh".ToCharArray());
        w.Write(56);
        w.Write("vids".ToCharArray());
        w.Write("MJPG".ToCharArray());
        w.Write(0); // flags
        w.Write(0); // priority, language
        w.Write(0); // initial frames
        w.Write(1); // scale
        w.Write(fps); // rate: fps frames a second
        w.Write(0); // start
        w.Write(jpegs.Count); // length
        w.Write(biggest); // suggested buffer
        w.Write(-1); // quality
        w.Write(0); // sample size
        w.Write((short)0);
        w.Write((short)0);
        w.Write((short)width);
        w.Write((short)height);

        w.Write("strf".ToCharArray());
        w.Write(40);
        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)24);
        w.Write("MJPG".ToCharArray());
        w.Write(width * height * 3);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);

        Close(w, strlSize);
        Close(w, hdrlSize);

        // the pictures
        w.Write("LIST".ToCharArray());
        var moviSize = Mark(w);
        var movi = stream.Position;
        w.Write("movi".ToCharArray());
        var offsets = new List<(int Offset, int Size)>();
        foreach (var jpg in jpegs) {
            offsets.Add(((int)(stream.Position - movi), jpg.Length));
            w.Write("00dc".ToCharArray());
            w.Write(jpg.Length);
            w.Write(jpg);
            if (jpg.Length % 2 == 1) {
                w.Write((byte)0);
            }
        }

        Close(w, moviSize);

        // the index: where each picture is, from the start of "movi"
        w.Write("idx1".ToCharArray());
        w.Write(offsets.Count * 16);
        foreach (var (offset, size) in offsets) {
            w.Write("00dc".ToCharArray());
            w.Write(0x10); // key frame
            w.Write(offset);
            w.Write(size);
        }

        Close(w, riffSize);
        w.Flush();
        return stream.ToArray();
    }

    // a chunk size to write once its end is known
    private static long Mark(BinaryWriter w)
    {
        var at = w.BaseStream.Position;
        w.Write(0);
        return at;
    }

    private static void Close(BinaryWriter w, long at)
    {
        var end = w.BaseStream.Position;
        w.BaseStream.Position = at;
        w.Write((int)(end - at - 4));
        w.BaseStream.Position = end;
    }
}
