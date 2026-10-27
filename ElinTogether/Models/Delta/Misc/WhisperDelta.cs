using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A line of a private chat (<see cref="PlayerWhisper" />): from a guest it goes to the host, which passes it on to
///     the one player it is for without showing it
/// </summary>
[MessagePackObject]
public class WhisperDelta : ElinDelta
{
    [Key(1)]
    public int FromUid { get; init; }

    [Key(2)]
    public int ToUid { get; init; }

    [Key(3)]
    public required string Text { get; init; }

    /// <summary>
    ///     The writer's name, for a player that does not know it
    /// </summary>
    [Key(4)]
    public string? Name { get; init; }

    /// <summary>
    ///     A guest writes as its own character, within the game's chat limit, when the host allows private chat
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (!NetSession.Instance.Rules.PrivateChat) {
            return "private chat is off";
        }

        return sender?.uid != FromUid ? "a line written as another player" : Text.Length > 500 ? "a line over the chat limit" : null;
    }

    protected override void OnApply(ElinNetBase net)
    {
        // a muted player's line goes no further than its own game
        if (net is ElinNetHost from && from.FromMuted(OriginPeer)) {
            return;
        }

        if (ToUid == EClass.pc.uid) {
            PlayerWhisper.Receive(this);
        } else if (net is ElinNetHost host && NetSession.Instance.Transport == host) {
            PlayerWhisper.Relay(host, this);
        }
    }
}
