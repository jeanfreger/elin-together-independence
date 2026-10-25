using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to everyone: a player was beaten (stopped at 0 hit points by a player of another team, the
///     host not allowing player kills). He is no longer that team's enemy on this map (council 2026-10-25, see
///     <see cref="TeamPvp" />)
/// </summary>
[MessagePackObject]
public class TeamBeatenDelta : ElinDelta
{
    [Key(0)]
    public required int Victim { get; init; }

    [Key(1)]
    public required int Team { get; init; }

    [Key(2)]
    public required int ZoneUid { get; init; }

    [Key(3)]
    public required int Striker { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetClient) {
            TeamPvp.Learn(this);
        }
    }

    internal void Say()
    {
        Msg.Say("emp_team_beaten".Loc(Teams.PlayerName(Victim), Teams.PlayerName(Striker)));
    }
}
