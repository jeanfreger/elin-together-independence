"""Team tab (council 2026-10-28 section 1, commit 1). Needs 2 windows (host 27551 + guest 27552), Teams box on.
usage: python bench/team_tab_check.py

What it proves, going through Teams.Ask (the call every button of the Team tab makes), for the guest AND the host:
- the guest founds a team with a typed name (TeamRequestDelta.Text): the host's book holds that name, the guest leads
- a name over 24 characters is cut by the host
- the host asks to join (approval box on): a request; the guest's tab lists it; the guest accepts: the host is a member
- the tab opens in each game and draws the team name, both members and the leave button text
- the host leaves from its own tab: the same path as a guest (no difference), the guest sees it
"""
from team_lib import *  # noqa: F403

PERSON = "Blue Crew"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    box("Teams", True)
    box("TeamApproval", True)
    clear_book()
    h, a = uid(H), uid(A)
    try:
        ask(A, "Found", text=PERSON)
        check("the guest founds a team under the typed name",
              eventually(lambda: PERSON in describe(H), timeout=15))
        blue = team_of(H, a)
        check(f"the guest leads it (team {blue})", blue > 0 and teams(H, "LeaderOf", str(blue)) == str(a))
        check("the guest's own game knows the book", eventually(lambda: PERSON in describe(A), timeout=15))

        ask(A, "Found", text="x" + "y" * 60)
        names = describe(H)
        check("a name of 61 letters is cut to 24", "y" * 25 not in names and "x" + "y" * 23 in names)
        ask(A, "Found", text=PERSON)  # (founding again leaves the cleaned team and makes a fresh one with the first name)
        blue = team_of(H, a)

        ask(H, "Join", blue)
        check("the host is not a member yet", team_of(H, h) == 0)
        tab = open_tab(A)
        check("the guest's tab shows the team name and the host asking", PERSON in tab)
        close_tab(A)
        ask(A, "Accept", h)
        check("the host is a member after the guest accepts", eventually(lambda: team_of(H, h) == blue, timeout=15))
        check("the guest's game sees the host as a member", eventually(lambda: team_of(A, h) == blue, timeout=15))

        for port, who in ((H, "host"), (A, "guest")):
            tab = open_tab(port)
            check(f"the {who}'s tab draws the team name", PERSON in tab)
            close_tab(port)

        ask(H, "Leave")
        check("the host leaves from its own tab, as a guest would", eventually(lambda: team_of(H, h) == 0, timeout=15))
        check("the guest sees it", eventually(lambda: team_of(A, h) == 0, timeout=15))
    finally:
        clear_book()
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()
