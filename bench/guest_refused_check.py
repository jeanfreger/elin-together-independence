"""Known bug (dev/TODO_ALL.md group 1): a player refused by the player holding a map received the whole world
(RefuseGuest + SendRejoin). Needs 3 windows: host 27551, guests A 27552 and B 27553, all on the Prairie.
usage: python bench/guest_refused_check.py   (~4 minutes)

What it proves:
  1. A holds Vernis alone and is busy leaving it (its own travel under way: it refuses visitors); B walks to
     Vernis: B is told no and stays on the host's map, playing on, without a copy of the world (same Game object
     before and after). Before the fix B left the host's map first, then came back with the whole world;
  2. A no longer busy: B goes to Vernis as A's visitor (the way that still works: the holder is asked first, then B
     leaves the host's map, then the holder is told where B stands);
  3. both come back to the Prairie.
Not played as a player: "A busy leaving" is A's waiting state set through the test bridge (cleared after 10 s, well
before A's own 45 s deadline); the moves are MoveZone.
"""
import time

from benchlib import A, H, HOME, VERNIS, T, check, client_settled, ev, eventually, finish, lines, log, move, now, wait

B = 27553
BUSY = (f'var z = EClass.game.spatials.Find({HOME}); {T}.Field("_pendingTravel")'
        '.SetValue(((Zone, ZoneTransition)?)(z, new ZoneTransition())); return "ok";')
FREE = f'{T}.Field("_pendingTravel").SetValue(null); return "ok";'
GAME_ID = 'return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(EClass.game).ToString();'
GUEST = 'return ElinTogether.Net.NetSession.Instance.IsGuest.ToString();'


def players(port):
    return int(ev(port, 'return ElinTogether.Net.NetSession.Instance.CurrentPlayers.Count(p => p != null).ToString();'))


def main():
    move(A, VERNIS)
    wait(client_settled(A, VERNIS, True), "A alone in Vernis")

    before = ev(B, GAME_ID)
    t0 = now()
    ev(A, BUSY)
    try:
        move(B, VERNIS)
        check("1 A refuses B (log)", eventually(lambda: bool(lines(t0, "Zone {ZoneUid} refused player {@Peer}")), timeout=15))
        time.sleep(3)
    finally:
        ev(A, FREE)
    check("1b B is still on the host's map, not away", eventually(client_settled(B, HOME, False), timeout=10))
    check("1c B got no copy of the world (same game)", ev(B, GAME_ID) == before)
    check("1d the host still has B on its map (host + B)", players(H) == 2)
    log(f"players seen by the host: {players(H)}")

    move(B, VERNIS)
    check("2 B goes to Vernis as A's visitor", eventually(client_settled(B, VERNIS, True), timeout=120))
    check("2b as a guest of A's map", ev(B, GUEST) == "True")

    move(B, HOME)
    wait(client_settled(B, HOME, False), "B back on the Prairie", timeout=180)
    move(A, HOME)
    wait(client_settled(A, HOME, False), "A back on the Prairie", timeout=180)
    check("3 everyone back on the Prairie", eventually(lambda: players(H) == 3, timeout=60))


if __name__ == "__main__":
    main()
    finish()
