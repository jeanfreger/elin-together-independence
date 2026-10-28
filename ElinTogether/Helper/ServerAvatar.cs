using ElinTogether.Net;
using ElinTogether.Net.Steam;
using HeathenEngineering.SteamworksIntegration;

namespace ElinTogether.Helper;

/// <summary>
///     A server (-empserver) plays no character (council 2026-10-28, "no host at all"): the save owner's character
///     stops being the game's local one and becomes an ordinary player character tied to its Steam id, which joins
///     like any guest. The server's own local character is a hidden stand-in that nobody can see, fight, hurt or
///     talk to. Only under -empserver, and only with EmpConfig.Server.ServerAvatar (on by default there): a game
///     hosted from the menu is never touched
/// </summary>
internal static class ServerAvatar
{
    /// <summary>
    ///     The stand-in carries this on its card, so a guest, which has no table of the save, knows it too
    /// </summary>
    internal const string Flag = "emp_server_avatar";

    private static readonly int _flagKey = Flag.GetHashCode();

    /// <summary>
    ///     The server plays no character of its own, see the class
    /// </summary>
    internal static bool Active => EmpServer.Requested && EmpConfig.Server.ServerAvatar.Value && !_abandoned;

    /// <summary>
    ///     How long a server waits for the exchange of characters before it plays the world as it is
    /// </summary>
    private const float SettleSeconds = 120f;

    private static bool _settled = true;
    private static bool _abandoned;
    private static float _waitingSince;

    /// <summary>
    ///     False from the load of a world until the exchange of characters is done (or given up): the session does
    ///     not open before, it would open on the owner's character. Not for ever: a migration that never ends (a
    ///     load that failed, a frame that never came) is given up after <see cref="SettleSeconds" />, with its reason
    ///     in the log, and the server is the old one (the owner's character planted at the base, no stand-in:
    ///     <see cref="Active" /> is false from then until the next world)
    /// </summary>
    internal static bool Settled {
        get {
            if (!_settled && UnityEngine.Time.unscaledTime - _waitingSince > SettleSeconds) {
                _settled = true;
                _abandoned = true;
                EmpLog.Warning("Server: the exchange of characters did not end in {Seconds} s, the world is played as it is " +
                               "(the owner's character at the base, no stand-in)", SettleSeconds);
            }

            return _settled;
        }
        set {
            _settled = value;
            if (!value) {
                _abandoned = false;
                _waitingSince = UnityEngine.Time.unscaledTime;
            }
        }
    }

    /// <summary>
    ///     That card is the server's stand-in. Asked in hot paths (who sees whom, who fights whom): one lookup
    /// </summary>
    internal static bool Is(Card? card)
    {
        return card is { isChara: true } && card.GetBool(_flagKey);
    }

    /// <summary>
    ///     That uid is the server's stand-in (the player lists of the mod are made from uids)
    /// </summary>
    internal static bool IsUid(int uid)
    {
        return uid != 0 && EClass.game?.cards.globalCharas.Find(uid) is { } chara && Is(chara);
    }

    /// <summary>
    ///     The uid of the character that plays the local seat as far as the world's stories, knowledge and boxes are
    ///     concerned ("the world owner", council 2026-10-28): the local character, except on a server, where it is the
    ///     owner's (the stand-in plays no story). Never the stand-in's own uid
    /// </summary>
    internal static int LocalUid()
    {
        return Is(EClass.pc) && ElinNetHost.AvatarOwnerCharaUid() is > 0 and var uid ? uid : EClass.pc.uid;
    }

    /// <summary>
    ///     The Steam id this game is known by in its own world. A server has an identity of its own, reserved here: it
    ///     runs on the account of the player who started it (a hidden server, see HiddenServer: two processes, one
    ///     account), and neither must take the other's place. The Steam account stays the server's transport (lobby,
    ///     relay, listen socket); this is only who "the host" is for the world's tables. The player keeps its real
    ///     account for its character, so that old worlds need no rekeying (council 2026-10-28, blocker 3)
    /// </summary>
    internal static ulong HostUser =>
        EmpServer.Requested && EmpConfig.Dev.Identity.Value == 0 ? SteamNetPeer.DevIdentityBase + ServerIdentity : UserData.Me;

    /// <summary>
    ///     The reserved identity of a server (the windows of the bench use 1 to 8, the ones with a local port): the last
    ///     one the setting allows
    /// </summary>
    internal const ulong ServerIdentity = 999;
}
