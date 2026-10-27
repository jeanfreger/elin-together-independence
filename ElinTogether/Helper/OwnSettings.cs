using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElinTogether.Helper.Extensions;
using ElinTogether.Models;
using ElinTogether.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     What is a player's own but lives in the game it plays (Game.config: auto combat type, tactics, zoom;
///     Player: window places, widgets, hotbars, sort, trackers) or only on its screen (the floating windows: bag,
///     open containers it carries, abilities). <br />
///     A player alone keeps all of it from map to map. A client gets a whole new game each time the host sends its
///     world (ElinNetClient.OnSaveDataProbe): the windows are closed with the old one, and the settings of the
///     new one are the host's. Noted here when a game goes away, put into the next one, the windows opened again
///     once the map is in place. Also written to a file of this machine, per world and character, never in a
///     save: a player who joins again finds its own
/// </summary>
internal static class OwnSettings
{
    private static Kept? _kept;
    private static bool _reopen;

    private static string Root => field ??= Path.Combine(Application.persistentDataPath, "ElinMP",
#if DEBUG
        // test instances on one machine share this folder
        EmpConfig.Dev.Identity.Value > 0 ? $"OwnSettings_{EmpConfig.Dev.Identity.Value}" :
#endif
        "OwnSettings");

    /// <summary>
    ///     Before the game of a client goes away (a world received, see ElinNetClient.OnSaveDataProbe, or the game
    ///     left, see OwnSettingsPatch)
    /// </summary>
    internal static void Keep()
    {
        // a second world before the first one stood on a map: what was kept for the first is still good
        if (!EClass.core.IsGameStarted || Game.id != ResourceFetch.EmpSaveId || EClass.pc is not { } pc ||
            EClass.player is not { } player || ReferenceEquals(_kept?.From, player)) {
            return;
        }

        try {
            // as Game.Save writes them down: place and size of the floating windows, place of the widgets
            player.OnBeforeSave();
            EClass.ui.widgets.UpdateConfigs();

            var kept = new Kept {
                From = player,
                Seed = EClass.game.seed,
                Chara = pc.uid,
                Config = EClass.game.config,
                Pref = player.pref,
                Windows = player.dataWindow,
                AbilityConfig = player.layerAbilityConfig,
                MainWidgets = player.mainWidgets,
                SubWidgets = player.subWidgets,
                SubTheme = player.useSubWidgetTheme,
                AllyInv = player.windowAllyInv,
                ContainerCenter = player.openContainerCenter,
                Pick = player.dataPick,
                FavAbility = player.favAbility,
                PriorityActions = player.priorityActions,
                QuestTracker = player.questTracker,
                TrackedCategories = player.trackedCategories,
                TrackedCards = player.trackedCards,
                TrackedElements = player.trackedElements,
                LastRecipes = player.lastRecipes,
                FavMoongate = player.favMoongate,
                Cinema = player.cinemaConfig,
                HotbarPage = player.hotbarPage,
                LightMod = player.customLightMod,
                Memo = player.memo,
                Memo2 = player.memo2,
                Hoard = player.hoard,
                KnownCraft = player.knownCraft,
                Sketches = player.sketches,
                PlayedStories = player.flags.playedStories,
                AvailableStories = player.flags.availableStories,
                DoneBackers = player.doneBackers,
                KnownBGMs = player.knownBGMs,
                KnownSongs = player.knownSongs,
                HangIcons = player.hangIcons,
                SpawnZone = player.uidSpawnZone,
                LastTown = player.uidLastTown,
                Hotbars = player.hotbars,
                Flags = DialogFlagSync.OwnFlags(player),
                Deepest = player.stats.deepest,
                DeepestVoid = player.stats.deepestVoid,
                LastChuryu = player.stats.lastChuryu,
                SeedFortune = player.seedFortune,
                LuckyCoin = player.luckycoin,
                LastFelmeraReward = player.lastFelmeraReward,
                GiftJure = player.giftJure,
                FishArtifact = player.fishArtifact,
                SeedShrine = player.seedShrine,
                LittleSaved = player.little_saved,
                LittleDead = player.little_dead,
                Log = EClass.game.log,
                Digs = player.stats.digs,
                Deaths = player.stats.death,
                EarlyDeaths = player.stats.death2,
                Slept = player.stats.slept,
                Ability = EClass.game.altAbility && EClass.ui.IsAbilityOpen,
                ZoomOut = ActionMode.Adv?.zoomOut2 ?? false,
            };

            foreach (var thing in pc.things.Flatten()) {
                if (thing.c_windowSaveData is { } data) {
                    kept.Bags[thing.uid] = data;
                }
            }

            foreach (var layer in LayerInventory.listInv) {
                if (!layer.IsFloat) {
                    continue;
                }

                if (layer.mainInv) {
                    kept.Inventory = true;
                } else if (layer.GetPlayerContainer() is { } bag) {
                    kept.Open.Add(bag.uid);
                }
            }

            _kept = kept;

            // the file: the hotbars too (the spells laid on the belt were lost at each new session), without the
            // cards and zones their items point at, which would be written whole: only their numbers
            var held = new List<(HotItem Item, Card? Thing, Zone? Zone)>();
            Hot(kept.Hotbars, (item, bar, page, slot) => {
                switch (item) {
                    case HotItemThing { thing: not null } hot:
                        kept.HotRefs.Add([bar, page, slot, hot.thing.uid]);
                        held.Add((hot, hot.thing, null));
                        hot.thing = null;
                        break;
                    case HotItemFocusPos { zone: not null } focus:
                        kept.HotRefs.Add([bar, page, slot, focus.zone.uid]);
                        held.Add((focus, null, focus.zone));
                        focus.zone = null;
                        break;
                }
            });

            try {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(PathOf(kept.Seed, kept.Chara), LZ4Bytes.Create(kept).Bytes);
            } finally {
                foreach (var (item, thing, zone) in held) {
                    switch (item) {
                        case HotItemThing hot:
                            hot.thing = thing as Thing;
                            break;
                        case HotItemFocusPos focus:
                            focus.zone = zone;
                            break;
                    }
                }
            }
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not note this player's own settings before its game goes away");
        }
    }

    /// <summary>
    ///     The new game is in place, not started yet (before Game.OnGameInstantiated, which reads the widgets, and
    ///     Player.OnLoad, which reads dataWindow): the settings of this player go in, in place of the host's
    /// </summary>
    internal static void Carry(Chara chara)
    {
        _reopen = false;
        if (EClass.game?.player is not { } player) {
            return;
        }

        try {
            // nothing noted for this character in this world (first world of this session): the file
            var kept = _kept is { } noted && noted.Seed == EClass.game.seed && noted.Chara == chara.uid
                ? noted
                : Read(EClass.game.seed, chara.uid);
            _kept = kept;

            // (nothing noted: a first time in this world, its own counts start at zero, not at the host's)
            DialogFlagSync.SetOwnFlags(player, kept?.Flags);
            // the depth reached is this player's own (see BossWinDelta), not the host's
            player.stats.deepest = kept?.Deepest ?? 0;
            player.stats.deepestVoid = kept?.DeepestVoid ?? 0;
            // and the count of cats it brought along (hunt 5, H5-15): shown by the "nyan" key until the next
            // Player.UpdateNyan. (The "you have played N hours" count, Player.realHour, is static: per process, so
            // already the player's own)
            player.stats.lastChuryu = kept?.LastChuryu ?? 0;
            // so are its fortune rolls and lucky coins (see FortuneCountersDelta)
            player.seedFortune = kept?.SeedFortune ?? 0;
            player.luckycoin = kept?.LuckyCoin ?? 0;
            // and its own reward history (audit H5-07): the Felmera sketch rewards already claimed (counted against
            // its own sketches, which are kept above), the Jure gifts opened, the fishing artifact luck, the shrines
            // opened, the little ones saved and lost. A world fact stays the host's: the stats.* counts
            player.lastFelmeraReward = kept?.LastFelmeraReward ?? 0;
            player.giftJure = kept?.GiftJure ?? 0;
            player.fishArtifact = kept?.FishArtifact ?? 0;
            player.seedShrine = kept?.SeedShrine ?? 0;
            player.little_saved = kept?.LittleSaved ?? 0;
            player.little_dead = kept?.LittleDead ?? 0;
            // the journal tells about this player (council 2026-10-28, OwnJournal): its own message history, not the
            // host's last lines (a first time in this world: none yet)
            if (NetSession.Instance.Rules.OwnJournal) {
                EClass.game.log = kept?.Log ?? new MsgLog { id = "log" };
                // and its own records, counted by the game only for its own character: blocks dug (hall of fame),
                // deaths (a dialog reads them), nights slept (also the seed of the recipe it dreams of). Kills stay
                // the world's: the game counts the whole faction's, and a story quest reads them (QuestDefense)
                player.stats.digs = kept?.Digs ?? 0;
                player.stats.death = kept?.Deaths ?? 0;
                player.stats.death2 = kept?.EarlyDeaths ?? 0;
                player.stats.slept = kept?.Slept ?? 0;
            }

            if (kept is null) {
                return;
            }

            EClass.game.config = kept.Config ?? EClass.game.config;
            // built from the auto combat type of the game it was first asked in
            chara._tactics = null;

            player.pref = kept.Pref ?? player.pref;
            player.dataWindow = kept.Windows ?? player.dataWindow;
            player.layerAbilityConfig = kept.AbilityConfig ?? player.layerAbilityConfig;
            player.mainWidgets = kept.MainWidgets ?? player.mainWidgets;
            player.subWidgets = kept.SubWidgets ?? player.subWidgets;
            player.useSubWidgetTheme = kept.SubTheme;
            player.windowAllyInv = kept.AllyInv ?? player.windowAllyInv;
            player.openContainerCenter = kept.ContainerCenter;
            player.dataPick = kept.Pick ?? player.dataPick;
            player.favAbility = kept.FavAbility ?? player.favAbility;
            player.priorityActions = kept.PriorityActions ?? player.priorityActions;
            player.questTracker = kept.QuestTracker;
            player.trackedCategories = kept.TrackedCategories ?? player.trackedCategories;
            player.trackedCards = kept.TrackedCards ?? player.trackedCards;
            player.trackedElements = kept.TrackedElements ?? player.trackedElements;
            player.lastRecipes = kept.LastRecipes ?? player.lastRecipes;
            player.favMoongate = kept.FavMoongate ?? player.favMoongate;
            player.cinemaConfig = kept.Cinema ?? player.cinemaConfig;
            player.hotbarPage = kept.HotbarPage;
            player.customLightMod = kept.LightMod;
            player.memo = kept.Memo ?? "";
            player.memo2 = kept.Memo2 ?? "";
            // where this player revives (waystone, core zone, last town): the host's in a copy of its world
            player.uidSpawnZone = kept.SpawnZone;
            player.uidLastTown = kept.LastTown;

            // what this player collected or learned (the hoard, crafts and sketches seen, tunes heard and played), not
            // the world: the host's copy of them is the host's own. Nothing noted (an older file): the copy stays
            player.hoard = kept.Hoard ?? player.hoard;
            player.knownCraft = kept.KnownCraft ?? player.knownCraft;
            player.sketches = kept.Sketches ?? player.sketches;
            // the story scenes this player saw or can see (Nymelle, Lothria, the base): the host's list skipped a
            // scene the guest never saw or played one again (hunt 5, H5-20)
            player.flags.playedStories = kept.PlayedStories ?? player.flags.playedStories;
            player.flags.availableStories = kept.AvailableStories ?? player.flags.availableStories;
            // the supporter characters and trees this player already dealt with (hunt 5, H5-08): the host's list made
            // them come back for the guest, or hid ones it never met
            player.doneBackers = kept.DoneBackers ?? player.doneBackers;
            player.knownBGMs = kept.KnownBGMs ?? player.knownBGMs;
            player.knownSongs = kept.KnownSongs ?? player.knownSongs;
            player.hangIcons = kept.HangIcons ?? player.hangIcons;

            // the cards are other objects in this copy of the world: found again by number
            var things = chara.things.Flatten().ToList();
            foreach (var thing in things) {
                if (kept.Bags.TryGetValue(thing.uid, out var data)) {
                    // closing the window with the old game marked it closed
                    data.open |= kept.Open.Contains(thing.uid);
                    thing.c_windowSaveData = data;
                }
            }

            _reopen = true;

            if (kept.Hotbars is not { } hotbars) {
                return;
            }

            // hotbars hold cards and zones, other objects in this copy of the world: found again by number
            // (read from the file they are not there at all, only their numbers are)
            player.hotbars = hotbars;
            foreach (var bar in hotbars.bars) {
                // its widget went with the old game
                bar?.actor = null;
            }

            Hot(hotbars, (item, bar, page, slot) => {
                var noted = kept.HotRefs.Find(r => r[0] == bar && r[1] == page && r[2] == slot)?[3] ?? 0;
                switch (item) {
                    case HotItemThing hot:
                        hot.thing = things.Find(t => t.uid == (hot.thing?.uid ?? noted));
                        if (hot.thing is null) {
                            hotbars.bars[bar].pages[page].items[slot] = null;
                        }

                        break;
                    case HotItemFocusPos focus when (focus.zone?.uid ?? noted) is var zone and not 0:
                        focus.zone = EClass.game.spatials.Find(zone);
                        break;
                }
            });
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not carry this player's own settings over to the world received");
        }
    }

    /// <summary>
    ///     The map is in place (Scene.Init is done): the windows that were open are opened again, the way Game.Load
    ///     does after a save is loaded. Never twice, never for a dead player, only what the player carries
    /// </summary>
    internal static void Reopen()
    {
        if (!_reopen || _kept is not { } kept) {
            return;
        }

        _reopen = false;

        try {
            if (EClass.pc is not { isDead: false } pc) {
                return;
            }

            SoundManager.ignoreSounds = true;

            if (EClass.game.altInv) {
                if (kept.Inventory && !LayerInventory.listInv.Exists(l => l.mainInv)) {
                    // the bag, and the containers in it whose window is marked open
                    EClass.ui.OpenFloatInv(true);
                    SoundManager.ignoreSounds = true;
                }

                // a container opened on its own, or one inside another
                foreach (var thing in pc.things.Flatten().ToList()) {
                    if (kept.Open.Contains(thing.uid) && thing.trait is { IsContainer: true } and not TraitToolBelt &&
                        !LayerInventory.IsOpen(thing)) {
                        LayerInventory.CreateContainerPC(thing);
                    }
                }
            }

            if (kept.Ability && EClass.game.altAbility && !EClass.ui.IsAbilityOpen) {
                EClass.ui.ToggleAbility();
            }

            if (ActionMode.Adv is { } adv) {
                adv.zoomOut2 = kept.ZoomOut;
            }

            TooltipManager.Instance.HideTooltips(immediate: true);
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not open the windows again after the world was replaced");
        } finally {
            SoundManager.ignoreSounds = false;
        }
    }

    /// <summary>
    ///     Every item of the hotbars, with its bar, page and slot
    /// </summary>
    private static void Hot(HotbarManager? hotbars, Action<HotItem, int, int, int> each)
    {
        for (var b = 0; b < (hotbars?.bars?.Length ?? 0); b++) {
            if (hotbars!.bars[b] is not { pages: not null } bar) {
                continue;
            }

            for (var p = 0; p < bar.pages.Count; p++) {
                for (var i = 0; i < (bar.pages[p]?.items?.Count ?? 0); i++) {
                    if (bar.pages[p].items[i] is { } item) {
                        each(item, b, p, i);
                    }
                }
            }
        }
    }

    private static string PathOf(int seed, int chara)
    {
        return Path.Combine(Root, $"{seed}_{chara}.lz4");
    }

    private static Kept? Read(int seed, int chara)
    {
        var path = PathOf(seed, chara);
        if (!File.Exists(path)) {
            return null;
        }

        try {
            var kept = new LZ4Bytes { Bytes = File.ReadAllBytes(path) }.Decompress<Kept>();
            return kept is { } read && read.Seed == seed && read.Chara == chara ? read : null;
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Could not read this player's own settings from {Path}", path);
            return null;
        }
    }

    /// <summary>
    ///     Written with the serializer of the saves: the game's own classes, as the game writes them
    /// </summary>
    private sealed class Kept
    {
        // which world (the host's copy carries its seed), which character
        public int Seed;
        public int Chara;

        public Game.Config? Config;
        public Player.Pref? Pref;
        public Dictionary<string, Window.SaveData>? Windows;
        public LayerAbility.Config? AbilityConfig;
        public WidgetManager.SaveData? MainWidgets;
        public WidgetManager.SaveData? SubWidgets;
        public bool SubTheme;
        public Window.SaveData? AllyInv;
        public bool ContainerCenter;
        public Window.SaveData? Pick;
        public HashSet<int>? FavAbility;
        public Dictionary<string, List<string>>? PriorityActions;
        public bool QuestTracker;
        public HashSet<string>? TrackedCategories;
        public HashSet<string>? TrackedCards;
        public HashSet<int>? TrackedElements;
        public Dictionary<string, string>? LastRecipes;
        public List<string>? FavMoongate;
        public CinemaConfig? Cinema;
        public int HotbarPage;
        public int LightMod;
        public string? Memo;
        public string? Memo2;
        public Hoard? Hoard;
        public HashSet<int>? KnownCraft;
        public HashSet<int>? Sketches;
        public List<int>? PlayedStories;
        public List<int>? AvailableStories;
        public HashSet<int>? DoneBackers;
        public HashSet<int>? KnownBGMs;
        public Dictionary<string, KnownSong>? KnownSongs;
        public Dictionary<string, string>? HangIcons;
        public int SpawnZone;
        public int LastTown;

        // windows of the cards the player carries, by card number; which of them were open
        public Dictionary<int, Window.SaveData> Bags = [];
        public HashSet<int> Open = [];
        public bool Inventory;
        public bool Ability;
        public bool ZoomOut;

        // the cards and zones the items of the hotbars point at, by number: bar, page, slot, number
        public HotbarManager? Hotbars;
        public List<int[]> HotRefs = [];

        // the counts the game keeps for "the player" that are this player's own, see DialogFlagSync
        public Dictionary<string, int>? Flags;
        public int Deepest;
        public int DeepestVoid;
        public int LastChuryu;
        public int SeedFortune;
        public int LuckyCoin;
        public int LastFelmeraReward;
        public int GiftJure;
        public int FishArtifact;
        public int SeedShrine;
        public int LittleSaved;
        public int LittleDead;

        // the message history (the journal's log), see OwnJournal
        public MsgLog? Log;
        public int Digs;
        public int Deaths;
        public int EarlyDeaths;
        public int Slept;

        // the game these were noted from: not noted twice
        [JsonIgnore]
        public Player? From;
    }
}
