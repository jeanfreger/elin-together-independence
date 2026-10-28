"""Host/guest audit item 2: the world-map auto-eat (Chara.InstantEat, no eating task) spends the food on the host too.
Needs 2 windows (host 27551 + guest 27552). usage: python bench/autoeat_spends_food_check.py

What it proves: the host puts a stack of 5 "bread" in the guest character's bag (it reaches the guest as a host-owned
card); the guest's character eats it through InstantEat, the call the world map makes for hungry party members
(no AI_Eat): the guest's stack and the HOST's copy of it both drop to 4. Before the fix the host kept 5.
Not tested: the same while the guest stands on another map than the host (the away whitelist,
ElinNetHost.OwnGrowth, is read in the code only).
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import check, ev, eventually  # noqa: E402

H, A = 27551, 27552
GUEST_ON_HOST = 'EClass._map.charas.First(c => c.IsRemotePlayer)'
BREAD = 'c.things.Find("bench_bread")'


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ok = total = 0

    def verify(label, good):
        nonlocal ok, total
        total += 1
        ok += bool(good)
        check(label, good)

    ev(H, f'var c = {GUEST_ON_HOST}; var t = ThingGen.Create("bread"); t.SetNum(5); t.c_idRefName = "bench_bread"; '
          'c.AddThing(t); return t.uid.ToString();')
    has = lambda port, who: ev(port, f'var t = {who}.things.Find(x => x.id == "bread" && x.c_idRefName == "bench_bread"); '
                                     'return t == null ? "-" : t.Num.ToString();')
    me = 'EClass.pc'
    verify("the guest got the stack", eventually(lambda: has(A, me) == "5", timeout=20))
    ev(A, f'var t = {me}.things.Find(x => x.id == "bread" && x.c_idRefName == "bench_bread"); '
          f'{me}.InstantEat(t, false); return "ok";')
    time.sleep(3)
    got_a = has(A, me)
    got_h = has(H, GUEST_ON_HOST)
    verify(f"guest: 4 left ({got_a})", got_a == "4")
    verify(f"host: its copy is 4 too ({got_h})", got_h == "4")
    print(f"{ok}/{total} verifications OK")


if __name__ == "__main__":
    main()
