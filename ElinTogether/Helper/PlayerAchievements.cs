using ElinTogether.Models;
using ElinTogether.Net;
using ElinTogether.Patches;
using HarmonyLib;

namespace ElinTogether.Helper;

/// <summary>
///     Hunt 5 H5-10: a Steam achievement goes to the account of the game whose code earns it. What the host's game
///     settles for another player (a use replayed for a guest, a kill by a guest's party, a priest healing a guest,
///     anything done while a guest stands in as "the player") went to the host's account, and a guest's game
///     replaying another player's use earned it too. The achievement now goes to the player it is about: the host
///     sends it to that player's game (AchievementDelta), a game never takes one earned by another player
/// </summary>
[HarmonyPatch]
internal static class PlayerAchievements
{
    // the player what runs now is about (null: "the player" of this game, as the game reads it)
    private static Chara? _for;

    /// <summary>
    ///     What runs until the scope ends is that player's doing: an achievement it earns is its own
    /// </summary>
    internal static ScopeExit For(Chara? player)
    {
        var before = _for;
        _for = player;
        return new() {
            OnExit = () => _for = before,
        };
    }

    /// <summary>
    ///     The player behind that card: a player's character itself, or the player whose companion or summon it is
    /// </summary>
    internal static Chara? PlayerOf(Card? card)
    {
        return card is Chara chara && (chara.IsPC || chara.IsRemotePlayer) ? chara : PlayerKarma.PlayerBehind(card);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(global::Steam), nameof(global::Steam.GetAchievement))]
    internal static bool OnAchievement(ID_Achievement id)
    {
        if (NetSession.Instance.Connection is not { } connection) {
            return true;
        }

        var owner = _for ?? (id == ID_Achievement.PRIEST ? TownLifePatch.LastPatient : null);
        // the host's game while another player stands in as "the player" (a dialog, a craft, a box opened for it)
        if (owner is null && connection is ElinNetHost && EClass.pc is { } pc && pc.IsRemotePlayer) {
            owner = pc;
        }

        // (the host's own player; a guest standing in as "the player" also reads IsPC, it is still the guest's)
        if (owner is null || owner.IsPC && !owner.IsRemotePlayer) {
            return true;
        }

        // another player's: its own game is told by the one that runs the map, never kept here
        if (connection is ElinNetHost host) {
            foreach (var (peerId, chara) in host.ActiveRemoteCharas) {
                if (chara == owner) {
                    host.SendDeltaTo(peerId, new AchievementDelta { Id = (int)id });
                    EmpLog.Information("Achievement {Id} goes to player {Uid}", id, owner.uid);
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     A death settled here: what it earns is the killer's player's (the putit, the game asks "the player's party")
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Die))]
    internal static void OnDie(Card? origin, out ScopeExit? __state)
    {
        __state = NetSession.Instance.Connection is not null && PlayerOf(origin) is { } killer ? For(killer) : null;
    }

    // a finalizer: the scope ends even if the death throws
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Die))]
    internal static void OnDieDone(ScopeExit? __state)
    {
        __state?.Dispose();
    }
}
