using System;
using System.Collections.Generic;
using System.IO;

namespace ElinTogether.Net;

/// <summary>
///     A serialized message too big for one transport message (Steam refuses 512 KB and more), cut into
///     numbered pieces. Nothing of Steam or of the game in here: dev/_tools/chunk_check runs it alone
/// </summary>
internal static class NetFragments
{
    /// <summary>
    ///     Pieces of big messages received so far: a world arriving piece by piece moves it, the small snapshots a
    ///     departed guest still gets every tick do not (the rejoin watchdog reads it)
    /// </summary>
    public static long PiecesReceived;

    /// <summary>
    ///     Stands where a whole message has its type hash
    /// </summary>
    public const uint Magic = 0xE7F2A61D;

    // magic, message id, piece index, piece count, message size
    public const int HeaderSize = 5 * sizeof(int);

    /// <summary>
    ///     A quarter of Steam's default send queue (512 KB): a piece gets in as soon as the queue is under
    ///     three quarters, and is far under the size of a message Steam accepts
    /// </summary>
    public const int PieceSize = 128 * 1024;

    /// <summary>
    ///     Nothing bigger is sent nor accepted; the receiver only holds what really arrived, not what a header says
    /// </summary>
    public const int MaxMessageSize = 64 * 1024 * 1024;

    public static bool IsFragment(byte[] bytes)
    {
        return bytes.Length >= HeaderSize && ReadInt(bytes, 0) == unchecked((int)Magic);
    }

    public static List<byte[]> Split(byte[] message, int id, int pieceSize = PieceSize)
    {
        var count = (message.Length + pieceSize - 1) / pieceSize;
        var pieces = new List<byte[]>(count);

        for (var index = 0; index < count; index++) {
            var offset = index * pieceSize;
            var size = Math.Min(pieceSize, message.Length - offset);
            var piece = new byte[HeaderSize + size];

            WriteInt(piece, 0, unchecked((int)Magic));
            WriteInt(piece, 4, id);
            WriteInt(piece, 8, index);
            WriteInt(piece, 12, count);
            WriteInt(piece, 16, message.Length);
            Buffer.BlockCopy(message, offset, piece, HeaderSize, size);

            pieces.Add(piece);
        }

        return pieces;
    }

    internal static int ReadInt(byte[] bytes, int at)
    {
        return bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);
    }

    private static void WriteInt(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
        bytes[at + 2] = (byte)(value >> 16);
        bytes[at + 3] = (byte)(value >> 24);
    }
}

/// <summary>
///     The receiving side of one connection: pieces come reliable and in order, one message at a time
/// </summary>
internal sealed class NetFragmentAssembler
{
    // the pieces as they came (header included): memory follows what really arrived, not what a header claims
    private List<byte[]>? _pieces;
    private int _count;
    private int _id;
    private int _next;
    private int _offset;
    private int _total;

    public bool IsPending => _pieces is not null;

    /// <summary>
    ///     The whole message once its last piece is in, null before that. A piece that does not fit throws,
    ///     and what was gathered of its message is dropped
    /// </summary>
    public byte[]? Add(byte[] piece)
    {
        if (!NetFragments.IsFragment(piece)) {
            throw Bad("not a piece");
        }

        var id = NetFragments.ReadInt(piece, 4);
        var index = NetFragments.ReadInt(piece, 8);
        var count = NetFragments.ReadInt(piece, 12);
        var total = NetFragments.ReadInt(piece, 16);
        var size = piece.Length - NetFragments.HeaderSize;

        if (index == 0) {
            var interrupted = _pieces is not null;
            Reset();

            if (count < 1 || total < 1 || total > NetFragments.MaxMessageSize || count > total) {
                throw Bad($"header of {total} bytes in {count} pieces");
            }

            _pieces = [];
            _id = id;
            _count = count;
            _total = total;

            if (interrupted) {
                // the new message is kept, the caller reads InterruptedMessages to tell the lost one
                InterruptedMessages++;
            }
        } else if (_pieces is null || id != _id || index != _next || count != _count || total != _total) {
            throw Bad($"piece {index}/{count} of message {id}, expected {_next}/{_count} of message {_id}");
        }

        var last = index == _count - 1;
        if (size < 1 || size > _total - _offset || (last && size != _total - _offset)) {
            throw Bad($"piece {index}/{count} of {size} bytes at {_offset}/{_total}");
        }

        _pieces!.Add(piece);
        _offset += size;
        _next = index + 1;

        if (!last) {
            return null;
        }

        var message = new byte[_total];
        var at = 0;
        foreach (var kept in _pieces) {
            var length = kept.Length - NetFragments.HeaderSize;
            Buffer.BlockCopy(kept, NetFragments.HeaderSize, message, at, length);
            at += length;
        }

        Reset();
        return message;
    }

    /// <summary>
    ///     Messages given up because another one started before their last piece
    /// </summary>
    public int InterruptedMessages { get; private set; }

    public void Reset()
    {
        _pieces = null;
        _id = _count = _next = _offset = _total = 0;
    }

    private InvalidDataException Bad(string what)
    {
        Reset();
        return new($"Bad message piece: {what}");
    }
}
