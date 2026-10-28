"""The money a base earns goes to its owner, not to the host (BaseIncome). Host 27551 + guest 27552 (2 players), same
home map, the guest's land claimed by the guest (zone int 47110001 = its character number; set here for the test).
B1 a card the guest put on sale carries the guest's number (the host applied the guest's CardSettingDelta)
B2 an NPC shopper buys it (AI_Shopping.Buy): the branch's incomeShop is not raised, the guest's purse is
B3 the day's inn income of a land the guest owns goes to the guest, the host's purse does not move
B4 a land the host owns still pays the host
Not played like a player: the shopper's purchase and the day's income are called from C#."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")))
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A = 27551, 27552
GOLD = 'return EClass.pc.GetCurrency("money").ToString();'
BRANCH = 'EClass.Branch'


def gold(port):
    return int(ev(port, GOLD))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    me = ev(A, 'return EClass.pc.uid.ToString();')
    zone = 'EClass._zone'
    ev(A, 'var t = ThingGen.Create("log"); EClass._zone.AddCard(t, EClass.pc.pos.GetNearestPoint(false, false) ?? EClass.pc.pos); '
          't.SetSale(true); return t.uid.ToString();')
    uid = ev(H, 'var t = EClass._map.things.Find(x => x.id == "log" && x.isSale); return t == null ? "" : t.uid.ToString();')
    if not check("the sale card exists on the host", uid != ""):
        sys.exit(1)
    check("B1 the host marks the card with the guest's number",
          eventually(lambda: ev(H, f'return EClass._map.things.Find(x => x.uid == {uid}).GetInt("emp_seller").ToString();') == me, timeout=15))
    ev(H, f'{zone}.SetInt(47110001, {me}); return "ok";')

    g0, h0 = gold(A), gold(H)
    inc0 = int(ev(H, f'return {BRANCH}.incomeShop.ToString();'))
    ev(H, f'var c = EClass._map.charas.First(x => !x.IsPC); '
          f'AI_Shopping.Buy(c, EClass._map.things.Find(x => x.uid == {uid}), false, null); return "ok";')
    check("B2 incomeShop did not take the host's price",
          int(ev(H, f'return {BRANCH}.incomeShop.ToString();')) == inc0)
    check("B2 the guest was paid", eventually(lambda: gold(A) > g0, timeout=15))

    g1, h1 = gold(A), gold(H)
    ev(H, f'{BRANCH}.incomeInn = 1000; {BRANCH}.GetDailyIncome(); return "ok";')
    check("B3 the inn income went to the guest", eventually(lambda: gold(A) > g1, timeout=15))
    check("B3 and not to the host", gold(H) == h1)

    ev(H, f'{zone}.SetInt(47110001, EClass.pc.uid); return "ok";')
    ev(H, f'{BRANCH}.incomeInn = 1000; {BRANCH}.GetDailyIncome(); return "ok";')
    check("B4 a land the host owns pays the host", gold(H) > h1)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
