using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Player -> host: read the access log of a container (host box "Container access log"), or with 0 the list of
///     the containers that have one (admins). The host answers with <see cref="ContainerLogAnswer" /> if the sender
///     may read it, nothing otherwise
/// </summary>
[MessagePackObject]
public class ContainerLogRequest : ElinDelta
{
    [Key(0)]
    public required int Uid { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return ContainerLog.MayRead(host.PlayerChara(OriginPeer), host.UserOf(OriginPeer), Uid) ? null : "may not read this log";
    }

    protected override void OnApply(ElinNetBase net)
    {
        // (checked again whatever the host's box says about refusals: a log is never handed out on a "would have")
        if (net is not ElinNetHost { IsZoneSession: false } host || !ContainerLog.On ||
            !ContainerLog.MayRead(host.PlayerChara(OriginPeer), host.UserOf(OriginPeer), Uid)) {
            return;
        }

        host.SendDeltaTo(OriginPeer, ContainerLog.Answer(Uid));
    }
}

/// <summary>
///     Host -> the player who asked: the lines of the log, newest last. With Uid 0 the list of containers, Ids being
///     the uid of each line
/// </summary>
[MessagePackObject]
public class ContainerLogAnswer : ElinDelta
{
    [Key(0)]
    public required int Uid { get; init; }

    [Key(1)]
    public required string Title { get; init; }

    [Key(2)]
    public required string[] Lines { get; init; }

    [Key(3)]
    public int[] Ids { get; set; } = [];

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetClient) {
            ContainerLog.Display(this);
        }
    }
}
