"""Council 7, step 6: a duel with the host is fought in an arena (Helper/PlayerDuel.cs, host box DuelArena, off by
default; the check turns it on, then back to what it was). Host + 1 client open (python _tools/mp_test.py), both in
town. usage: python bench/arena6_check.py [--only a1,a2,a3]

A1 the host challenges the guest through the game's menu on the guest's character, the guest clicks Yes in the real box:
   the host goes to a field made for the duel (an instance zone, not the town), the guest follows it there (it is on the
   host's map again, connected), the countdown, the fight. The host wins with the game's melee act. Both are then back in
   town, each on the very tile it left (in its own game, and the guest as the host sees it); the arena is gone from the
   host's world; nobody is dead, nobody is still "in a duel".
A2 the same, the guest challenging and winning.
A3 the box off: the duel is fought where they stand (no map change), as before.
Red before the change: there was no arena, every duel was fought where the two stood.

What the bench does not play like a player: as duel_suite D1/D2 (the menu built by the bridge, the blow is the game's
melee act on the target, the loser's hit points set to 5 by the host, strength 300, players placed side by side by
teleport); the box's Yes is clicked through the bridge. The loading of the arena on two real PCs is not measured.
"""
import argparse
import time

from duel_bench import (A, H, check, click_yes, describe, dialog_open, done, ev, eventually, finish, log, menu, next_to,  # noqa: F401
                        prepare, scan_logs, set_option, settled, shot, start_duel, state, to_home, zone_uid)
from duel_suite import challenge, other  # noqa: E402


def tile(port, uid=None):
    """Where this player stands: in its own game, or (uid) as the host sees it."""
    if uid is None:
        return ev(port, 'return EClass.pc.pos.x + "," + EClass.pc.pos.z;')
    return ev(H, f'var c = EClass._map.charas.Find(x => x.uid == {uid}); return c == null ? "-" : c.pos.x + "," + c.pos.z;')


def rule(port):
    return ev(port, 'return ElinTogether.Net.NetSession.Instance.Rules.DuelArena.ToString();')


def arena_duel(ctx, key):
    """`key` challenges the other one with the box on: both fight in the arena, `key` wins, both come back."""
    town = zone_uid(H)
    prepare(ctx)
    before = {"h": tile(H), "a": tile(A)}
    log(f"before: town {town}, host {before['h']}, guest {before['a']}")
    oport = ctx[other(key)][0]
    r = challenge(ctx, key)
    # (the challenge stands next to the other one: the tiles to come back to are those of now)
    before = {"h": tile(H), "a": tile(A)}
    if not check(f"challenge through the menu ({r[:50]}): the other one sees the box", eventually(lambda: dialog_open(oport), timeout=10)):
        return
    click_yes(oport)
    arena = 0
    if eventually(lambda: zone_uid(H) != town and ev(H, 'return EClass._zone.IsInstance.ToString();') == "True", timeout=60):
        arena = zone_uid(H)
    if not check(f"the host is in an arena, an instance made for the duel ({arena}, town {town})", arena):
        return
    check("the guest follows it there: on the host's map again, connected", eventually(settled(A, arena, False), timeout=150))
    check(f"after the countdown the duel is fought, in both games ({describe(H)} / {describe(A)})",
          eventually(lambda: all(describe(p).startswith("Fighting") for p in (H, A)), timeout=30))
    if not finish(ctx, key):
        return
    check("both are back in town: the host", eventually(lambda: zone_uid(H) == town, timeout=180))
    check("and the guest, connected", eventually(lambda: (state(A).get("zone") or {}).get("uid") == town and state(A).get("connected"), timeout=180))
    time.sleep(3)
    check(f"the host stands on the tile it left ({before['h']} -> {tile(H)})", eventually(lambda: tile(H) == before["h"], timeout=10))
    check(f"the guest stands on the tile it left, in its game ({before['a']} -> {tile(A)})", eventually(lambda: tile(A) == before["a"], timeout=10))
    check(f"and as the host sees it ({tile(H, ctx['a'][1])})", eventually(lambda: tile(H, ctx["a"][1]) == before["a"], timeout=20))
    check("the arena is gone from the host's world",
          eventually(lambda: ev(H, f'return (EClass.game.spatials.Find({arena}) == null).ToString();') == "True", timeout=30))
    for port, who in ((H, "host"), (A, "guest")):
        check(f"the {who} is alive and no longer in a duel ({describe(port)})",
              ev(port, 'return EClass.pc.isDead.ToString();') == "False" and not describe(port).startswith(("Fighting", "Countdown")))


def a1(ctx):
    """the host challenges, the duel is fought in the arena, the host wins"""
    arena_duel(ctx, "h")


def a2(ctx):
    """the guest challenges, the duel is fought in the arena, the guest wins"""
    arena_duel(ctx, "a")


def a3(ctx):
    """the box off: the duel is fought where they stand"""
    set_option("DuelArena", False)
    eventually(lambda: rule(A) == "False", timeout=10)
    town = zone_uid(H)
    prepare(ctx)
    if start_duel(ctx, "h"):
        check(f"no map change: both still in town ({zone_uid(H)}, {zone_uid(A)})", zone_uid(H) == town and zone_uid(A) == town)
        finish(ctx, "h")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    a = ap.parse_args()
    from datetime import datetime, timezone
    t0 = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")
    ctx = {"a": (A, state(A)["pc"]["uid"]), "h": (H, state(H)["pc"]["uid"])}
    was = rule(H)
    to_home()
    steps = [a1, a2, a3]
    if a.only:
        steps = [s for s in steps if s.__name__ in a.only.split(",")]
    try:
        for step in steps:
            if step is not a3:
                set_option("DuelArena", True)
                check("the box DuelArena reaches the guest's game", eventually(lambda: rule(A) == "True", timeout=10))
            log(f"--- {step.__name__.upper()}: {step.__doc__}")
            try:
                step(ctx)
            except Exception as ex:  # noqa: BLE001
                check(f"{step.__name__} interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
            for name, port in (("host", H), ("guest", A)):
                try:
                    print(f"    shot {name}: {shot(f'arena6-{step.__name__}-{name}', port)}")
                except Exception:  # noqa: BLE001
                    pass
            to_home()
    finally:
        set_option("DuelArena", was == "True")
    scan_logs(t0)
    done()


if __name__ == "__main__":
    main()
