using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

public enum BillAnswer : byte
{
    /// <summary>
    ///     A request, client to host
    /// </summary>
    None,

    /// <summary>
    ///     Paid, told to everyone: Payer, Id and Amount say what
    /// </summary>
    Done,

    /// <summary>
    ///     The bill is already paid, or not the sender's to pay
    /// </summary>
    Gone,

    /// <summary>
    ///     The sender's gold no longer covers it
    /// </summary>
    Poor,

    /// <summary>
    ///     A request, client to host: a player alone on another map paid this bill with its own gold, in its own game
    ///     (it keeps no tax chest of the host's). The host lowers its counter once and tells everyone
    /// </summary>
    Paid,

    /// <summary>
    ///     Told to everyone: Payer put Amount of gold in the bank
    /// </summary>
    BankIn,

    /// <summary>
    ///     Told to everyone: Payer took Amount of gold out of the bank
    /// </summary>
    BankOut,
}

/// <summary>
///     A bill (tax or delivery) put in the tax chest by a player who is not the host: the counters of the bills
///     (<c>player.taxBills</c>, <c>unpaidBill</c>) are the host's, a guest's copy of the world never has them. The guest
///     asks, the host reads the bill and the gold again, takes the gold of the sender, lowers its counter once and
///     tells everyone. A second request for the same bill finds it destroyed and is answered <see cref="BillAnswer.Gone" />,
///     before any gold moves
/// </summary>
[MessagePackObject]
public class BillPayDelta : ElinDelta
{
    // the last line shown, for the test bench
    internal static string LastLine = "";

    [Key(0)]
    public RemoteCard? Bill { get; init; }

    [Key(1)]
    public BillAnswer Answer { get; init; }

    [Key(2)]
    public string? Payer { get; init; }

    [Key(3)]
    public string? Id { get; init; }

    [Key(4)]
    public int Amount { get; init; }

    /// <summary>
    ///     With <see cref="BillAnswer.Paid" />: what the game gives back for the extra tax (the bill's int 35 / 1000)
    /// </summary>
    [Key(5)]
    public int Gift { get; init; }

    /// <summary>
    ///     With <see cref="BillAnswer.Paid" />: the team the tax bill was made for, -1 for none (see TeamBills)
    /// </summary>
    [Key(6)]
    public int Team { get; set; } = -1;

    internal static void Send(Thing bill)
    {
        if (NetSession.Instance.Connection is ElinNetClient client) {
            client.Delta.AddRemote(new BillPayDelta { Bill = bill });
        }
    }

    /// <summary>
    ///     Alone on another map, the game paid the bill itself (its gold, its copy of the counters): the host's
    ///     counter is told, the host reads nothing it could not know (the bill is in this player's hands). Also from
    ///     a zone session (a guest of another player, or the holder of a zone with guests): the link to the host of
    ///     the world is the main one
    /// </summary>
    internal static void SendAway(Thing bill)
    {
        if (NetSession.Instance is { IsAway: true, Transport: ElinNetClient main }) {
            main.SendWhileAway(new BillPayDelta {
                Answer = BillAnswer.Paid,
                Payer = EClass.pc.NameSimple,
                Id = bill.id,
                Amount = bill.c_bill,
                Gift = bill.GetInt(35) / 1000,
                Team = TeamBills.TeamOf(bill) ?? -1,
            });
        }
    }

    /// <summary>
    ///     Gold put in or taken out of the bank by a player: a line for everyone, the host's own included. Alone,
    ///     nobody is told
    /// </summary>
    internal static void TellBank(ElinNetHost host, string who, int amount, bool deposit)
    {
        if (amount <= 0 || !NetCompany.HasCompany) {
            return;
        }

        var answer = deposit ? BillAnswer.BankIn : BillAnswer.BankOut;
        host.SendDeltaToAllExcept(-1, new BillPayDelta { Answer = answer, Payer = who, Amount = amount });
        ShowBank(answer, who, amount);
    }

    private static void ShowBank(BillAnswer answer, string who, int amount)
    {
        var id = answer == BillAnswer.BankIn ? "emp_ui_bank_deposit" : "emp_ui_bank_withdraw";
        Msg.Say(LastLine = id.Loc(who, Lang._currency(amount, "money")));
    }

    /// <summary>
    ///     The host paid, or served a payment: the line is shown here and sent to every player
    /// </summary>
    internal static void Tell(ElinNetHost host, string payer, string id, int amount)
    {
        EmpLog.Information("Bill {Id} of {Amount} paid by {Payer}", id, amount, payer);
        host.SendDeltaToAllExcept(-1, new BillPayDelta { Answer = BillAnswer.Done, Payer = payer, Id = id, Amount = amount });
        Show(payer, id, amount);
    }

    private static void Show(string payer, string id, int amount)
    {
        var name = sources.things.map.TryGetValue(id, out var row) ? row.GetName() : id;
        Msg.Say(LastLine = "emp_ui_bill_paid".Loc(payer, name, Lang._currency(amount, "money")));
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (Answer == BillAnswer.Paid) {
            // only from a player away from this map: on it, the host does the paying (see Pay)
            if (net is ElinNetHost { IsZoneSession: false } away && away.IsAwayPeer(OriginPeer)) {
                SettleAway(away);
            }

            return;
        }

        if (Answer != BillAnswer.None) {
            if (net.IsClient) {
                OnAnswer();
            }

            return;
        }

        if (net is not ElinNetHost { IsZoneSession: false } host) {
            return;
        }

        // as the host's own gesture, so that the gold and the bill reach the players like any change made here
        using var simulate = Simulate();
        var sender = host.ActiveRemoteCharas.GetValueOrDefault(OriginPeer);
        var answer = sender is null || host.IsAwayPeer(OriginPeer) ? BillAnswer.Gone : Pay(host, sender);
        if (answer != BillAnswer.Done) {
            host.SendDeltaTo(OriginPeer, new BillPayDelta { Answer = answer });
        }
    }

    // all of it is read again from this game, whatever the sender believed
    private BillAnswer Pay(ElinNetHost host, Chara sender)
    {
        if (Bill?.Find() is not Thing { isDestroyed: false } bill || bill.id is not ("bill_tax" or "bill") || bill.c_bill <= 0 ||
            (bill.GetRootCard() is Chara { IsPlayer: true } holder && holder != sender)) {
            return BillAnswer.Gone;
        }

        var tax = bill.id == "bill_tax";
        if (tax && player.taxBills <= 0) {
            return BillAnswer.Gone;
        }

        var amount = bill.c_bill;
        if (sender.GetCurrency() < amount) {
            return BillAnswer.Poor;
        }

        sender.ModCurrency(-amount);
        LowerCounters(tax, amount, bill.GetInt(35) / 1000, TeamBills.TeamOf(bill));

        bill.Destroy();
        Tell(host, sender.NameSimple, bill.id, amount);
        return BillAnswer.Done;
    }

    // what the game does to the counters of the bills (InvOwnerDeliver.PayBill), once
    private static void LowerCounters(bool tax, int amount, int gift, int? team)
    {
        if (tax) {
            player.stats.taxBillsPaid += amount;
            player.taxBills = Math.Max(0, player.taxBills - 1);
            TeamBills.OnPaid(team);
            // what the game gives back for the extra tax
            if (gift > 0) {
                world.SendPackage(ThingGen.CreateParcel("parcel_mysiliaGift", ThingGen.Create("money2", "copper").SetNum(gift)));
            }
        } else {
            player.unpaidBill -= amount;
        }
    }

    // a player away from this map paid with its own gold: only the counter is left to lower, and only while
    // it says there is something to pay (a repeated word of the same player finds it at 0). The bill is in the
    // sender's hands, so its words are kept within what this game knows: the amount within the tax of the
    // moment (or the delivery bills owed), the gift within what the bill can hold <br />
    // No more "one a player every 10 s": a player who paid three late bills in a row at the tax chest of
    // Palmia had only the first one counted, the host stayed in debt (real game, 2026-10-10)
    private void SettleAway(ElinNetHost host)
    {
        var tax = Id == "bill_tax";
        if (Id is not ("bill_tax" or "bill") || Amount <= 0 || (tax && player.taxBills <= 0)) {
            return;
        }

        var amount = Math.Min(Amount, tax ? EClass.Home?.GetTotalTax(false) ?? 0 : player.unpaidBill);
        if (amount <= 0) {
            return;
        }

        // the gift is the extra tax of the bill / 1000, and the extra tax is a part of the bill's amount
        // (the team is the bill's as its holder read it: a team that owes nothing is not lowered, the world's is)
        LowerCounters(tax, amount, Math.Clamp(Gift, 0, amount / 1000), Team >= 0 ? Team : null);
        Tell(host, Payer ?? "", Id!, amount);
    }

    private void OnAnswer()
    {
        if (Answer is BillAnswer.BankIn or BillAnswer.BankOut) {
            SE.Pay();
            ShowBank(Answer, Payer ?? "", Amount);
            return;
        }

        if (Answer == BillAnswer.Done) {
            SE.Pay();
            Show(Payer ?? "", Id ?? "", Amount);
            return;
        }

        SE.Beep();
        Msg.Say(Answer == BillAnswer.Poor ? "notEnoughMoney" : "emp_ui_bill_already_paid".Loc());
    }
}
