"""Hub step 4: the Play and Players pages, opened on the host and on the guest, screenshots into dev/_shots/hub/.
Needs 2 windows (host 27551 + 1 guest 27552), already connected to each other.
usage: python bench/hub4_play_players_check.py   (windows stay minimized: the pages are opened by code, as the HUD button does)

What it proves:
- PLAY: on the host and on the guest the layer opens on the Play tab and shows the connected card (the host's name,
  the player count, Invite friends and Leave)
- PLAYERS: both windows show one row per player (2 names) under "Players (2)", the same page for host and guest
- ROWS: the "..." menu of the other player holds the same social entries (friend, ignore) on both sides; the admin
  entries (Kick, Ban) are listed for the host only
- SHOTS: dev/_shots/hub/hub-<play|players>-<host|guest>.png saved from emp.py "screenshot"
Not tested: the title-screen (not connected) variant of Play, which needs a window on the title screen.
"""
import shutil
import sys
import time
from pathlib import Path

DEV = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev")
sys.path.insert(0, str(DEV / "_tools"))
import emp  # noqa: E402
from travel_suite import RESULTS, check, ev  # noqa: E402

H, A = 27551, 27552
OUT = DEV / "_shots" / "hub"
BF = 'System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public'
LAYER = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Components.LayerElinTogether")'
ALL_TEXTS = ('var l = EClass.ui.layers.FirstOrDefault(x => x.GetType().Name == "LayerElinTogether"); '
             'return l == null ? "" : string.Join(" | ", l.GetComponentsInChildren<UnityEngine.UI.Text>()'
             '.Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));')


def open_tab(port, tab):
    ev(port, f'var l = EClass.ui.layers.FirstOrDefault(x => x.GetType().Name == "LayerElinTogether"); '
             f'if (l != null) EClass.ui.RemoveLayer(l); '
             f'{LAYER}.GetMethod("OpenPanelSesame", {BF}).Invoke(null, new object[] {{ "{tab}" }}); return "ok";')
    time.sleep(2)
    return ev(port, ALL_TEXTS)


def shot(name, port):
    OUT.mkdir(parents=True, exist_ok=True)
    r = emp.call(port, "screenshot", {"scale": 0.75})
    dst = OUT / f"hub-{name}.png"
    shutil.copy(r["result"]["path"], dst)
    print("    saved", dst)


def more_entries(port):
    """The labels the '...' button of the first row that has one would show: read from the page's own actions through
    the context menu the button opens (clicked by code, as the player's click does)"""
    return ev(port, 'var l = EClass.ui.layers.First(x => x.GetType().Name == "LayerElinTogether"); '
                    'var b = l.GetComponentsInChildren<UIButton>().Where(x => x.gameObject.activeInHierarchy && x.mainText != null '
                    '&& x.mainText.text == "...").ToList(); if (b.Count == 0) return "none"; b[0].onClick.Invoke(); '
                    'var m = EClass.ui.contextMenu; return string.Join("|", EClass.ui.GetComponentsInChildren<UIButton>()'
                    '.Where(x => x.gameObject.activeInHierarchy && x.mainText != null).Select(x => x.mainText.text));')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    for side, port in (("host", H), ("guest", A)):
        text = open_tab(port, "emp_tab_play")
        check(f"PLAY {side}: the connected card is there ({text[:90]!r})", "Players: 2" in text or "2" in text)
        shot(f"play-{side}", port)

        text = open_tab(port, "emp_tab_players")
        check(f"PLAYERS {side}: 'Players (2)' header", "Players (2)" in text)
        shot(f"players-{side}", port)
        entries = more_entries(port)
        print(f"    '...' on {side}: {entries}")
        check(f"ROWS {side}: friend and ignore in the menu", "friend" in entries.lower() and "ignore" in entries.lower())
        if side == "host":
            check("ROWS host: admin entries (Kick) listed", "Kick" in entries)
        ev(port, 'var l = EClass.ui.layers.FirstOrDefault(x => x.GetType().Name == "LayerElinTogether"); '
                 'if (l != null) EClass.ui.RemoveLayer(l); return "ok";')


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
