"""Closed lands (council 2026-10-28 section 2, commit 4). Needs 2 windows (host 27551 + guest 27552), both standing
on the host's land, ~2 minutes. usage: python bench/land_closed_check.py

Host team Red holds the land the host stands on, the guest is in team Blue. Box "Land rules" on. What it proves:
- the rule "others: Closed" set while the guest stands on the land (the guest as a player walking on it): the guest
  gets the 30 s grace (the host's grace list holds it), then its game travels out by itself - never left standing,
  never stuck: it ends on a zone that is not the land
- ENTRY: the guest asks to go back (Chara.MoveZone, what its menus and the world map do): the host refuses (the
  lease is denied with emp_land_closed), the guest is not on the land a few seconds later
- reopening to "Visit" lets it in
- the host's own player: moving into a closed land of the other team is refused (the host stays where it is)
- never a trap: the way out was never blocked
Not tested: soft rejoin / reconnect onto a closed land (the same sweep moves the player out after the grace), quest
zones (they belong to no team).
"""
import time

from team_lib import *  # noqa: F403
from travel_suite import zone_uid  # noqa: E402

HOSTT_ = HOSTT


def grace_count():
    return int(ev(H, f'var f = HarmonyLib.AccessTools.Field({HOSTT_}, "_landGrace"); '
                     f'return ((System.Collections.IDictionary)f.GetValue(ElinTogether.Net.NetSession.Instance.Transport)).Count.ToString();'))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a = uid(H), uid(A)
    land = int(ev(H, 'return EClass._zone.uid.ToString();'))
    check("the guest starts on the land", zone_uid(A) == land)
    box("Teams", True)
    box("TeamBases", True)
    box("LandRules", True)
    clear_book()
    start = None
    try:
        ask(H, "Found", text="Red")
        ask(A, "Found", text="Blue")
        check("both teams exist", eventually(lambda: team_of(H, h) > 0 and team_of(H, a) > 0, timeout=15))
        teams(H, "OnClaimed", "EClass._zone")

        closed = teams(H, "PackRule", "3", "0", "false")
        ask(H, "LandRule", closed, land)
        check("the host's book holds the rule", eventually(lambda: teams(H, "RuleOf", str(land)) == closed, timeout=15))
        check("the grace starts for the guest standing on the land", eventually(lambda: grace_count() >= 1, timeout=15))
        t0 = time.time()
        check("the guest leaves by itself (no longer on the land) within 60 s",
              eventually(lambda: zone_uid(A) != land, timeout=60))
        print(f"    (moved out after {time.time() - t0:.0f} s)")
        check("the guest is somewhere (a zone), not stuck", zone_uid(A) not in (None, 0))
        check("the grace list is empty again", eventually(lambda: grace_count() == 0, timeout=15))

        # ENTRY
        ev(A, f'EClass.pc.MoveZone(EClass.game.spatials.Find({land}) as Zone, ZoneTransition.EnterState.Auto); return "";')
        time.sleep(8)
        check("ENTRY: asking to go back to the closed land does not get the guest in", zone_uid(A) != land)

        ask(H, "LandRule", teams(H, "PackRule", "3", "1", "false"), land)
        check("reopened to Visit: the rule is the default again", eventually(lambda: teams(H, "RuleOf", str(land)) == teams(H, "PackRule", "3", "1", "false"), timeout=15))
        ev(A, f'EClass.pc.MoveZone(EClass.game.spatials.Find({land}) as Zone, ZoneTransition.EnterState.Auto); return "";')
        check("the guest gets in", eventually(lambda: zone_uid(A) == land, timeout=60))

        # the host's own player into a closed land of the other team
        start = int(ev(H, 'var s = EClass.game.StartZone; return s == null ? "0" : s.uid.ToString();'))
        if start and start != land:
            ev(H, 'var t = HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost");'
                  f'var b = HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams");'
                  f'((System.Collections.Generic.Dictionary<int,int>)HarmonyLib.AccessTools.Property(t, "ZoneTeams").GetValue(null))[{start}] = {team_of(H, a)};'
                  f'((System.Collections.Generic.Dictionary<int,int>)HarmonyLib.AccessTools.Property(t, "ZoneRules").GetValue(null))[{start}] = {closed}; return "";')
            check("the host's player is denied entry to the other team's closed land",
                  teams(H, "EntryDenied", "EClass.pc", f"EClass.game.spatials.Find({start}) as Zone") == "emp_land_closed")
            ev(H, f'EClass.pc.MoveZone(EClass.game.spatials.Find({start}) as Zone, ZoneTransition.EnterState.Auto); return "";')
            time.sleep(4)
            check("the host's move was refused: it stands where it was", zone_uid(H) == land)
        else:
            print("    [SKIP] the start meadow is the land itself: no second land to refuse the host from")
    finally:
        clear_book()
        box("LandRules", False)
        box("TeamBases", False)
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()
