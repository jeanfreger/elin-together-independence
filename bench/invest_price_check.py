"""Investment size is the game's price, not the sender's word (review finding, MEDIUM). Needs 2 windows (host + 1 guest),
both on a town map, trust enforced. The guest pays 1 gold and sends a ZoneInvestDelta of Cost 1 (a forged 'cheap
investment'): refused, the host's zone investment does not move. Then it pays the game's real price (CalcMoney.InvestZone
read in the guest's game) and sends that: accepted, the investment goes up by it. usage: python bench/invest_price_check.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402
from resync_suite import now  # noqa: E402
from permit_suite import refusals, send_raw, set_enforce, M  # noqa: E402

H, A = 27551, 27552


def invested(port):
    return int(ev(port, 'return EClass._zone.investment.ToString();'))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    set_enforce(True)
    try:
        ev(A, 'EClass.pc.ModCurrency(10000000, "money"); return "ok";')
        base = invested(H)
        ev(A, 'EClass.pc.ModCurrency(-1, "money"); return "ok";')
        t0 = now()
        send_raw(A, f'new {M}ZoneInvestDelta {{ Cost = 1 }}')
        check("a 1 gold investment is refused as under the price (log)",
              eventually(lambda: any("price" in (x.get("Reason") or "") for x in refusals(t0, "ZoneInvestDelta")), timeout=15))
        check(f"the zone investment did not move ({invested(H)} == {base})", invested(H) == base)

        price = int(ev(A, 'return CalcMoney.InvestZone(EClass.pc).ToString();'))
        ev(A, f'EClass.pc.ModCurrency(-{price}, "money"); return "ok";')
        t0 = now()
        send_raw(A, f'new {M}ZoneInvestDelta {{ Cost = {price} }}')
        check(f"the real price ({price}) goes through: investment +{price}",
              eventually(lambda: invested(H) == base + price, timeout=15))
        check("no refusal of it", not refusals(t0, "ZoneInvestDelta"))
    finally:
        set_enforce(False)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
