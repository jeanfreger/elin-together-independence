"""Hub step 7: Server info, My settings and Help pages, and the World rules tab for a guest.
Needs 2 windows (host 27551, guest 27552), both minimized, in a world. usage: python bench/hub7_pages_check.py
Proves: the host's Server page offers "Write one" when empty, the pencil field saves a text and it reaches the guest's
page as a note (the guest has no Write/Edit button); Friends and Ignored are on My settings and no longer on the
Server page; Help shows the how-it-works cards, About and the report box (a report under 3 characters sends nothing);
the guest is offered the World rules tab (its read-only view is covered by hub6_world_rules_check).
Screenshots go to dev/_shots/hub/ (07-*.png)."""
import shutil
import sys
import time
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import emp  # noqa: E402
from mp_test import SHOTS, ok  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A = 27551, 27552
OUT = SHOTS / "hub"
MENU = ('HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("ElinTogether.Components.LayerElinTogether"), '
        '"OpenPanelSesame").Invoke(null, new object[] { "%s" }); return "ok";')
CLOSE = 'foreach (var l in EClass.ui.layers.ToList()) l.Close(); return "ok";'
NL = "((char)10).ToString()"


def snap(name, port):
    OUT.mkdir(parents=True, exist_ok=True)
    r = ok(emp.call(port, "screenshot", {"scale": 1.0}))
    shutil.copy(r["path"], OUT / f"{name}.png")


def shown(port, lang_id):
    return ev(port, f'var s = "{lang_id}".lang(); return UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()'
                    '.Any(t => t.gameObject.activeInHierarchy && t.text.Contains(s)).ToString();') == "True"


def has_text(port, text):
    return ev(port, 'return UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()'
                    f'.Any(t => t.gameObject.activeInHierarchy && t.text.Contains("{text}")).ToString();') == "True"


def click_button(port, text_id):
    return ev(port, f'var s = "{text_id}".lang(); var b = UnityEngine.Object.FindObjectsOfType<UIButton>()'
                    '.FirstOrDefault(x => x.gameObject.activeInHierarchy && x.mainText != null && x.mainText.text == s); '
                    'if (b == null) return "none"; b.onClick.Invoke(); return "clicked";')


def open_tab(port, tab, marker):
    ev(port, CLOSE)
    ev(port, MENU % tab)
    return eventually(lambda: shown(port, marker), timeout=15)


def host_writes():
    check("host: Server page opens", open_tab(H, "emp_tab_info", "emp_info_motd"))
    time.sleep(1)
    snap("07-server-host", H)
    check("host: Friends/Ignored are not on the Server page", not shown(H, "emp_info_ignored"))
    # an empty note offers Write one; a note left by an earlier run offers the pencil
    wrote = click_button(H, "emp_info_write") == "clicked" or click_button(H, "emp_info_edit") == "clicked"
    check("host: Write one / pencil opens the field in the note",
          wrote and eventually(lambda: shown(H, "emp_info_save"), timeout=10))
    ev(H, 'var f = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.InputField>().First(x => x.gameObject.activeInHierarchy); '
          f'f.text = "Welcome to the hub bench" + {NL} + "Be kind"; return "ok";')
    snap("07-server-host-writing", H)
    check("host: Save", click_button(H, "emp_info_save") == "clicked")
    check("host: the note shows the text", eventually(lambda: has_text(H, "Welcome to the hub bench"), timeout=15))
    check("host: the pencil is offered", shown(H, "emp_info_edit"))


def guest_reads():
    check("guest: Server page opens", open_tab(A, "emp_tab_info", "emp_info_motd"))
    check("guest: reads the host's note", eventually(lambda: has_text(A, "Welcome to the hub bench"), timeout=20))
    check("guest: no pencil, no Write one", not shown(A, "emp_info_edit") and not shown(A, "emp_info_write"))
    snap("07-server-guest", A)
    check("guest: World rules tab is offered", open_tab(A, "emp_tab_server", "emp_ui_sv_banner_guest"))
    snap("07-rules-guest", A)


def settings_and_help(port, who):
    check(f"{who}: My settings opens", open_tab(port, "emp_tab_client", "emp_ui_cl_sec_controls"))
    time.sleep(1)
    snap(f"07-settings-{who}", port)
    check(f"{who}: Friends and Ignored are on My settings", shown(port, "emp_info_friends") and shown(port, "emp_info_ignored"))
    check(f"{who}: Help opens", open_tab(port, "emp_tab_help", "emp_help_about"))
    time.sleep(1)
    snap(f"07-help-{who}", port)
    for t in ("hosting", "joining", "teams", "lands", "trading", "duels"):
        check(f"{who}: Help card {t}", shown(port, f"emp_help_{t}_t"))
    check(f"{who}: About has the GitHub button", shown(port, "emp_help_github"))
    check(f"{who}: the report box is inline", shown(port, "emp_ui_report_what"))
    # fewer than 3 characters: the send button does nothing (no "sending" pop-up)
    click_button(port, "emp_ui_report_send")
    time.sleep(1)
    check(f"{who}: a too-short report sends nothing", not shown(port, "emp_ui_report_sending"))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    host_writes()
    guest_reads()
    settings_and_help(H, "host")
    settings_and_help(A, "guest")
    ev(H, CLOSE)
    ev(A, CLOSE)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    total = len(RESULTS)
    print(f"{total - len(failed)}/{total} verifications OK")
    for label in failed:
        print("FAILED:", label)
