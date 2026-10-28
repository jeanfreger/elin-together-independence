using System;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Net
{
    internal partial class ElinNetHost
    {
        /// <summary>
        ///     The highest fame among the players connected (the host's own included). Read only: the host already
        ///     keeps the fame of every player, see StoreStanding
        /// </summary>
        internal int HighestFame()
        {
            // (a server's stand-in has no fame of its own, see ServerAvatar)
            var fame = ServerAvatar.Is(pc) ? 0 : player.fame;
            foreach (var peer in Socket.Peers) {
                if (SavedRemoteCharas.TryGetValue(peer.User, out var uid) &&
                    PlayerStandings.TryGetValue(uid, out var standing) && standing.Length >= 2) {
                    fame = Math.Max(fame, standing[StandingFame]);
                }
            }

            return fame;
        }
    }
}

namespace ElinTogether.Patches
{
    /// <summary>
    ///     Council 10, bills: the fame tax of the month is computed on the fame of the host's character, so the glory
    ///     of a guest never cost anything and the host's always did. In a session with company, the host computes it
    ///     on the highest fame of the players connected. Alone, or without personal fame (the rule off: nobody keeps
    ///     the fame of others): the game's own. Only the host's game makes the bill; a guest's tax estimate on its
    ///     own screen still reads its own fame
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.GetFameTax))]
    internal static class SharedTaxPatch
    {
        [HarmonyPrefix]
        internal static void OnFameTax(out int? __state)
        {
            __state = null;
            if (NetSession.Instance.Transport is ElinNetHost { IsZoneSession: false } host &&
                NetSession.Instance.Rules.UsePersonalQuests && NetSession.Instance.Rules.UseSharedTax && NetCompany.HasCompany) {
                __state = EClass.player.fame;
                EClass.player.fame = host.HighestFame();
            }
        }

        // a finalizer: the fame of the host is put back even if the game's own method throws
        [HarmonyFinalizer]
        internal static void OnFameTaxDone(int? __state)
        {
            if (__state is { } fame) {
                EClass.player.fame = fame;
            }
        }
    }
}
