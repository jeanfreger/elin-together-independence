using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Guest -> host: a bug report is being made, send the end of your logs (host box "ShareLogForReports"). The host
///     answers with <see cref="HostLogAnswer" />: the logs, redacted, or why not (box off, asked less than a minute ago)
/// </summary>
[MessagePackObject]
public class HostLogRequest : ElinDelta
{
    // matches the answer to the request (a late answer to an older one is dropped)
    [Key(0)]
    public required int Nonce { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            host.SendDeltaTo(OriginPeer, BugReport.HostLogFor(OriginPeer, Nonce));
        }
    }
}

/// <summary>
///     Host -> the guest who asked: the end of the host's mod session log and of its Player.log, Steam ids, IP
///     addresses and Windows user names already removed; or Refused and both empty
/// </summary>
[MessagePackObject]
public class HostLogAnswer : ElinDelta
{
    [Key(0)]
    public required int Nonce { get; init; }

    [Key(1)]
    public string Session { get; set; } = "";

    [Key(2)]
    public string Player { get; set; } = "";

    // why nothing is sent, null when the logs are there
    [Key(3)]
    public string? Refused { get; set; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetClient) {
            BugReport.OnHostLog(this);
        }
    }
}
