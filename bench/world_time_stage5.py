"""World time, stage 5 (council 2026-10-26, PLAN_world_time_mmo.md): a long catch-up is capped and spread over frames,
each daily event once, on the host; the cap is the host setting "Catch-up lives" (CatchUpDays: 1/3/7 days, 7).
Host + 1 guest open at the Prairie; they are back there at the end.

    python bench/world_time_stage5.py [host3,away3,week]

HOST3  "Catch-up lives" 3 days, on both games. A 10-day catch-up on the host (a player's long trip told to it): 7 days
       skipped over, the last 3 lived a day per frame (3 steps at least); the host counts 3 new days (stats.days);
       the guest, on the host's map, counts 3 days too and ends on the host's day (World.dayData: luck and seed, drawn
       by the world keeper only and told); both dates reach the target; a quest of the guest loses none of the 10
       days (another player's time, the 7 skipped over included).
AWAY3  the same cap on a map a guest holds alone: the guest at Vernis, idle (its map frozen, stage 3); the host lives
       10 days (AdvanceMin); the guest's map catches up: 7 days skipped over, the last 3 lived over 3 frames at least,
       its date reaches the world's. Red before: a jump over a month of the world never caught up at all, and a
       guest's map used its own machine's cap, not the host's.
WEEK   hunt5 W10 with the default (7 days) back.

Not played like a player: the catch-up is WorldDateAdvanceEvent.CatchUpNextFrame by eval (what a trip report does),
the host's 10 days are GameDate.AdvanceMin by eval, idleness by eval (PlayerIdle.LastInput), the trip to Vernis is
MoveZone. "Each daily event once on the host" is read from the day counts and the told day data, not by counting
every daily hook (the guest's copies of the world hooks are skipped by WorldKeeper, UseWorldKeeper on).
"""
import sys
import time
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
from hunt5_suite import ASM, BF, SET_IDLE, WDE, get_rule, raw_date, rule_on_both, set_rule  # noqa: E402
from travel_suite import (HOME, RESULTS, VERNIS, both_joined, check, client_settled, dismiss_dialogs, ev,  # noqa: E402
                          eventually, move, wait)

H, A = 27551, 27552
DAY = 24 * 60


def days(port):
    return int(ev(port, 'return EClass.player.stats.days.ToString();'))


def last(port, name):
    return int(ev(port, f'return {WDE}.GetProperty("{name}", {BF}).GetValue(null).ToString();'))


def day_seed(port):
    """The day's data the world keeper drew (World.dayData), as text: a new one each lived day."""
    return ev(port, 'var d = EClass.world.dayData; return d == null ? "-" : d.seed + "/" + d.luck;')


def catch_up(port, target):
    ev(port, f'{WDE}.GetMethods({BF}).First(m => m.Name == "CatchUpNextFrame" && m.GetParameters()[0].ParameterType == typeof(int))'
             f'.Invoke(null, new object[] {{ {target}, null, 0 }}); return "ok";')


def set_cap(n):
    set_rule("CatchUpDays", n)
    return rule_on_both("CatchUpDays", n)


def host3():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    from quest_suite import accept, in_log, offer  # noqa: PLC0415
    print("\n== HOST3 a 10-day catch-up on the host, 3 days lived", flush=True)
    preflight()
    saved = (get_rule("CatchUpDays"), get_rule("PersonalQuests"))
    quest = None
    try:
        set_rule("PersonalQuests", "true")
        check("HOST3 catch-up lives 3 days, personal quests, on both games", set_cap(3) and rule_on_both("UsePersonalQuests", True))
        quest, _ = offer(A)
        accept(A, quest)
        eventually(lambda: in_log(A, quest), timeout=15)
        ev(A, f'var q = EClass.game.quests.list.Find(x => x.uid == {quest}); q.deadline = EClass.world.date.GetRaw() + {14 * DAY}; return "ok";')
        left = lambda: int(ev(A, f'var q = EClass.game.quests.list.Find(x => x.uid == {quest}); '  # noqa: E731
                                 'return q == null ? "-99999" : (q.deadline - EClass.world.date.GetRaw()).ToString();'))
        left0 = left()
        hd0, gd0, t0 = days(H), days(A), raw_date(H)
        target = t0 + 10 * DAY
        catch_up(H, target)
        check(f"HOST3 the host's date reached the target (+{raw_date(H) - t0} of {10 * DAY} min)",
              eventually(lambda: raw_date(H) >= target, timeout=60))
        steps, collapsed = last(H, "LastCatchUpSteps"), last(H, "LastCatchUpCollapsed")
        check(f"HOST3 spread over {steps} frames (3 at least), {collapsed} min skipped over ({7 * DAY})",
              steps >= 3 and collapsed == 7 * DAY)
        check(f"HOST3 the host counted 3 days ({hd0} -> {days(H)}; 7 with the default, 10 before stage 5)", days(H) - hd0 == 3)
        check(f"HOST3 the guest's date reached the target (+{raw_date(A) - t0} min)", eventually(lambda: raw_date(A) >= target, timeout=30))
        check(f"HOST3 the guest counted 3 days ({gd0} -> {days(A)})", eventually(lambda: days(A) - gd0 == 3, timeout=20))
        check(f"HOST3 the guest's day is the host's ({day_seed(A)} / {day_seed(H)})", eventually(lambda: day_seed(A) == day_seed(H), timeout=15))
        check(f"HOST3 the guest's quest lost none of the 10 days ({left0} -> {left()} min)", abs(left() - left0) <= 60)
    finally:
        if quest:
            ev(A, f'var q = EClass.game.quests.list.Find(x => x.uid == {quest}); if (q != null) EClass.game.quests.list.Remove(q); return "ok";')
        set_rule("CatchUpDays", saved[0])
        set_rule("PersonalQuests", saved[1].lower())
        cleanup()


def away3():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    print("\n== AWAY3 a map held alone and frozen catches up 10 days, 3 lived", flush=True)
    preflight()
    saved = (get_rule("CatchUpDays"), get_rule("IdleMinutes"), get_rule("TimeStopsWhenEmpty"))
    try:
        set_rule("IdleMinutes", 5)
        set_rule("TimeStopsWhenEmpty", "true")
        check("AWAY3 catch-up lives 3 days, time stops when nobody plays, on both games",
              set_cap(3) and rule_on_both("TimeStopsWhenEmpty", True))
        move(A, VERNIS)
        wait(client_settled(A, VERNIS, True), "guest alone at Vernis", timeout=180)
        time.sleep(3)
        dismiss_dialogs(A)
        # (the host plays: a minimized bench window has no input of its own, its world would freeze too)
        ev(H, SET_IDLE.format(0))
        ev(A, SET_IDLE.format(3600))
        frozen = f'return {ASM}.GetType("ElinTogether.Patches.EmptyWorld").GetProperty("Frozen", {BF}).GetValue(null).ToString();'
        check("AWAY3 the guest's map is frozen", eventually(lambda: ev(A, frozen) == "True", timeout=10))
        gd0, a0 = days(A), raw_date(A)
        ev(H, f'EClass.world.date.AdvanceMin({10 * DAY}); return "ok";')
        target = raw_date(H)
        check(f"AWAY3 the guest's map reached the world's date (+{raw_date(A) - a0} of {target - a0} min)",
              eventually(lambda: raw_date(A) >= target, timeout=60))
        steps, collapsed = last(A, "LastCatchUpSteps"), last(A, "LastCatchUpCollapsed")
        check(f"AWAY3 spread over {steps} frames (3 at least), {collapsed} min skipped over (about {7 * DAY})",
              steps >= 3 and abs(collapsed - (target - a0 - 3 * DAY)) <= 60)
        check(f"AWAY3 the guest counted 3 days ({gd0} -> {days(A)})", days(A) - gd0 == 3)
    finally:
        ev(A, SET_IDLE.format(0))
        set_rule("CatchUpDays", saved[0])
        set_rule("IdleMinutes", saved[1])
        set_rule("TimeStopsWhenEmpty", saved[2].lower())
        if not eventually(client_settled(A, HOME, False), timeout=5):
            move(A, HOME)
        both_joined(H, A, HOME)
        cleanup()


def week():
    from hunt5_suite import w10  # noqa: PLC0415
    print("\n== WEEK hunt5 W10 with the default cap", flush=True)
    check("WEEK catch-up lives 7 days, on both games", set_cap(7))
    w10()


STEPS = {"host3": host3, "away3": away3, "week": week}


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
