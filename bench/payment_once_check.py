"""One payment counts once (review finding, MEDIUM-LOW). Needs 2 windows (host + 1 guest), trust enforced.
Gold spent inside a shop transaction is marked 'shop' (the test sets the same mark the Transaction.Process patch sets,
through the private field, then pays as the game does) and does not also pay a debt: the debt lowered by that amount is
refused and the host keeps it. Control: a plain payment, then the debt lowered by it, goes through.
The build refund (deferred one flush, taken from 'build' spends only) is not driven here: it needs a failing build
click in the game.
usage: python bench/payment_once_check.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402
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
        ev(A, 'EClass.pc.ModCurrency(100000, "money"); return "ok";')
        # goods: the purse goes down inside a shop transaction (the patch marks the spend 'shop')
        mark = 'HarmonyLib.Traverse.Create(typeof(ElinTogether.Patches.CardModCurrencyEvent)).Field("_purpose").SetValue('
        ev(A, mark + '"shop"); EClass.pc.ModCurrency(-20000, "money"); ' + mark + '(string)null); return "ok";')
        t0 = now()
        ev(A, 'EClass.player.debt = 80000; return "ok";')
        check("goods gold does not pay the debt as well (refused, log)",
              eventually(lambda: bool(refusals(t0, "DialogFlagDelta")), timeout=15))
        check(f"the host keeps the debt at 100000 ({debt(H)})", debt(H) == 100000)
        # control: a plain payment
        ev(A, 'EClass.pc.ModCurrency(-20000, "money"); EClass.player.debt = 80000; return "ok";')
        check(f"a payment made for the debt goes through ({debt(H)})", eventually(lambda: debt(H) == 80000, timeout=15))
    finally:
        set_enforce(False)
        ev(H, f'EClass.player.debt = {was}; return "ok";')


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
