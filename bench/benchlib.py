"""Shared bits of the reliability bench checks (audit 2026-10-27): finds the test bench of dev/_tools (the main
checkout keeps it, it is not in the repository) and gives its helpers. Host on port 27551, guest on 27552."""
import sys
from pathlib import Path

_HERE = Path(__file__).resolve().parent
for _tools in (_HERE.parent / "dev" / "_tools", Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")):
    if (_tools / "travel_suite.py").exists():
        sys.path.insert(0, str(_tools))
        break

from mp_test import log, wait  # noqa: E402,F401
from travel_suite import (HOME, RESULTS, VERNIS, both_joined, check, client_settled, ev, eventually,  # noqa: E402,F401
                          leases, marker, move, on_map, zone_uid)
from resync_suite import lines, now  # noqa: E402,F401

H, A = 27551, 27552

# the transport of a game, for its private members
T = "HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport)"


def call(port, method, *args):
    """A private method of the transport of that game, by name"""
    arg = ", ".join(args)
    return ev(port, f'return ({T}.Method("{method}"{", " + arg if arg else ""}).GetValue() ?? "null").ToString();')


def finish():
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
