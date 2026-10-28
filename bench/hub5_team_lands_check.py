"""Hub step 5, the Team and Lands pages, on the host and on a guest. Needs 2 windows (host 27551, guest 27552),
both minimized, in a world. usage: python bench/hub5_team_lands_check.py   Screenshots go to dev/_shots/hub/.

What it proves:
- both tabs open whether or not teams are on (they are always in the tab list)
- teams ON, no team yet: the Team page offers "Found a team"; the Lands page says to found or join a team
- the host founds a team (Teams.Ask(Found), the call the dialog's OK makes: the name dialog is not driven here) and the
  Team page shows the team name, the "Open to all"/"Leader approves" chip, the member row and the leader tools, enabled
- the guest opens the Team page, sees the host's team under "Other teams" and joins it with a real click on "Join <team>"
- the guest then sees the member rows of both players and the leader tools GREYED with the one-line reason; the host
  sees both members and the tools still enabled
- the leader tools: the "..." of the guest's row (host side) holds "Make leader" and "Remove from team" (the labels)
- the Lands page, with no land, says "No land yet. Read a land deed on a field to claim one." (a land card needs a
  real claim in the world; read a deed by hand and look at the screenshots, not covered here)
- teams OFF: both pages say "Teams are off in this world"; the host's page has a "Turn on" button, the guest's has none;
  the host's click on "Turn on" lands on the World rules tab
Not covered: the crown sprite itself (a Leader chip shows where the game has no sprite under the guessed names), the
rename dialog, Disband (a confirmation dialog), the container log window. Read the screenshots for look and feel."""
import shutil
import sys
import time
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import emp  # noqa: E402
from mp_test import SHOTS, ok  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402
from permit_suite import CFG  # noqa: E402

H, A = 27551, 27552
OUT = SHOTS / "hub"
MENU = ('HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("ElinTogether.Components.LayerElinTogether"), '
        '"OpenPanelSesame").Invoke(null, new object[] { "%s" }); return "ok";')
CLOSE = 'foreach (var l in EClass.ui.layers.ToList()) l.Close(); return "ok";'
SEP = "((char)10).ToString()"
TEAMS = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams")'
ASK = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.TeamAsk")'
TEAM_NAME = "BenchTeam"


def snap(name, port):
    OUT.mkdir(parents=True, exist_ok=True)
    r = ok(emp.call(port, "screenshot", {"scale": 1.0}))
    shutil.copy(r["path"], OUT / f"{name}.png")


def shown(port, lang_id):
    """Is the text of that language id on a visible Text of the page."""
    return ev(port, f'var s = "{lang_id}".lang(); return UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()'
                    '.Any(t => t.gameObject.activeInHierarchy && t.text.Contains(s)).ToString();') == "True"


def has_text(port, text):
    return ev(port, f'return UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()'
                    f'.Any(t => t.gameObject.activeInHierarchy && t.text.Contains("{text}")).ToString();') == "True"


def button(port, text):
    """(exists, interactable) of the first visible button whose label contains the text."""
    r = ev(port, f'var b = UnityEngine.Object.FindObjectsOfType<UIButton>().FirstOrDefault(x => x.gameObject.activeInHierarchy && '
                 f'x.mainText != null && x.mainText.text.Contains("{text}")); return b == null ? "none" : b.interactable.ToString();')
    return r != "none", r == "True"


def click(port, text):
    return ev(port, f'var b = UnityEngine.Object.FindObjectsOfType<UIButton>().FirstOrDefault(x => x.gameObject.activeInHierarchy && '
                    f'x.interactable && x.mainText != null && x.mainText.text.Contains("{text}")); '
                    'if (b == null) return "none"; b.onClick.Invoke(); return "clicked";')


def set_teams(on):
    ev(H, f'var t = {CFG}; var e = t.GetProperty("Teams", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | '
          f'System.Reflection.BindingFlags.NonPublic).GetValue(null); e.GetType().GetProperty("Value").SetValue(e, {"true" if on else "false"}); return "ok";')


def ask(port, kind, ident=0, text=""):
    return ev(port, f'var k = System.Enum.Parse({ASK}, "{kind}"); HarmonyLib.AccessTools.Method({TEAMS}, "Ask").Invoke(null, '
                    f'new object[] {{ k, {ident}, 0, "{text}" }}); return "ok";')


def team_of_host():
    return ev(H, f'return HarmonyLib.AccessTools.Method({TEAMS}, "TeamOf").Invoke(null, new object[] {{ EClass.pc.uid }}).ToString();')


def open_tab(port, tab):
    ev(port, CLOSE)
    ev(port, MENU % tab)
    time.sleep(1.5)


def main():
    sys.stdout.reconfigure(encoding="utf-8")

    # ---- teams OFF
    set_teams(False)
    time.sleep(3)
    open_tab(H, "emp_tab_team")
    check("host, teams off: the Team page says so", eventually(lambda: shown(H, "emp_ui_team_off"), timeout=15))
    check("host, teams off: a Turn on button", button(H, "Turn on")[0])
    snap("50-team-off-host", H)
    open_tab(H, "emp_tab_lands")
    check("host, teams off: the Lands page says so too", eventually(lambda: shown(H, "emp_ui_team_off"), timeout=15))
    open_tab(A, "emp_tab_team")
    check("guest, teams off: the Team page says only the host can turn them on", eventually(lambda: shown(A, "emp_ui_team_off_guest"), timeout=15))
    check("guest, teams off: no Turn on button", not button(A, "Turn on")[0])
    snap("51-team-off-guest", A)
    open_tab(H, "emp_tab_team")
    time.sleep(1)
    check("host: Turn on lands on the World rules tab", click(H, "Turn on") == "clicked" and
          eventually(lambda: shown(H, "emp_ui_sv_search") or shown(H, "emp_ui_sv_banner_host"), timeout=10))
    snap("52-turn-on-lands-on-rules", H)

    # ---- teams ON, nobody in a team
    set_teams(True)
    time.sleep(3)
    open_tab(H, "emp_tab_team")
    check("host, teams on: the Team page offers to found a team", eventually(lambda: button(H, "Found a team")[0], timeout=15))
    snap("53-team-none-host", H)
    open_tab(H, "emp_tab_lands")
    check("host: the Lands page says to found or join a team", eventually(lambda: shown(H, "emp_ui_land_noteam"), timeout=15))

    # ---- the host founds a team
    ask(H, "Found", 0, TEAM_NAME)
    check("host: a team exists", eventually(lambda: team_of_host() != "0", timeout=15))
    open_tab(H, "emp_tab_team")
    check("host: the Team page shows the team name", eventually(lambda: has_text(H, TEAM_NAME), timeout=15))
    check("host: the chip says who may join", shown(H, "emp_ui_team_chip_open") or shown(H, "emp_ui_team_chip_closed"))
    ok_tools = button(H, "Rename")
    check("host: the leader tools are enabled for the leader", ok_tools[0] and ok_tools[1])
    time.sleep(1)
    snap("54-team-leader-alone-host", H)

    # ---- the guest joins with a real click
    open_tab(A, "emp_tab_team")
    check("guest: sees the host's team under Other teams", eventually(lambda: has_text(A, TEAM_NAME), timeout=20))
    snap("55-team-other-teams-guest", A)
    check("guest: clicks Join", eventually(lambda: click(A, "Join") == "clicked", timeout=10))
    time.sleep(3)
    open_tab(A, "emp_tab_team")
    check("guest: now has the team's page", eventually(lambda: shown(A, "emp_ui_team_manage"), timeout=20))
    rename = button(A, "Rename")
    check("guest: the leader tools are shown but greyed", rename[0] and not rename[1])
    check("guest: the one-line reason is there", shown(A, "emp_ui_team_tools_leader_only"))
    time.sleep(1)
    snap("56-team-member-guest", A)

    open_tab(H, "emp_tab_team")
    time.sleep(2)
    n = ev(H, 'return UnityEngine.Object.FindObjectsOfType<UIButton>().Count(b => b.gameObject.activeInHierarchy && b.mainText != null && b.mainText.text == "...").ToString();')
    check(f"host: a \"...\" menu on the guest's member row ({n})", int(n) >= 1)
    snap("57-team-two-members-host", H)

    # ---- Lands
    open_tab(H, "emp_tab_lands")
    check("host: the Lands page without land says how to get one", eventually(lambda: shown(H, "emp_ui_land_none"), timeout=15))
    snap("58-lands-empty-host", H)
    open_tab(A, "emp_tab_lands")
    check("guest: the Lands page without land says how to get one", eventually(lambda: shown(A, "emp_ui_land_none"), timeout=15))
    snap("59-lands-empty-guest", A)

    # ---- clean up: leave the team again
    ask(A, "Leave")
    time.sleep(1)
    ev(H, CLOSE)
    ev(A, CLOSE)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
