"""H5-04 (encounters): an encounter on the world map is rolled by the game of the player who walks into it, with that
player's own fame. No code change: this test proves the reading of 2026-10-27.

Why no fix is needed (read in the game and the mod): Chara.MoveZone on the world map calls
Player.EnterLocalZone(encounter) in the walking player's own game; the field it makes is a zone of that game
(ElinNetClient._localZones), leased and simulated there, and ZonePreEnterEncounter.Execute runs when that game
enters it, reading its own EClass.pc.FameLv (a guest's fame lives in its game, PersonalStateDelta). The same holds for
what a map makes when it is generated or regenerated (message bottles on a beach, Zone.Generate; the mad rich man of a
town, Zone_Town.OnRegenerate): the game that first enters it makes it, with its own player's fame, as "the one who
visits" (user decision H5-04). A guest's catch and craft made by the host's game use the guest's fame through
RemoteCraft.AsCrafter (AIFishPatch, crafting; commit 3120d64). Not driven here: bottles, the mad rich man, the
crafted treasure map's level cap.

Host fame 0, guest fame 8000 (FameLv 81): the guest goes out to the world map alone and walks into a travelling
merchant's encounter (as Chara.cs does when a merchant stands on its tile: EnterLocalZone(encounter: true, mob)). The
merchant's level is Max(20, FameLv * 2): 162 for the guest, it would be 20 with the host's fame.
Not played like a player: the fames are set by eval, the encounter is entered by EnterLocalZone with a merchant made by
CharaGen.Create (no walking on the world map until a merchant crosses the path).

Host + 1 client open (ports 27551 / 27552), both at the Meadow (HOME).
usage (from dev/, PYTHONPATH=_tools/pylib): python <worktree>/bench/h504_encounter.py
"""
import sys
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import hunt5_suite as h5  # noqa: E402
from mp_test import state  # noqa: E402
from travel_suite import (HOME, RESULTS, both_joined, check, client_settled, dismiss_dialogs, enter_at, ev,  # noqa: E402
                          eventually, free_spot, wait)

H, A = 27551, 27552


def main():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    sys.stdout.reconfigure(encoding="utf-8")
    print("H504e a travelling merchant met by the guest has the guest's level (guest fame 8000, host 0)")
    preflight()
    host0, guest0 = int(ev(H, "return EClass.player.fame.ToString();")), int(ev(A, "return EClass.player.fame.ToString();"))
    region = int(ev(A, "return EClass._zone.ParentZone.uid.ToString();"))
    try:
        h5.set_fame(H, 0)
        h5.set_fame(A, 8000)
        check("H504e the guest's FameLv is 81", ev(A, "return EClass.pc.FameLv.ToString();") == "81")
        ev(A, 'EClass.player.ExitBorder(); return "ok";')
        wait(client_settled(A, region, True), "the guest on the world map", timeout=120)
        spot = free_spot(A, HOME)
        check(f"H504e a free tile near the Meadow ({spot})", bool(spot))
        x, y = spot.split(",")
        ev(A, "var em = EClass.scene.elomap; var mob = CharaGen.Create(\"merchant_travel\"); "
              f"EClass.player.EnterLocalZone(new Point({x} - em.minX, {y} - em.minY), null, true, mob); return \"ok\";")

        def in_field():
            dismiss_dialogs(A)
            s = state(A)
            return s.get("sceneMode") == "Zone" and bool(s.get("awayZone")) and (s.get("zone") or {}).get("uid") != region

        check("H504e the guest is in the encounter's field", eventually(in_field, timeout=120))
        level = ev(A, "var m = EClass._map.charas.Where(c => c.trait is TraitMerchantTravel).ToList(); "
                      'return m.Count == 0 ? "-1" : m.Max(c => c.LV).ToString();')
        check(f"H504e the merchant's level follows the guest's fame (got {level}, expected >= 100; 20 with the host's)",
              int(level) >= 100)
    finally:
        h5.set_fame(H, host0)
        h5.set_fame(A, guest0)
        dismiss_dialogs(A)
        if (state(A).get("zone") or {}).get("uid") != HOME:
            ev(A, 'EClass.player.ExitBorder(); return "ok";')
            wait(client_settled(A, region, True), "the guest back on the world map", timeout=120)
            enter_at(A, ev(A, f'var z = EClass.game.spatials.Find({HOME}); return z.x + "," + z.y;'))
            both_joined(H, A, HOME)
        cleanup()


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
