using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using MessagePack;

namespace ElinTogether.Net.Steam;

/// <summary>
///     A message whose root array holds one list, assembled from entries serialized once each: the same bytes as
///     <see cref="SteamNetSerializer.Serialize{T}" /> of the whole, without serializing an entry again for every player
///     it goes to (host box "Large crowds": each player is sent its own part of the same entries)
/// </summary>
internal sealed class ListSplice
{
    private const int HeaderSize = sizeof(uint);
    private const byte EmptyArray = 0x90;

    private readonly byte[] _prefix;
    private readonly byte[] _suffix;

    private ListSplice(byte[] prefix, byte[] suffix)
    {
        _prefix = prefix;
        _suffix = suffix;
    }

    /// <summary>
    ///     The message itself with no entry
    /// </summary>
    internal byte[] Empty { get; private init; } = [];

    /// <param name="template">A whole message (type header included) whose root field <paramref name="field" /> is an empty list</param>
    internal static ListSplice Of(byte[] template, int field)
    {
        var reader = new MessagePackReader(new ReadOnlyMemory<byte>(template, HeaderSize, template.Length - HeaderSize));
        if (reader.ReadArrayHeader() <= field) {
            throw new InvalidOperationException($"no field {field} in the template");
        }

        for (var i = 0; i < field; i++) {
            reader.Skip();
        }

        var at = HeaderSize + (int)reader.Consumed;
        if (template[at] != EmptyArray) {
            throw new InvalidOperationException($"field {field} of the template is not an empty list");
        }

        var prefix = new byte[at];
        Buffer.BlockCopy(template, 0, prefix, 0, at);
        var suffix = new byte[template.Length - at - 1];
        Buffer.BlockCopy(template, at + 1, suffix, 0, suffix.Length);
        return new(prefix, suffix) { Empty = template };
    }

    /// <summary>
    ///     The message with these entries (each one serialized alone, <see cref="SteamNetSerializer.SerializePart{T}" />)
    /// </summary>
    internal byte[] Build(List<byte[]> parts)
    {
        if (parts.Count == 0) {
            return Empty;
        }

        var size = _prefix.Length + ArrayHeaderSize(parts.Count) + _suffix.Length;
        foreach (var part in parts) {
            size += part.Length;
        }

        var bytes = new byte[size];
        Buffer.BlockCopy(_prefix, 0, bytes, 0, _prefix.Length);
        var at = WriteArrayHeader(bytes, _prefix.Length, parts.Count);
        foreach (var part in parts) {
            Buffer.BlockCopy(part, 0, bytes, at, part.Length);
            at += part.Length;
        }

        Buffer.BlockCopy(_suffix, 0, bytes, at, _suffix.Length);
        return bytes;
    }

    private static int ArrayHeaderSize(int count)
    {
        return count < 16 ? 1 : count <= ushort.MaxValue ? 3 : 5;
    }

    private static int WriteArrayHeader(byte[] bytes, int at, int count)
    {
        if (count < 16) {
            bytes[at] = (byte)(EmptyArray | count);
            return at + 1;
        }

        if (count <= ushort.MaxValue) {
            bytes[at] = 0xdc;
            bytes[at + 1] = (byte)(count >> 8);
            bytes[at + 2] = (byte)count;
            return at + 3;
        }

        bytes[at] = 0xdd;
        bytes[at + 1] = (byte)(count >> 24);
        bytes[at + 2] = (byte)(count >> 16);
        bytes[at + 3] = (byte)(count >> 8);
        bytes[at + 4] = (byte)count;
        return at + 5;
    }

    // the first assembled messages of each kind are checked against the serializer's own: any difference and that
    // kind is serialized whole again for the rest of the session
    private const int Checks = 16;

    internal sealed class Guard(string what)
    {
        private int _checked;
        internal bool Broken { get; private set; }
        internal bool WantsCheck => !Broken && _checked < Checks;

        internal void Check(byte[] built, byte[] reference)
        {
            _checked++;
            if (built.AsSpan().SequenceEqual(reference)) {
                return;
            }

            Broken = true;
            EmpLog.Warning("Assembled {What} message differs from the serializer's ({Built} vs {Reference} bytes): whole from now on",
                what, built.Length, reference.Length);
        }

        internal void Fail(Exception ex)
        {
            Broken = true;
            EmpLog.Warning(ex, "Assembling a {What} message failed: whole from now on", what);
        }
    }
}

/// <summary>
///     Messages to many players that are often the same: one byte array per distinct list of entry indices
/// </summary>
internal sealed class SameListBytes
{
    private readonly Dictionary<int, List<(int[] Indices, byte[] Bytes)>> _byHash = [];

    internal void Clear()
    {
        _byHash.Clear();
    }

    internal byte[]? Find(List<int> indices, out int hash)
    {
        hash = Hash(indices);
        if (!_byHash.TryGetValue(hash, out var known)) {
            return null;
        }

        foreach (var (seen, bytes) in known) {
            if (Same(seen, indices)) {
                return bytes;
            }
        }

        return null;
    }

    internal void Add(int hash, List<int> indices, byte[] bytes)
    {
        if (!_byHash.TryGetValue(hash, out var known)) {
            known = _byHash[hash] = [];
        }

        known.Add((indices.ToArray(), bytes));
    }

    private static int Hash(List<int> indices)
    {
        var hash = indices.Count;
        foreach (var i in indices) {
            hash = unchecked(hash * 31 + i);
        }

        return hash;
    }

    private static bool Same(int[] seen, List<int> indices)
    {
        if (seen.Length != indices.Count) {
            return false;
        }

        for (var i = 0; i < seen.Length; i++) {
            if (seen[i] != indices[i]) {
                return false;
            }
        }

        return true;
    }
}
