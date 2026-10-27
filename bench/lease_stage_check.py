"""Reliability audit 2026-10-27 #2: a map a player hands back reaches the host's save with the save of the host's
game, never before it. Host + 1 guest open on the Prairie (2 windows). usage: python bench/lease_stage_check.py

What it proves:
  1. the guest holds Vernis alone and drops a bucket; its checkpoint reaches the host: the map waits beside the save
     (<save>/_lease/16) and the map file of Vernis in the save is untouched. Before the fix that file was rewritten at
     once while game.txt (the guest's character, the lease ranges) waited up to two minutes for the next save: the
     state a crash of the host at that moment leaves on disk (thing dropped: in the map and still in the bag of the
     saved character, i.e. doubled) is what checks 1b/1c read on disk;
  2. the host saves: the waiting map moves into the save in the same save (file newer, folder gone);
  3. the host walks into a map that waits (its autosave off, so nothing saved meanwhile): it is moved in before the
     load and the host finds the guest's bucket where it was dropped.
Not played as a player: the checkpoint is sent at once (the method the 60 s timer calls), the host's save is
Game.Save, the drop is AddCard next to the guest. The crash itself is not played (no window is killed): the files
on disk at that moment are read instead.
"""
import time

from benchlib import (A, H, HOME, VERNIS, both_joined, call, check, client_settled, ev, eventually, finish, log,
                      marker, move, on_map, wait, zone_uid)

STAGED = 'return System.IO.Directory.Exists(GameIO.pathCurrentSave + "_lease/{0}").ToString();'
MTIME = 'return System.IO.File.GetLastWriteTimeUtc(GameIO.pathCurrentSave + "{0}/map").Ticks.ToString();'
GAME = 'return System.IO.File.GetLastWriteTimeUtc(GameIO.pathCurrentSave + "game.txt").Ticks.ToString();'


def staged(uid):
    return ev(H, STAGED.format(uid)) == "True"


def main():
    move(A, VERNIS)
    wait(client_settled(A, VERNIS, True), "guest alone in Vernis")

    bucket, pos = marker(A)
    live0, game0 = int(ev(H, MTIME.format(VERNIS))), int(ev(H, GAME))
    call(A, "SendTravelCheckpoint")
    check("1 the checkpoint waits beside the save (_lease/16)", eventually(lambda: staged(VERNIS), timeout=20))
    check("1b the map of Vernis in the save is untouched", int(ev(H, MTIME.format(VERNIS))) == live0)
    check("1c game.txt is untouched too: what is on disk still goes together", int(ev(H, GAME)) == game0)

    ev(H, 'EClass.game.Save(false, true); return "ok";')
    check("2 the host saves: the map is in the save, written with it", int(ev(H, MTIME.format(VERNIS))) > live0)
    check("2b nothing waits anymore", not staged(VERNIS))

    # 3: nothing saves on the host until it stands in Vernis
    ev(H, 'EClass.debug.ignoreAutoSave = true; return "ok";')
    try:
        bucket2, pos2 = marker(A)
        call(A, "SendTravelCheckpoint")
        check("3 a second checkpoint waits", eventually(lambda: staged(VERNIS), timeout=20))
        move(H, VERNIS)
        wait(lambda: zone_uid(H) == VERNIS, "host in Vernis", timeout=180)
        time.sleep(3)
        found = on_map(H, [bucket, bucket2])
        log(f"host finds {found}, dropped at {pos} and {pos2}")
        check(f"3b the host finds both buckets where the guest dropped them ({found})",
              found.get(bucket) == pos and found.get(bucket2) == pos2)
    finally:
        ev(H, 'EClass.debug.ignoreAutoSave = false; return "ok";')

    move(H, HOME)
    wait(lambda: zone_uid(H) == HOME, "host back on the Prairie", timeout=180)
    if zone_uid(A) != HOME:
        move(A, HOME)
    both_joined(H, A, HOME)


if __name__ == "__main__":
    main()
    finish()
