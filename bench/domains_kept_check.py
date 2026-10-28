"""A guest's magic domains survive the host resending the world (OwnSettings.Keep/Carry; Player.RefreshDomain reset them
to the job's at every copy). Host 27551 + guest 27552 (2 players), both on the home map.
The guest's domains are set to a marker list (not the job's), it follows the host through the world map so it receives
a new copy, then its domains must still be the marker list and the host's own must not have it.
Red before the fix: the guest's list is the job's. Run from the main checkout's dev/_tools path (helpers live there)."""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")))
from mp_test import state  # noqa: E402
from place_suite import back_home, game_id, region_uid  # noqa: E402
from travel_suite import RESULTS, both_joined, check, ev, scan_logs, wait, zone_uid  # noqa: E402

H, A = 27551, 27552
READ = 'return string.Join(",", EClass.player.domains);'
MARK = "6,7,9"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    start = time.time()
    from duel_suite import to_home
    to_home()
    if not check("both players are on the same map", (state(H).get("zone") or {}).get("uid") == (state(A).get("zone") or {}).get("uid")):
        sys.exit(1)
    host_before = ev(H, READ)
    ev(A, f'EClass.player.domains = new List<int> {{ {MARK} }}; return "ok";')
    check("guest: the marker domains are set", ev(A, READ) == MARK)
    region = region_uid(H)
    game = game_id(A)
    ev(H, 'EClass.player.ExitBorder(); return "ok";')
    wait(lambda: zone_uid(H) == region, "host on the world map", timeout=180)
    time.sleep(4)
    ev(A, 'EClass.player.ExitBorder(); return "ok";')
    both_joined(H, A, region)
    time.sleep(3)
    if check("the guest received a new copy of the world", game_id(A) != game):
        check(f"guest: the domains are kept ({ev(A, READ)})", ev(A, READ) == MARK)
        check(f"host: its own domains did not change ({ev(H, READ)})", ev(H, READ) == host_before)
    back_home()
    scan_logs(start)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
