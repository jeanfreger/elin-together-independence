using ElinTogether.Net;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     Hunt 5 H5-01 / H5-04: the game reads "the player" for the level of what it makes (EClass.pc.FameLv comes from
///     EClass.player.fame, which is the host's own, a guest's fame lives in its game). While the host makes
///     something because a guest asked for it, that guest's fame stands in for the host's
/// </summary>
internal static class PlayerFame
{
    /// <summary>
    ///     The fame of this player as its game last told it, null when it is not a player the host keeps books for
    ///     (the host itself, a zone hosted by a client, fame shared by the rules)
    /// </summary>
    internal static int? Of(Chara? player)
    {
        return player is not null && NetSession.Instance.Connection is ElinNetHost host ? host.FameOf(player) : null;
    }

    /// <summary>
    ///     EClass.player.fame is that player's until the scope ends. Null when nothing changes
    /// </summary>
    internal static ScopeExit? As(Chara? player)
    {
        if (Of(player) is not { } fame) {
            return null;
        }

        var host = EClass.player.fame;
        EClass.player.fame = fame;
        return new() {
            OnExit = () => EClass.player.fame = host,
        };
    }
}

/// <summary>
///     A quest is drawn for the whole map by the host's game (Quest.Init reads the host's fame and skills), the
///     level-dependent fields follow the player who takes it (user decision H5-01)
/// </summary>
internal static class QuestForTaker
{
    /// <summary>
    ///     Draw again what depends on the player, as Quest.Init does: the danger level, the difficulty of a harvest
    ///     or a concert (their skills) and what follows from it. To be called while the taker stands in for the
    ///     local player (see PlayerStandIn), which also gives its fame
    /// </summary>
    internal static void Rescale(Quest quest)
    {
        var oldBase = quest.BaseMoney;
        var oldDifficulty = quest.difficulty;

        if (quest.difficultyType is Quest.DifficultyType.Farm or Quest.DifficultyType.Music) {
            var skill = quest.difficultyType == Quest.DifficultyType.Farm ? 250 : 241;
            quest.difficulty = Mathf.Clamp(1 + EClass.rnd(3 + EClass.pc.Evalue(skill) / 10), 1, 7);
            if (quest is QuestMusic music) {
                music.partyLv = 0;
            }

            // what the difficulty sets (weight to harvest, score to reach, the level of the party)
            quest.OnInit();
        }

        var fameLv = EClass.pc.FameLv;
        quest.dangerLv = Mathf.Max(fameLv + EClass.rnd(fameLv / 10 + 5) - EClass.rnd(fameLv / 10 + 5), 1);

        // not for a quest whose money does not follow (it would draw a new one, or read the old one)
        if (quest.BaseMoney != oldBase || quest.difficulty != oldDifficulty) {
            quest.rewardMoney = (quest.BaseMoney + EClass.rnd(quest.BaseMoney / 4) + quest.GetExtraMoney()) *
                                (55 + quest.difficulty * 15) / 100;
        }
    }
}
