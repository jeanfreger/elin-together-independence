using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.LangMod;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-25, host option "Teams pay their own land" (dev/PLAN_teams_knowledge.md). The game makes one
///     tax bill a month for every land of the player faction, on the host's fame, and counts the unpaid ones for the
///     world. With the box on, each team that holds land gets its own bill, for its own lands (lands of no team: the
///     Wanderers'): the base tax and the fame tax of its members' highest fame, lowered by its own lands' tax evasion.
///     Each team's unpaid bills are counted apart (<see cref="ElinNetHost.TeamTaxBills" />); four or more cost karma
///     to that team's players only. A bill knows its team by its <c>refVal</c> (team + 1), which also keeps two
///     teams' bills from stacking. The world's count (<c>player.taxBills</c>) stays the sum, so a vanilla save, or the
///     box off, finds every bill as the game counts them. Bills older than the box belong to no team: they weigh on
///     every team, as the world's did
/// </summary>
[HarmonyPatch]
internal static class TeamBills
{
    // the game that keeps the book: the host of the world, or a game playing alone
    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    /// <summary>
    ///     The box as this game knows it: the host's own setting, a guest's from the host's rules
    /// </summary>
    internal static bool On => KeepsBook
        ? EmpConfig.Server.Teams.Value && EmpConfig.Server.TeamBills.Value
        : NetSession.Instance.Rules is { UseTeams: true, TeamBills: true };

    /// <summary>
    ///     The team a tax bill was made for, null for one of no team (older than the box)
    /// </summary>
    internal static int? TeamOf(Thing bill)
    {
        return bill.id == "bill_tax" && bill.refVal > 0 ? bill.refVal - 1 : null;
    }

    private static Dictionary<int, int> Owed => ElinNetHost.TeamTaxBills;

    /// <summary>
    ///     The world's unpaid bills that are no team's
    /// </summary>
    private static int Unassigned => Mathf.Max(0, EClass.player.taxBills - Owed.Values.Sum());

    /// <summary>
    ///     The unpaid bills that weigh on that team: its own and the world's of no team
    /// </summary>
    internal static int UnpaidOf(int team)
    {
        if (KeepsBook) {
            return Owed.GetValueOrDefault(team) + Unassigned;
        }

        var told = Teams.ToldBills;
        return told.GetValueOrDefault(team) + told.GetValueOrDefault(-1);
    }

    /// <summary>
    ///     What every game is told with the teams: none while the box is off
    /// </summary>
    internal static Dictionary<int, int>? Told()
    {
        return On ? new Dictionary<int, int>(Owed) { [-1] = Unassigned } : null;
    }

    /// <summary>
    ///     A tax bill was paid in the game that keeps the book (the world's count already went down): its team's too
    /// </summary>
    internal static void OnPaid(int? team)
    {
        if (!KeepsBook || team is not { } id || Owed.GetValueOrDefault(id) <= 0) {
            return;
        }

        if (--Owed[id] == 0) {
            Owed.Remove(id);
        }

        EmpLog.Information("Team {TeamId} paid a tax bill, {Count} left unpaid", id, Owed.GetValueOrDefault(id));
        Fit();
        Tell();
    }

    /// <summary>
    ///     The teams' counts never hold more than the world's (lowered by the repair of phantom bills, by a cheat):
    ///     the largest is lowered first
    /// </summary>
    internal static void Fit()
    {
        while (Owed.Count > 0 && Owed.Values.Sum() > EClass.player.taxBills) {
            var most = Owed.OrderByDescending(o => o.Value).First().Key;
            if (--Owed[most] <= 0) {
                Owed.Remove(most);
            }
        }
    }

    private static void Tell()
    {
        (NetSession.Instance.Transport as ElinNetHost)?.TellTeams();
    }

    // a bill paid by the game's own PayBill (by hand at the tax chest, or by the bank): destroyed when paid
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InvOwnerDeliver), nameof(InvOwnerDeliver.PayBill))]
    private static void OnPayBill(Thing t, out bool __state)
    {
        __state = t is { id: "bill_tax", isDestroyed: false };
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InvOwnerDeliver), nameof(InvOwnerDeliver.PayBill))]
    private static void OnPaidBill(Thing t, bool __state)
    {
        // (box off too: a bill of a team paid meanwhile is no longer that team's when it is ticked again)
        if (__state && t.isDestroyed) {
            OnPaid(TeamOf(t));
        }
    }

    /// <summary>
    ///     The month in the game that keeps the book (only the world's keeper runs it, see WorldKeeperHooks): the
    ///     game's own salary, then a bill per team instead of the world's one. Box off: the game's own
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(Faction), nameof(Faction.OnAdvanceMonth))]
    private static bool OnMonth(Faction __instance, bool __runOriginal)
    {
        if (!__runOriginal || !KeepsBook || !On || __instance != EClass.pc.faction) {
            return __runOriginal;
        }

        var branches = __instance.GetChildren();
        if (branches.Count == 0) {
            return false;
        }

        PaySalary(__instance, branches);

        // the extra tax a player chose to give is the world's: on the first bill only (each story its own: on its
        // team's bill, see TeamOwnStory)
        var extra = EClass.player.extraTax;
        foreach (var lands in branches.GroupBy(b => Teams.TeamOfZone(b.owner)).OrderBy(g => g.Key).ToArray()) {
            var team = lands.Key;
            extra = TeamOwnStory.ExtraTaxOf(team) ?? extra;
            var evasion = lands.Sum(b => b.Evalue(2119));
            var tax = Evade(__instance.GetBaseTax(false), evasion) + Evade(FameTax(TeamFame(team)), evasion) + extra;
            var bill = ThingGen.CreateBill(tax, true);
            bill.SetInt(35, extra);
            bill.refVal = team + 1;
            extra = 0;
            Owed[team] = Owed.GetValueOrDefault(team) + 1;
            EmpLog.Information("Team {TeamId}: a tax bill of {Tax} for {Lands} lands", team, tax, lands.Count());
            Msg.Say("emp_team_bill".Loc(Teams.NameOf(team), Lang._currency(tax, "money")));
            // (paid by the bank at once when the policy says so: OnPaidBill lowers the team's count)
            __instance.TryPayBill(bill);
        }

        Msg.Say("bills", EClass.player.taxBills.ToString());
        if (EClass.game.principal.tax && !EClass.debug.godMode) {
            Punish();
        }

        Tell();
        return false;
    }

    // as Faction.OnAdvanceMonth gives the month's income
    private static void PaySalary(Faction faction, List<FactionBranch> branches)
    {
        var num = 0;
        num += faction.GetResidentTax();
        num += faction.GetRankIncome();
        num += faction.GetFactionSalary();
        if (num <= 0) {
            return;
        }

        var bank = EClass.game.cards.container_deposit;
        if (EClass.pc.homeBranch.policies.IsActive(2711) && (bank.GetCurrency() > 0 || !bank.things.IsFull())) {
            bank.ModCurrency(num);
            Msg.Say("bankIncome", Lang._currency(num, "money"));
            return;
        }

        // one share per team, paid to its members (their own accounts) instead of the world's mailbox: what a team's
        // lands earn is not the host's. The guilds' salary (what the lands do not explain) goes with the first team
        // that has players, like the extra tax
        var shares = branches.GroupBy(b => Teams.TeamOfZone(b.owner)).OrderBy(g => g.Key)
            .Select(g => (Members: Teams.MembersOf(g.Key).ToArray(),
                Share: g.Sum(b => b.GetResidentTax() + EClass.game.spatials.ranks.GetIncome(b.owner)))).ToList();
        var salary = Math.Max(0, num - shares.Sum(s => s.Share));
        var host = NetSession.Instance.Transport as ElinNetHost;
        var left = num;
        foreach (var (members, own) in shares) {
            var share = own;
            if (members.Length > 0) {
                share += salary;
                salary = 0;
            }

            share = Math.Min(share, left);
            if (host is null || members.Length == 0 || share <= 0) {
                continue;
            }

            left -= share;
            foreach (var uid in members) {
                host.PayPlayer(uid, share / members.Length + (uid == members[0] ? share % members.Length : 0));
            }
        }

        // (lands of no team, and what the split left: the world's mailbox, as before)
        if (left > 0) {
            EClass.world.SendPackage(ThingGen.CreateParcel("parcel_salary", ThingGen.Create("money").SetNum(left)));
        }
    }

    // as Faction.EvadeTax, with the evasion of one team's lands
    private static int Evade(int v, int evasion)
    {
        return (int)(long)(v * 100f / (100f + Mathf.Sqrt(evasion * 5)));
    }

    // as Faction.GetFameTax, on that fame
    private static int FameTax(int fame)
    {
        var num = EClass.curve(fame * 2, 10000, 2000, 80);
        return num is > 1000000 or < 0 ? 1000000 : num;
    }

    /// <summary>
    ///     The highest fame of that team's players (the Wanderers: the players in no team). Without personal fame,
    ///     the world's
    /// </summary>
    private static int TeamFame(int team)
    {
        if (!PersonalQuests.Enabled) {
            return EClass.player.fame;
        }

        return ElinNetHost.PlayerCharaUids().Where(uid => Teams.TeamOf(uid) == team)
            .Select(ElinNetHost.FameOfPlayer).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    ///     Four unpaid bills or more cost 50 karma to the players of that team only (the game: to "the player" when
    ///     the world has four). Without personal karma there is one karma for everyone: once
    /// </summary>
    private static void Punish()
    {
        var teams = Teams.Ids().Where(team => UnpaidOf(team) >= 4).ToArray();
        if (teams.Length == 0) {
            return;
        }

        if (!PersonalQuests.Enabled) {
            EClass.player.ModKarma(-50);
            return;
        }

        foreach (var team in teams) {
            EmpLog.Information("Team {TeamId}: {Count} unpaid tax bills, 50 karma from its players", team, UnpaidOf(team));
            if (Teams.TeamOf(EClass.player.uidChara) == team) {
                EClass.player.ModKarma(-50);
            }

            // (the others' karma is in their own games: told by the keeper, as WorldKeeperPlayers does)
            if (NetSession.Instance.Transport is ElinNetHost { IsZoneSession: false } host && NetSession.Instance.Rules.UseWorldKeeper) {
                host.GiveKarmaToTeam(team, -50);
            }
        }
    }
}
