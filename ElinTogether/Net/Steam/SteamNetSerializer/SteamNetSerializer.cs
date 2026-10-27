using System;
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

    public byte[] Serialize<T>(T obj)
    {
        var typeHash = SteamNetTypeRegistry.GetHash<T>();
        var payload = MessagePackSerializer.Serialize(obj, _options);

        var bytes = new byte[HeaderSize + payload.Length];

        FastBitConverter.GetBytes(bytes, 0, typeHash);
        Buffer.BlockCopy(payload, 0, bytes, HeaderSize, payload.Length);

        return bytes;
    }

    public object Deserialize(byte[] data, Type type)
    {
        return MessagePackSerializer.Deserialize(type, data, _options)!;
    }

    public static (uint typeHash, byte[] payload) ExtractTypeAndPayload(byte[] data)
    {
        var typeHash = BitConverter.ToUInt32(data, 0);
        var payload = new byte[data.Length - HeaderSize];
        Buffer.BlockCopy(data, HeaderSize, payload, 0, payload.Length);

        return (typeHash, payload);
    }
}