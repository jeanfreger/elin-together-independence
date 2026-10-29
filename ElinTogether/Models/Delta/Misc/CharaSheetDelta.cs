using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Team character sheets (host box "Team character sheets"): a request (player -> host, Uid of the teammate) and
///     its answer (host -> player, the snapshot) in one class
/// </summary>
[MessagePackObject]
public class CharaSheetDelta : ElinDelta
{
    public const int Request = 0;
    public const int Answer = 1;

    [Key(0)]
    public required int Kind { get; init; }

    [Key(1)]
    public required int Uid { get; init; }

    // the answer's snapshot (the CharaExport data), empty in a request
    [Key(2)]
    public byte[] Data { get; set; } = [];

    protected override void OnApply(ElinNetBase net)
    {
        // TODO(PLAN_hub_features WP-A, Helper/TeamSheet.cs): host checks same team and 1 request / 2 s / player, guest opens the sheet
    }
}
