"""Abandoned land and the lands-per-team cap (council 2026-10-28 gap 5, commit 6). Needs 2 windows (host 27551 +
guest 27552) on a land. usage: python bench/land_abandon_check.py

- the book's "last seen" day per team is moved back by hand (days are real time: the bench cannot wait a week), then
  Teams.CheckAbandon runs as the host runs it every minute
- CONTROL box off: a team away for 8 days keeps its land
- box on, 7 days: the team away 8 days loses its claim (the land is nobody's, rule row gone); a team away 3 days keeps
  it; a team with a member in the game (Red, the host) is seen today whatever its old row said
- cap: with 1 land per team, Red (one land) is at its cap, Blue (none) is not; 0 = no limit
Not tested: reading a real deed with the cap (the prefix on TraitDeed.OnRead and the ClaimZoneDelta refusal).
"""
from team_lib import *  # noqa: F403

BOOK = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost")'


def d(name):
    return f'((System.Collections.IDictionary)HarmonyLib.AccessTools.Property({BOOK}, "{name}").GetValue(null))'


def today():
    return int(ev(H, 'return ((int)(System.DateTime.UtcNow - new System.DateTime(2020, 1, 1)).TotalDays).ToString();'))


def put(book, key, value):
    ev(H, f'{d(book)}[{key}] = {value}; return "";')


def owner(land):
    return ev(H, f'var z = {d("ZoneTeams")}; return z.Contains({land}) ? z[{land}].ToString() : "none";')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a = uid(H), uid(A)
    box("Teams", True)
    box("TeamBases", True)
    box("LandAbandon", False)
    number("AbandonDays", 7)
    number("LandsPerTeam", 0)
    clear_book()
    try:
        ask(H, "Found", text="Red")
        ask(A, "Found", text="Blue")
        check("both teams exist", eventually(lambda: team_of(H, h) > 0 and team_of(H, a) > 0, timeout=15))
        red = team_of(H, h)
        teams(H, "OnClaimed", "EClass._zone")
        land = int(ev(H, 'return EClass._zone.uid.ToString();'))
        now = today()

        # two synthetic teams of players who are not connected, each with a land of its own (fake zone numbers)
        for team, zone, away in ((98, 880098, 8), (99, 880099, 3)):
            put("TeamList", team, "new int[] { 999990 + " + str(team) + ", 1, 999990 + " + str(team) + " }")
            put("ZoneTeams", zone, team)
            put("TeamSeen", team, now - away)
        teams(H, "CheckAbandon")
        check("CONTROL box off: the team away 8 days keeps its land", owner(880098) == "98")

        box("LandAbandon", True)
        teams(H, "CheckAbandon")
        check("box on: the team away 8 days (limit 7) loses its claim", owner(880098) == "none")
        check("the team away 3 days keeps its land", owner(880099) == "99")

        put("TeamSeen", red, now - 30)
        teams(H, "CheckAbandon")
        check("Red has a member in the game: seen today, its land stays", owner(land) == str(red))

        # cap
        number("LandsPerTeam", 1)
        pc_h = f"EClass.game.cards.globalCharas.Find({h})"
        pc_a = f"EClass.game.cards.globalCharas.Find({a})"
        check("cap 1: Red, with one land, is at its cap", teams(H, "LandCapReached", pc_h) == "True")
        check("cap 1: Blue, with none, is not", teams(H, "LandCapReached", pc_a) == "False")
        number("LandsPerTeam", 0)
        check("no limit: Red is not at a cap", teams(H, "LandCapReached", pc_h) == "False")
    finally:
        clear_book()
        ev(H, f'{d("TeamSeen")}.Clear(); return "";')
        box("LandAbandon", False)
        number("AbandonDays", 30)
        number("LandsPerTeam", 0)
        box("TeamBases", False)
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()
