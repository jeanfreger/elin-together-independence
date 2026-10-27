using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;

namespace ElinTogether.Net;

/// <summary>
///     The count of unpaid tax bills is the world's (<c>player.taxBills</c> of the game that hosts): the game adds
///     one per bill it makes and takes one off per bill paid, and takes 50 karma each month it stands at four or
///     more. Before 0.26.652 a bill paid by a player alone on another map was not always counted (three paid in a
///     row, one counted): such a world keeps a count above the bills that exist, with nothing left to pay to bring
///     it down ("phantom bills"). <br />
///     The count is brought down to the tax bills this game can find: in the bags of every character of the world
///     (the players' included), in the mail, the bank and the shipping box, and on the map shown with all it holds.
///     Never raised. A bill kept on a map that is not loaded is not seen: once the count is at zero the game
///     refuses such a bill and hands it back ("bad idea"), it is forgiven, nobody is charged twice. <br />
///     Once per world when it is hosted, and again from the host's settings tab when asked
/// </summary>
internal partial class ElinNetHost
{
    [ElinGameIOProperty("bills_repaired")]
    private static int[] BillsRepaired
    {
        get => field is { Length: 1 } ? field : field = new int[1];
        set;
    }

    private static IEnumerable<Thing> Within(Thing thing)
    {
        yield return thing;
        foreach (var held in thing.things.List(_ => true, true)) {
            yield return held;
        }
    }

    /// <summary>
    ///     The tax bills that exist, as far as this game can see
    /// </summary>
    internal static int CountTaxBills()
    {
        var charas = game.cards.globalCharas.Values.Concat(_map?.charas ?? []).Where(c => c is not null).Distinct();
        var loose = new List<Thing>();
        loose.AddRange(game.cards.listPackage);
        if (game.cards.container_deposit is { } bank) {
            loose.Add(bank);
        }

        if (game.cards.container_shipping is { } shipping) {
            loose.Add(shipping);
        }

        if (_map is not null) {
            loose.AddRange(_map.things);
        }

        return charas.Sum(c => c.things.List(t => t.id == "bill_tax", true).Count) +
               loose.Where(t => t is { isDestroyed: false }).SelectMany(Within).Distinct().Count(t => t.id == "bill_tax");
    }

    /// <param name="again">asked by the host: also when this world was already looked at</param>
    /// <returns>how many bills the count was lowered by</returns>
    internal static int RepairBillCount(bool again = false)
    {
        if (!core.IsGameStarted || player is null || NetSession.Instance.Transport is not ElinNetHost ||
            (!again && BillsRepaired[0] != 0)) {
            return 0;
        }

        BillsRepaired[0] = 1;
        var found = CountTaxBills();
        var phantom = player.taxBills - found;
        if (phantom <= 0) {
            return 0;
        }

        EmpLog.Information("Unpaid tax bills: the world counted {Count}, {Found} exist: {Phantom} that nothing could pay are taken off",
            player.taxBills, found, phantom);
        player.taxBills = found;
        TeamBills.Fit();
        return phantom;
    }
}
