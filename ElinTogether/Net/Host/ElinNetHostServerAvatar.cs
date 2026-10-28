using System;
using ElinTogether.Helper;

namespace ElinTogether.Net;

/// <summary>
///     A server that plays no character, see ServerAvatar. A world opened by `-empserver` has its owner's character
///     as the local one, planted at the base: it is exchanged, once and silently, with a stand-in made for the
///     server (Exchange, the same swap as a world taken over by another player), and the owner's character becomes
///     the character of an absent player that comes back when its player joins by its Steam id. Nothing is asked
///     of anyone and nothing is copied: the character keeps its uid, so the teams, the stories and the rosters
///     that know it by uid need no change
/// </summary>
internal partial class ElinNetHost
{
    private const int AvatarSchema = 0;
    private const int AvatarOwnerUser = 1;
    private const int AvatarOwnerUid = 2;
    private const int AvatarOwnerZone = 3;
    private const int AvatarOwnerX = 4;
    private const int AvatarOwnerZ = 5;
    private const int AvatarUid = 6;

    /// <summary>
    ///     [schema (0 until the first exchange, 1 after), the Steam id of the owner of the world, its character's
    ///     uid, the uid of the zone it was in and where (x, z) when the server took its place, the stand-in's uid].
    ///     The owner's own character is told by PcOwners/SavedRemoteCharas like any player's; this keeps what only
    ///     the server needs: that a backup was made, and where the owner was, for its first join
    /// </summary>
    [ElinGameIOProperty("server_avatar")]
    internal static long[] ServerAvatarTable
    {
        get => field is { Length: 7 } ? field : field = new long[7];
        set;
    }

    /// <summary>
    ///     The server just loaded a world: the owner's character is exchanged for the stand-in, unless it already is
    ///     (a world the server saved before, or a crash save). True when the world was saved with the exchange and
    ///     must be loaded again, so that the game starts from it as from any save
    /// </summary>
    internal static bool MigrateToServerAvatar()
    {
        if (!ServerAvatar.Active) {
            return false;
        }

        var table = ServerAvatarTable;
        if (ServerAvatar.Is(pc)) {
            // a world the server saved already: nothing to exchange, only the table to keep right
            table[AvatarSchema] = 1;
            table[AvatarUid] = pc.uid;
            return false;
        }

        var home = pc.homeBranch?.owner;
        if (home is null || pc.party is not { } party || pc.isDead) {
            EmpLog.Warning("Server: no stand-in made for the owner's character, it has no base, no party or is dead");
            return false;
        }

        // the save as it was, kept with the game's own backups, on the first exchange only. Without it nothing is
        // exchanged: the server is the old one, with the owner's character planted at the base
        if (table[AvatarSchema] == 0) {
            try {
                GameIO.MakeBackup(new GameIndex { id = Game.id, cloud = game.isCloud });
            } catch (Exception ex) {
                EmpLog.Warning(ex, "Server: no backup before the exchange of characters, nothing exchanged");
                return false;
            }
        }

        // whose the owner's character is: the player the save says, else a player who comes without a character
        // gets it back (GiveOrphanTo)
        var owner = PcOwner();
        var former = pc;
        var zone = former.currentZone;
        var (x, z) = (former.pos.x, former.pos.z);

        var standIn = CharaGen.Create("chara");
        try {
            standIn.SetBool("emp_creating", true);
            standIn.SetGlobal();
            standIn.SetFaction(EClass.Home);
            standIn.homeZone = former.homeZone;
            standIn.c_altName = "Server";
            standIn.SetBool(ServerAvatar.Flag, true);
            standIn.isHidden = true;
            standIn.hp = standIn.MaxHP;
        } finally {
            standIn.SetBool("emp_creating", false);
        }

        // (a player's books, so that the swap puts the owner's standing and random quests in theirs)
        PlayerStandings[standIn.uid] = [0, NewPlayerKarma];
        Exchange(standIn, owner, LocalUser);

        // the stand-in waits at the edge of the base, where nobody walks. A owner who was elsewhere: the game puts
        // whoever has no zone back at the base by itself (Game.OnLoad)
        if (zone == home && standIn.currentZone == home) {
            var edge = new Point(_map.bounds.x, _map.bounds.z).GetNearestPoint(allowBlock: false, allowChara: false);
            if (edge.IsValid) {
                standIn.pos.Set(edge);
            }
        } else {
            standIn.currentZone = null;
        }

        table[AvatarSchema] = 1;
        table[AvatarOwnerUser] = (long)owner;
        table[AvatarOwnerUid] = former.uid;
        table[AvatarOwnerZone] = zone?.uid ?? 0;
        table[AvatarOwnerX] = x;
        table[AvatarOwnerZ] = z;
        table[AvatarUid] = standIn.uid;
        EmpLog.Information("Server: the character {Owner} of player {User} is a player's now, the server plays {StandIn}",
            former.uid, owner, standIn.uid);

        // saved with the exchange; the caller loads it again. A save that fails leaves the server on the exchanged
        // characters in memory, which the next save writes
        if (game.Save(false, true)) {
            return true;
        }

        EmpLog.Warning("Server: the world could not be saved after the exchange of characters, played as it is");
        return false;
    }
}
