using System;

namespace ElinTogether.Net.Steam;

public interface ISteamNetSerializer
{
    public byte[] Serialize<T>(T obj);
    public object Deserialize(ReadOnlyMemory<byte> data, Type type);
}