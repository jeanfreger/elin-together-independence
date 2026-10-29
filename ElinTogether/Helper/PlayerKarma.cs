using ElinTogether.Models;
using ElinTogether.Net;
using ElinTogether.Patches;

namespace ElinTogether.Helper;

/// <summary>
///     Karma is each player's own (see <see cref="PersonalQuests" />), but the game's code that takes it runs
///     wherever the deed is settled: a kill on the host, the end of a task on the host and again on every
///     client. What a deed costs goes to the player who did it, once
/// </summary>
internal static class PlayerKarma
{
    private static int _own;
    private static int _skipped;

    /// <summary>
    ///     The host settles a meal of another player: the karma it costs (poison, a food of sin) is taken in that
    ///     player's own game, with the meal. Nothing is taken from this game's player here
    /// </summary>
    internal static ScopeExit Skip()
    {
        _skipped++;
        return new() {
            OnExit = () => _skipped--,
        };
    }

    /// <summary>
    ///     Who killed, while the game settles a death on the host
    /// </summary>
    internal static Card? Killer { get; set; }

    /// <summary>
    ///     Who the game asks about, when it asks whether "the player" is a criminal
    /// </summary>
    internal static Chara? Subject { get; set; }

    /// <summary>
    ///     The game asks whether this map has a criminal on it
    /// </summary>
    internal static bool AnyPlayer { get; set; }

    /// <summary>
    ///     What is taken in there is for this game's player, whatever is going on
    /// </summary>
    internal static ScopeExit Own()
    {
        _own++;
        return new() {
            OnExit = () => _own--,
        };
    }

    /// <summary>
    ///     The player behind a chara of the party: itself, the owner of a companion, the owner of a summon's master
    /// </summary>
    internal static Chara? PlayerBehind(Card? card)
    {
        if (card?.Chara is not { } chara) {
            return null;
        }

        if (CompanionHelper.OwnerOf(chara) is { } owner) {
            return owner;
        }

        return chara.IsMinion && chara.master is { } master && master != chara ? CompanionHelper.OwnerOf(master) : null;
    }

    /// <summary>
    ///     The game takes karma from "the player" here
    /// </summary>
    /// <returns>true when it is not this game's player's to take</returns>
    internal static bool Reroute(int karma)
    {
        if (_skipped > 0) {
            return true;
        }

        if (_own > 0 || !PersonalQuests.Enabled || NetSession.Instance.Connection is not { } connection) {
            return false;
        }

        if (connection is not ElinNetHost host) {
            // what the host settled, played again here: the host tells the one who did it
            return ElinDelta.IsRemoteStateLanding;
        }

        var culprit = PlayerBehind(Killer);
        if (culprit is null && Killer is null && CharaProgressCompleteEvent.IsHappening) {
            culprit = PlayerBehind(CharaProgressCompleteEvent.Chara);
        }

        if (culprit is { IsRemotePlayer: true }) {
            host.GiveKarma(culprit, karma);
            return true;
        }

        // a deed nobody did on a server: the stand-in has no karma to lose, and the owner's is its game's (never
        // charged twice, the mirror of the stand-in's standing is refreshed from the books, see MirrorOwnerStanding)
        if (culprit is null && ServerAvatar.Is(EClass.pc)) {
            EmpLog.Debug("Karma {Karma} of a deed nobody did is not taken from the server's stand-in", karma);
            return true;
        }

        // an action of a player played again here: its own game took it
        return culprit is null && ElinDelta.IsApplying;
    }
}
