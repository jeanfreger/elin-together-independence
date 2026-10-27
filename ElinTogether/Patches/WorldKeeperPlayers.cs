using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     What the date does for "the player" in the keeper's hooks (GameDate, Player, Faction) only reached the
///     host's: each other player gets its own, from the keeper. Solo games (no session) are untouched
/// </summary>
[HarmonyPatch]
internal static class WorldKeeperPlayers
{
    // set while Player.OnAdvanceHour runs on the host: its own draw is for its own companions
    private static bool _hour;
    private static bool _giving;

    private static ElinNetHost? Host =>
        NetSession.Instance.Connection is ElinNetHost { IsZoneSession: false } host ? host : null;

    /// <summary>
    ///     Hourly exp of the party: one party holds every player's companions, and the host's draw, sleep and
    ///     death decided for all of them. Each player on the map draws for its own, as the game does for its
    ///     player. (A guest's replay of the hour touches its copies of them only: the host's or the guest's copy
    ///     replaces the other on a trip, never both, nothing is counted twice)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnAdvanceHour))]
    internal static void OnHour()
    {
        if (Host is not { } host) {
            return;
        }

        _giving = true;
        try {
            foreach (var player in host.ActiveRemoteCharas.Values) {
                if (!player.IsAliveInCurrentZone || player.IsDeadOrSleeping || EClass.rnd(2) != 0 || EClass._zone.IsRegion) {
                    continue;
                }

                foreach (var companion in CompanionHelper.CompanionsOf(player)) {
                    companion.AddExp(1);
                }
            }
        } finally {
            _giving = false;
        }

        _hour = true;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Player), nameof(Player.OnAdvanceHour))]
    internal static void OnHourEnd()
    {
        _hour = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Card), nameof(Card.AddExp))]
    internal static bool OnAddExp(Card __instance)
    {
        if (!_hour || _giving || __instance is not Chara chara) {
            return true;
        }

        return CompanionHelper.OwnerOf(chara) is not { } owner || owner.uid == EClass.player.uidChara;
    }

    /// <summary>
    ///     4 unpaid bills cost "the player" 50 karma each month: the bills are the world's, every player pays.
    ///     Connected players only: an absent one's karma is in its own game, and its time stands still
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Faction), nameof(Faction.OnAdvanceMonth))]
    internal static void OnMonth(Faction __instance)
    {
        // (teams paying their own land: each team's players only, see TeamBills)
        if (Host is not { } host || !PersonalQuests.Enabled || !NetSession.Instance.Rules.UseWorldKeeper || TeamBills.On ||
            __instance.GetChildren().Count == 0) {
            return;
        }

        // (as Faction.OnAdvanceMonth decides for the host's own player)
        if (EClass.player.taxBills >= 4 && EClass.game.principal.tax && !EClass.debug.godMode) {
            host.GiveKarmaToEveryone(-50);
        }
    }

    /// <summary>
    ///     The wedding anniversary music box names "the player" as the spouse: a resident married to another
    ///     player is hidden from the game's loop and gets its box from here, in its spouse's name
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceDay))]
    internal static void OnDay(out List<(Chara Npc, Chara Spouse)>? __state)
    {
        __state = null;
        if (Host is null || !SharedMarriages.Enabled) {
            return;
        }

        foreach (var npc in EClass.game.cards.globalCharas.Values) {
            if (npc is not { IsPCFaction: true, IsMarried: true, c_love.gotMusicBox: false } ||
                !ElinNetHost.Spouses.TryGetValue(npc.uid, out var uid) || uid == EClass.player.uidChara ||
                EClass.game.cards.globalCharas.Find(uid) is not { } spouse) {
                continue;
            }

            npc.c_love.gotMusicBox = true;
            (__state ??= []).Add((npc, spouse));
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceDay))]
    internal static void OnDayEnd(GameDate __instance, List<(Chara Npc, Chara Spouse)>? __state, Exception? __exception)
    {
        if (Host is null || __exception is not null) {
            foreach (var (npc, _) in __state ?? []) {
                npc.c_love.gotMusicBox = false;
            }

            return;
        }

        foreach (var (npc, spouse) in __state ?? []) {
            var date = Date.ToDate(npc.c_love.dateMarriage);
            if (date.month != __instance.month || date.day != __instance.day) {
                npc.c_love.gotMusicBox = false;
                continue;
            }

            // (as GameDate.AdvanceDay makes it)
            var box = ThingGen.Create("musicbox_memory2");
            box.MakeRefFrom(npc, spouse, simple: true);
            var letter = ThingGen.Create("parchment");
            letter.MakeRefFrom(npc, null, simple: true);
            letter.SetStr(53, "letter_love");
            ElinNetHost.MailTo(spouse, box, ThingGen.Create("1165"), letter);
        }

        // the Christmas box was one for the world, the first to open the mail had it: one for each player
        if (__instance.day == 25 && __instance.month == 12) {
            foreach (var other in ElinNetHost.OtherPlayerCharas()) {
                ElinNetHost.MailTo(other, ThingGen.Create("box_xmas"));
            }
        }
    }

    /// <summary>
    ///     The New Year gift, as the Christmas box: one for each player
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceYear))]
    internal static void OnYear()
    {
        if (Host is null) {
            return;
        }

        foreach (var other in ElinNetHost.OtherPlayerCharas()) {
            ElinNetHost.MailTo(other, ThingGen.Create("gift_newyear"));
        }
    }
}
