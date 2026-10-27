"""Reliability audit 2026-10-27 #4: no map stays held by nobody. Host + 1 guest open on the Prairie (2 windows).
usage: python bench/orphan_lease_check.py   (~3 minutes)

What it proves:
  1. the host cannot read the map of Vernis (its file held open by the test, as a disk error would): the guest asking
     for Vernis is told no at once and stays on the host's map, and the host holds no lease. Before the fix the lease
     was taken before the map was read: Vernis stayed held by nobody for good (nobody else could go in) and the
     guest waited with no answer;
  2. once the file is free the guest goes to Vernis and back as usual;
  3. a guest away in Vernis is handed another map (Lumiest) it does not stand on (the host made the lease and sent
     the handover, the message a holder leaving gives): it declines, and the host forgets that lease. Before the fix
     the guest only wrote a warning and Lumiest stayed held by it.
Not played as a player: the disk error is a file opened without sharing by the host's own process; the handover of 3
is made by hand through the test bridge (ReserveLease + the grant HandOverZone sends), not by a third player leaving.
"""
import time

from benchlib import (A, H, HOME, VERNIS, T, both_joined, check, client_settled, ev, eventually, finish, leases, lines,
                      log, move, now, wait)

LUMIEST = 42
MAP = 'GameIO.pathCurrentSave + "{0}/map"'
LOCK = ('AppDomain.CurrentDomain.SetData("emp_lock", new System.IO.FileStream(' + MAP + ', System.IO.FileMode.Open, '
        'System.IO.FileAccess.Read, System.IO.FileShare.None)); return "ok";')
UNLOCK = ('var s = AppDomain.CurrentDomain.GetData("emp_lock") as System.IO.Stream; if (s != null) s.Dispose(); '
          'AppDomain.CurrentDomain.SetData("emp_lock", null); return "ok";')
HAND = (f'var t = {T}; var peer = t.Field("Socket").Property("Peers")'
        '.GetValue<System.Collections.Generic.IReadOnlyList<ElinTogether.Net.Steam.ISteamNetPeer>>()[0]; '
        'var zone = EClass.game.spatials.Find({0}); var start = t.Method("ReserveLease", peer, zone).GetValue<int>(); '
        'var g = (ElinTogether.Models.ZoneLeaseGrant)System.Runtime.Serialization.FormatterServices'
        '.GetUninitializedObject(typeof(ElinTogether.Models.ZoneLeaseGrant)); var tg = HarmonyLib.Traverse.Create(g); '
        'tg.Property("ZoneUid").SetValue({0}); tg.Property("RequestedUid").SetValue({0}); '
        'tg.Property("UidRangeStart").SetValue(start); tg.Property("ZoneState").SetValue(zone._ints.ToArray()); '
        'tg.Property("Handoff").SetValue(true); peer.Send(g); return start.ToString();')


def main():
    if ev(H, f'return System.IO.File.Exists({MAP.format(VERNIS)}).ToString();') != "True":
        log("Vernis has no map in the host's save yet: one trip there first")
        move(A, VERNIS)
        wait(client_settled(A, VERNIS, True), "guest alone in Vernis")
        move(A, HOME)
        both_joined(H, A, HOME)
        ev(H, 'EClass.game.Save(false, true); return "ok";')

    t0 = now()
    ev(H, LOCK.format(VERNIS))
    try:
        move(A, VERNIS)
        check("1 the host says why it cannot lease Vernis (log)",
              eventually(lambda: bool(lines(t0, "could not be read, lease denied")), timeout=15))
        time.sleep(3)
        check("1b no lease held on the host", leases(H) == 0)
        check("1c the guest stays on the host's map, not away",
              eventually(client_settled(A, HOME, False), timeout=10))
    finally:
        ev(H, UNLOCK)

    move(A, VERNIS)
    check("2 file free: the guest goes to Vernis", eventually(client_settled(A, VERNIS, True), timeout=120))

    t1 = now()
    ev(H, HAND.format(LUMIEST))
    check("3 two leases on the host for a moment (Vernis, Lumiest)", leases(H) in (1, 2))
    check("3b the guest declines Lumiest, which it does not stand on (log)",
          eventually(lambda: bool(lines(t1, "declined the lease of zone")), timeout=15))
    check("3c the host holds Vernis only", eventually(lambda: leases(H) == 1, timeout=10))

    move(A, HOME)
    both_joined(H, A, HOME)
    check("3d back home: no lease left", leases(H) == 0)


if __name__ == "__main__":
    main()
    finish()
