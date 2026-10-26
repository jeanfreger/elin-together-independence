using System.Linq;
using ElinTogether.LangMod;
using ElinTogether.Models;

namespace ElinTogether.Net;

/// <summary>
///     What the world keeper's date does for "the player" (see WorldKeeperPlayers), done for every player of the
///     world: the game only knows the host's
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Karma the world's date costs every player (the month's unpaid tax): told to each connected player,
    ///     on this map or away. Its game keeps the count and tells it back
    /// </summary>
    internal void GiveKarmaToEveryone(int karma)
    {
        if (IsZoneSession) {
            return;
        }

        foreach (var peer in Socket.Peers.ToArray()) {
            if (ActiveRemoteCharas.TryGetValue(peer.Id, out var chara)) {
                GiveKarma(chara, karma);
            } else if (SavedRemoteCharas.ContainsKey(peer.User)) {
                SendDeltaTo(peer.Id, new PlayerStandingDelta {
                    Fame = 0,
                    Karma = karma,
                    Relative = true,
                });
            }
        }
    }

    /// <summary>
    ///     The characters of the other players of this world, connected or not
    /// </summary>
    internal static Chara[] OtherPlayerCharas()
    {
        return SavedRemoteCharas.Values.Distinct()
            .Where(uid => uid != player.uidChara)
            .Select(uid => game.cards.globalCharas.Find(uid))
            .OfType<Chara>()
            .ToArray();
    }

    /// <summary>
    ///     A gift of the date for one player, in a parcel in its name in the mail of the base: there until that
    ///     player comes for it
    /// </summary>
    internal static void MailTo(Chara player, params Thing[] things)
    {
        var parcel = ThingGen.CreateParcel(null, things);
        parcel.c_idRefName = "emp_story_share".Loc(player.NameSimple);
        world.SendPackage(parcel);
        EmpLog.Information("Gift of the date {Id} for player {Uid} in the mail", things[0].id, player.uid);
    }
}
