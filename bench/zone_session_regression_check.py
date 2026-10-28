"""Regression of the normal zone-session path after the virtual-port / Local-flag change (no-host decision
2026-10-28): a guest holds a zone, another guest visits it. Needs 3 windows (host 27551, guests A 27552 and
B 27553, all on the Prairie). usage: python bench/zone_session_regression_check.py   (~3 minutes)

What it proves (the real walk, as security8_zone_check does):
  1. A walks to Vernis and holds it alone
  2. B walks to Vernis: it becomes A's visitor in a zone session (guest, zone_session "Client") - the bench link
     is the local port, so this is the Local flag set by the holder (the visitor dials 127.0.0.1:port)
  3. A's log says it accepted the guest, the host's log says B joins A's zone session
  4. both go back to the Prairie
Existing suites that cover zone sessions on the same path: security8_zone_check.py (holder + visitor, trust
rules), shared_suite.py and trio_suite.py (settled(..., zone_session="Client"): B visits A, A visits B), the
zone-session part of travel_suite.py (s14-s15), base_core_zone_check.py. Run shared_suite after this one.
Not covered here (needs a hidden server, no bench for it yet): VirtualPort = 1 of the owner seat.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from benchlib import A, H, HOME, VERNIS, check, client_settled, ev, eventually, finish, lines, move, now, wait  # noqa: E402

B = 27553
GUEST = 'return ElinTogether.Net.NetSession.Instance.IsGuest.ToString();'


def main():
    try:
        t0 = now()
        move(A, VERNIS)
        wait(client_settled(A, VERNIS, True), "A alone in Vernis", timeout=180)
        move(B, VERNIS)
        check("2 B walks into A's map", eventually(client_settled(B, VERNIS, True), timeout=180))
        check("2b as a guest of a zone session", ev(B, GUEST) == "True")
        check("3 A accepted the guest (log)", bool(lines(t0, "Guest {RemoteIdentity} for zone {ZoneUid}: {Accepted}")))
        check("3b the host logged the join", bool(lines(t0, "joins the zone session of")))
        check("3c no refusal of the visitor", not lines(t0, "Refused {@Visitor} on the map of {@Holder}"))
    finally:
        for port in (B, A):
            move(port, HOME)
            wait(client_settled(port, HOME, False), f"{port} back on the Prairie", timeout=180)


if __name__ == "__main__":
    main()
    finish()
