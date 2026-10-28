using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Net
{
    internal partial class ElinNetHost
    {
        /// <summary>
        ///     The players standing on this map (the host's character first) with the fame the host keeps for each.
        ///     Read only: see StoreStanding
        /// </summary>
        internal List<(Chara Player, int Fame)> PlayersOnMap()
        {
            var list = new List<(Chara, int)>();
            if (!ServerAvatar.Is(pc)) {
                list.Add((pc, player.fame));
            }

            foreach (var chara in ActiveRemoteCharas.Values) {
                if (chara.IsAliveInCurrentZone && PlayerStandings.TryGetValue(chara.uid, out var standing) &&
                    standing.Length >= 2) {
                    list.Add((chara, standing[StandingFame]));
                }
            }

            return list;
        }
    }
}

namespace ElinTogether.Patches
{
    /// <summary>
    ///     Council 2026-10-23, town boards: the offers a town board draws were weighed on the host's character alone.
    ///     When the game regenerates the board (and only then: the offers stay as they are when players come and go),
    ///     the gate on fame reads the highest fame among the players on the map, and each offer's difficulty and
    ///     danger are those of a player on the map, taken in turn, with the average fame: a guest's glory opens the
    ///     high-fame offers without making them harder than the group. The reward of a supply offer is priced when
    ///     it is turned in, with the player who delivers (PlayerStandIn already stands in then). Alone, or off:
    ///     the game's own
    /// </summary>
    [HarmonyPatch]
    internal static class TownBoardPatch
    {
        private static List<(Chara Player, int Fame)>? _players;
        private static int _next;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Zone), nameof(Zone.UpdateQuests))]
        internal static void OnUpdateQuests(Zone __instance, out int? __state)
        {
            __state = null;
            if (_players is not null || NetSession.Instance.Transport is not ElinNetHost { IsZoneSession: false } host ||
                !NetSession.Instance.Rules.UsePersonalQuests || !NetSession.Instance.Rules.TownBoards ||
                __instance != EClass._zone || !__instance.IsPCFaction && !(__instance is Zone_Town && __instance.lv == 0)) {
                return;
            }

            var players = host.PlayersOnMap();
            if (players.Count < (ServerAvatar.Is(EClass.pc) ? 1 : 2)) {
                return;
            }

            _players = players;
            _next = 0;
            __state = EClass.player.fame;
            EClass.player.fame = players.Max(p => p.Fame);
        }

        // a finalizer: the host's own fame is put back even if the game's method throws
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Zone), nameof(Zone.UpdateQuests))]
        internal static void OnUpdateQuestsDone(int? __state)
        {
            if (__state is { } fame) {
                EClass.player.fame = fame;
                _players = null;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Quest), nameof(Quest.Init))]
        internal static void OnInit(out (Chara Chara, int Fame)? __state)
        {
            __state = null;
            if (_players is not { } players) {
                return;
            }

            var (player, _) = players[_next++ % players.Count];
            __state = (EClass.player.chara, EClass.player.fame);
            EClass.player.chara = player;
            EClass.player.fame = (int)players.Average(p => (long)p.Fame);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Quest), nameof(Quest.Init))]
        internal static void OnInitDone((Chara Chara, int Fame)? __state)
        {
            if (__state is { } was) {
                EClass.player.chara = was.Chara;
                EClass.player.fame = was.Fame;
            }
        }
    }
}
