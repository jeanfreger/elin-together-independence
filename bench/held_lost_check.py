"""Reliability audit 2026-10-27 #7: what a guest loses while it loads a map is repaired. Host + 1 guest open on the
Prairie (2 windows), the host's "repair the map by itself" box ticked (AutoResync, the default).
usage: python bench/held_lost_check.py   (~2 minutes)

What it proves: the guest keeps at most 20000 messages while it loads the world or a map; past that they were
dropped and nothing ever asked for them again. Here the limit is lowered to 3 on the guest, which then comes back
from Vernis (the world, or the host's map, is loaded: the host's messages meanwhile are kept, most are dropped):
  1. the guest says how many were lost, as a warning;
  2. it asks for the map again (the reload of a checksum repair), without a checksum difference being needed.
Not played as a player: the limit is lowered through the test bridge (20000 is not reached on the bench).
"""
from benchlib import A, H, HOME, VERNIS, both_joined, check, client_settled, ev, eventually, finish, lines, log, move, now, wait

FIELD = 'HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinDeltaManager"), "MaxHeld")'
RULE = 'return ElinTogether.Net.NetSession.Instance.Rules.AutoResync.ToString();'


def main():
    if ev(A, RULE) != "True":
        log("the host's AutoResync box is unticked: the reload of 2 cannot happen, tick it first")
    move(A, VERNIS)
    wait(client_settled(A, VERNIS, True), "guest alone in Vernis")
    ev(A, f'{FIELD}.SetValue(null, 3); return "ok";')
    t0 = now()
    try:
        move(A, HOME)
        both_joined(H, A, HOME)
        check("1 the guest warns that messages were lost while loading",
              eventually(lambda: bool(lines(t0, "were over the limit of {MaxHeld}")), timeout=20))
        check("2 and asks for the map again",
              eventually(lambda: any("kept while loading" in str(x.get("Detail", "")) for x in lines(t0, "Reloading {ZoneFullName} after {Detail}")),
                         timeout=90))
    finally:
        ev(A, f'{FIELD}.SetValue(null, 20000); return "ok";')


if __name__ == "__main__":
    main()
    finish()
