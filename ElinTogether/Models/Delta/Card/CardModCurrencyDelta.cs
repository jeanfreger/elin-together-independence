using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CardModCurrencyDelta : ElinDelta
{
    [Key(0)]
    public required int Amount { get; init; }

    [Key(1)]
    public required string CurrencyId { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        // client -> host intent only
        if (net is not ElinNetHost host) {
            return;
        }

        if (host.PlayerChara(OriginPeer) is not { } chara) {
            return;
        }

        if (Refused(host)) {
            return;
        }

        if (Amount < 0) {
            Spent.Add((OriginPeer, UnityEngine.Time.realtimeSinceStartup, -Amount, CurrencyId));
            Spent.RemoveAll(s => UnityEngine.Time.realtimeSinceStartup - s.At > SpentWindow);
        }

        // away from the host map the purse travels with the character, its game already paid: only the record is kept
        if (host.IsAwayPeer(OriginPeer)) {
            return;
        }

        EmpLog.Debug("Mod currency {CurrencyId} by {CurrencyAmount} on chara {Uid}",
            CurrencyId, Amount, chara.uid);

        using var _ = Simulate();
        chara.ModCurrency(Amount, CurrencyId);
    }

    // the currencies of the game a player holds; and what one may gain in an hour on a server that enforces trust
    private static readonly System.Collections.Generic.Dictionary<string, int> HourlyGain = new() {
        ["money"] = 2_000_000, ["money2"] = 2_000, ["money3"] = 2_000, ["plat"] = 2_000, ["medal"] = 200,
        ["ticket_sky"] = 50, ["casino_coin"] = 200_000,
    };

    private static readonly System.Collections.Generic.Dictionary<(int Peer, string Id), (float Since, long Gained)> Gains = [];

    // what each player paid lately: what it says it paid elsewhere (its debt in gold, land in gold bars) must have
    // left its purse
    private const float SpentWindow = 120f;
    private static readonly System.Collections.Generic.List<(int Peer, float At, long Amount, string Id)> Spent = [];

    /// <summary>
    ///     Gold this player paid in the last two minutes, and forgets it (one payment pays one thing)
    /// </summary>
    /// <param name="since">only what it paid from then on (a duel's stake: after the yes)</param>
    /// <param name="currency">gold unless said (gold bars for land)</param>
    internal static long TakeSpent(int peer, long wanted, float since = 0f, string currency = "money")
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        long found = 0;
        for (var i = 0; i < Spent.Count && found < wanted; i++) {
            if (Spent[i].Peer != peer || Spent[i].Id != currency || now - Spent[i].At > SpentWindow || Spent[i].At < since) {
                continue;
            }

            var take = System.Math.Min(Spent[i].Amount, wanted - found);
            found += take;
            Spent[i] = (Spent[i].Peer, Spent[i].At, Spent[i].Amount - take, Spent[i].Id);
        }

        Spent.RemoveAll(s => s.Amount <= 0);
        return found;
    }

    /// <summary>
    ///     A guest says what it gained or spent of its own currency (it trades and loots on its own maps). On a public
    ///     or dedicated server (TrustEnforced): only the game's currencies, spending always, gains up to an hourly
    ///     amount (security audit 2026-10-27: a forged amount minted any sum, any id made any item)
    /// </summary>
    private bool Refused(ElinNetHost host)
    {
        if (!TrustEnforced) {
            return false;
        }

        if (!HourlyGain.TryGetValue(CurrencyId, out var cap)) {
            EmpLog.Warning("Refused currency {CurrencyId} from peer {Peer}: not a currency of the game", CurrencyId, OriginPeer);
            return true;
        }

        if (Amount <= 0) {
            return false;
        }

        var now = UnityEngine.Time.realtimeSinceStartup;
        var key = (OriginPeer, CurrencyId);
        if (!Gains.TryGetValue(key, out var gained) || now - gained.Since > 3600f) {
            gained = (now, 0);
        }

        if (gained.Gained + Amount > cap) {
            EmpLog.Warning("Refused {Amount} {CurrencyId} from peer {Peer}: over {Cap} in an hour", Amount, CurrencyId, OriginPeer, cap);
            ElinNetHost.AdminLog($"refused {Amount} {CurrencyId} from peer {OriginPeer}: over {cap} in an hour");
            return true;
        }

        Gains[key] = (gained.Since, gained.Gained + Amount);
        return false;
    }
}
