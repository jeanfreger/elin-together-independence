using System;
using System.Buffers;
using ElinTogether.Helper;
using ElinTogether.Helper.Steam;
using MessagePack;
using MessagePack.Resolvers;

namespace ElinTogether.Net.Steam;

internal class SteamNetSerializer : ISteamNetSerializer
{
    private const int HeaderSize = sizeof(uint);

    // a bounded depth (security audit 2026-10-27): delta lists nest (CharaBuildDelta.DeltaList), and a peer's packet
    // nested a hundred thousand times overflowed the stack, which nothing catches: the host's game was gone. Real
    // messages nest a dozen levels at most
    private static readonly MessagePackSerializerOptions _options =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                SteamDataResolver.Default,
                StandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.TrustedData.WithMaximumObjectGraphDepth(96));

    // the header and the payload written into one buffer kept per thread, then one copy of the exact size: the
    // payload was serialized into MessagePack's own buffer, copied to an array, then copied again behind the header
    // (efficiency audit 2026-10-27)
    [ThreadStatic]
    private static GrowingBuffer? _buffer;

    public byte[] Serialize<T>(T obj)
    {
        var buffer = _buffer ??= new();
        buffer.Reset();

        var header = buffer.GetSpan(HeaderSize);
        var typeHash = SteamNetTypeRegistry.GetHash<T>();
        header[0] = (byte)typeHash;
        header[1] = (byte)(typeHash >> 8);
        header[2] = (byte)(typeHash >> 16);
        header[3] = (byte)(typeHash >> 24);
        buffer.Advance(HeaderSize);

        MessagePackSerializer.Serialize(buffer, obj, _options);

        var bytes = buffer.ToArray();
        buffer.Shrink();
        return bytes;
    }

    /// <summary>
    ///     One entry of a list alone, no type header: a piece of a message assembled by <see cref="ListSplice" />
    /// </summary>
    internal static byte[] SerializePart<T>(T obj)
    {
        return MessagePackSerializer.Serialize(obj, _options);
    }

    public object Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        return MessagePackSerializer.Deserialize(type, data, _options)!;
    }

    /// <summary>
    ///     The message without its header, read in place
    /// </summary>
    public static ReadOnlyMemory<byte> Payload(byte[] data)
    {
        return new(data, HeaderSize, data.Length - HeaderSize);
    }

    private sealed class GrowingBuffer : IBufferWriter<byte>
    {
        // a world copy grows it to megabytes: not kept past this
        private const int KeptSize = 256 * 1024;

        private byte[] _array = new byte[4096];
        private int _written;

        public void Reset()
        {
            _written = 0;
        }

        public void Shrink()
        {
            if (_array.Length > KeptSize) {
                _array = new byte[4096];
            }
        }

        public byte[] ToArray()
        {
            var bytes = new byte[_written];
            Buffer.BlockCopy(_array, 0, bytes, 0, _written);
            return bytes;
        }

        public void Advance(int count)
        {
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _array.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _array.AsSpan(_written);
        }

        private void Ensure(int sizeHint)
        {
            var needed = _written + Math.Max(sizeHint, 1);
            if (needed <= _array.Length) {
                return;
            }

            Array.Resize(ref _array, Math.Max(needed, _array.Length * 2));
        }
    }

    public static (uint typeHash, byte[] payload) ExtractTypeAndPayload(byte[] data)
    {
        var typeHash = BitConverter.ToUInt32(data, 0);
        var payload = new byte[data.Length - HeaderSize];
        Buffer.BlockCopy(data, HeaderSize, payload, 0, payload.Length);

        return (typeHash, payload);
    }
}