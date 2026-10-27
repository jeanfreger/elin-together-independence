"""Reliability audit 2026-10-27 #3: a travel that never ends has a deadline and a clean way back.
Host + 1 guest open on the Prairie (2 windows). usage: python bench/travel_watchdog_check.py   (~5 minutes)

What it proves:
  1. a lease request whose answer never comes (made here by marking the request sent without sending it: the same
     state as a host that threw in the middle of it) is given up after 45 s: the guest asks to come back, the host,
     which still has it on its map, takes it off and sends the world (before the fix it ignored that release and the
     guest refused every travel for good, "a request is in flight");
  2. the guest is back on the host's map, two players on both sides, no lease left, and can travel again
     (Vernis and back);
  3. a return whose world never comes (marked as sent, never sent) ends after 90 s: the guest drops the link and
     joins again by itself (AutoReconnect, on by default), two players again.
Not played as a player: the lost answer is made by setting the guest's waiting state through the test bridge; the
travel of 2 is MoveZone.
"""
import time

from benchlib import (A, H, HOME, VERNIS, T, both_joined, check, client_settled, ev, eventually, finish, leases, lines,
                      log, move, now, wait)

PENDING = (f'var z = EClass.game.spatials.Find({VERNIS}); {T}.Field("_pendingTravel")'
           '.SetValue(((Zone, ZoneTransition)?)(z, new ZoneTransition())); return "ok";')
REJOINING = f'{T}.Field("_rejoining").SetValue(true); return "ok";'


def players(port):
    return int(ev(port, 'return ElinTogether.Net.NetSession.Instance.CurrentPlayers.Count(p => p != null).ToString();'))


def main():
    t0 = now()
    ev(A, PENDING)
    log("guest waits for a lease that was never asked")
    time.sleep(3)
    move(A, VERNIS)
    time.sleep(2)
    check("0 meanwhile the guest goes nowhere (a request is in flight)", not ev(A, 'return (EClass._zone.uid == ' + str(VERNIS) + ').ToString();') == "True")

    check("1 after 45 s the guest gives up and asks to come back",
          eventually(lambda: bool(lines(t0, "got no answer for")), timeout=70))
    check("1b the host takes it off its map and sends the world",
          eventually(lambda: bool(lines(t0, "asks to come back while on the host map")), timeout=20))
    wait(client_settled(A, HOME, False), "guest back on the Prairie", timeout=180)
    check("2 two players on both sides, no lease left",
          eventually(lambda: players(H) == 2 and players(A) == 2 and leases(H) == 0, timeout=60))

    move(A, VERNIS)
    check("2b the guest travels again", eventually(client_settled(A, VERNIS, True), timeout=120))
    move(A, HOME)
    both_joined(H, A, HOME)

    t1 = now()
    ev(A, REJOINING)
    log("guest waits for a world that was never asked")
    check("3 after 90 s the guest drops the link to join again",
          eventually(lambda: bool(lines(t1, "No world {Seconds:F0}s after handing back")), timeout=120))
    wait(client_settled(A, HOME, False), "guest joined again", timeout=240)
    check("3b two players again", eventually(lambda: players(H) == 2 and players(A) == 2, timeout=60))


if __name__ == "__main__":
    main()
    finish()
