using System;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The extra tax a player chooses to pay each month (daExtraTax, the maid or the investor): a number of the
///     world's bill, kept on "the player" of each game. Chosen by a guest it stayed on its copy and the bill, made
///     by the host, never had it. The game where it was chosen sends it, every game takes it
/// </summary>
[MessagePackObject]
public class ExtraTaxDelta : ElinDelta
{
    // (each story its own extra tax, host option "Each player or team lives its own story": checked and kept in the
    // sender's, told to its players only)
    internal override bool InSendersStory => true;

    [Key(0)]
    public required int Value { get; init; }

    internal static void Emit()
    {
        if (!IsApplying && NetSession.Instance.Connection is { } connection) {
            connection.Delta.AddRemote(new ExtraTaxDelta { Value = EClass.player.extraTax });
        }
    }

    /// <summary>
    ///     Within the game's range (security audit 2026-10-27, item 10: any number came, the world's bill has it)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return Value is < 0 or > DialogFlagDelta.MaxExtraTax ? $"an extra tax of {Value}" : null;
    }

    internal override void OnHostRefused(ElinNetHost host)
    {
        host.SendDeltaTo(OriginPeer, new ExtraTaxDelta { Value = player.extraTax });
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            // the host keeps the base to itself: the guest gets the world's number back
            if (NetSession.Instance.Rules.HostManagesBase) {
                host.SendDeltaTo(OriginPeer, new ExtraTaxDelta { Value = player.extraTax });
                return;
            }

            host.SendDeltaToAllExcept(OriginPeer, this);
        }

        player.extraTax = Math.Max(0, Value);
    }
}
