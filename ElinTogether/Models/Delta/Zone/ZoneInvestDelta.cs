using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     An investment paid in a dialog (the zone's or a merchant's): the client's game raised its own copy only, where
///     the zone and the merchant are not its own to change, it paid for nothing. The host gives the same to its zone or
///     to its merchant, which it simulates and saves <br />
///     Client to host only, see <see cref="DramaInvestPatch" />. The experience stays in the client's game, it gave it
/// </summary>
[MessagePackObject]
public class ZoneInvestDelta : ElinDelta
{
    /// <summary>
    ///     What was paid, the zone's investment goes up by as much
    /// </summary>
    [Key(0)]
    public required int Cost { get; init; }

    /// <summary>
    ///     The merchant invested in, none for the zone
    /// </summary>
    [Key(1)]
    public RemoteCard? Merchant { get; init; }

    /// <summary>
    ///     Security audit 2026-10-27 (item 6): each investment raises the town's development and influence whatever
    ///     it cost; the sender must have just paid that gold (its own currency request, CardModCurrencyDelta)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        var paid = CardModCurrencyDelta.TakeSpent(OriginPeer, Cost);
        return paid < Cost ? $"an investment of {Cost} with {paid} gold paid" : null;
    }

    protected override void OnApply(ElinNetBase net)
    {
        // a cost from the network: no more than the game's own counter can take, and only from a player standing here
        if (net is not ElinNetHost host || Cost <= 0 || Cost > int.MaxValue - System.Math.Max(_zone.investment, 0) ||
            !host.ActiveRemoteCharas.ContainsKey(OriginPeer) || host.IsAwayPeer(OriginPeer)) {
            return;
        }

        // the "gain" lines were said in the client's game, the host's must not tell them again
        using var told = MsgRelayContext.Suppress();

        if (Merchant is null) {
            _zone.investment += Cost;
            _zone.ModDevelopment(5 + rnd(5));
            _zone.ModInfluence(2);
            return;
        }

        if (Merchant.Find() is not Chara { isDead: false } merchant || !merchant.IsInActiveMap || !merchant.trait.CanInvest) {
            return;
        }

        merchant.c_invest++;
        _zone.ModInfluence(1);
    }
}
