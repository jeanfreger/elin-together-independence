"""Reliability audit 2026-10-27 #11: a thing a guest takes from a container on the host's map is never lost, and an
answer that comes too late does nothing. Host + 1 guest open on the Prairie, side by side (2 windows).
usage: python bench/thing_request_check.py   (~1 minute)

What it proves:
  1. the host throws right after taking the thing out of the box for the guest (a bench switch makes that step
     throw): the guest is told no, and the thing is back in the box after the host's 10 s. Before the fix the thing
     was out of the box and in nobody's hands for good, and the guest never heard back;
  2. the host's game freezes 9 s just as the guest asks (a long save, a map loading): the answer comes after the
     guest's 8 s, which no longer takes it (a drag or a purchase starting long after the click) and says why; the
     host puts the thing back in the box, the guest's bag does not have it.
Not played as a player: the take is the request the game's drag sends (ThingRequest.Create/Send/Then, as
InvStartDragEvent does), made through the test bridge; the box and the bucket are made by the host's bridge; the
freeze is a sleep of the host's main thread.
"""
import time

from benchlib import A, H, check, ev, eventually, finish, lines, log, now

MAKE = ('var row = EClass.sources.things.rows.First(r => r.trait != null && r.trait.Length > 0 && r.trait[0] == "Container"); '
        'var box = ThingGen.Create(row.id); var p = EClass.pc.pos.Copy(); p.x += 1; p = p.GetNearestPoint(allowChara: false) ?? EClass.pc.pos; '
        'EClass._zone.AddCard(box, p); var b = ThingGen.Create("bucket"); box.AddCard(b); return box.uid + "|" + b.uid;')
SWITCH = ('HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.ThingRequest"), '
          '"BenchThrowAfterTake").SetValue(null, {0}); return "ok";')
IN_BOX = 'var box = EClass._map.things.Find(t => t.uid == {0}); return (box != null && box.things.Find({1}) != null).ToString();'
ASK = ('AppDomain.CurrentDomain.SetData("emp_tr", null); var box = EClass._map.things.Find(t => t.uid == {0}); '
       'var b = box.things.Find({1}); ElinTogether.Models.ThingRequest.Create(b, 1).Send().Then('
       'x => AppDomain.CurrentDomain.SetData("emp_tr", "taken"), () => AppDomain.CurrentDomain.SetData("emp_tr", "refused")); '
       'return "ok";')
ANSWER = 'return (AppDomain.CurrentDomain.GetData("emp_tr") as string) ?? "";'


def main():
    box, bucket = ev(H, MAKE).split("|")
    log(f"box {box}, bucket {bucket}")
    check("0 the guest sees the bucket in the box", eventually(lambda: ev(A, IN_BOX.format(box, bucket)) == "True", timeout=15))

    t0 = now()
    ev(H, SWITCH.format("true"))
    try:
        ev(A, ASK.format(box, bucket))
        check("1 the host says the take failed and the thing goes back (log)",
              eventually(lambda: bool(lines(t0, "failed after taking {Uid}")), timeout=10))
        check("1b the guest is told no", eventually(lambda: ev(A, ANSWER) == "refused", timeout=10))
    finally:
        ev(H, SWITCH.format("false"))
    check("1c the bucket is back in the box on the host", eventually(lambda: ev(H, IN_BOX.format(box, bucket)) == "True", timeout=20))
    check("1d and on the guest", eventually(lambda: ev(A, IN_BOX.format(box, bucket)) == "True", timeout=15))

    t1 = now()
    ev(A, ASK.format(box, bucket))
    ev(H, 'System.Threading.Thread.Sleep(9000); return "ok";', timeout=60)
    check("2 the answer came too late: not taken, and said (log)",
          eventually(lambda: bool(lines(t1, "answered after {Seconds:F1}s, not taken")), timeout=15))
    check("2b the guest's request ends as refused", ev(A, ANSWER) == "refused")
    check("2c the host puts the bucket back in the box", eventually(lambda: ev(H, IN_BOX.format(box, bucket)) == "True", timeout=20))
    time.sleep(2)
    check("2d the guest's bag does not have it",
          ev(A, f'return (EClass.pc.things.Find({bucket}) == null).ToString();') == "True")

    ev(H, f'var box = EClass._map.things.Find(t => t.uid == {box}); if (box != null) box.Destroy(); return "ok";')


if __name__ == "__main__":
    main()
    finish()
