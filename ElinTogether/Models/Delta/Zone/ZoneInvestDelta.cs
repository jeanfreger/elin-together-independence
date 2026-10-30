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
    ///     Host to clients: the merchant's invested count after it (issue #41). Their copies of a resident merchant
    ///     (Kettle) stayed behind, and the copy a player hands back with the base it held alone replaced the host's
    /// </summary>
    [Key(2)]
    public int? Count { get; init; }

    /// <summary>
    ///     Security audit 2026-10-27 (item 6): each investment raises the town's development and influence whatever
    ///     it cost; the sender must have just paid that gold (its own currency request, CardModCurrencyDelta)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        // the paid gold is the sender's word too: it must be the game's price (CalcMoney.InvestZone / InvestShop; they
        // read EClass.pc, so the sender's stats here). A tenth less is allowed: its skill (exp it just gained) may be
        // ahead of this copy of it, and a member's merchant discount (the guild rank is shared) is the lowest price
        if (sender is not null && Price(sender) is var price && Cost < price - price / 10) {
            return $"an investment of {Cost} for a price of {price}";
        }

        var paid = CardModCurrencyDelta.TakeSpent(OriginPeer, Cost);
        return paid < Cost ? $"an investment of {Cost} with {paid} gold paid" : null;
    }

    private long Price(Chara sender)
    {
        // CalcMoney.Invest with the sender as the player
        long Invest(long a)
        {
            var num = a * 100 / (long)(100f + System.Math.Max(0, sender.CHA / 2 + sender.Evalue(292)) * 2f);
            return num is >= 0 and < 100000000 ? num : 100000000;
        }

        if (Merchant is null) {
            var dev = (long)_zone.development;
            return System.Math.Max(Invest(dev * 50), Invest(dev * dev / 4 + 500));
        }

        if (Merchant.Find() is not Chara { isDead: false } merchant) {
            return 0;
        }

        // (as a member: the lowest price)
        var member = 100 / (110 + Guild.Merchant.relation.rank / 2f);
        long Shop(long a) => Invest((long)(a * member));
        long count = merchant.c_invest;
        return System.Math.Max(Shop(count * 700), Shop(count * count * 80 + 200));
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost) {
            // never lower: what the client's own dialog just counted stays
            if (Count is { } count && Merchant?.Find() is Chara known && count > known.c_invest) {
                known.c_invest = count;
            }

            return;
        }

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
        Tell(host, merchant);
    }

    /// <summary>
    ///     Every client's copy of that merchant takes the host's count
    /// </summary>
    internal static void Tell(ElinNetHost host, Chara merchant)
    {
        host.Delta.AddRemote(new ZoneInvestDelta {
            Cost = 1,
            Merchant = merchant,
            Count = merchant.c_invest,
        });
    }
}
