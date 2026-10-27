"""Reliability audit 2026-10-27 #10: a save a network message asks for (a player comes back, one drops while away)
waits until the game may save, and a save the game could not make is said. Host + 1 guest open on the Prairie
(2 windows; bench windows do not save by themselves, so these saves are the ones made on a message).
usage: python bench/save_from_message_check.py   (~2 minutes)

What it proves:
  1. the guest comes back from Vernis: the host's world is saved on that message (game.txt newer);
  2. the same save asked while the host's game is loading (game.isLoading, as during a change of map): put off
     (log), game.txt untouched; made by itself once the game may save again. Before the fix it was made in the
     middle of the load;
  3. a save the game cannot make (game.txt held open by the test, as a full or locked disk would): a warning in the
     log. Before the fix nothing said it.
Not played as a player: 2 and 3 call the helper the two handlers call (EmpAutoHost.SaveFromMessage) with the
loading flag set, or the file locked, through the test bridge; the load itself is not played.
"""
import time

from benchlib import A, H, HOME, VERNIS, both_joined, check, client_settled, ev, eventually, finish, lines, move, now, wait

GAME = 'return System.IO.File.GetLastWriteTimeUtc(GameIO.pathCurrentSave + "game.txt").Ticks.ToString();'
SAVE = ('HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpAutoHost"), "SaveFromMessage")'
        '.Invoke(null, new object[] { "bench" }); return "ok";')
LOCK = ('AppDomain.CurrentDomain.SetData("emp_lock", new System.IO.FileStream(GameIO.pathCurrentSave + "game.txt", '
        'System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None)); return "ok";')
UNLOCK = ('var s = AppDomain.CurrentDomain.GetData("emp_lock") as System.IO.Stream; if (s != null) s.Dispose(); '
          'AppDomain.CurrentDomain.SetData("emp_lock", null); return "ok";')


def game_txt():
    return int(ev(H, GAME))


def main():
    move(A, VERNIS)
    wait(client_settled(A, VERNIS, True), "guest alone in Vernis")
    g0 = game_txt()
    move(A, HOME)
    both_joined(H, A, HOME)
    check("1 the guest came back: the host's world is saved", game_txt() > g0)

    t0 = now()
    g1 = game_txt()
    ev(H, 'EClass.game.isLoading = true; return "ok";')
    try:
        ev(H, SAVE)
        time.sleep(2)
        check("2 asked while the game loads: put off (log)", bool(lines(t0, "put off: the game cannot save right now")))
        check("2b game.txt untouched meanwhile", game_txt() == g1)
    finally:
        ev(H, 'EClass.game.isLoading = false; return "ok";')
    check("2c made by itself once the game may save", eventually(lambda: game_txt() > g1, timeout=10))

    t1 = now()
    ev(H, LOCK)
    try:
        ev(H, SAVE)
    finally:
        ev(H, UNLOCK)
    check("3 a save the game cannot make is a warning", eventually(lambda: bool(lines(t1, "The game could not save the world")), timeout=10))
    ev(H, 'EClass.game.Save(false, true); return "ok";')


if __name__ == "__main__":
    main()
    finish()
