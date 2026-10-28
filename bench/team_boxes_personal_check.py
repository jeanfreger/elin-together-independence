"""Personal bank AND team bank (council 2026-10-28 amendment, ElinTogether/Helper/TeamBoxes.cs).
Needs 2 windows: host 27551 + guest 27552, both on the host's map.
usage: cd dev && set PYTHONPATH=_tools/pylib && python ../bench/team_boxes_personal_check.py
(the game's script engine cannot see internal types: everything goes by reflection)

What it proves (the host and the guest share one team, the box on):
- P1 each has its own personal bank (different boxes), both see the same team bank, and the bank window the game
  opens for each shows the personal bank first, the team bank beside it
- P2 gold put in the personal bank stays there, gold put in the team bank stays there (the guest's copy reaches
  the host's real box as any card change does)
- P3 TeamBoxes.CanTake: a player may take from its own personal bank and the team's, not from the other's personal
- P4 the guest's copies hold its own gold and the team's, not the host's (a personal bank's deltas reach its player)
- P5 the guest leaves the team: it keeps its personal bank, loses the team one (no copy of it any more)
- P6 unticked: personal banks are merged back into the world's bank with the team's (gold summed)
Not played like a player: the gold goes in by C# as a drop does, the team by Teams.Found / Join.
"""
import sys
import time

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from team_lore_suite import BF, H, A, config, team_of, uid  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

TB = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamBoxes")'
T = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams")'
CLOSE = 'foreach (var l in EClass.ui.layers.OfType<LayerInventory>().ToList()) l.Close();'
WORLD = 'int.Parse(ev)'


def tb(method, *args):
    """C# expression: a static method of TeamBoxes called by reflection"""
    return f'{TB}.GetMethod("{method}", {BF}).Invoke(null, new object[] {{ {", ".join(args)} }})'


def personal(u):
    return tb("Personal", str(u))


def team_box(team):
    return tb("Box", str(team), "0")


def gold_of(box_expr):
    return int(ev(H, f'return ((Thing){box_expr}).GetCurrency().ToString();'))


def world_gold():
    return int(ev(H, 'return EClass.game.cards.container_deposit.GetCurrency().ToString();'))


def windows(port):
    """Open the bank like the banker's dialog: "uid:gold;uid:gold" of every container window the game opened"""
    return ev(port, CLOSE + ' LayerInventory.CreateContainer(EClass.game.cards.container_deposit); '
                    'var r = string.Join(";", EClass.ui.layers.OfType<LayerInventory>().Select(l => l.Inv.Container.uid + ":" + l.Inv.Container.GetCurrency())); '
                    + CLOSE + ' return r;')


def copies(port):
    return ev(port, f'var t = {tb("Replica", "2")}; return ((Thing){tb("Replica", "0")}).GetCurrency() + "," + (t == null ? "none" : ((Thing)t).GetCurrency().ToString());')


def leave(port_chara):
    ev(H, f'{T}.GetMethod("Leave", {BF}).Invoke(null, new object[] {{ {port_chara} }}); return "";')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a = uid(H), uid(A)
    guest = f'EClass.game.cards.globalCharas.Find({a})'
    was = {name: config(name) for name in ("Teams", "TeamApproval", "TeamBoxes")}
    try:
        config("Teams", True)
        config("TeamApproval", False)
        config("TeamBoxes", True)
        leave("EClass.pc")
        leave(guest)
        team = int(ev(H, f'return {T}.GetMethod("Found", {BF}).Invoke(null, new object[] {{ EClass.pc, "BenchTeam" }}).ToString();'))
        ev(H, f'return {T}.GetMethod("Join", {BF}).Invoke(null, new object[] {{ {guest}, {team} }}).ToString();')
        check("P0 host and guest are in one team", eventually(lambda: team_of(h) == team and team_of(a) == team, timeout=15))
        check("P0 the guest was sent copies of its boxes", eventually(
            lambda: ev(A, f'return ({tb("Replica", "0")} != null).ToString();') == "True", timeout=20))

        print("P1 who has what")
        uids = ev(H, f'return ((Thing){personal(h)}).uid + "," + ((Thing){personal(a)}).uid + "," + ((Thing){team_box(team)}).uid;').split(",")
        check(f"two different personal banks and a team bank ({uids})", len(set(uids)) == 3)
        hw, aw = windows(H).split(";"), windows(A).split(";")
        check(f"the host's windows: personal then team ({hw})", hw[0].startswith(uids[0] + ":") and hw[1].startswith(uids[2] + ":"))
        check(f"the guest's windows: its personal then the team's ({aw})", aw[0].startswith(uids[1] + ":") and aw[1].startswith(uids[2] + ":"))

        print("P2 a deposit lands where it was dropped")
        ev(H, f'((Thing){personal(h)}).AddThing(ThingGen.Create("money").SetNum(100)); return "";')
        ev(A, f'((Thing){tb("Replica", "0")}).AddThing(ThingGen.Create("money").SetNum(30)); return "";')
        ev(H, f'((Thing){team_box(team)}).AddThing(ThingGen.Create("money").SetNum(500)); return "";')
        check("the guest's 30 reached its real personal bank", eventually(lambda: gold_of(personal(a)) == 30, timeout=20))
        check("the host's 100 and the team's 500 are where they were put", gold_of(personal(h)) == 100 and gold_of(team_box(team)) == 500)

        print("P3 who may take")
        can = ev(H, f'var g = {guest}; '
                    f'var inHost = ((Thing){personal(h)}).things.Find("money"); '
                    f'var inTeam = ((Thing){team_box(team)}).things.Find("money"); '
                    f'var inGuest = ((Thing){personal(a)}).things.Find("money"); '
                    f'Func<Chara, Thing, bool> C = (c, x) => (bool){tb("CanTake", "c", "x")}; '
                    'return C(g, inHost) + "," + C(EClass.pc, inHost) + "," + C(g, inTeam) + "," + C(g, inGuest) + "," + C(EClass.pc, inGuest);')
        check(f"the guest cannot take from the host's personal bank, can from the team's and its own; the host not from the guest's ({can})",
              can == "False,True,True,True,False")

        print("P4 the guest's copies are its own")
        check(f"the guest's copies: personal 30, team 500 ({copies(A)})", eventually(lambda: copies(A) == "30,500", timeout=15))

        print("P5 the guest leaves the team")
        leave(guest)
        check("the guest keeps its personal bank (30) and has no team copy", eventually(lambda: copies(A) == "30,none", timeout=20))
        check("the team's 500 stayed with the team", gold_of(team_box(team)) == 500)

        print("P6 unticked")
        total = gold_of(personal(h)) + gold_of(personal(a)) + gold_of(team_box(team))
        world0 = world_gold()
        config("TeamBoxes", False)
        check(f"all three banks merged back into the world's (+{total})", eventually(lambda: world_gold() == world0 + total, timeout=15))
    finally:
        config("TeamBoxes", False)
        time.sleep(3)
        leave("EClass.pc")
        leave(guest)
        for name, value in was.items():
            config(name, value)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
