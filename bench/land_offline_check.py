"""Offline protection (council 2026-10-28 section 2, commit 5). Needs 2 windows (host 27551 + guest 27552) on a land.
usage: python bench/land_offline_check.py

The per-land option "Closed to others while no member of the team is online" (bit 4 of the land's rule):
- a land of Red (the host's team, the host is online): the option changes nothing for the guest (still visits)
- a land whose team has no member in the game (a team of a player who is not connected, made in the book): without the
  option the guest visits; with it the guest is closed out, and that team's own member would not be
- the leader's tab draws the option (the toggle), the other team's tab shows the "(closed while away)" tag
"""
from team_lib import *  # noqa: F403
from travel_suite import zone_uid  # noqa: E402

VISIT, BUILD = 1, 3
HOSTBOOK = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost")'


def book(name):
    return f'((System.Collections.IDictionary)HarmonyLib.AccessTools.Property({HOSTBOOK}, "{name}").GetValue(null))'


def allowed(port, who, need, zone):
    return teams(port, "Allowed", f"EClass.game.cards.globalCharas.Find({who})", zone, str(need)) == "True"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a = uid(H), uid(A)
    box("Teams", True)
    box("TeamBases", True)
    box("LandRules", True)
    clear_book()
    try:
        ask(H, "Found", text="Red")
        ask(A, "Found", text="Blue")
        check("both teams exist", eventually(lambda: team_of(H, h) > 0 and team_of(H, a) > 0, timeout=15))
        red = team_of(H, h)
        teams(H, "OnClaimed", "EClass._zone")
        land = int(ev(H, 'return EClass._zone.uid.ToString();'))
        zone = "EClass._zone"

        offline = teams(H, "PackRule", str(BUILD), str(VISIT), "true")
        ask(H, "LandRule", offline, land)
        check("rule with the option stored", eventually(lambda: teams(H, "RuleOf", str(land)) == offline, timeout=15))
        check("Red is online, so the option changes nothing: Blue still visits", allowed(H, a, VISIT, zone))
        tab = open_tab(H)
        close_tab(H)
        check("the leader's tab draws the option", "no member of the team is online" in tab)

        # a team nobody of which is in the game
        ev(H, f'((System.Collections.Generic.Dictionary<int,int[]>){book("TeamList")})[99] = new int[] {{ 999999, 1, 999999 }};'
              f'{book("ZoneTeams")}[{land}] = 99; {book("ZoneRules")}[{land}] = {teams(H, "PackRule", str(BUILD), str(VISIT), "false")}; return "";')
        check("no option: Blue visits a land whose team is away", allowed(H, a, VISIT, zone))
        ev(H, f'{book("ZoneRules")}[{land}] = {offline}; return "";')
        check("with the option: Blue is closed out while that team is away", not allowed(H, a, VISIT, zone))
        check("...and the Red player (not of that team) too, as any other", not allowed(H, h, VISIT, zone))
        ev(H, f'{book("ZoneTeams")}[{land}] = {red}; return "";')
        check("the land back with Red (online): Blue visits again", allowed(H, a, VISIT, zone))
    finally:
        clear_book()
        box("LandRules", False)
        box("TeamBases", False)
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()
