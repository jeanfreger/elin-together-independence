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
    // as SteamNetSerializer
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(CompositeResolver.Create(
            ElinTogether.Helper.Steam.SteamDataResolver.Default, StandardResolver.Instance));

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

        Section($"SessionPlayersSnapshot, {players} players on one map (every 2 s to every peer)");
        Compare(Old(OldPlayers(players)), Size(Players(players)));

        Section("Game time on an idle map (players on their own clock, the default)");
        // a WorldStateDeltaList with one GameDelta, sent at every 50 Hz flush (the host makes one per frame), plus
        // the 4-byte type hash; now only while the host's world fast-forwards (5 Hz) and once when it stops
        var idle = 4 + Size(new WorldStateDeltaList { DeltaList = [new GameDelta { Delta = 0.0167f, Turbo = 0f }] });
        Console.WriteLine($"   one message {idle} bytes + Steam's per-message overhead; before: up to 50 messages/s " +
                          $"per peer ({idle * 50 / 1024.0:0.0} KB/s, {50 * (players - 1)} sends/s for the host), after: 0 " +
                          "when nothing happens, 5/s while fast-forwarding");
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
            // the snapshot's own zone, see WorldStateSnapshot.ZoneUid
            CurrentZoneUid = 0,
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
        ZoneUid = ZoneUid,
    };

    internal static WorldStateDeltaList Moves(IEnumerable<Chara> charas) => new() {
        DeltaList = [..charas.Select(c => (ElinDelta)new CharaMoveDelta {
            Owner = new() { Uid = c.Uid, Type = RemoteCard.CardType.Chara, Num = 1 },
            Pos = new() { X = c.X + 1, Z = c.Z },
            ZoneUid = ZoneUid,
        })],
    };

    internal static ElinTogether.Models.SessionPlayersSnapshot Players(int players) => new() {
        Current = [..Enumerable.Range(0, players).Select(i => new ElinTogether.Net.NetPeerState {
            Index = i,
            User = 76561198000000000UL + (ulong)i,
            CharaUid = 1000 + i,
            Speed = 70 + i,
            LastAct = 3,
            LastReceivedTick = 123_456,
            LastPingMs = 40 + i,
            AvgPingMsWire = 41 + i,
            ConnectionQualityLocalWire = 99,
            ConnectionQualityRemoteWire = 99,
        })],
        Sums = new() {
            ZoneUid = ZoneUid, Charas = 48, CharaMix = 123456789, Things = 900, ThingMix = -987654321,
            Bags = Enumerable.Range(0, players).ToDictionary(i => 1000 + i, i => 1_000_000_000 + i), Locals = 40,
            LocalMix = 55555555,
        },
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

    // NetPeerState with float ping and qualities; MapSums
    internal static object?[] OldPlayers(int players) => [
        Enumerable.Range(0, players).Select(i => (object?)new object?[] {
            i, 76561198000000000UL + (ulong)i, 1000 + i, 70 + i, 3, 123_456, 40 + i, 41.37f + i, 0.987f, 0.993f, false,
            false, false,
        }).ToArray(),
        new object?[] {
            ZoneUid, 48, 123456789, 900, -987654321,
            Enumerable.Range(0, players).ToDictionary(i => 1000 + i, i => 1_000_000_000 + i), 40, 55555555,
        },
    ];

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
