"""Efficiency audit 2026-10-27, the wire changes (compact RemoteCard, one zone uid per snapshot, no game time on
an own clock, whole-number ping, changed-only snapshots). Host 27551 + 1 guest 27552, both on the host's map, players
on their own clock (the default rules). usage: python bench/wire_efficiency_check.py   (~1 minute)

What it proves:
  1. the guest walks (its own AI_Goto, as a click on the map does) and the host sees it arrive: moves still read
     with the one-integer RemoteCard;
  2. the guest's snapshots come both partial and full, and a full one names every character the host has on the
     map: the zone uid written once is put back (the guest's copies stand on its zone);
  3. a character that stands still on the host but was put elsewhere on the guest goes back to its host tile
     within a few seconds (it is only in the full snapshots now, 1 s apart, window 2.2 s);
  4. a character the host has no more (taken off its map list through the test bridge: a missed removal) leaves
     the guest's map, from the full snapshots alone;
  5. with nothing fast-forwarding the host sends no game time any more (the guest's last host-turbo stamp stands
     still for 5 s; before, one came every frame);
  6. the ping the guest is told for itself is a whole number.
Not played as a player: 3 and 4 move and drop a character through the test bridge (that is the desync they repair);
5 reads a private stamp.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A = 27551, 27552

LAST_TICK = ('var c = (ElinTogether.Net.ElinNetClient)ElinTogether.Net.NetSession.Instance.Connection; '
             'var s = (ElinTogether.Models.WorldStateSnapshot)HarmonyLib.AccessTools.Field(c.GetType(), "_lastTick").GetValue(c); ')
TURBO_TIME = ('return HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName('
              '"ElinTogether.Patches.GameSynchronizationContext"), "_hostTurboTime").GetValue(null).ToString();')
# a character of the map, not anyone's, not global: what the snapshots are about
NPC = ('EClass._map.charas.FirstOrDefault(c => !c.IsPCFactionOrMinion && !c.IsGlobal && !c.isDead && c.pos.IsValid '
       '&& !c.GetBool("remote_chara"))')


def pos(port, uid):
    return ev(port, f'var c = EClass._map.charas.FirstOrDefault(x => x.uid == {uid}); '
                    'return c == null ? "-" : c.pos.x + "," + c.pos.z;')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    check("both games on the same map",
          ev(H, 'return EClass._zone.uid.ToString();') == ev(A, 'return EClass._zone.uid.ToString();'))

    # 1
    me = ev(A, 'return EClass.pc.uid.ToString();')
    x, z = map(int, ev(A, 'return EClass.pc.pos.x + "," + EClass.pc.pos.z;').split(","))
    to = ev(A, f'var p = EClass.pc.pos.GetNearestPoint(false, false); for (var i = 3; i < 8; i++) {{ var q = new Point({x} + i, {z}); '
               'if (q.IsValid && q.IsInBounds && !q.IsBlocked && !q.HasChara) { p = q; break; } } '
               'EClass.pc.SetAI(new AI_Goto(p, 0)); return p.x + "," + p.z;')
    check(f"the guest walks to {to} and the host sees it there", eventually(lambda: pos(H, me) == to, timeout=20))

    # 2
    seen = set()
    for _ in range(12):
        seen.add(ev(A, LAST_TICK + 'return s == null ? "-" : s.Partial.ToString();'))
        time.sleep(0.4)
    check(f"partial and full snapshots both arrive ({sorted(seen)})", {"True", "False"} <= seen)
    host_count = int(ev(H, 'return EClass._map.charas.Count(c => c.currentZone == EClass._zone && c.pos.IsValid).ToString();'))

    def full_names_all():
        r = ev(A, LAST_TICK + 'return s == null || s.Partial ? "-" : s.CharaSnapshots.Length + "/" + '
                              's.CharaSnapshots.Count(e => e.CurrentZoneUid == EClass._zone.uid);')
        return r != "-" and r.split("/")[0] == r.split("/")[1] and abs(int(r.split("/")[0]) - host_count) <= 2
    check(f"a full snapshot names the host's {host_count} characters, all on the guest's zone",
          eventually(full_names_all, timeout=10))

    # 3
    npc = ev(H, f'var c = {NPC}; if (c == null) return "-"; c.noMove = true; return c.uid.ToString();')
    if check("a character of the map to move", npc != "-"):
        time.sleep(2)
        at = pos(H, npc)
        ev(A, f'var c = EClass._map.charas.First(x => x.uid == {npc}); var p = c.pos.Copy(); '
              'for (var i = 4; i < 12; i++) { var q = new Point(c.pos.x + i, c.pos.z); '
              'if (q.IsValid && q.IsInBounds && !q.IsBlocked && !q.HasChara) { p = q; break; } } '
              'EClass._map.MoveCard(p, c); return "ok";')
        check(f"put elsewhere on the guest ({pos(A, npc)}, host {at})", pos(A, npc) != at)
        check("it goes back to its host tile within 6 s", eventually(lambda: pos(A, npc) == pos(H, npc), timeout=6))
        ev(H, f'var c = EClass._map.charas.FirstOrDefault(x => x.uid == {npc}); if (c != null) c.noMove = false; return "ok";')

    # 4
    # (still on its cell: put back on the list from there afterwards)
    gone = ev(H, f'var c = {NPC}; if (c == null) return "-"; EClass._map.charas.Remove(c); '
                 'return c.uid + "," + c.pos.x + "," + c.pos.z;')
    if check("a character taken off the host's map list", gone != "-"):
        uid, gx, gz = gone.split(",")
        try:
            check("it leaves the guest's map within 6 s", eventually(lambda: pos(A, uid) == "-", timeout=6))
        finally:
            ev(H, f'var c = new Point({gx}, {gz}).Charas.FirstOrDefault(x => x.uid == {uid}); '
                  'if (c != null && !EClass._map.charas.Contains(c)) EClass._map.charas.Add(c); return "ok";')

    # 5
    t0 = ev(A, TURBO_TIME)
    time.sleep(5)
    check(f"no game time from the host while nothing fast-forwards (stamp {t0} -> {ev(A, TURBO_TIME)})",
          ev(A, TURBO_TIME) == t0)

    # 6
    ping = ev(A, 'var s = ElinTogether.Net.NetSession.Instance.CurrentPlayers.First(p => p.CharaUid == EClass.pc.uid); '
                 'var v = (float)HarmonyLib.AccessTools.Property(s.GetType(), "AvgPingMs").GetValue(s); '
                 'return v.ToString(System.Globalization.CultureInfo.InvariantCulture);')
    check(f"the guest's ping as the host tells it is a whole number ({ping})", "." not in ping)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
