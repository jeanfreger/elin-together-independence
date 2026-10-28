"""Land rules data and checks (council 2026-10-28 section 2, commit 3). Needs 2 windows (host 27551 + guest 27552)
standing on a land of the world. usage: python bench/land_rules_check.py

Host team Red holds the land, the guest is in team Blue. The rules are set through Teams.Ask (the call the leader's
dropdowns of the Team tab make) and read through Teams.Allowed, the test every host check uses (CanTouch = Build,
CanTake/CanOpen = Use). What it proves:
- CONTROL box "Land rules" off: Blue visits (yes), uses (no), builds (no); Red builds - today's behaviour
- box on, no rule set = the default: the same answers
- Red's leader sets team Build / others Use: Blue now uses but cannot build; the guest's own game computes the same
- a non-leader (Blue's leader, on Red's land) cannot change the rule
- the default rule written back leaves no row in the book; a land of no team stays open to everyone
- others Closed: Blue cannot even visit (the entry checks of commit 4 read this level)
- "closed while the team is away": a land of a team with nobody in the game is closed to others, open to its own
"""
from team_lib import *  # noqa: F403
from travel_suite import host_goto, zone_uid  # noqa: E402

USE, BUILD, VISIT = 2, 3, 1


def allowed(port, who_uid, need, zone="EClass._zone"):
    chara = f"EClass.game.cards.globalCharas.Find({who_uid})"
    return teams(port, "Allowed", chara, zone, str(need)) == "True"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a = uid(H), uid(A)
    base = int(ev(H, 'return EClass._zone.uid.ToString();'))
    if zone_uid(A) != base:
        host_goto(H, A, base)
    box("Teams", True)
    box("TeamBases", True)
    box("LandRules", False)
    clear_book()
    try:
        ask(H, "Found", text="Red")
        ask(A, "Found", text="Blue")
        check("both teams exist", eventually(lambda: team_of(H, h) > 0 and team_of(H, a) > 0 and team_of(H, h) != team_of(H, a), timeout=15))
        red, blue = team_of(H, h), team_of(H, a)
        teams(H, "OnClaimed", "EClass._zone")
        check("Red holds the land", teams(H, "TeamOfZone", "EClass._zone") == str(red))
        zone = int(ev(H, 'return EClass._zone.uid.ToString();'))

        check("CONTROL box off: Blue visits", allowed(H, a, VISIT))
        check("CONTROL box off: Blue does not use", not allowed(H, a, USE))
        check("CONTROL box off: Blue does not build", not allowed(H, a, BUILD))
        check("CONTROL box off: Red builds", allowed(H, h, BUILD))

        box("LandRules", True)
        check("box on, default rule: the same (Blue visits, no use, no build; Red builds)",
              allowed(H, a, VISIT) and not allowed(H, a, USE) and not allowed(H, a, BUILD) and allowed(H, h, BUILD))

        rule = int(teams(H, "PackRule", str(BUILD), str(USE), "false"))
        ask(H, "LandRule", rule, zone)
        check("the leader's rule (team Build, others Use): Blue uses", eventually(lambda: allowed(H, a, USE), timeout=15))
        check("...but does not build", not allowed(H, a, BUILD))
        check("the guest's own game computes the same", eventually(lambda: allowed(A, a, USE) and not allowed(A, a, BUILD), timeout=15))

        ask(A, "LandRule", teams(H, "PackRule", str(BUILD), str(BUILD), "false"), zone)
        eventually(lambda: False, timeout=3)
        check("Blue's leader cannot change Red's land: still no build", not allowed(H, a, BUILD))

        ask(H, "LandRule", teams(H, "PackRule", str(BUILD), "0", "false"), zone)
        check("others Closed: Blue cannot even visit", eventually(lambda: not allowed(H, a, VISIT), timeout=15))
        check("Red still builds", allowed(H, h, BUILD))

        ask(H, "LandRule", teams(H, "PackRule", str(BUILD), str(VISIT), "false"), zone)
        check("the default written back: Blue visits again", eventually(lambda: allowed(H, a, VISIT), timeout=15))
        rows = ev(H, 'var f = HarmonyLib.AccessTools.Property(HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost"), "ZoneRules");'
                     'return ((System.Collections.IDictionary)f.GetValue(null)).Count.ToString();')
        check("...and the book keeps no row for it", rows == "0")

        # a team with nobody in the game: closed while away, for others only
        ev(H, 'var t = HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost");'
              'var l = (System.Collections.Generic.Dictionary<int,int[]>)HarmonyLib.AccessTools.Property(t, "TeamList").GetValue(null);'
              'l[99] = new int[] { 999999, 1, 999999 };'
              f'var z = (System.Collections.Generic.Dictionary<int,int>)HarmonyLib.AccessTools.Property(t, "ZoneTeams").GetValue(null); z[{zone}] = 99;'
              'return "";')
        check("a land of a team nobody of which is online: still visited by default", allowed(H, a, VISIT))
        ask_rule = teams(H, "PackRule", str(BUILD), str(VISIT), "true")
        ev(H, 'var t = HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost");'
              f'((System.Collections.Generic.Dictionary<int,int>)HarmonyLib.AccessTools.Property(t, "ZoneRules").GetValue(null))[{zone}] = {ask_rule}; return "";')
        check("closed while its team is away: Blue cannot visit", not allowed(H, a, VISIT))
    finally:
        clear_book()
        box("LandRules", False)
        box("TeamBases", False)
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()
