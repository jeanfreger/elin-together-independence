"""H5-04 (world-map sites): a new random site (nefia) is drawn for a player in turn, so each player finds sites of its
own level, and every game shows the same level for the same site.

Host fame 0 and level 1, guest fame 8000 (FameLv 81): the host's game makes 20 sites as its daily
Region.CheckRandomSites does (level drawn from "the player"). Half of them are drawn for the guest: 2 in 3 of those from
its fame, which gives 25..50 (the game caps a draw >= 50 to rndHalf(50) without the adventurer's license), the host's
own draws stay under 10. Red before the fix: every site is drawn from the host (0 sites at 20 or more), and the guest's
copy of a site had the level of its source and was no random site (SpatialGenDelta did not carry them).

Proves: PlayerInTurn on Region.CreateRandomSite (the guest's fame reaches the level), SpatialGenDelta keys 8-10 (the
guest's copy has the same level, is a random site, expires).
Not played like a player: the fames and the host's level are set by eval, the sites are made by
Region.CreateRandomSite on the host (the daily check does the same, once a day), nobody walks into them.

Host + 1 client open (ports 27551 / 27552), same map.
usage (from dev/, PYTHONPATH=_tools/pylib): python <worktree>/bench/h504_sites.py
"""
import sys
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import hunt5_suite as h5  # noqa: E402
from travel_suite import HOME, RESULTS, check, ev, eventually, host_goto, zone_uid  # noqa: E402

H, A = 27551, 27552


def main():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    sys.stdout.reconfigure(encoding="utf-8")
    print("H504s a new nefia is drawn for a player in turn (guest fame 8000, host fame 0 and level 1)")
    preflight()
    host0, guest0 = int(ev(H, "return EClass.player.fame.ToString();")), int(ev(A, "return EClass.player.fame.ToString();"))
    lv0 = int(ev(H, "return EClass.pc.LV.ToString();"))
    ids = []
    try:
        gu = h5.guest_uid()
        h5.set_fame(H, 0)
        h5.set_fame(A, 8000)
        ev(H, 'EClass.pc.LV = 1; return "ok";')
        check("H504s the host knows the guest's fame (8000)", h5.host_knows_fame(gu, 8000))
        h5.SITES = 20
        ids = h5.make_sites()
        check(f"H504s set up: the host made {len(ids)} sites", len(ids) >= 18)
        host = [int(x.split("|")[0]) for x in h5.site_levels(H, ids)]
        high = sum(v >= 20 for v in host)
        check(f"H504s some sites are drawn for the guest (levels >= 20: {high} of {len(host)}, expected >= 4; {host})", high >= 4)
        check(f"H504s and some for the host (levels < 10: {sum(v < 10 for v in host)}, expected >= 5)", sum(v < 10 for v in host) >= 5)
        check("H504s the guest knows every site", eventually(lambda: "-" not in h5.site_levels(A, ids), timeout=20))
        guest = h5.site_levels(A, ids)
        check(f"H504s the guest's copies are random sites of the same level (host {host}, guest {guest})",
              guest == [f"{v}|True" for v in host])
        expire = ev(A, "var sp = EClass.game.spatials; return string.Join(\",\", new[] {" + ",".join(map(str, ids)) + "}"
                       '.Select(u => sp.Find(u) is Zone z ? z.dateExpire.ToString() : "-"));')
        check(f"H504s and they expire ({expire})", all(x not in ("-", "0") for x in expire.split(",")))
    finally:
        if ids:
            h5.expire_sites(ids)
        h5.set_fame(H, host0)
        h5.set_fame(A, guest0)
        ev(H, f'EClass.pc.LV = {lv0}; return "ok";')
        if zone_uid(H) != HOME:
            host_goto(H, A, HOME)
        cleanup()


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
