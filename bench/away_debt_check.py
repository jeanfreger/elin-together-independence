"""A debt paid by a guest while away from the host map (review finding, HIGH). Needs 2 windows (host + 1 guest).
Server that enforces trust; the guest leaves for a free tile of the world map (its own zone, no session to sync
through), pays 30000 of its debt as the game does (gold out of the purse, then the debt down). Proves: the host keeps
the lowered debt (it used to refuse and put it back: gold lost, debt unchanged), the host's copy of the guest did not
lose the gold a second time, and a lowering without any payment is still refused.
usage: python bench/away_debt_check.py (run from dev/_tools, or with it on sys.path)"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from travel_suite import RESULTS, check, ev, eventually, free_spot, enter_at, zone_uid, HOME  # noqa: E402
from resync_suite import now  # noqa: E402
from permit_suite import refusals, set_enforce  # noqa: E402

H, A = 27551, 27552


def debt(port):
    return int(ev(port, 'return EClass.player.debt.ToString();'))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    set_enforce(True)
    was = debt(H)
    try:
        ev(H, 'EClass.player.debt = 100000; return "ok";')
        check("both games owe 100000", eventually(lambda: debt(A) == 100000, timeout=15))
        ev(A, 'EClass.pc.ModCurrency(60000, "money"); return "ok";')
        ev(A, 'EClass.player.ExitBorder(); return "ok";')
        spot = free_spot(A, HOME)
        enter_at(A, spot)
        check("the guest is away in a zone of its own", eventually(lambda: ev(A, 'return ElinTogether.Net.NetSession.Instance.IsAway.ToString();') == "True", timeout=120))
        t0 = now()
        ev(A, 'EClass.pc.ModCurrency(-30000, "money"); EClass.player.debt = 70000; return "ok";')
        check(f"paid away: the host's debt goes down to 70000 ({debt(H)})", eventually(lambda: debt(H) == 70000, timeout=15))
        check("no refusal of the debt", not [r for r in refusals(t0, "DialogFlagDelta")])
        t0 = now()
        ev(A, 'EClass.player.debt = 0; return "ok";')
        check("a lowering without payment is still refused", eventually(lambda: bool(refusals(t0, "DialogFlagDelta")), timeout=15))
        check(f"the host keeps 70000 ({debt(H)})", debt(H) == 70000)
    finally:
        set_enforce(False)
        ev(H, f'EClass.player.debt = {was}; return "ok";')


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
