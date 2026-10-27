"""H5-10: a Steam achievement earned in the host's game for another player goes to that player's game (AchievementDelta,
union 1041), not to the host's account; a game never keeps one earned by another player.

The bench is offline (Steam.GetAchievement returns at once when BaseCore.IsOffline), so the mod's own log lines are
read: the host's "Achievement {Id} goes to player {Uid}" and the guest's "Achievement {Id} earned in the host's game for
this player". Red before: no line at all (the host's game kept every one, there was no message).

A1  a putit killed by the guest (a death settled in the host's game with the guest as origin): PUTIT goes to the guest.
A2  the same death by the host's own character: no "goes to" line (the host's own, as the game has it).
A3  anything done while the guest stands in as "the player" in the host's game (a box opened for it, a dialog, a craft;
    RemoteCraft.AsCrafter): the achievement goes to the guest.
A4  a priest heals the wounded guest beside it (TownLifePatch, rule "Town life" on): PRIEST goes to the guest.

Not played like a player: A1/A2 call Chara.Die with the origin (no real fight); A3 calls Steam.GetAchievement(SMOKE)
inside RemoteCraft.AsCrafter by eval; A4 places the priest and ticks its idle AI by hand as npcplayers_suite pr1 does
(the guest wounded by hp = 1 on the host's copy). Not covered: a use replayed by CardOnUseDelta (PlayerAchievements.For
on the user) and the real unlock on a Steam account (needs two online accounts).

Host + 1 client open (ports 27551 / 27552), same map, rule "Town life" on for A4.
usage (from dev/, PYTHONPATH=_tools/pylib): python <worktree>/bench/h510_achievements.py
"""
import sys
import time
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
from guest_suite import awake, cell, chara, stand  # noqa: E402
from mp_test import state  # noqa: E402
from resync_suite import lines, now  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A = 27551, 27552


def went(t0, ach, uid):
    return [d for d in lines(t0, "goes to player") if str(d.get("Id")) == ach and str(d.get("Uid")) == str(uid)]


def earned(t0, ach):
    return [d for d in lines(t0, "earned in the host's game") if str(d.get("Id")) == ach]


def putit(G, origin):
    uid = int(ev(H, f'var c = CharaGen.Create("putty"); EClass._zone.AddCard(c, {G}.pos.GetNearestPoint(allowChara: false)); '
                    'return c.uid.ToString();'))
    ev(H, f'var c = EClass._map.charas.Find(x => x.uid == {uid}); c.Die(null, {origin}); return "ok";')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    awake(H)
    awake(A)
    h, a = int(state(H)["pc"]["uid"]), int(state(A)["pc"]["uid"])
    G = chara(H, a)

    print("A1 a putit killed by the guest: the achievement goes to the guest")
    t0 = now()
    time.sleep(1)
    putit(G, G)
    check("A1 the host sends PUTIT to the guest", eventually(lambda: bool(went(t0, "PUTIT", a)), timeout=15))
    check("A1 the guest's game takes it", eventually(lambda: bool(earned(t0, "PUTIT")), timeout=15))

    print("A2 a putit killed by the host: the host's own")
    t0 = now()
    time.sleep(1)
    putit(G, "EClass.pc")
    time.sleep(5)
    check("A2 nothing goes to the guest", not lines(t0, "goes to player") and not earned(t0, "PUTIT"))

    print("A3 the guest stands in as the player in the host's game")
    t0 = now()
    time.sleep(1)
    ev(H, 'var m = HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.RemoteCraft"), "AsCrafter"); '
          f'var s = (System.IDisposable)m.Invoke(null, new object[] {{ {G}, true }}); '
          'try { Steam.GetAchievement(ID_Achievement.SMOKE); } finally { s.Dispose(); } return "ok";')
    check("A3 the host sends SMOKE to the guest", eventually(lambda: bool(went(t0, "SMOKE", a)), timeout=15))
    check("A3 the guest's game takes it", eventually(lambda: bool(earned(t0, "SMOKE")), timeout=15))

    print("A4 a priest heals the wounded guest: the achievement goes to the guest")
    home_h, home_a = cell(H, h), cell(A, a)
    uid = 0
    t0 = now()
    time.sleep(1)
    try:
        far = str(ev(H, 'var o = ' + G + '.pos; var b = EClass._map.bounds; for (int x = b.x; x <= b.maxX; x += 3) for (int z = b.z; z <= b.maxZ; z += 3) { '
                        'var p = new Point(x, z); if (p.IsValid && p.IsInBounds && !p.IsBlocked && !p.HasChara && !p.cell.IsTopWaterAndNoSnow && p.Distance(o) >= 25) return x + "," + z; } return "";'))
        if check(f"A4 set up: a free tile 25+ tiles from the guest ({far or 'none'})", bool(far)):
            stand(H, h, *far.split(","))
            time.sleep(1)
            uid = int(ev(H, 'var c = CharaGen.Create("priest"); c.c_originalHostility = c.hostility = Hostility.Friend; '
                            f'EClass._zone.AddCard(c, {G}.pos.GetNearestPoint(allowChara: false)); return c.uid.ToString();'))
            ev(H, f'var g = {G}; g.hp = 1; return "ok";')
            r = str(ev(H, f'var c = EClass._map.charas.Find(x => x.uid == {uid}); var g = {G}; var ai = new AI_Idle(); c.SetAI(ai); int n = 0; '
                          'for (; n < 3000 && c.ai == ai && g.hp <= 1; n++) ai.Tick(); c.SetAIIdle(); return n + "|" + g.hp;'))
            n, hp = (int(v) for v in r.split("|"))
            check(f"A4 set up: the priest healed the guest (after {n} ticks, hp {hp})", hp > 1)
            check("A4 the host sends PRIEST to the guest", eventually(lambda: bool(went(t0, "PRIEST", a)), timeout=15))
            check("A4 the guest's game takes it", eventually(lambda: bool(earned(t0, "PRIEST")), timeout=15))
    finally:
        if uid:
            ev(H, f'var c = EClass._map.charas.Find(x => x.uid == {uid}); if (c != null) c.Destroy(); return "ok";')
        ev(H, f'var g = {G}; g.hp = g.MaxHP; return "ok";')
        stand(H, h, *home_h.split(","))
        stand(A, a, *home_a.split(","))


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
