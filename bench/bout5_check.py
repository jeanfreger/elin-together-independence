"""Council 7, step 5: a guest duels an adventurer as the host does (Helper/AdventurerBout.cs, host box GuestBouts, on by
default). Host + 1 client open (python _tools/mp_test.py), both on the same town map.
usage: python bench/bout5_check.py [--only b1,b2,b3,b4]

B1 the guest wins. It talks to an adventurer standing next to it and picks the game's "Duel", then "Yes", in the real
   dialog; its game takes it to the bout's zone. Meanwhile, in the host's game, the adventurer has left town ("somewhere")
   and its week before the next bout has started. The guest fights it with the game's melee act until it yields (the
   game's "bout_win" dialog, clicked through), which brings the guest back to town. Then, in both games: the adventurer
   stands on the tile it left, friendly, healed, beaten once more; the guest's game holds one copy of it; the reward (the
   piece the host's adventurer wore, picked by the game's rule, or platinum) left the adventurer and is in the guest's
   bag; the guest's fame went up; the dialog offers no bout to either player any more (the week).
B2 the guest loses: the adventurer stops it (the game's "bout_lose"); back in town the adventurer is there, not beaten
   again, it kept its gear, the guest's fame went down (the game's own loss).
B3 the guest walks out of the bout's zone before the end: the adventurer comes back to its tile, nobody beaten.
B4 the host's own bout (the game's, untouched): the guest's game learns the adventurer's week as soon as the host says
   yes, and its "beaten" count once the host won and came back.
Red before the change (2026-10-12 notes): the adventurer stayed in town in the host's game during a guest's bout, and
nothing of the bout (week, beaten, reward) existed after the guest's next copy of the world.

What the bench does not play like a player: the adventurer is brought next to the player and its week cleared by eval;
the fighter's strength is raised to 300 and its foe's hit points set to 5 by the game running the bout before each blow
(the fighter is kept at full hit points while it wins); the blows are the game's melee act called through the bridge,
not the mouse; dialog lines are clicked through as a click does (DramaEventTalk.Play); the losing blow (B2) is the
adventurer's melee act called on the guest; the walk out (B3) is the game's MoveZone to the town.
"""
import argparse
import time

from duel_bench import (A, H, RESULTS, chara, check, drama_open, drive, ev, eventually, free_next_to, host_table, log,  # noqa: F401
                        scan_logs, settled, shot, stand, state, talk, to_home, zone_uid, choices, done)

_used = []


def adventurer():
    """A wandering adventurer of the host's world that is not on this map (each step takes another one)."""
    skip = ",".join(str(u) for u in _used) or "0"
    uid = int(ev(H, 'var skip = new[] { ' + skip + ' }; var c = EClass.game.cards.globalCharas.Values.FirstOrDefault(x => '
                    'x.trait is TraitAdventurer && x.trait.CanBout && !x.IsPCFaction && !x.isDead && !skip.Contains(x.uid) && '
                    'x.currentZone != null && x.currentZone != EClass._zone && !x.currentZone.IsInstance); '
                    'return c == null ? "0" : c.uid.ToString();'))
    _used.append(uid)
    return uid


def bring(adv, guest_uid):
    """The host brings the adventurer next to the guest, friendly, its week cleared, in both games."""
    spot = free_next_to(H, guest_uid, 2) or free_next_to(H, guest_uid, 3)
    x, z = (int(v) for v in spot.split(","))
    ev(H, f'var c = EClass.game.cards.Find({adv}); c.SetInt(59, 0); '
          f'c.MoveZone(EClass._zone, new ZoneTransition {{ state = ZoneTransition.EnterState.Exact, x = {x}, z = {z} }}); '
          'c.hostility = c.c_originalHostility = Hostility.Friend; c.SetEnemy(); c.HealAll(); return "ok";')
    seen = eventually(lambda: ev(A, f'return ({chara(A, adv)} != null).ToString();') == "True", timeout=20)
    if seen:
        ev(A, f'{chara(A, adv)}.SetInt(59, 0); "ok"')
    pos = ev(H, f'var c = {chara(H, adv)}; return c.pos.x + "," + c.pos.z;')
    log(f"adventurer {adv} brought to {pos}, seen by the guest {seen}")
    return seen, pos


def where(adv):
    return ev(H, f'var c = EClass.game.cards.Find({adv}); return c == null ? "gone" : c.currentZone == null ? "nowhere" : c.currentZone.id + ":" + c.currentZone.uid + ":" + c.pos.x + "," + c.pos.z;')


def ints(port, adv):
    return ev(port, f'var c = EClass.game.cards.Find({adv}) ?? EClass._map.charas.Find(x => x.uid == {adv}); '
                    'return c == null ? "-" : c.GetInt(59) + "/" + c.GetInt(111);')


def reward_of(adv):
    """The piece the game's ZonePreEnterBoutWin would take off this adventurer, by the host's copy: uid, 0 for platinum."""
    return int(ev(H, f'var c = EClass.game.cards.Find({adv}); Thing best = null; var price = 0; foreach (var t in c.things) '
                     '{ if (t.isEquipped && !t.isGifted && (t.GetPrice() > price || t.rarity >= Rarity.Artifact)) { best = t; price = t.GetPrice(); } } '
                     'return best == null ? "0" : best.uid.ToString();'))


def fame(port):
    return int(ev(port, 'return EClass.player.fame.ToString();'))


def start_bout(port, adv):
    """The player talks to the adventurer, picks the game's "Duel", then "Yes": true once it stands in the bout's zone."""
    if not check(f"port {port}: the adventurer's dialog opens", talk(port, adv)):
        return False
    duel = ev(port, 'return "daBout".lang();')
    offered = choices(port)
    if not check(f"port {port}: the dialog offers '{duel}' ({offered})", any(duel in c for c in offered)):
        drive(port, [])
        return False
    drive(port, [duel, ev(port, 'return "yes".lang();')])
    inside = eventually(lambda: ev(port, 'return (EClass._zone.instance is ZoneInstanceBout).ToString();') == "True", timeout=90)
    return check(f"port {port}: said yes, the player stands in the bout's zone", inside)


def fight(port, adv, win=True):
    """In the game running the bout: the player beats the adventurer (win) or the adventurer beats the player. True once
    the game's bout dialog is open."""
    me = 'EClass.pc'
    foe = f'EClass._map.charas.Find(x => x.uid == {adv})'
    if win:
        ev(port, f'{me}.elements.SetBase(70, 300); "ok"')
    else:
        ev(port, f'{foe}.elements.SetBase(70, 300); "ok"')
    for n in range(1, 31):
        ev(port, f'var t = {foe}; if (t != null && t.pos.Distance({me}.pos) > 1) {{ var p = t.pos.GetNearestPoint(allowBlock: false, allowChara: false); '
                 f'if (p != null) {me}.Teleport(p, true, true); }} "ok"')
        if win:
            ev(port, f'var t = {foe}; t.hp = 5; {me}.hp = {me}.MaxHP; ACT.Melee.Perform({me}, t, t.pos); "ok"')
        else:
            ev(port, f'var t = {foe}; {me}.hp = 1; ACT.Melee.Perform(t, {me}, {me}.pos); "ok"')
        if eventually(lambda: drama_open(port), timeout=3):
            log(f"port {port}: bout dialog after {n} blow(s)")
            return True
    return False


def back_in_town(port, town):
    """The bout's dialog clicked through: the game takes the player back to town."""
    drive(port, [])
    if port == A:
        return check("the guest is back in town, on the host's map", eventually(settled(A, town, False), timeout=150))
    return check("the host is back in town", eventually(lambda: zone_uid(H) == town, timeout=150))


def b1(ctx):
    """the guest wins its bout"""
    town = zone_uid(H)
    guest = ctx["a"]
    adv = adventurer()
    if not check(f"the host's world has a wandering adventurer ({adv})", adv):
        return
    seen, pos = bring(adv, guest)
    if not check("the guest sees it next to it", seen):
        return
    beaten0 = int(ints(H, adv).split("/")[1])
    reward = reward_of(adv)
    fame0 = fame(A)
    log(f"before: marks {ints(H, adv)}, reward {reward}, guest fame {fame0}")
    if not start_bout(A, adv):
        return
    check(f"during the bout the host's adventurer left town ({where(adv)})", eventually(lambda: where(adv).startswith("somewhere"), timeout=15))
    check(f"its week started in the host's game ({ints(H, adv)})", int(ints(H, adv).split("/")[0]) > 0)
    check(f"the host keeps the bout in its save ({host_table('AdventurerBouts', adv)})",
          host_table("AdventurerBouts", adv).startswith(f"{town},"))
    if not check("the guest beats it with the game's melee act: the bout dialog opens", fight(A, adv)):
        return
    if not back_in_town(A, town):
        return
    check(f"the adventurer is back on its tile in the host's game ({where(adv)})",
          eventually(lambda: where(adv) == f"{ev(H, 'return EClass._zone.id;')}:{town}:{pos}", timeout=15))
    for port, who in ((H, "host"), (A, "guest")):
        copies = ev(port, f'return EClass._map.charas.Count(x => x.uid == {adv}).ToString();')
        check(f"{who}'s game: one copy of the adventurer on the map ({copies})", eventually(
            lambda p=port: ev(p, f'return EClass._map.charas.Count(x => x.uid == {adv}).ToString();') == "1", timeout=20))
        state_ = ev(port, f'var c = {chara(port, adv)}; return c == null ? "-" : c.hostility + " " + (c.hp >= c.MaxHP);')
        check(f"{who}'s game: friendly and healed ({state_})", state_ == "Friend True")
        check(f"{who}'s game: beaten once more ({ints(port, adv)}, was {beaten0})",
              eventually(lambda p=port: int(ints(p, adv).split("/")[1]) == beaten0 + 1, timeout=15))
    if reward:
        has = lambda p, u: ev(p, f'var c = {chara(p, u)}; return (c != null && c.things.Find(x => x.uid == {reward}) != null).ToString();')  # noqa: E731
        check(f"the reward {reward} is in the guest's bag in the host's game", eventually(lambda: has(H, guest) == "True", timeout=20))
        check("and in the guest's own game", eventually(lambda: ev(A, f'return (EClass.pc.things.Find(x => x.uid == {reward}) != null).ToString();') == "True", timeout=20))
        check("and no longer on the adventurer", has(H, adv) == "False")
    else:
        check("the reward is platinum: the guest has some, in both games",
              eventually(lambda: ev(A, 'return EClass.pc.things.Where(x => x.id == "plat").Sum(x => x.Num).ToString();') != "0", timeout=20))
    check(f"the record is settled ({host_table('AdventurerBouts', adv)})", eventually(lambda: host_table("AdventurerBouts", adv) == "none", timeout=20))
    check(f"the guest's fame went up ({fame0} -> {fame(A)})", fame(A) > fame0)
    for port, who in ((A, "guest"), (H, "host")):
        if talk(port, adv):
            offered = choices(port)
            duel = ev(port, 'return "daBout".lang();')
            check(f"the {who}'s dialog offers no bout any more ({offered})", not any(duel in c for c in offered))
            drive(port, [])


def b2(ctx):
    """the guest loses its bout"""
    town = zone_uid(H)
    adv = adventurer()
    seen, pos = bring(adv, ctx["a"])
    if not check(f"an adventurer ({adv}) next to the guest", adv and seen):
        return
    beaten0 = int(ints(H, adv).split("/")[1])
    gear0 = ev(H, f'return {chara(H, adv)}.things.Count(x => x.isEquipped).ToString();')
    fame0 = fame(A)
    if not start_bout(A, adv):
        return
    if not check("the adventurer beats the guest: the bout dialog opens", fight(A, adv, win=False)):
        return
    if not back_in_town(A, town):
        return
    check(f"the adventurer is back on its tile ({where(adv)})", eventually(lambda: where(adv).endswith(f":{town}:{pos}"), timeout=15))
    check(f"not beaten again ({ints(H, adv)})", int(ints(H, adv).split("/")[1]) == beaten0)
    check(f"it kept its gear ({gear0})", ev(H, f'return {chara(H, adv)}.things.Count(x => x.isEquipped).ToString();') == gear0)
    check(f"the record is gone ({host_table('AdventurerBouts', adv)})", host_table("AdventurerBouts", adv) == "none")
    check(f"the guest's fame went down, as the game takes it ({fame0} -> {fame(A)})", fame(A) < fame0 or fame0 == 0)


def b3(ctx):
    """the guest walks out of its bout"""
    town = zone_uid(H)
    adv = adventurer()
    seen, pos = bring(adv, ctx["a"])
    if not check(f"an adventurer ({adv}) next to the guest", adv and seen):
        return
    beaten0 = int(ints(H, adv).split("/")[1])
    if not start_bout(A, adv):
        return
    ev(A, f'EClass.pc.MoveZone(EClass.game.spatials.Find({town})); "ok"')
    if not check("the guest walked out, back in town", eventually(settled(A, town, False), timeout=150)):
        return
    check(f"the adventurer is back on its tile ({where(adv)})", eventually(lambda: where(adv).endswith(f":{town}:{pos}"), timeout=15))
    check(f"nobody beaten ({ints(H, adv)})", int(ints(H, adv).split("/")[1]) == beaten0)
    check(f"the record is gone ({host_table('AdventurerBouts', adv)})", host_table("AdventurerBouts", adv) == "none")


def b4(ctx):
    """the host's own bout: the guest's game learns the adventurer's week and its beaten count"""
    town = zone_uid(H)
    adv = adventurer()
    seen, _ = bring(adv, ctx["h"])
    if not check(f"an adventurer ({adv}) next to the host, seen by the guest", adv and seen):
        return
    beaten0 = int(ints(H, adv).split("/")[1])
    if not start_bout(H, adv):
        return
    check(f"the guest's game learns the week ({ints(A, adv)})", eventually(lambda: int(ints(A, adv).split("/")[0]) > 0, timeout=20))
    if not check("the host beats it: the bout dialog opens", fight(H, adv)):
        return
    if not back_in_town(H, town):
        return
    check(f"the guest's game learns it was beaten ({ints(A, adv)}, was {beaten0})",
          eventually(lambda: int(ints(A, adv).split("/")[1]) == beaten0 + 1, timeout=30))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    a = ap.parse_args()
    from datetime import datetime, timezone
    t0 = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")
    ctx = {"a": state(A)["pc"]["uid"], "h": state(H)["pc"]["uid"]}
    to_home()
    check("the host's box GuestBouts is on in the guest's game",
          ev(A, 'return ElinTogether.Net.NetSession.Instance.Rules.GuestBouts.ToString();') == "True")
    steps = [b1, b2, b3, b4]
    if a.only:
        steps = [s for s in steps if s.__name__ in a.only.split(",")]
    for step in steps:
        log(f"--- {step.__name__.upper()}: {step.__doc__}")
        try:
            step(ctx)
        except Exception as ex:  # noqa: BLE001
            check(f"{step.__name__} interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
        for name, port in (("host", H), ("guest", A)):
            try:
                print(f"    shot {name}: {shot(f'bout5-{step.__name__}-{name}', port)}")
            except Exception:  # noqa: BLE001
                pass
        to_home()
        time.sleep(3)
    scan_logs(t0)
    done()


if __name__ == "__main__":
    main()
