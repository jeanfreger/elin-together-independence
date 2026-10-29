using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ElinTogether.Helper.Extensions;
using LZ4;
using Newtonsoft.Json;

namespace ElinTogether.Helper;

/// <summary>
///     Joining with the character of one of the player's own solo saves: read beside the running game, never
///     loaded and never written. Only the character travels (stats, feats, looks, equipment, bag, gold), with
///     its fame and karma; companions, mount, base, quests and bank stay in the solo save
/// </summary>
internal static class CharaImport
{
    internal const int MaxSaves = 8;

    /// <summary>
    ///     Card string kept on the copy at the host: which save it came from, one copy of each per player
    /// </summary>
    internal const string SourceKey = "emp_import";

    internal record Imported(Chara Chara, int Fame, int Karma, string Source);

    /// <summary>
    ///     The most recent saves of this player the game can read, local ones and Steam Cloud ones
    /// </summary>
    internal static List<GameIndex> Saves()
    {
        var saves = new List<GameIndex>();
        foreach (var root in new[] { CorePath.RootSave, CorePath.RootSaveCloud }) {
            try {
                saves.AddRange(GameIO.GetGameList(root).Where(index => !index.isBackup && DataFile(index) is not null && Readable(index)));
            } catch (Exception ex) {
                EmpLog.Warning(ex, "Could not list the saves of {Root}", root);
            }
        }

        // the last ones written first: the date a save carries is the one of its world, a copy keeps it
        return saves
            .OrderByDescending(index => File.GetLastWriteTimeUtc(DataFile(index)!))
            .Take(MaxSaves)
            .ToList();
    }

    /// <summary>
    ///     Where the game of a save is: its game.txt, or the archive a Steam Cloud save is kept in between two
    ///     sessions (the game unpacks it in place when it loads the save, which this never does)
    /// </summary>
    private static string? DataFile(GameIndex index)
    {
        var plain = Path.Combine(index.path, "game.txt");
        if (File.Exists(plain)) {
            return plain;
        }

        var packed = Path.Combine(index.path, "cloud.zip");
        return File.Exists(packed) ? packed : null;
    }

    internal static bool IsCloud(GameIndex index)
    {
        return Path.GetFullPath(index.path).StartsWith(Path.GetFullPath(CorePath.RootSaveCloud), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Readable(GameIndex index)
    {
        try {
            return EClass.core.version.IsSaveCompatible(index.version);
        } catch {
            return false;
        }
    }

    internal static string Label(GameIndex index)
    {
        try {
            return $"{index.pcName} - {index.zoneName}, {index.RealDate} ({Id(index)})";
        } catch {
            // an index without its dates
            return $"{index.pcName} ({Id(index)})";
        }
    }

    private static string Id(GameIndex index)
    {
        return IsCloud(index) ? $"Steam Cloud {index.id}" : index.id;
    }

    /// <summary>
    ///     Reads the player character out of a save. GameIO.LoadGame without what makes it the current game:
    ///     the file is opened for reading only, and the statics a deserialized Game overwrites are put back
    /// </summary>
    internal static Imported? Read(GameIndex index)
    {
        var instance = Game.Instance;
        var fallback = new Dictionary<string, string>(ModUtil.fallbackTypes);
        // the list holds the saves made of another game's world too (world_*, PeerMapPatch): any type their text
        // names would be made (security audit 2026-10-29)
        var binder = GameIO.jsReadGame.SerializationBinder;
        if (Patches.PeerMapPatch.IsPeerSave(index.id, index.path)) {
            GameIO.jsReadGame.SerializationBinder = Models.NetTypeBinder.Lenient;
        }

        try {
            foreach (var (type, other) in index.fallbackTypes) {
                ModUtil.fallbackTypes[type] = other;
            }

            var save = JsonConvert.DeserializeObject<Game>(ReadText(index), GameIO.jsReadGame);
            if (save?.player is null || save.cards.globalCharas.Find(save.player.uidChara) is not { } chara) {
                EmpLog.Warning("Save {Id} has no player character", index.id);
                return null;
            }

            Detach(chara);
            return new(chara, save.player.fame, save.player.karma, $"{Id(index)}/{save.player.uidChara}/{chara.Name}");
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not read the character of save {Id}", index.id);
            return null;
        } finally {
            GameIO.jsReadGame.SerializationBinder = binder;
            Game.Instance = instance;
            ModUtil.fallbackTypes.Clear();
            foreach (var (type, other) in fallback) {
                ModUtil.fallbackTypes[type] = other;
            }
        }
    }

    private static string ReadText(GameIndex index)
    {
        var path = DataFile(index) ?? throw new FileNotFoundException("no game.txt nor cloud.zip", index.path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var entry = zip.GetEntry("game.txt") ?? throw new FileNotFoundException("no game.txt in cloud.zip", path);
            using var packed = entry.Open();
            packed.CopyTo(bytes);
        } else {
            file.CopyTo(bytes);
        }

        bytes.Position = 0;
        if (!IsCompressed(bytes)) {
            using var plain = new StreamReader(bytes, Encoding.UTF8);
            return plain.ReadToEnd();
        }

        using var lz4 = new LZ4Stream(bytes, CompressionMode.Decompress);
        using var reader = new StreamReader(lz4);
        return reader.ReadToEnd();
    }

    /// <summary>
    ///     IO.IsCompressed on bytes already read: a save that does not start like JSON is LZ4
    /// </summary>
    private static bool IsCompressed(MemoryStream bytes)
    {
        try {
            int b;
            while ((b = bytes.ReadByte()) != -1) {
                if (b is 9 or 10 or 13 or 32) {
                    continue;
                }

                return !(b is 34 or 45 or (>= 48 and <= 57) or 91 or 102 or 110 or 116 or 123);
            }

            return false;
        } finally {
            bytes.Position = 0;
        }
    }

    /// <summary>
    ///     Cuts what ties the character to its own world, so that only the character is sent: fields only, there
    ///     is no game around it here
    /// </summary>
    internal static void Detach(Chara chara)
    {
        chara.party = null;
        chara.ride = null;
        chara.parasite = null;
        chara.host = null;
        chara.held = null;
        chara.quest = null;
        chara.enemy = null;
        chara.global = null;
        // currentZone and homeZone: uids of the solo world, another zone in the host's
        chara._cints[1] = 0;
        chara._cints[2] = 0;
        chara.c_uidMaster = 0;
    }

    /// <summary>
    ///     Host side: the copy becomes a character of this world, with uids of this world
    /// </summary>
    internal static void Adopt(Chara chara, string source)
    {
        var before = chara.uid;

        chara.SetBool(CINT.IsPC, false);
        chara.currentZone = null;
        chara.homeZone = null;

        // debts of the other world
        foreach (var bill in chara.things.Flatten().Where(t => t.trait is TraitBill).ToList()) {
            bill.Destroy();
        }

        EClass.game.cards.AssignUIDRecursive(chara);

        // what is bound to this character stays bound to it under its new uid
        foreach (var thing in chara.things.Flatten()) {
            if (thing.c_uidAttune == before) {
                thing.c_uidAttune = chara.uid;
            }
        }

        if (chara.isDead || chara.hp <= 0) {
            chara.isDead = false;
            chara.hp = chara.MaxHP;
        }

        chara.SetStr(SourceKey, source);
    }
}
