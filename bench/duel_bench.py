"""Shared helpers of the duel bench checks (council 7, steps 5 to 7): bout5_check.py, arena6_check.py, bets7_check.py.
They run on windows already open (host 27551, guest 27552), with the suites of dev/_tools (machine only, not in git)."""
import sys
import time
from pathlib import Path

# dev/_tools is on this machine only: next to this folder in the main checkout, or the main checkout seen from a worktree
for tools in (Path(__file__).resolve().parents[1] / "dev" / "_tools",
              Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")):
    if tools.is_dir():
        sys.path.insert(0, str(tools))
        break

from base_suite import click_yes, dialog_open  # noqa: E402,F401
from combat_suite import set_option  # noqa: E402,F401
from duel_suite import describe, ended, finish, menu, next_to, prepare, start_duel, to_home, winner  # noqa: E402,F401
from guest_suite import chara, free_next_to, stand  # noqa: E402,F401
from mp_test import log, shot, state  # noqa: E402,F401
from travel_suite import RESULTS, check, ev, eventually, scan_logs  # noqa: E402,F401

H, A = 27551, 27552

# what a click on a dialog line does (DramaEventTalk.Play): the next line, or the jump the line carries
NEXT = ('var d = LayerDrama.Instance; if (d == null) return "closed"; var s = d.drama.sequence; '
        'var t = HarmonyLib.Traverse.Create(s).Field("currentEvent").GetValue() as DramaEventTalk; if (t == null) return "wait"; '
        'if (t.temp) s.tempEvents.Clear(); if (t.idJump.IsEmpty()) s.PlayNext(); else s.Play(t.idJump); return "next";')
CHOICES = ('var d = LayerDrama.Instance; if (d == null) return "closed"; '
           'return string.Join("|", d.GetComponentsInChildren<UnityEngine.UI.Button>(false).Where(x => x.interactable)'
           '.Select(x => string.Join(" ", x.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text))).Where(t => t.Trim().Length > 0));')


def drama_open(port):
    return ev(port, 'return (LayerDrama.Instance != null).ToString();') == "True"


def talk(port, uid):
    """The player talks to this character (the game's "talk" click opens its dialog)."""
    ev(port, f'var m = EClass._map.charas.Find(x => x.uid == {uid}); m.ShowDialog(); "ok"')
    return eventually(lambda: drama_open(port), timeout=10)


def pick(port, label):
    """Clicks the dialog choice whose text contains `label`."""
    r = ev(port, 'var d = LayerDrama.Instance; if (d == null) return "closed"; var b = d.GetComponentsInChildren<UnityEngine.UI.Button>(false)'
                 f'.FirstOrDefault(x => x.interactable && x.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("{label}"))); '
                 'if (b == null) return "no choice"; b.onClick.Invoke(); return "click";')
    time.sleep(1.2)
    return r


def choices(port):
    r = ev(port, CHOICES)
    return [] if r == "closed" else [c for c in r.split("|") if c]


def drive(port, labels, limit=40):
    """Plays the open dialog as a player clicks it: each label of `labels` is picked, in order, when offered; the lines
    in between are clicked through; returns when the dialog is closed (True) or after `limit` clicks (False)."""
    wanted = list(labels)
    for _ in range(limit):
        if not drama_open(port):
            return True
        offered = choices(port)
        if wanted and any(wanted[0] in c for c in offered):
            log(f"dialog on {port}: picks '{wanted[0]}' among {offered}")
            pick(port, wanted.pop(0))
            continue
        if ev(port, NEXT) == "wait" and offered:
            # a menu with none of what we want: the last choice is the way out (bye)
            pick(port, offered[-1])
            continue
        time.sleep(0.8)
    return not drama_open(port)


def settled(port, zone_uid, away):
    """This guest stands on that map, away or on the host's map. Unlike travel_suite.client_settled it leaves the
    game's dialogs alone: a bout ends in one."""
    def cond():
        try:
            s = state(port)
        except Exception:  # noqa: BLE001  (the map is loading)
            return False
        return (s.get("sceneMode") == "Zone" and (s.get("zone") or {}).get("uid") == zone_uid
                and bool(s.get("awayZone")) == away and s.get("connected"))
    return cond


def zone_uid(port):
    return int(ev(port, 'return EClass._zone.uid.ToString();'))


def host_table(name, key):
    """A table the host keeps in its save (an internal property of ElinNetHost), its entry for `key` as text."""
    return ev(H, 'var t = HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost"); '
                 f'var d = HarmonyLib.AccessTools.Property(t, "{name}").GetValue(null) as System.Collections.IDictionary; '
                 f'if (d == null) return "no table"; var v = d[{key}]; if (v == null) return "none"; '
                 'return v is System.Collections.IEnumerable e && !(v is string) ? string.Join(",", e.Cast<object>()) : v.ToString();')


def gold(port, uid=None):
    """Gold of this player: in its own game (uid None) or as the host keeps it."""
    if uid is None:
        return int(ev(port, 'return EClass.pc.GetCurrency().ToString();'))
    return int(ev(H, f'return {chara(H, uid)}.GetCurrency().ToString();'))


def done():
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    for label in failed:
        print(f"  FAILED: {label}")
    sys.exit(1 if failed else 0)
