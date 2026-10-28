"""Council 2026-10-28 items 6 and 7: container access log, moderator, freeze, jail.
Needs 2 windows (host 27551 + 1 guest 27552), the host at home on a claimed land.
usage: python bench/workerA_check.py   (or copied to dev/_tools)

What it proves (NOT RUN when written: the worker had no game):
- MODERATOR: a guest made a moderator (ElinNetHost.MakeModerator) mutes a stranger (AdminCommandDelta) but its
  "freeze" order about the same stranger is refused (no Frozen entry, "refused order" line in admin.log)
- FREEZE: ElinNetHost.Freeze(guest) puts the game's paralysis on the guest's character in both games; Release takes
  it off again
- JAIL: Freeze(guest, jail: true) after SetJail(host zone) marks the guest jailed (2) and paralysed
- LOG: a guest putting an item in a chest of the land (its own AddThing, replayed by the host) leaves a line with its
  name and count in ElinNetHost.ContainerLogs; taking it out adds a negative line; a guest who is neither admin nor
  of the land's team gets no window from ContainerLogRequest, an admin gets one
Everything internal is reached by reflection: the game's script engine cannot see the mod's internal types.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import LOCALLOW, RESULTS, check, ev, eventually  # noqa: E402
from permit_suite import BF, CFG, M, send_raw  # noqa: E402

H, A = 27551, 27552
HT = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost")'
CONN = 'ElinTogether.Net.NetSession.Instance.Connection'
GUEST = 'ElinTogether.Net.NetSession.Instance.CurrentPlayers.First(p => p.Index != 0).User'
LOG = LOCALLOW / "ElinMP" / "Logs" / "admin.log"
STRANGER = 76561190000004343


def call(method, args):
    return f'HarmonyLib.AccessTools.Method({HT}, "{method}").Invoke({CONN}, new object[] {{ {args} }}).ToString()'


def table(name, key_type="ulong"):
    return f'((System.Collections.IDictionary)HarmonyLib.AccessTools.Property({HT}, "{name}").GetValue(null))'


def set_box(name, on):
    got = ev(H, f'var t = {CFG}; var e = t.GetProperty("{name}", {BF}); if (e == null) return "no setting"; '
                f'var v = e.GetValue(null); v.GetType().GetProperty("Value").SetValue(v, {str(on).lower()}); return "ok";')
    check(f"{name} set to {on} ({got})", got == "ok")


def paralysed(port, uid):
    return ev(port, f'var c = EClass.game.cards.globalCharas.Find({uid}); return (c != null && c.HasCondition<ConParalyze>()).ToString();') == "True"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    guest = ev(H, f'return ((ulong){GUEST}).ToString();')
    guest_uid = ev(A, 'return EClass.pc.uid.ToString();')
    set_box("AdminExtras", True)
    set_box("ContainerAccessLog", True)
    time.sleep(2)
    try:
        # MODERATOR
        ev(H, f'return {call("MakeModerator", f"{guest}UL, true")};')
        time.sleep(2)
        send_raw(A, f'new {M}AdminCommandDelta {{ Verb = "mute", Target = {STRANGER}UL, Reason = "bench" }}')
        check("MODERATOR: its mute is done", eventually(
            lambda: ev(H, f'return {table("Mutes")}.Contains({STRANGER}UL).ToString();') == "True", timeout=15))
        send_raw(A, f'new {M}AdminCommandDelta {{ Verb = "freeze", Target = {STRANGER}UL }}')
        time.sleep(3)
        check("MODERATOR: its freeze order is refused", ev(H, f'return {table("Frozen")}.Contains({STRANGER}UL).ToString();') == "False")
        check("MODERATOR: the refusal is in admin.log", LOG.exists() and "refused order freeze" in LOG.read_text(encoding="utf-8", errors="replace"))
        ev(H, f'return {call("Unmute", f"{STRANGER}UL")};')

        # FREEZE
        ev(H, f'return {call("Freeze", f"{guest}UL, false")};')
        check("FREEZE: the guest's character is paralysed on the host", eventually(lambda: paralysed(H, guest_uid), timeout=15))
        check("FREEZE: and in its own game", eventually(lambda: paralysed(A, guest_uid), timeout=15))
        ev(H, f'return {call("Release", f"{guest}UL")};')
        check("FREEZE: released, the paralysis is gone", eventually(lambda: not paralysed(H, guest_uid), timeout=15))

        # JAIL (the host's zone is the jail, the guest is already in it: the move is a no-op, the freeze is the test)
        ev(H, f'return HarmonyLib.AccessTools.Method({HT}, "SetJail").Invoke({CONN}, new object[] {{ EClass._zone }}).ToString();')
        ev(H, f'return {call("Freeze", f"{guest}UL, true")};')
        check("JAIL: marked jailed (2)", ev(H, f'return {table("Frozen")}[{guest}UL].ToString();') == "2")
        check("JAIL: paralysed", eventually(lambda: paralysed(H, guest_uid), timeout=15))
        ev(H, f'return {call("Release", f"{guest}UL")};')
        ev(H, f'return {call("MakeModerator", f"{guest}UL, false")};')

        # LOG
        chest = int(ev(H, 'var t = ThingGen.Create("chest"); EClass._zone.AddCard(t, EClass.pc.pos.GetNearestPoint(false, false)); return t.uid.ToString();'))
        check("LOG: the guest sees the chest", eventually(lambda: ev(A, f'return (EClass._map.things.Find(x => x.uid == {chest}) != null).ToString();') == "True", timeout=20))
        ev(A, f'var b = ThingGen.Create("log"); b.SetNum(3); EClass.pc.AddThing(b); EClass._map.things.Find(x => x.uid == {chest}).AddThing(b); return "ok";')
        text = lambda: ev(H, f'var d = {table("ContainerLogs")}; return d.Contains({chest}) ? (string)d[{chest}] : "";')
        check("LOG: a put by the guest is logged with its count", eventually(lambda: "\t3\t" in text(), timeout=15))
        ev(A, f'var c = EClass._map.things.Find(x => x.uid == {chest}); EClass.pc.AddThing(c.things[0]); return "ok";')
        check("LOG: and the take is a negative line", eventually(lambda: "\t-3\t" in text(), timeout=15))

        has_window = 'return EClass.ui.layers.Any(l => l is LayerList).ToString();'
        send_raw(A, f'new {M}ContainerLogRequest {{ Uid = {chest} }}')
        time.sleep(3)
        check("LOG: a guest neither admin nor of the team gets no window", ev(A, has_window) == "False")
        ev(H, f'return {call("MakeAdmin", f"{guest}UL, true")};')
        time.sleep(2)
        send_raw(A, f'new {M}ContainerLogRequest {{ Uid = {chest} }}')
        check("LOG: an admin gets the window", eventually(lambda: ev(A, has_window) == "True", timeout=15))
    finally:
        ev(H, f'{call("MakeAdmin", f"{guest}UL, false")}; return "ok";')
        ev(H, f'{call("Release", f"{guest}UL")}; return "ok";')
        set_box("AdminExtras", False)
        set_box("ContainerAccessLog", False)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
