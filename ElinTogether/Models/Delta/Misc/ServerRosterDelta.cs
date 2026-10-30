using System.Collections.Generic;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     One connected player of the server as the host sees it, on its map or away (user 2026-10-30: the players list
///     showed only the players of the same map)
/// </summary>
[MessagePackObject]
public class RosterEntry
{
    // the host's peer id of that player, 0 for the host itself
    [Key(0)]
    public int Peer { get; init; }

    [Key(1)]
    public ulong Steam { get; init; }

    [Key(2)]
    public string Name { get; set; } = "";

    [Key(3)]
    public int CharaUid { get; init; }

    [Key(4)]
    public string CharaName { get; set; } = "";

    [Key(5)]
    public int Level { get; init; }

    [Key(6)]
    public int Team { get; init; }

    // the map it stands on, its name as the game shows it
    [Key(7)]
    public string Where { get; set; } = "";

    // not on the host's map
    [Key(8)]
    public bool Away { get; init; }

    // as the host measures it, 0 when unknown (the host's own row)
    [Key(9)]
    public int PingMs { get; init; }

    [Key(10)]
    public bool Idle { get; init; }

    internal bool IsHost => Peer == 0;
}

/// <summary>
///     Host -> every connected player, away ones too: who is on the server (see ElinNetHostRoster), kept as
///     <see cref="NetSession.ServerRoster" /> for the menus. <see cref="NetSession.CurrentPlayers" /> stays the map's list
/// </summary>
[MessagePackObject]
public class ServerRosterDelta : ElinDelta
{
    [Key(0)]
    public List<RosterEntry> Players { get; set; } = [];

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        // (only the server's own link: a zone session of a player visited never sends one)
        if (net is ElinNetClient { IsZoneSession: false }) {
            NetSession.Instance.SetServerRoster(Players);
        }
    }
}
