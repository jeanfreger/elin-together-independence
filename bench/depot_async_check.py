"""Reliability audit 2026-10-27 #9: a "host:port" depot (Elin Together Server) no longer freezes the game while a save
travels or while it beats. Needs the host window only (port 27551). usage: python bench/depot_async_check.py (~1 min)

A slow depot is played by this script: a TCP server on 127.0.0.1 speaking the server's protocol, which waits 6 s
before each answer. The host's depot setting points at it for the test and is put back after.
What it proves:
  1. the beat each minute returns at once (it froze the game 6 s here before the fix: the same call, timed);
  2. a save of the depot's world returns at once too, the world reaches the server whole (the archive of the
     local copy), the marker "not sent" stands while it travels and is gone once the server took it;
  3. a server that refuses the save: the player is told (warning in the log), the marker stays, so the save is
     offered again next time (what the synchronous write did);
  4. a request asked by the player (WHO is not one; TAKE is) waits for the save still on its way, so the depot
     never sees them out of order.
Not played as a player: the save of 2-3 is the method Game.Save's postfix calls (the bench world is not the depot's
world, which a real save needs); the local copy "world_depot" is made by the test when the bench has none, and
removed after.
"""
import socket
import threading
import time

from benchlib import H, check, ev, eventually, finish, lines, log, now

DELAY = 6.0
DEPOT = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.SaveDepot")'
CALL = 'HarmonyLib.AccessTools.Method(' + DEPOT + ', "{0}").Invoke(null, null); return "ok";'
ENTRY = ('var e = HarmonyLib.AccessTools.Property(HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpConfig+Client"), '
         '"DepotPath").GetValue(null); var v = HarmonyLib.AccessTools.Property(e.GetType(), "Value"); ')
GET = ENTRY + 'return (string)v.GetValue(e);'
SET = ENTRY + 'v.SetValue(e, @"{0}"); return "ok";'
LOCAL = 'CorePath.RootSave + "world_depot"'


class SlowDepot:
    def __init__(self):
        self.got = []
        self.answer = "OK"
        self.sock = socket.socket()
        self.sock.bind(("127.0.0.1", 0))
        self.sock.listen(8)
        self.port = self.sock.getsockname()[1]
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try:
                conn, _ = self.sock.accept()
            except OSError:
                return
            threading.Thread(target=self.one, args=(conn,), daemon=True).start()

    def one(self, conn):
        with conn:
            f = conn.makefile("rb")
            head = [f.readline().decode().rstrip("\n") for _ in range(5)]
            body = f.read(int(head[4])) if int(head[4]) > 0 else b""
            self.got.append((time.time(), head[1], len(body)))
            time.sleep(DELAY)
            conn.sendall(f"{self.answer}\n0\n".encode())


def timed(code):
    t = time.time()
    ev(H, code)
    return time.time() - t


def main():
    depot = SlowDepot()
    was = ev(H, GET)
    made = ev(H, f'return System.IO.Directory.Exists({LOCAL}).ToString();') != "True"
    unsent = f'{LOCAL} + ".unsent"'
    had_marker = ev(H, f'return System.IO.File.Exists({unsent}).ToString();') == "True"
    ev(H, SET.format(f"127.0.0.1:{depot.port}"))
    try:
        if made:
            ev(H, f'System.IO.Directory.CreateDirectory({LOCAL}); System.IO.File.WriteAllText({LOCAL} + "/game.txt", '
                  'new string(\'x\', 200000)); return "ok";')

        took = timed(CALL.format("Beat"))
        check(f"1 the beat returns at once ({took:.1f} s, the server answers in {DELAY:.0f} s)", took < 2.0)
        check("1b the server got it", eventually(lambda: any(c == "BEAT" for _, c, _ in depot.got), timeout=10))

        t0 = now()
        took = timed(CALL.format("SendToServer"))
        check(f"2 a save returns at once ({took:.1f} s)", took < 2.0)
        check("2b the marker 'not sent' stands while it travels",
              ev(H, f'return System.IO.File.Exists({unsent}).ToString();') == "True")
        check("2c the server receives the whole archive",
              eventually(lambda: any(c == "PUT" and n > 1000 for _, c, n in depot.got), timeout=15))
        check("2d once taken, the marker is gone",
              eventually(lambda: ev(H, f'return System.IO.File.Exists({unsent}).ToString();') == "False", timeout=20))
        check("2e the log says it was sent", eventually(lambda: bool(lines(t0, "World sent to the depot")), timeout=10))

        depot.answer = "ERR disk full"
        t1 = now()
        timed(CALL.format("SendToServer"))
        check("3 a refusal is told (log)", eventually(lambda: bool(lines(t1, "refused the world")), timeout=20))
        check("3b the marker stays: offered again next time",
              ev(H, f'return System.IO.File.Exists({unsent}).ToString();') == "True")

        depot.answer = "OK"
        time.sleep(DELAY + 1)
        k = len(depot.got)
        timed(CALL.format("SendToServer"))
        ask = ('var m = HarmonyLib.AccessTools.Method(' + DEPOT + ', "Ask"); '
               'var r = m.Invoke(null, new object[] { "TAKE", null, null }); return "ok";')
        took = timed(ask)
        order = [c for _, c, _ in depot.got[k:]]
        log(f"requests after the save: {order}")
        check(f"4 TAKE asked by the player went after the save on its way ({order}, waited {took:.1f} s)",
              order[:2] == ["PUT", "TAKE"] and took >= DELAY)
    finally:
        ev(H, SET.format(was))
        if made:
            ev(H, f'System.IO.Directory.Delete({LOCAL}, true); return "ok";')
        if not had_marker:
            ev(H, f'System.IO.File.Delete({unsent}); return "ok";')


if __name__ == "__main__":
    main()
    finish()
