"""Hunt 6 item 3, the arrival through a world copy: the guest walks from its own world map into the host's town and
gets its travel experience (skill 240) once, its steps counted from 0, its last town the new one. The fix is
OwnerPartyPatch.PayTravel, called from the first scene of a new world (ElinNetClientZone.OnZoneActivateResponse,
player.zone null), where Player.MoveZone never ran. Needs 2 windows (host 27551, guest 27552). Proves what hunt6_suite
H603 plays (steps by TryMove, the town entered by Player.EnterLocalZone); this script only runs it.

    cd dev && PYTHONPATH=_tools/pylib python ../bench/h603_world_copy_travel_check.py
"""
import sys
from pathlib import Path

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from hunt6_suite import h603  # noqa: E402
from travel_suite import RESULTS  # noqa: E402

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    h603()
    bad = [r for r in RESULTS if not r[0]] if RESULTS and isinstance(RESULTS[0], tuple) else []
    sys.exit(1 if bad else 0)
