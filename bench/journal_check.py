"""Per-player journal (host setting OwnJournal, NetSessionRules key 150; design: docs_council_journal.md).

    cd dev ; python _tools/mp_test.py ; python <worktree>/bench/journal_check.py [--only j0,j1,j2]

Needs 2 windows: host on 27551, one guest on 27552, both on the home map.

j0  the host's box reaches the guest (on by default; off then on again is told to the guest at once).
j1  message history: a line the guest's game wrote and one the host's game wrote; both walk out to the world map
    (ExitBorder, the path of ownknow_suite) so the guest receives a new copy of the world. On: the guest still has
    its own line and not the host's; the host never gets the guest's. Off (then on again): the guest's history is
    the host's again, the old behaviour (proves the box decides). Red before this part: the guest's line is gone.
    The lines are written with Msg.SayRaw, the call every game message ends in, not by a played action.
Not covered: the file route (the guest closes the game and joins again), three players.
"""
import sys
import time

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from place_suite import back_home, game_id, region_uid  # noqa: E402
from travel_suite import RESULTS, both_joined, check, ev, eventually, scan_logs, wait, zone_uid  # noqa: E402

H, A = 27551, 27552

ASM = 'System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ElinTogether")'
BF = 'System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public'
RULE = 'return ElinTogether.Net.NetSession.Instance.Rules.OwnJournal.ToString();'


def set_own_journal(on):
    """The host ticks or unticks the box; the rules go out as when the settings page changes them."""
    got = ev(H, f'var t = {ASM}.GetType("ElinTogether.EmpConfig+Server"); var e = t.GetProperty("OwnJournal", {BF}); '
                'if (e == null) return "no setting"; var v = e.GetValue(null); '
                f'v.GetType().GetProperty("Value").SetValue(v, {str(on).lower()}); '
                'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("UpdateRemoteSessionRules").GetValue(); '
                'return "ok";')
    check(f"host: OwnJournal set to {on} ({got})", got == "ok")


def j0():
    check(f"host: the box is on by default ({ev(H, RULE)})", ev(H, RULE) == "True")
    check("guest: told the host's rule (on)", eventually(lambda: ev(A, RULE) == "True", timeout=15))
    set_own_journal(False)
    check("guest: told at once when the host unticks it", eventually(lambda: ev(A, RULE) == "False", timeout=15))
    set_own_journal(True)
    check("guest: and when it ticks it again", eventually(lambda: ev(A, RULE) == "True", timeout=15))


def trip():
    """Host then guest walk out to the world map: the guest gets a new copy of the world. False: nothing proved."""
    region = region_uid(H)
    game = game_id(A)
    ev(H, 'EClass.player.ExitBorder(); "ok"')
    wait(lambda: zone_uid(H) == region, "host on the world map", timeout=180)
    time.sleep(4)
    ev(A, 'EClass.player.ExitBorder(); "ok"')
    both_joined(H, A, region)
    time.sleep(3)
    ok = check("the guest received a new copy of the world (else nothing is proved)", game_id(A) != game)
    back_home()
    time.sleep(3)
    return ok


def say(port, text):
    ev(port, f'Msg.SayRaw("{text}"); return "ok";')


def has_line(port, text):
    return ev(port, f'return EClass.game.log.GetList().Exists(d => d.text != null && d.text.Contains("{text}")).ToString();') == "True"


def j1():
    tag = int(time.time()) % 100000
    mine, theirs = f"journal marker guest {tag}", f"journal marker host {tag}"
    say(A, mine)
    say(H, theirs)
    check("guest: its line is in its history", has_line(A, mine))
    if not trip():
        return
    check("on: the guest still has its own line after the trip", has_line(A, mine))
    check("on: the host's line is not in the guest's history", not has_line(A, theirs))
    check("the host never got the guest's line", not has_line(H, mine))

    set_own_journal(False)
    if not trip():
        return
    check("off: the guest's history is the host's again (host's line there)", has_line(A, theirs))
    set_own_journal(True)


TESTS = {"j0": j0, "j1": j1}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else list(TESTS)
    start = time.time()
    from duel_suite import to_home
    to_home()
    for name in only:
        print(f"\n== {name}", flush=True)
        try:
            TESTS[name]()
        except Exception as ex:  # noqa: BLE001
            check(f"{name} interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
    try:
        set_own_journal(True)
    except Exception:  # noqa: BLE001
        pass
    scan_logs(start)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    for label in failed:
        print(f"  FAILED: {label}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
