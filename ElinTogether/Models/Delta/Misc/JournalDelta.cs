using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Activity journal (host box "Activity journal"): host -> players, the entries the peer may see (at join) or one
///     new entry
/// </summary>
[MessagePackObject]
public class JournalDelta : ElinDelta
{
    [Key(0)]
    public required int[] Day { get; init; }

    [Key(1)]
    public required int[] Kind { get; init; }

    [Key(2)]
    public required int[] Actor { get; init; }

    [Key(3)]
    public required int[] Team { get; init; }

    [Key(4)]
    public required string[] Arg { get; init; }

    // true: the whole ring replaces the guest's copy (join), false: the entries are appended
    [Key(5)]
    public bool Full { get; set; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        ActivityJournal.Receive(this);
    }
}
