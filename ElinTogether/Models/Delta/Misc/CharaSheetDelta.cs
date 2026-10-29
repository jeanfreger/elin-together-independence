using ElinTogether.Helper;
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

    // the answer's standing of the character (the sheet shows "???" for a character that is not the player's own)
    [Key(3)]
    public int Karma { get; set; }

    [Key(4)]
    public int Fame { get; set; }

    [Key(5)]
    public int Deposit { get; set; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return Kind == Answer ? "not a request" : NoCharacter(sender);
    }

    protected override void OnApply(ElinNetBase net)
    {
        switch (net) {
            case ElinNetHost host when Kind == Request:
                TeamSheet.OnRequest(host, OriginPeer, Uid);
                break;
            case ElinNetClient when Kind == Answer:
                TeamSheet.Show(this);
                break;
        }
    }
}
