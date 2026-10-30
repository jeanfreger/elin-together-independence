using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Market (host box "Market"): player -> host, list the board, post an ad or remove one. The host checks
///     everything again (<see cref="Market.Handle" />) and answers with the board
/// </summary>
[MessagePackObject]
public class MarketAskDelta : ElinDelta
{
    internal const int List = 0, Post = 1, Remove = 2;

    [Key(0)]
    public required int Verb { get; init; }

    // 0 selling, 1 looking for
    [Key(1)]
    public int Kind { get; set; }

    // a thing id of the game, or empty for a free text item
    [Key(2)]
    public string Item { get; set; } = "";

    [Key(3)]
    public string Text { get; set; } = "";

    [Key(4)]
    public int Qty { get; set; }

    [Key(5)]
    public int Price { get; set; }

    [Key(6)]
    public string Note { get; set; } = "";

    // the ad to remove
    [Key(7)]
    public int AdId { get; set; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (Verb is < List or > Remove) {
            return "no such verb";
        }

        return (Item?.Length ?? 0) > 100 || (Text?.Length ?? 0) > 200 || (Note?.Length ?? 0) > 500 ? "text too long" : NoCharacter(sender);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host && host.PlayerChara(OriginPeer) is { } from) {
            Market.Handle(this, from, host.UserOf(OriginPeer), false, host);
        }
    }
}

/// <summary>
///     Market: host -> player, the whole board (Full) with what became of the player's own ask (Result), or only the
///     board's new version number (every player is told it at each change; an open Market tab asks again)
/// </summary>
[MessagePackObject]
public class MarketBoardDelta : ElinDelta
{
    [Key(0)]
    public required int Version { get; init; }

    [Key(1)]
    public bool Full { get; set; }

    [Key(2)]
    public int[] Ids { get; set; } = [];

    [Key(3)]
    public int[] Kinds { get; set; } = [];

    [Key(4)]
    public int[] Qtys { get; set; } = [];

    [Key(5)]
    public int[] Prices { get; set; } = [];

    // seconds since each ad was posted (the host's clock: the guest's may differ)
    [Key(6)]
    public long[] Ages { get; set; } = [];

    [Key(7)]
    public int[] Authors { get; set; } = [];

    [Key(8)]
    public string[] Items { get; set; } = [];

    [Key(9)]
    public string[] Names { get; set; } = [];

    [Key(10)]
    public string[] Texts { get; set; } = [];

    [Key(11)]
    public string[] Notes { get; set; } = [];

    // a lang id: what became of the asker's post or removal, empty for a plain listing
    [Key(12)]
    public string Result { get; set; } = "";

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost) {
            Market.Receive(this);
        }
    }
}
