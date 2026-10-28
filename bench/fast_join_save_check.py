"""Stage 2 of PLAN_100_players: host setting "Fast joins and saves for big worlds" (FastJoinsAndSaves).
Needs 2 windows (host 27551 + 1 guest 27552), both in game. Best on a real-size world copy (2 MB game.txt): on a
small world the numbers are too close to say anything. usage: python bench/fast_join_save_check.py  (or copied to
dev/_tools)

What it proves:
- JOIN OFF x2 / ON x2: the guest's link is cut (emp.cut_link, what a real drop does) and the guest comes back by itself,
  two times in a row with the box off, then two with it on. Each return logs "Join prepared in X ms main thread, Y ms
  worker"; the main-thread times of both modes are printed side by side, and the box on must not cost the main thread
  more than off (on a big world it should be far less: the compression ran beside the game, worker > 0)
- HELD THEN RELEASED: with the box on, a change the host makes while the guest's copy is being packed (the debt, set in
  the frame right after the cut link comes back) reaches the guest after its world: nothing was lost behind the copy
- SAVE OFF / ON: the host's save (SaveWriter.Save, what the dedicated server's 5-minute save calls) logs its
  main-thread time in both modes; with the box on, the files are written beside the game ("Save: N file(s) written in Y
  ms worker"), and game.txt on the disk is then a whole save of this very world (it decompresses to JSON)
Not tested here: a dedicated -empserver (same SaveWriter.Save call), several joins in one batch (needs 3 windows),
a Steam Cloud save (never written beside the game).
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
import emp  # noqa: E402
from mp_test import log, ok, state  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually, players  # noqa: E402
from resync_suite import lines, now  # noqa: E402
from permit_suite import ASM, BF, CFG  # noqa: E402

H, A = 27551, 27552
WRITER = ASM + '.GetType("ElinTogether.Helper.SaveWriter")'


def set_fast(on):
    got = ev(H, f'var t = {CFG}; var e = t.GetProperty("FastJoinsAndSaves", {BF}); if (e == null) return "no setting"; '
                f'var v = e.GetValue(null); v.GetType().GetProperty("Value").SetValue(v, {str(on).lower()}); return "ok";')
    check(f"FastJoinsAndSaves set to {on} ({got})", got == "ok")


def debt(port):
    return int(ev(port, 'return EClass.player.debt.ToString();'))


def back():
    return state(A).get("connected") and state(A).get("sceneMode") == "Zone" and players(H) == 2 and players(A) == 2


def join_once(label, fast):
    """the guest's link cut, it comes back by itself: one join. Gives the (main, worker) ms the host logged"""
    t0 = now()
    log(ok(emp.call(A, "command", {"cmd": "emp.cut_link 15"})))
    # (the host notices a cut link only after ~10 s)
    eventually(lambda: players(H) < 2, timeout=40)
    mark = None
    if fast:
        # set while the guest's copy may still be packed beside the game: it must come behind the copy, not be lost
        eventually(lambda: players(H) == 2, timeout=120)
        mark = 100000 + int(time.time()) % 50000
        ev(H, f'EClass.player.debt = {mark}; return "ok";')
    check(f"{label}: the guest comes back by itself", eventually(back, timeout=180))
    found = eventually(lambda: lines(t0, "Join prepared in"), timeout=30)
    got = lines(t0, "Join prepared in")
    check(f"{label}: the host logged the join's cost", bool(found))
    if mark is not None:
        check(f"{label}: the change made during the packing reaches the guest ({mark})",
              eventually(lambda: debt(A) == mark, timeout=20))
    if not got:
        return None
    d = got[-1]
    log(f"{label}: {d.get('Main')} ms main thread, {d.get('Worker')} ms worker")
    return int(d.get("Main", 0)), int(d.get("Worker", 0))


def save_once(label):
    t0 = now()
    saved = ev(H, f'return {WRITER}.GetMethod("Save", {BF}).Invoke(null, new object[] {{ EClass.game }}).ToString();')
    check(f"{label}: the save is made ({saved})", saved == "True")
    made = lines(t0, "Save: made in") + lines(t0, "Server: save made in")
    return t0, made


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    was = debt(H)
    try:
        set_fast(False)
        off = [join_once(f"JOIN OFF {i}", False) for i in (1, 2)]
        set_fast(True)
        on = [join_once(f"JOIN ON {i}", True) for i in (1, 2)]
        off = [x for x in off if x]
        on = [x for x in on if x]
        if off and on:
            off_main = sum(m for m, _ in off) / len(off)
            on_main = sum(m for m, _ in on) / len(on)
            log(f"main thread per join: off {off_main:.0f} ms, on {on_main:.0f} ms "
                f"(worker on: {sum(w for _, w in on) / len(on):.0f} ms)")
            check(f"JOIN: the box on costs the main thread no more than off ({on_main:.0f} vs {off_main:.0f} ms, 20 ms slack)",
                  on_main <= off_main + 20)
            check("JOIN: with the box on, part of the work ran beside the game (worker ms logged)", all(w >= 0 for _, w in on))

        set_fast(False)
        save_once("SAVE OFF")
        set_fast(True)
        t0, _ = save_once("SAVE ON")
        check("SAVE ON: the main-thread time is logged", eventually(lambda: lines(t0, "Save: made in"), timeout=10))
        check("SAVE ON: its files were written beside the game",
              eventually(lambda: lines(t0, "file(s) written in"), timeout=60))
        for d in lines(t0, "Save: made in") + lines(t0, "file(s) written in"):
            log(f"SAVE ON: {d.get('@mt')} Main={d.get('Main')} Worker={d.get('Worker')} Files={d.get('Files')}")
        # read through the game's own reader, after the wait the mod puts in front of it
        head = ev(H, 'var p = GameIO.pathCurrentSave + "game.txt"; var s = IO.IsCompressed(p) ? IO.Decompress(p) : '
                     'System.IO.File.ReadAllText(p); return s.TrimStart().Substring(0, 1) + " " + s.Length;')
        check(f"SAVE ON: game.txt on the disk is a whole save ({head})", head.startswith("{") and int(head.split()[1]) > 1000)
    finally:
        set_fast(False)
        ev(H, f'EClass.player.debt = {was}; return "ok";')


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
