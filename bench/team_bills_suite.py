"""Teams pay their own land, commit 8 of dev/PLAN_teams_knowledge.md. Needs 3 windows: host + 2 guests already in
game, same map, two teams (the host and guest A in one, guest B in another):

    cd dev && set PYTHONPATH=_tools/pylib
    python _tools/mp_test.py --clients 2
    python ../bench/team_bills_suite.py

  P0 box on: both guests are told the rule; the host founds a team, A joins it, B founds its own (the team menus'
     own entries); the world's lands are given to the teams (the first to B's team, a second one, if any, to the
     host's); the world's old unpaid bills are set aside (count 0)
  P1 the month: one tax bill per team that holds land, each marked with its team; the counts go up for those teams
     only; the world's count is their sum; both guests' team cards show the same counts as the host's
  P2 B pays its team's bill at the tax chest (the game's PayBill in B's own game, as a drop in the chest does): the
     host takes B's gold, B's team's count goes down, the other team's does not
  P3 consequences: B's team owes 4, the month makes it 5: B loses 50 karma, A and the host (another team) do not
  P4 box off: the next month makes one bill for the world again, with no team

Not played like a player: the month is run by C# (Faction.OnAdvanceMonth, what the date calls on the first day),
lands are given to teams by C# (claiming with a deed needs a trip), the bill reaches B's bag from the mail by C#.
Proves: who is billed, the per-team counts, a guest's payment lowering its own team, karma for one team only.
"""
import sys
import time

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A, B = 27551, 27552, 27553
BF = 'System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public'
T = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams")'
TB = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamBills")'
HOST = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost")'
CFG = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpConfig+Server")'
OWED = f'((System.Collections.Generic.Dictionary<int, int>){HOST}.GetProperty("TeamTaxBills", {BF}).GetValue(null))'
ZONES = f'((System.Collections.Generic.Dictionary<int, int>){HOST}.GetProperty("ZoneTeams", {BF}).GetValue(null))'
TELL = 'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("TellTeams").GetValue();'


def uid(port):
    return int(ev(port, 'return EClass.pc.uid.ToString();'))


def config(name, value=None):
    """A host setting: read it, or set it (then the guests are told the rules)"""
    get = f'var e = {CFG}.GetProperty("{name}", {BF}).GetValue(null); var p = e.GetType().GetProperty("Value");'
    if value is None:
        return ev(H, f'{get} return p.GetValue(e).ToString();') == "True"
    ev(H, f'{get} p.SetValue(e, {str(value).lower()}); return "";')
    ev(H, 'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("UpdateRemoteSessionRules").GetValue(); return "";')
    return value


def guest_rule(port):
    return ev(port, 'return ElinTogether.Net.NetSession.Instance.Rules.TeamBills.ToString();') == "True"


def team_of(charauid):
    return int(ev(H, f'return System.Convert.ToString({T}.GetMethod("TeamOf", {BF}).Invoke(null, new object[] {{ {charauid} }}));'))


def menu(port, who, pick):
    """Runs the first entry of the team menu of that player's row whose label `l` passes `pick`, as a click does"""
    return ev(port, f'var m = (System.Collections.IEnumerable){T}.GetMethod("MenuFor", {BF}).Invoke(null, new object[] {{ {who} }}); '
                    'foreach (var e in m) { var l = (string)e.GetType().GetField("Item1").GetValue(e); '
                    f'if ({pick}) {{ ((System.Action)e.GetType().GetField("Item2").GetValue(e))(); return l; }} }} return "";')


def unpaid(port, team):
    """The unpaid bills that game shows for that team (its team card)"""
    return int(ev(port, f'return System.Convert.ToString({TB}.GetMethod("UnpaidOf", {BF}).Invoke(null, new object[] {{ {team} }}));'))


def owed(team):
    return int(ev(H, f'return {OWED}.TryGetValue({team}, out var n) ? n.ToString() : "0";'))


def world_bills():
    return int(ev(H, 'return EClass.player.taxBills.ToString();'))


def karma(port):
    return int(ev(port, 'return EClass.player.karma.ToString();'))


def month():
    ev(H, 'EClass.pc.faction.OnAdvanceMonth(); return "ok";')


def mail_bills(team):
    """Uids of the tax bills of that team waiting in the mail (refVal = team + 1)"""
    got = ev(H, f'return string.Join(",", EClass.game.cards.listPackage.Where(t => t.id == "bill_tax" && t.refVal == {team + 1}).Select(t => t.uid));')
    return [int(x) for x in got.split(",") if x]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a, b = uid(H), uid(A), uid(B)
    was = {name: config(name) for name in ("Teams", "TeamBills", "TeamApproval")}
    bills0 = world_bills()
    karma0 = {p: karma(p) for p in (H, A, B)}
    zones0 = ev(H, f'return string.Join(",", {ZONES}.Select(z => z.Key + ":" + z.Value));')
    try:
        print("P0 box on, two teams, their lands")
        config("Teams", True)
        config("TeamApproval", False)
        config("TeamBills", True)
        check("both guests are told the rule", eventually(lambda: guest_rule(A) and guest_rule(B), timeout=15))
        menu(H, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("the host leads a team", eventually(lambda: team_of(h) != 0, timeout=10))
        red = team_of(h)
        time.sleep(2)
        menu(A, f'EClass.game.cards.globalCharas.Find({h})', 'true')
        check("A joined the host's team", eventually(lambda: team_of(a) == red, timeout=15))
        menu(B, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("B leads a team of its own", eventually(lambda: team_of(b) not in (0, red), timeout=15))
        blue = team_of(b)
        lands = [int(x) for x in ev(H, 'return string.Join(",", EClass.pc.faction.GetChildren().Select(br => br.owner.uid));').split(",") if x]
        if not check(f"the world holds land ({lands})", lands):
            return
        ev(H, f'{ZONES}.Clear(); {ZONES}[{lands[0]}] = {blue}; ' + (f'{ZONES}[{lands[1]}] = {red}; ' if len(lands) > 1 else '') +
           f'{OWED}.Clear(); EClass.player.taxBills = 0; EClass.player.extraTax = 0; {TELL} return "ok";')
        red_land = len(lands) > 1
        # (more lands: the others are the Wanderers', who get a bill too)
        wanderer_land = len(lands) > 2

        print("P1 the month")
        month()
        check(f"B's team owes 1 ({owed(blue)})", owed(blue) == 1)
        check(f"the host's team owes {int(red_land)} ({owed(red)})", owed(red) == int(red_land))
        check(f"the Wanderers owe {int(wanderer_land)} ({owed(0)})", owed(0) == int(wanderer_land))
        teams_billed = 1 + int(red_land) + int(wanderer_land)
        check(f"the world's count is the sum ({world_bills()})", world_bills() == teams_billed)
        blue_bills = mail_bills(blue)
        check(f"B's team's bill is in the mail, marked ({blue_bills})", len(blue_bills) == 1)
        for port, name in ((A, "A"), (B, "B")):
            check(f"{name}'s team card shows B's team 1 and the host's {int(red_land)}",
                  eventually(lambda: unpaid(port, blue) == 1 and unpaid(port, red) == int(red_land), timeout=15))

        print("P2 B pays its team's bill")
        if blue_bills:
            amount = int(ev(H, f'return EClass.game.cards.listPackage.First(t => t.uid == {blue_bills[0]}).c_bill.ToString();'))
            ev(H, f'var c = EClass.game.cards.globalCharas.Find({b}); c.AddThing(ThingGen.Create("money").SetNum({amount + 1000}), false); '
                  f'var bill = EClass.game.cards.listPackage.First(t => t.uid == {blue_bills[0]}); EClass.game.cards.listPackage.Remove(bill); '
                  'c.AddThing(bill, false); return "ok";')
            check("the bill reached B's bag", eventually(lambda: ev(B, f'return (EClass.pc.things.Find(x => x.uid == {blue_bills[0]}) != null).ToString();') == "True", timeout=15))
            ev(B, f'InvOwnerDeliver.PayBill(EClass.pc.things.Find(x => x.uid == {blue_bills[0]})); return "ok";')
            check("B's team owes nothing now", eventually(lambda: owed(blue) == 0, timeout=15))
            check(f"the host's team still owes {int(red_land)}", owed(red) == int(red_land))
            check(f"the world's count went down by one ({world_bills()})", world_bills() == teams_billed - 1)
            check("B's team card says 0", eventually(lambda: unpaid(B, blue) == 0, timeout=15))

        print("P3 four unpaid bills cost one team's players karma")
        principal = ev(H, 'return EClass.game.principal.tax.ToString();') == "True"
        personal = ev(H, 'return ElinTogether.Net.NetSession.Instance.Rules.UsePersonalQuests.ToString();') == "True"
        keeper = ev(H, 'return ElinTogether.Net.NetSession.Instance.Rules.UseWorldKeeper.ToString();') == "True"
        if check(f"the world asks karma for unpaid bills, karma is each player's ({principal}, {personal}, {keeper})",
                 principal and personal and keeper):
            for port in (H, A, B):
                ev(port, 'EClass.player.karma = 0; return "ok";')
            time.sleep(3)
            ev(H, f'{OWED}.Clear(); {OWED}[{blue}] = 4; EClass.player.taxBills = 4; {TELL} return "ok";')
            month()
            check(f"B's team owes 5 ({owed(blue)})", owed(blue) == 5)
            check(f"B lost 50 karma ({karma(B)})", eventually(lambda: karma(B) == -50, timeout=15))
            time.sleep(3)
            check(f"A (the other team) did not ({karma(A)})", karma(A) == 0)
            check(f"the host (the other team) did not ({karma(H)})", karma(H) == 0)

        print("P4 box off: one bill for the world")
        config("TeamBills", False)
        before = world_bills()
        ev(H, 'foreach (var t in EClass.game.cards.listPackage.Where(t => t.id == "bill_tax").ToList()) EClass.game.cards.listPackage.Remove(t); return "ok";')
        month()
        check(f"one bill more for the world ({world_bills()})", world_bills() == before + 1)
        check("and it is no team's", ev(H, 'return EClass.game.cards.listPackage.Count(t => t.id == "bill_tax" && t.refVal == 0).ToString();') == "1")
    finally:
        config("TeamBills", False)
        ev(H, 'foreach (var t in EClass.game.cards.listPackage.Where(t => t.id == "bill_tax").ToList()) EClass.game.cards.listPackage.Remove(t); '
              f'{OWED}.Clear(); EClass.player.taxBills = {bills0}; {ZONES}.Clear(); return "ok";')
        for pair in [z for z in zones0.split(",") if z]:
            zone, team = pair.split(":")
            ev(H, f'{ZONES}[{zone}] = {team}; return "ok";')
        for port, value in karma0.items():
            ev(port, f'EClass.player.karma = {value}; return "ok";')
        for who in (h, a, b):
            ev(H, f'{T}.GetMethod("Leave", {BF}).Invoke(null, new object[] {{ EClass.game.cards.globalCharas.Find({who}) }}); return "";')
        for name, value in was.items():
            config(name, value)
        print(f"{sum(ok for _, ok in RESULTS)}/{len(RESULTS)}")


if __name__ == "__main__":
    main()
