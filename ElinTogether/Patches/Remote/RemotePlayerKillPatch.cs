using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     A player cannot kill another player (council 7): a strike of a player or of what fights for one (shift +
///     click, a spell gone wide, a companion) that would kill another player's character leaves it at 0 hit
///     points instead, as the game does for what it marks invulnerable. A real death by a friend's hand cost a
///     grave, gold and experience <br />
///     The host can allow it (PlayerKill). Only the game that simulates the map decides a death <br />
///     In a duel (PlayerDuel) it holds whatever the host allows, also for what follows a duellist, and the
///     duellist stopped at 0 by the other has lost <br />
///     Not covered: what kills without a striker, bleeding, poison, fire and the death sentence a player left
///     on another
/// </summary>
[HarmonyPatch(typeof(Card), nameof(Card.DamageHP),
    typeof(long), typeof(int), typeof(int), typeof(AttackSource), typeof(Card), typeof(bool), typeof(Thing), typeof(Chara),
    typeof(int))]
internal static class RemotePlayerKillPatch
{
    private const string Tag = nameof(EditorTag.Invulnerable);

    [HarmonyPrefix]
    internal static void OnDamage(Card __instance, Card origin, out string? __state)
    {
        // alone, nobody to be protected from: an ally's blast can kill the host as in the game
        __state = NetSession.Instance.Connection is ElinNetHost && NetCompany.HasCompany ? Shield(__instance, origin) : null;
    }

    [HarmonyFinalizer]
    internal static void OnDamageEnd(Card __instance, Card origin, string? __state)
    {
        Unshield(__instance, __state, origin);
    }

    /// <summary>
    ///     The game's own "cannot die" (Card.DamageHP, EvadeDeath), for the time of one strike. Also around the
    ///     replay of that strike in the other games (CardDamageHpDelta): the replay would say "X kills Y" and
    ///     count a kill there
    /// </summary>
    /// <returns>the tags to give back to <see cref="Unshield" />, null when the strike is not shielded</returns>
    internal static string? Shield(Card card, Card? origin)
    {
        if (card is not Chara target || origin?.Chara is not { } attacker || attacker == target ||
            !(IsPlayer(attacker) || attacker.IsPCFactionOrMinion) || target.HasEditorTag(EditorTag.Invulnerable)) {
            return null;
        }

        // in a duel nobody dies of the other side, whatever the host allows: the duellists and what follows them
        if (!PlayerDuel.Protects(target) && (NetSession.Instance.Rules.AllowPlayerKill || !IsPlayer(target))) {
            return null;
        }

        var tags = target.c_editorTags ?? "";
        target.c_editorTags = tags.Length == 0 ? Tag : tags + "," + Tag;
        return tags;
    }

    /// <param name="origin">who struck: a duellist stopped at 0 hit points by the other lost the duel</param>
    internal static void Unshield(Card card, string? tags, Card? origin)
    {
        if (tags is not null) {
            card.c_editorTags = tags.Length == 0 ? null : tags;
            PlayerDuel.Struck(card, origin);
            TeamPvp.Struck(card, origin);
        }
    }

    private static bool IsPlayer(Chara chara)
    {
        return chara.IsPC || chara.IsRemotePlayer;
    }
}
