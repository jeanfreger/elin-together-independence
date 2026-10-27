"""World time, stage 3 (council 2026-10-26, PLAN_world_time_mmo.md): an empty map freezes. Host + 1 guest open, both
at the Prairie; they are back there at the end. Copy to dev/_tools or run from here (it imports dev/_tools).

    python bench/world_time_stage3.py [alone,world]

ALONE  "Time stops when nobody plays" on, away after 5 min. The guest goes alone to Vernis (its own map, zone lease)
       and turns idle: its map is frozen and paused; an hour of waiting or rest there (AdvanceMin(60)) does not move
       its date; 15 real seconds do not either; the host, still playing, makes 2 hours pass: the frozen map still
       catches up with the world (a catch-up is not its player's time); the guest plays again: the hour passes. The
       box off, the guest idle again: the hour passes as before. Red before the change: the guest's map ran on while
       its player was away (with autopause off, in real time) and its report pulled the world's date by those hours
       once it played again.
WORLD  hunt5 W08 again (the whole world frozen when the host and the guest are both idle; box off: time goes on).

Not played like a player: the idleness is set by eval (PlayerIdle.LastInput one hour back, not 5 real minutes); the
hour of waiting and the host's 2 hours are GameDate.AdvanceMin by eval; the trip to Vernis is MoveZone (as
travel_suite). Not run: an always-on server (-empserver) with nobody connected (the same code path as the host idle,
EmptyWorld.Frozen with EmpServer.Requested), a visitor on the idle guest's map (the map then goes on, by design).
"""
import sys
import time
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
from hunt5_suite import ASM, BF, SET_IDLE, get_rule, raw_date, rule_on_both, set_rule  # noqa: E402
from travel_suite import (HOME, RESULTS, VERNIS, both_joined, check, client_settled, dismiss_dialogs, ev,  # noqa: E402
                          eventually, move, wait)

H, A = 27551, 27552


def frozen(port):
    return ev(port, f'return {ASM}.GetType("ElinTogether.Patches.EmptyWorld").GetProperty("Frozen", {BF}).GetValue(null).ToString();') == "True"


def paused(port):
    return ev(port, 'return EClass.scene.actionMode.ShouldPauseGame.ToString();') == "True"


def hour_moves(port):
    d0 = raw_date(port)
    ev(port, 'EClass.world.date.AdvanceMin(60); return "ok";')
    return raw_date(port) - d0


def alone():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    print("\n== ALONE a guest alone on its map and away: its clock stops", flush=True)
    preflight()
    saved = (get_rule("IdleMinutes"), get_rule("TimeStopsWhenEmpty"))
    try:
        set_rule("IdleMinutes", 5)
        set_rule("TimeStopsWhenEmpty", "true")
        check("ALONE away after 5 min, time stops when nobody plays, on both games",
              rule_on_both("IdleMinutes", 5) and rule_on_both("TimeStopsWhenEmpty", True))
        move(A, VERNIS)
        wait(client_settled(A, VERNIS, True), "guest alone at Vernis", timeout=180)
        time.sleep(3)
        dismiss_dialogs(A)

        # (the host plays: a minimized bench window has no input of its own, its world would freeze too)
        ev(H, SET_IDLE.format(0))
        ev(A, SET_IDLE.format(3600))
        check(f"ALONE the guest's map is frozen ({frozen(A)}) and paused ({paused(A)})",
              eventually(lambda: frozen(A) and paused(A), timeout=10))
        check("ALONE the host's world is not (the host plays)", not frozen(H))
        moved = hour_moves(A)
        check(f"ALONE an hour of waiting on the guest's map: its date did not move (+{moved} min; before: 60)", moved == 0)
        d0 = raw_date(A)
        time.sleep(15)
        check(f"ALONE 15 real seconds: the guest's date stood still (+{raw_date(A) - d0} min)", raw_date(A) == d0)

        target = raw_date(H) + 120
        ev(H, 'EClass.world.date.AdvanceMin(120); return "ok";')
        check(f"ALONE the host made 2 hours pass: the frozen map caught up ({raw_date(A)} >= {target - 5})",
              eventually(lambda: raw_date(A) >= target - 5, timeout=30))
        check("ALONE and stays frozen after the catch-up", frozen(A))

        ev(A, SET_IDLE.format(0))
        check("ALONE the guest plays again: its map is no longer frozen", eventually(lambda: not frozen(A), timeout=10))
        h0 = raw_date(H)
        moved = hour_moves(A)
        check(f"ALONE the guest plays: the hour passes on its map (+{moved} min)", moved >= 60)
        # (its report of that hour moves the world by it, not by the time it was away: under 2 hours)
        time.sleep(5)
        check(f"ALONE the world moved by the guest's hour only (+{raw_date(H) - h0} min, under 120)", raw_date(H) - h0 < 120)

        set_rule("TimeStopsWhenEmpty", "false")
        check("ALONE box off on both games", rule_on_both("TimeStopsWhenEmpty", False))
        ev(A, SET_IDLE.format(3600))
        check("ALONE box off, the guest idle: its map is not frozen", eventually(lambda: not frozen(A), timeout=5) and not frozen(A))
        moved = hour_moves(A)
        check(f"ALONE box off, the guest idle: the hour passes as before (+{moved} min)", moved >= 60)
    finally:
        ev(A, SET_IDLE.format(0))
        set_rule("IdleMinutes", saved[0])
        set_rule("TimeStopsWhenEmpty", saved[1].lower())
        if not eventually(client_settled(A, HOME, False), timeout=5):
            move(A, HOME)
        both_joined(H, A, HOME)
        cleanup()


def world():
    from hunt5_suite import w08  # noqa: PLC0415
    print("\n== WORLD hunt5 W08: the whole world frozen", flush=True)
    w08()


STEPS = {"alone": alone, "world": world}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    names = sys.argv[1].split(",") if len(sys.argv) > 1 else list(STEPS)
    for name in names:
        try:
            STEPS[name]()
        except Exception as ex:  # noqa: BLE001
            check(f"{name} interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} checks OK")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
