using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using LZ4;
using MessagePack;
using Newtonsoft.Json;

namespace ElinTogether.Models;

[MessagePackObject]
public class LZ4Bytes
{
    private static readonly JsonSerializer _serializer = JsonSerializer.Create(GameIOContext.Settings);

    // what another game sends is read with the types of the game and its mods only: the game's settings name the type
    // to make in the text, any type at all (upstream 6f0fda1, 2026-10-14)
    private static readonly JsonSerializer _deserializer = CreateDeserializer();
    public static LZ4Bytes Empty => field ??= new() { Bytes = [] };

    // a map is some MB once decompressed; this only stops a buggy peer's bomb from eating the memory of the game
    private const long MaxDecompressed = 256L * 1024 * 1024;

    [Key(0)]
    public required byte[] Bytes { get; init; }

    public static LZ4Bytes Create<T>(T data)
    {
        using var ms = new MemoryStream();
        using var lz4 = new LZ4Stream(ms, CompressionMode.Compress);
        using var sw = new StreamWriter(lz4, Encoding.UTF8);
        using var jw = new JsonTextWriter(sw);

        _serializer.Serialize(jw, data, typeof(T));
        jw.Flush();
        sw.Flush();
        lz4.Flush();

        return new() {
            Bytes = ms.ToArray(),
        };
    }

    /// <summary>
    ///     The same text as <see cref="Create{T}" /> compresses, to compare cheaply
    /// </summary>
    public static string ToJson<T>(T data)
    {
        using var sw = new StringWriter();
        using var jw = new JsonTextWriter(sw);
        _serializer.Serialize(jw, data, typeof(T));
        jw.Flush();
        return sw.ToString();
    }

    public static LZ4Bytes CreateFromFile(string filePath)
    {
        using var input = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        // fast mode: high compression froze the main thread at each join (~0.4 s on a small map, empload 2026-10-27).
        // Maps are ~40% bigger, which SteamNetPeer sends in pieces (NetFragments)
        using var lz4 = new LZ4Stream(ms, CompressionMode.Compress);

        input.CopyTo(lz4);
        lz4.Flush();

        return new() {
            Bytes = ms.ToArray(),
        };
    }

    public static LZ4Bytes CreateFromBytes(byte[] input)
    {
        using var ms = new MemoryStream();
        using var lz4 = new LZ4Stream(ms, CompressionMode.Compress);

        lz4.Write(input, 0, input.Length);
        lz4.Flush();

        return new() {
            Bytes = ms.ToArray(),
        };
    }

    public T Decompress<T>()
    {
        using var input = new MemoryStream(Bytes);
        using var lz4 = new CappedStream(new LZ4Stream(input, CompressionMode.Decompress));
        using var sr = new StreamReader(lz4, Encoding.UTF8);
        using var jr = new JsonTextReader(sr);

        return _deserializer.Deserialize<T>(jr)!;
    }

    private static JsonSerializer CreateDeserializer()
    {
        var deserializer = JsonSerializer.Create(GameIOContext.Settings);
        deserializer.SerializationBinder = NetTypeBinder.Instance;
        return deserializer;
    }

    public string DecompressToString()
    {
        using var input = new MemoryStream(Bytes);
        using var lz4 = new CappedStream(new LZ4Stream(input, CompressionMode.Decompress));
        using var sr = new StreamReader(lz4, Encoding.UTF8);

        return sr.ReadToEnd();
    }

    public byte[] DecompressToBytes()
    {
        using var input = new MemoryStream(Bytes);
        using var lz4 = new CappedStream(new LZ4Stream(input, CompressionMode.Decompress));
        using var ms = new MemoryStream();

        lz4.CopyTo(ms);

        return ms.ToArray();
    }

    public void DecompressToStream(Stream output)
    {
        using var input = new MemoryStream(Bytes);
        using var lz4 = new CappedStream(new LZ4Stream(input, CompressionMode.Decompress));

        lz4.CopyTo(output);
    }

    public void DecompressToFile(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var fs = File.Create(filePath);

        DecompressToStream(fs);
    }

    private sealed class CappedStream(Stream inner) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > MaxDecompressed) {
                throw new InvalidDataException($"Decompressed data over {MaxDecompressed} bytes");
            }

            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
