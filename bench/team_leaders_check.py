"""Leader actions behind the host box "Team leaders manage their team" (council 2026-10-28 section 1, commit 2).
Needs 2 windows (host 27551 + guest 27552). usage: python bench/team_leaders_check.py

Everything goes through Teams.Ask, the call the Team tab's buttons make, in the game of the player who asks (the
guest's request travels to the host as a TeamRequestDelta). What it proves:
- CONTROL box off: a leader's Rename and Kick are refused (today's behaviour), Invite makes no invitation
- box on: the leader renames (a member's Rename is refused), invites (one pending invitation per player, a refused
  team cannot ask again), kicks, promotes, disbands (members Wanderers, team gone)
- team size cap: a full team refuses a new member
- found cooldown: a second Found right after the first is refused
- the tab draws "Disband" for the leader only
"""
from team_lib import *  # noqa: F403


def book(port=H):
    return describe(port)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    box("Teams", True)
    box("TeamApproval", True)
    box("TeamLeaders", False)
    number("TeamMaxSize", 10)
    number("TeamFoundCooldown", 0)
    clear_book()
    h, a = uid(H), uid(A)
    try:
        ask(A, "Found", text="Blue")
        check("the guest founded Blue", eventually(lambda: team_of(H, a) > 0, timeout=15))
        blue = team_of(H, a)

        # CONTROL: box off
        ask(A, "Rename", text="Hacked")
        ask(A, "Invite", h)
        eventually(lambda: False, timeout=2)
        check("CONTROL box off: no rename", "Hacked" not in book() and "Blue" in book())
        check("CONTROL box off: no invitation", teams(H, "InviteOf", str(h)) == "0")

        box("TeamLeaders", True)
        ask(A, "Rename", text="Blue Crew")
        check("box on: the leader renames its team", eventually(lambda: "Blue Crew" in book(), timeout=15))

        ask(A, "Invite", h)
        check("the leader invites the host", eventually(lambda: teams(H, "InviteOf", str(h)) == str(blue), timeout=15))
        check("the invitation reaches the host's own tab data and the guest's copy", teams(A, "InviteOf", str(h)) in ("0", str(blue)))
        ask(A, "Invite", h)
        check("a second invitation to the same player changes nothing", teams(H, "InviteOf", str(h)) == str(blue))
        ask(H, "RefuseInvite", blue)
        check("the host refuses: no invitation left", eventually(lambda: teams(H, "InviteOf", str(h)) == "0", timeout=15))
        eventually(lambda: False, timeout=2)  # (the one-a-second limit)
        ask(A, "Invite", h)
        eventually(lambda: False, timeout=2)
        check("a refused team cannot invite that player again this session", teams(H, "InviteOf", str(h)) == "0")

        # the host joins by asking, the leader accepts
        ask(H, "Join", blue)
        ask(A, "Accept", h)
        check("the host is a member", eventually(lambda: team_of(H, h) == blue, timeout=15))

        ask(H, "Rename", text="Mine")
        eventually(lambda: False, timeout=2)
        check("a member cannot rename the team", "Mine" not in book())
        ask(H, "Kick", a)
        ask(H, "Disband")
        eventually(lambda: False, timeout=2)
        check("a member cannot kick the leader nor disband", team_of(H, a) == blue and blue > 0 and "Blue Crew" in book())

        tab_leader, tab_member = open_tab(A), None
        close_tab(A)
        tab_member = open_tab(H)
        close_tab(H)
        check("the tab draws Disband for the leader", "Disband" in tab_leader)
        check("the tab does not for a member", "Disband" not in tab_member)

        ask(A, "Promote", h)
        check("the leader hands the team over", eventually(lambda: teams(H, "LeaderOf", str(blue)) == str(h), timeout=15))
        ask(A, "Kick", h)
        eventually(lambda: False, timeout=2)
        check("the former leader can no longer kick", team_of(H, h) == blue)

        ask(H, "Kick", a)
        check("the new leader kicks the guest", eventually(lambda: team_of(H, a) == 0, timeout=15))
        check("the guest's game knows", eventually(lambda: team_of(A, a) == 0, timeout=15))

        # size cap
        number("TeamMaxSize", 1)
        ask(A, "Join", blue)
        ask(H, "Accept", a)
        eventually(lambda: False, timeout=3)
        check("a full team (cap 1) refuses a new member", team_of(H, a) == 0)
        number("TeamMaxSize", 10)

        # cooldown
        number("TeamFoundCooldown", 30)
        ask(A, "Found", text="Red")
        eventually(lambda: False, timeout=3)
        check("founding again at once is refused while the wait runs", team_of(H, a) == 0)
        number("TeamFoundCooldown", 0)
        ask(A, "Found", text="Red")
        check("with no wait it founds", eventually(lambda: team_of(H, a) > 0, timeout=15))

        ask(H, "Disband")
        check("the leader disbands: the host is a Wanderer and the team is gone",
              eventually(lambda: team_of(H, h) == 0 and "Blue Crew" not in book(), timeout=15))
    finally:
        clear_book()
        box("TeamLeaders", False)
        number("TeamMaxSize", 10)
        number("TeamFoundCooldown", 5)
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()
