"""Shared helpers of the team-page benches (worker T, council 2026-10-28). Needs host (27551) + 1 guest (27552)
connected in the same world. The C# sent through ev() cannot see the mod's internal types: everything goes through
reflection, the way the game's own buttons reach it (Teams.Ask is what the Team tab's buttons call)."""
import sys

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402,F401

H, A = 27551, 27552
BF = 'System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public'
T = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams")'
ASK = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.TeamAsk")'
CFG = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpConfig+Server")'
HOSTT = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost")'
LAYER = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Components.LayerElinTogether")'


def teams(port, method, *args):
    """Teams.<method>(args) in that game, as text"""
    return ev(port, f'return System.Convert.ToString({T}.GetMethod("{method}", {BF}).Invoke(null, new object[] {{ {", ".join(args)} }}));')


def ask(port, kind, id=0, zone=0, text=""):
    """What a button of the Team tab does: Teams.Ask in the game of that player (a guest asks the host)"""
    ev(port, f'{T}.GetMethod("Ask", {BF}).Invoke(null, new object[] {{ System.Enum.Parse({ASK}, "{kind}"), {id}, {zone}, "{text}" }}); return "";')


def box(name, on):
    ev(H, f'var e = {CFG}.GetProperty("{name}", {BF}).GetValue(null); e.GetType().GetProperty("Value").SetValue(e, {str(on).lower()}); return "";')
    ev(H, 'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("UpdateRemoteSessionRules").GetValue(); return "";')


def number(name, value):
    ev(H, f'var e = {CFG}.GetProperty("{name}", {BF}).GetValue(null); e.GetType().GetProperty("Value").SetValue(e, {value}); return "";')
    ev(H, 'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("UpdateRemoteSessionRules").GetValue(); return "";')


def uid(port):
    return int(ev(port, 'return EClass.pc.uid.ToString();'))


def clear_book():
    for d in ("TeamList", "TeamNames", "ZoneTeams", "CardOwners", "TeamRequests", "TeamInvites", "ZoneRules"):
        ev(H, f'var f = {HOSTT}.GetMember("{d}", {BF}).FirstOrDefault(); if (f == null) return ""; '
              'var v = f is System.Reflection.PropertyInfo pi ? pi.GetValue(null) : ((System.Reflection.FieldInfo)f).GetValue(null); '
              '((System.Collections.IDictionary)v).Clear(); return "";')


def team_of(port, chara_uid):
    return int(teams(port, "TeamOf", str(chara_uid)))


def describe(port):
    return teams(port, "Describe")


def open_tab(port, tab="emp_tab_team"):
    """Opens the mod's menu on that tab, as the context menu entry does; gives every text drawn in the game"""
    ev(port, f'{LAYER}.GetMethod("OpenPanelSesame", {BF}).Invoke(null, new object[] {{ "{tab}" }}); return "";')
    return ev(port, 'return string.Join((char)10 + "", UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>().Where(t => t.isActiveAndEnabled).Select(t => t.text));')


def close_tab(port):
    ev(port, f'var i = {LAYER}.GetProperty("Instance", {BF}).GetValue(null); if (i != null) HarmonyLib.Traverse.Create(i).Method("Close").GetValue(); return "";')


def report():
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    for label in failed:
        print("  FAIL:", label)
    sys.exit(1 if failed else 0)
