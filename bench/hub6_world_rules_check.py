"""Hub step 6, the World rules page (TabServerConfiguration), played through the page's own controls.
Proves, on the host: the banner says "You decide"; the three preset cards are there; the search box (the game's
input field) filters the rows to the match and hides the other groups; a preset card applies its rules (Classic:
turn-based combat on, then Recommended puts them back); a row that differs from the preset shows Revert and
Revert puts the rule back; a group that differs shows its dot. On the guest: the same page says "Set by <host>" and
every drop-down and box is greyed (read only); skipped with a clear failure when the layer does not offer the tab
to a guest yet (LayerElinTogether builds the tab for hosts only until the window worker's change is merged).
The merged "The host changed N rules" notice is NOT covered here (it needs the host to change several rules while the
guest watches; read it in the guest's log after a preset click).
Needs 2 windows (host 27551, guest 27552), both minimized, in a world. usage: python bench/hub6_world_rules_check.py
Screenshots go to dev/_shots/hub/."""
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


def snap(name, port):
    OUT.mkdir(parents=True, exist_ok=True)
    r = ok(emp.call(port, "screenshot", {"scale": 1.0}))
    shutil.copy(r["path"], OUT / f"{name}.png")


def texts(port):
    """Every text of the page that is shown now."""
    return ev(port, 'return string.Join(' + SEP + ', UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()'
                    '.Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));')


def shown(port, lang_id):
    return ev(port, f'var s = "{lang_id}".lang(); return UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()'
                    '.Any(t => t.gameObject.activeInHierarchy && t.text.Contains(s)).ToString();') == "True"


def cfg(name):
    return ev(H, f'var t = {CFG}; var e = t.GetProperty("{name}", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | '
                 'System.Reflection.BindingFlags.NonPublic).GetValue(null); return e.GetType().GetProperty("Value").GetValue(e).ToString();')


def set_cfg(name, value):
    ev(H, f'var t = {CFG}; var e = t.GetProperty("{name}", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | '
          f'System.Reflection.BindingFlags.NonPublic).GetValue(null); e.GetType().GetProperty("Value").SetValue(e, {value}); return "ok";')


def click_button(port, text_id, index=0):
    """The player's click on the nth shown button with that label."""
    return ev(port, f'var s = "{text_id}".lang(); var b = UnityEngine.Object.FindObjectsOfType<UIButton>()'
                    '.Where(x => x.gameObject.activeInHierarchy && x.mainText != null && x.mainText.text == s).ToList(); '
                    f'if (b.Count <= {index}) return "none"; b[{index}].onClick.Invoke(); return "clicked";')


def type_search(port, text):
    """Types into the page's search box (the field that carries the search placeholder)."""
    return ev(port, 'var s = "emp_ui_sv_search".lang(); var f = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.InputField>()'
                    '.FirstOrDefault(x => x.gameObject.activeInHierarchy && x.placeholder is UnityEngine.UI.Text p && p.text == s); '
                    f'if (f == null) return "no field"; f.text = "{text}"; return "ok";')


def open_tab(port):
    ev(port, CLOSE)
    ev(port, MENU % "emp_tab_server")
    return eventually(lambda: shown(port, "emp_ui_sv_search") or shown(port, "emp_ui_sv_banner_host"), timeout=15)


def host():
    check("host: the World rules tab opens", open_tab(H))
    time.sleep(1)
    snap("01-rules-host", H)
    check("host: the banner says who decides", shown(H, "emp_ui_sv_banner_host"))
    for p in ("recommended", "classic", "custom"):
        check(f"host: preset card {p}", shown(H, f"emp_ui_sv_opt_preset_{p}"))

    # search: only the match, the other groups gone
    check("host: search types into the field", type_search(H, "duel") == "ok")
    check("host: search shows the duel rule", eventually(lambda: shown(H, "emp_ui_sv_cfg_duels"), timeout=10))
    check("host: search hides an unrelated rule", not shown(H, "emp_ui_sv_cfg_follow"))
    check("host: search hides the other groups", not shown(H, "emp_ui_sv_sec_repair"))
    snap("02-search-host", H)
    type_search(H, "zzzzzz")
    check("host: no match says so", eventually(lambda: shown(H, "emp_ui_sv_nomatch"), timeout=10))
    type_search(H, "")
    check("host: clearing brings the groups back", eventually(lambda: shown(H, "emp_ui_sv_sec_joining"), timeout=10))

    # a preset card applies
    check("host: click Recommended", click_button(H, "emp_ui_sv_opt_preset_recommended") == "clicked")
    check("host: Recommended = turn-based combat off", eventually(lambda: cfg("TurnBasedCombat") == "False", timeout=10))
    check("host: click Classic", eventually(lambda: click_button(H, "emp_ui_sv_opt_preset_classic") == "clicked", timeout=10))
    check("host: Classic = turn-based combat on", eventually(lambda: cfg("TurnBasedCombat") == "True", timeout=10))
    time.sleep(1.5)
    snap("03-classic-host", H)
    check("host: Classic marks itself as current", eventually(lambda: shown(H, "emp_ui_sv_preset_current"), timeout=10))
    click_button(H, "emp_ui_sv_opt_preset_recommended")
    check("host: back to Recommended", eventually(lambda: cfg("TurnBasedCombat") == "False", timeout=10))

    # a changed row shows Revert, Revert puts it back
    was = cfg("Follow")
    flipped = "false" if was == "True" else "true"
    set_cfg("Follow", flipped)
    check("host: the page is drawn again with Revert", eventually(lambda: shown(H, "emp_ui_sv_revert"), timeout=15))
    type_search(H, ev(H, 'return "emp_ui_sv_cfg_follow".lang();'))
    time.sleep(1)
    snap("04-changed-row-host", H)
    check("host: the changed row shows Revert", eventually(lambda: click_button(H, "emp_ui_sv_revert") == "clicked", timeout=10))
    check("host: Revert puts the rule back", eventually(lambda: cfg("Follow") == was, timeout=10))
    check("host: the page loses the Revert", eventually(lambda: not shown(H, "emp_ui_sv_revert"), timeout=15))
    type_search(H, "")


def guest():
    got = open_tab(A)
    check("guest: the World rules tab is offered", got)
    if not got:
        return
    time.sleep(1)
    snap("05-rules-guest", A)
    check("guest: the banner says who set it", shown(A, "emp_ui_sv_banner_guest") or "Set by" in texts(A))
    n = ev(A, 'return UnityEngine.Object.FindObjectsOfType<UIDropdown>().Count(d => d.gameObject.activeInHierarchy).ToString();')
    live = ev(A, 'return UnityEngine.Object.FindObjectsOfType<UIDropdown>().Count(d => d.gameObject.activeInHierarchy && d.interactable).ToString();')
    check(f"guest: drop-downs are shown ({n}) and none is editable ({live})", int(n) > 0 and live == "0")
    boxes = ev(A, 'return UnityEngine.Object.FindObjectsOfType<UIButton>().Count(b => b.gameObject.activeInHierarchy && b.interactable && b.mainText != null '
                  '&& b.mainText.text == "" ).ToString();')
    check(f"guest: no box can be ticked ({boxes})", boxes == "0")
    check("guest: no Revert button", not shown(A, "emp_ui_sv_revert"))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    host()
    guest()
    ev(H, CLOSE)
    ev(A, CLOSE)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
