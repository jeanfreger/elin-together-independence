"""Per-player journal (host setting OwnJournal, NetSessionRules key 150; design: docs_council_journal.md).

    cd dev ; python _tools/mp_test.py ; python <worktree>/bench/journal_check.py [--only j0,j1,j2]

Needs 2 windows: host on 27551, one guest on 27552, both on the home map.

j0  the host's box reaches the guest (on by default; off then on again is told to the guest at once).
"""
import sys
import time

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

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


TESTS = {"j0": j0}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else list(TESTS)
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
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    for label in failed:
        print(f"  FAILED: {label}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
