using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     From the host to one player (host option "Each player or team lives its own story", <see cref="TeamOwnStory" />):
///     where its story stands now, in place of what its game held. Sent when the player changes team, and when the box
///     is unticked (the world owner's story); a copy of the world carries it too (SaveDataProbe.Story)
/// </summary>
[MessagePackObject]
public class StoryBundleDelta : ElinDelta
{
    [Key(0)]
    public required StoryBundle Bundle { get; init; }

    /// <summary>
    ///     One quiet line in the journal: the player's story changed with its team
    /// </summary>
    [Key(1)]
    public bool Line { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return "not a request";
    }

    protected override void OnApply(ElinNetBase net)
    {
        // (a zone hosted by a client: the visitor hears it from the host of the world)
        if (net is ElinNetHost || FromPeer) {
            return;
        }

        TeamOwnStory.Hear(Bundle, Line);
    }
}
