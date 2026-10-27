// Wire sizes of the mod's hot messages with the mod's own formatters (the built ElinTogether.dll), against the shape
// they had before the efficiency audit of 2026-10-27, written by hand as the old formatters wrote it.
// Usage: dotnet run -c Release [-- players npcs]
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using ElinTogether.Models;
using MessagePack;
using MessagePack.Resolvers;

var managed = Environment.GetEnvironmentVariable("ElinGamePath") is { Length: > 0 } game
    ? Path.Combine(game, "Elin_Data", "Managed")
    : @"C:\Program Files (x86)\Steam\steamapps\common\Elin\Elin_Data\Managed";
var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_out"));
AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
    // the framework's own (the mod is built against newer reference versions of these)
    if (new AssemblyName(e.Name).Name is { } simple && (simple.StartsWith("System.") || simple == "netstandard")) {
        return Assembly.Load(simple);
    }

    var name = new AssemblyName(e.Name).Name + ".dll";
    foreach (var dir in new[] { outDir, managed }) {
        var path = Path.Combine(dir, name);
        if (File.Exists(path)) {
            return Assembly.LoadFrom(path);
        }
    }

    return null;
};

var players = args.Length > 0 ? int.Parse(args[0]) : 8;
var npcs = args.Length > 1 ? int.Parse(args[1]) : 40;
Bench.Run(players, npcs);

internal static class Bench
{
    // as SteamNetSerializer (the Steam resolver only knows UserData/LobbyData, not used by what is measured here)
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance);

    // a lived-in world: card uids in the hundreds of thousands, the zone uid a few hundred
    private const int ZoneUid = 412;

    public static void Run(int players, int npcs)
    {
        Console.WriteLine($"players {players}, other characters on the map {npcs}");

        Section("RemoteCard (a character)");
        Console.WriteLine($"   formatter in use: {StandardResolver.Instance.GetFormatter<RemoteCard>()?.GetType().Name}");
        var card = new RemoteCard { Uid = 734_211, Type = RemoteCard.CardType.Chara, Num = 1 };
        Compare(Old(OldCard(734_211, 1)), Size(card));
        RoundTrip(card);
        RoundTripOld(OldCard(734_211, 1));

        var charas = Charas(players, npcs);

        Section($"WorldStateSnapshot, {charas.Count} characters on the host map (5 Hz to every peer on it)");
        var before = Old(OldSnapshot(charas));
        var after = Size(Snapshot(charas));
        Compare(before, after);
        Console.WriteLine($"   per peer at 5 Hz: {before * 5 / 1024.0:0.0} -> {after * 5 / 1024.0:0.0} KB/s; " +
                          $"host upload with {players - 1} guests: {before * 5 * (players - 1) / 1024.0:0.0} -> " +
                          $"{after * 5 * (players - 1) / 1024.0:0.0} KB/s");

        Section("WorldStateDeltaList, one step of every player (CharaMoveDelta)");
        Compare(Old(OldMoves(charas.Take(players))), Size(Moves(charas.Take(players))));
    }

    // ---- what a map holds: players first, then the others

    internal sealed record Chara(int Uid, int X, int Z, int Hp, bool Player, int Master, int? Hunger);

    internal static List<Chara> Charas(int players, int npcs)
    {
        var rng = new Random(7);
        var list = new List<Chara>();
        for (var i = 0; i < players + npcs; i++) {
            var player = i < players;
            list.Add(new(rng.Next(100_000, 2_000_000), rng.Next(10, 120), rng.Next(10, 120),
                player ? rng.Next(200, 3000) : rng.Next(20, 800), player, 0, null));
        }

        return list;
    }

    internal static WorldStateSnapshot Snapshot(IEnumerable<Chara> charas) => new() {
        ServerTick = 123_456,
        GameDate = [2026, 7, 14, 13, 27, 0],
        CharaSnapshots = [..charas.Select(c => new CharaStateSnapshot {
            Owner = new() { Uid = c.Uid, Type = RemoteCard.CardType.Chara, Num = 1 },
            Pos = new() { X = c.X, Z = c.Z },
            CurrentZoneUid = ZoneUid,
            Hp = c.Hp,
            IsDead = false,
            Hostility = c.Player ? Hostility.Ally : Hostility.Neutral,
            OriginalHostility = c.Player ? Hostility.Ally : Hostility.Neutral,
            UidMaster = c.Master,
            MinionType = MinionType.Default,
            Hunger = c.Hunger,
        })],
        GlobalUidNext = 2_345_678,
        SharedSpeed = 0,
    };

    internal static WorldStateDeltaList Moves(IEnumerable<Chara> charas) => new() {
        DeltaList = [..charas.Select(c => (ElinDelta)new CharaMoveDelta {
            Owner = new() { Uid = c.Uid, Type = RemoteCard.CardType.Chara, Num = 1 },
            Pos = new() { X = c.X + 1, Z = c.Z },
            ZoneUid = ZoneUid,
        })],
    };

    // ---- the old shapes, field by field as the generated formatters wrote them

    internal static object?[] OldCard(int uid, byte type, int num = 1) => [uid, type, null, null, num];

    // CharaStateSnapshot: Owner, Pos, CurrentZoneUid, Hp, State, IsDead, Hostility, OriginalHostility, UidMaster,
    // MinionType, Hunger (State was already gone from the host's snapshots, 3430980)
    internal static object?[] OldChara(Chara c)
    {
        var hostility = (int)(c.Player ? Hostility.Ally : Hostility.Neutral);
        return [OldCard(c.Uid, 1), new object[] { c.X, c.Z }, ZoneUid, c.Hp, null, false, hostility, hostility, c.Master,
            (int)MinionType.Default, c.Hunger];
    }

    internal static object?[] OldSnapshot(IEnumerable<Chara> charas) =>
        [123_456, new object[] { 2026, 7, 14, 13, 27, 0 }, charas.Select(OldChara).ToArray<object?>(), 2_345_678, 0];

    // union [id, [Owner, Pos, MoveType, ZoneUid]]
    internal static object?[] OldMoves(IEnumerable<Chara> charas) =>
        [charas.Select(c => (object?)new object?[] { 200, new object?[] { OldCard(c.Uid, 1), new object[] { c.X + 1, c.Z }, 0, ZoneUid } }).ToArray()];

    // ---- helpers

    internal static int Old(object? shape) => OldBytes(shape).Length;

    // as the generated formatters write: integers in their shortest form, floats as float32
    internal static byte[] OldBytes(object? shape)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        WriteOld(ref writer, shape);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteOld(ref MessagePackWriter writer, object? value)
    {
        switch (value) {
            case null: writer.WriteNil(); break;
            case bool b: writer.Write(b); break;
            case byte b: writer.Write(b); break;
            case int i: writer.Write(i); break;
            case long l: writer.Write(l); break;
            case ulong u: writer.Write(u); break;
            case float f: writer.Write(f); break;
            case string str: writer.Write(str); break;
            case object?[] array:
                writer.WriteArrayHeader(array.Length);
                foreach (var item in array) {
                    WriteOld(ref writer, item);
                }

                break;
            case Dictionary<int, int> map:
                writer.WriteMapHeader(map.Count);
                foreach (var (k, v) in map) {
                    writer.Write(k);
                    writer.Write(v);
                }

                break;
            default: throw new NotSupportedException(value.GetType().Name);
        }
    }

    internal static int Size<T>(T value) => MessagePackSerializer.Serialize(value, Options).Length;

    internal static void Section(string name) => Console.WriteLine($"\n== {name}");

    internal static void Compare(int before, int after, string unit = "bytes")
    {
        Console.WriteLine($"   before {before,7} {unit}   after {after,7} {unit}   {(after - before) * 100.0 / before,6:+0.0;-0.0}%");
    }

    private static void RoundTrip(RemoteCard card)
    {
        var back = MessagePackSerializer.Deserialize<RemoteCard>(MessagePackSerializer.Serialize(card, Options), Options)!;
        Debug.Assert(back.Uid == card.Uid && back.Type == card.Type && back.Num == card.Num);
        Console.WriteLine($"   round trip: {back.Uid} {back.Type} x{back.Num}");
    }

    private static void RoundTripOld(object?[] shape)
    {
        var back = MessagePackSerializer.Deserialize<RemoteCard>(OldBytes(shape), Options)!;
        Console.WriteLine($"   old array read by the new formatter: {back.Uid} {back.Type} x{back.Num}");
    }
}
