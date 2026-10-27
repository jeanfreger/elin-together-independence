"""Council 7, step 7: bets on duels (Helper/PlayerDuel.cs, host box DuelBets, off by default; the check turns it on,
then back to what it was). Host + 1 client open (python _tools/mp_test.py), both in town, the arena box off.
usage: python bench/bets7_check.py [--only s1,s2,s3,s4,s5,s6]

S1 the guest challenges through the game's menu and picks "100 gold" in the stake list; the host's box says the stake;
   Yes: each one's own game shows 100 gold less, the host notes both stakes in its save (duel_stakes), the gold of the
   two purses plus the notes stays the same at every step; the guest wins: it has 100 more than before, the host 100
   less, in both games, the notes are gone.
S2 a stake declined: no gold moves.
S3 the loser gives up through its own menu: the other one takes the stakes (giving up loses them).
S4 a duellist leaves the map in the middle of the duel: the duel ends without a winner and each one gets its stake
   back (the guest, away on its own map, through the money the host owes it).
S5 "as much as both can put down": the stake is what the poorer one has (the guest, 300), shown in the box.
S6 a guest that says it paid and did not (a modified game: its own game is kept from paying, the "paid" message is
   sent by hand): the host finds no gold left its purse, the duel is called off after 20 s, the host's own stake comes
   back, nothing changed hands.

What the bench does not play like a player: as duel_suite D1/D2 (menu built by the bridge, the blow is the game's melee
act, the loser's hit points set to 5 by the host, strength 300, teleport side by side); the stake list and the Yes of
the box are clicked through the bridge; the gold is set by the host (duel_suite.prepare, give) and by ModCurrency.
"""
import argparse
import time

from duel_bench import (A, H, chara, check, click_yes, describe, dialog_open, done, ev, eventually, finish, gold,  # noqa: F401
                        host_table, log, menu, next_to, prepare, scan_logs, set_option, shot, start_duel, state, to_home, winner)
from duel_suite import DUEL, challenge, click_no, ended, other  # noqa: E402
from travel_suite import HOME, VERNIS, both_joined, client_settled, dismiss_dialogs, move, wait  # noqa: E402


def rule(port):
    return ev(port, 'return ElinTogether.Net.NetSession.Instance.Rules.DuelBets.ToString();')


def pick_stake(port, text):
    """Clicks the line of the stake list (the game's LayerList) whose text contains `text`."""
    return ev(port, 'var l = EClass.ui.GetLayer<LayerList>(); if (l == null) return "no list"; '
                    'var b = l.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(x => '
                    f'x.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("{text}"))); '
                    'if (b == null) return "no line"; b.onClick.Invoke(); return "click";')


def box_text(port):
    return ev(port, 'var d = EClass.ui.layers.OfType<Dialog>().LastOrDefault(); if (d == null) return "-"; '
                    'return string.Join(" ", d.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text));')


def purses(ctx):
    """(host's own gold, guest's own gold, the guest's gold as the host keeps it, the two stakes the host notes)"""
    h, a = ctx["h"][1], ctx["a"][1]
    stakes = [host_table("DuelStakes", u) for u in (h, a)]
    held = sum(int(s) for s in stakes if s.lstrip("-").isdigit())
    return gold(H), gold(A), gold(H, a), held


def conserved(ctx, total, when):
    h, a, a_host, held = purses(ctx)
    check(f"{when}: purses + stakes = {total} (host {h} + guest {a_host} + stakes {held}; guest's own game {a})",
          h + a_host + held == total and a == a_host)


def stake_challenge(ctx, key, stake_text):
    """`key` challenges the other one through the menu and picks a stake: True once the other one sees the box."""
    port = ctx[key][0]
    oport = ctx[other(key)][0]
    r = challenge(ctx, key)
    log(f"menu: {r[:80]}")
    shown = eventually(lambda: ev(port, 'return (EClass.ui.GetLayer<LayerList>() != null).ToString();') == "True", timeout=5)
    if not check("the challenger's game shows the stake list", shown):
        return False
    check(f"it picks '{stake_text}' ({pick_stake(port, stake_text)})", True)
    return check("the other one sees the box", eventually(lambda: dialog_open(oport), timeout=10))


def total_of(ctx):
    h, a, a_host, held = purses(ctx)
    return h + a_host + held


def s1(ctx):
    """a stake of 100, the guest wins: it takes both stakes"""
    prepare(ctx)
    total = total_of(ctx)
    h0, a0 = gold(H), gold(A)
    if not stake_challenge(ctx, "a", "100 "):
        return
    check(f"the host's box says the stake ({box_text(H)[:120]})", "100" in box_text(H))
    click_yes(H)
    check(f"each one's own game shows 100 less (host {h0} -> {gold(H)}, guest {a0} -> {gold(A)})",
          eventually(lambda: gold(H) == h0 - 100 and gold(A) == a0 - 100, timeout=15))
    check(f"the host notes both stakes ({host_table('DuelStakes', ctx['h'][1])}, {host_table('DuelStakes', ctx['a'][1])})",
          eventually(lambda: host_table("DuelStakes", ctx["h"][1]) == "100" and host_table("DuelStakes", ctx["a"][1]) == "100", timeout=15))
    conserved(ctx, total, "stakes down")
    check(f"the duel is fought ({describe(H)})", eventually(lambda: all(describe(p).startswith("Fighting") for p in (H, A)), timeout=20))
    if not finish(ctx, "a"):
        return
    check(f"the guest won the stakes: {a0} -> {gold(A)} (+100), host {h0} -> {gold(H)} (-100)",
          eventually(lambda: gold(A) == a0 + 100 and gold(H) == h0 - 100, timeout=15))
    check("and the host keeps the same for the guest", eventually(lambda: gold(H, ctx["a"][1]) == a0 + 100, timeout=15))
    check("the notes are gone", host_table("DuelStakes", ctx["h"][1]) == "none" and host_table("DuelStakes", ctx["a"][1]) == "none")
    conserved(ctx, total, "after the duel")


def s2(ctx):
    """a stake declined: no gold moves"""
    prepare(ctx)
    h0, a0 = gold(H), gold(A)
    if not stake_challenge(ctx, "h", "100 "):
        return
    click_no(A)
    check(f"declined ({describe(H)})", eventually(lambda: describe(H).endswith("emp_duel_declined"), timeout=10))
    ended()
    time.sleep(3)
    check(f"no gold moved (host {h0} -> {gold(H)}, guest {a0} -> {gold(A)})", gold(H) == h0 and gold(A) == a0)


def s3(ctx):
    """the loser gives up: the other one takes the stakes"""
    prepare(ctx)
    total = total_of(ctx)
    h0, a0 = gold(H), gold(A)
    if not stake_challenge(ctx, "h", "100 "):
        return
    click_yes(A)
    if not check("the duel is fought", eventually(lambda: all(describe(p).startswith("Fighting") for p in (H, A)), timeout=30)):
        return
    r = menu(A, ctx["a"][1], 'i.act is DynamicAct d && d.id == "emp_act_duel_giveup"')
    check(f"the guest gives up through its own menu ({r[:60]})", r.startswith("ok"))
    check(f"the host wins ({describe(H)})", eventually(lambda: describe(H).startswith("Won") and winner(H) == ctx["h"][1], timeout=15))
    ended()
    check(f"giving up loses the stake: guest {a0} -> {gold(A)} (-100), host {h0} -> {gold(H)} (+100)",
          eventually(lambda: gold(A) == a0 - 100 and gold(H) == h0 + 100, timeout=15))
    conserved(ctx, total, "after giving up")


def s4(ctx):
    """a duellist leaves in the middle: no winner, each one gets its stake back"""
    prepare(ctx)
    h0, a0 = gold(H), gold(A)
    if not stake_challenge(ctx, "h", "100 "):
        return
    click_yes(A)
    if not check("the duel is fought", eventually(lambda: all(describe(p).startswith("Fighting") for p in (H, A)), timeout=30)):
        return
    try:
        move(A, VERNIS)
        check(f"the guest left: the duel ends without a winner ({describe(H)})",
              eventually(lambda: describe(H).endswith("winner 0 emp_duel_over"), timeout=60))
        ended()
        check(f"the host gets its stake back ({h0} -> {gold(H)})", eventually(lambda: gold(H) == h0, timeout=15))
        wait(client_settled(A, VERNIS, True), "guest alone in Vernis", timeout=180)
        check(f"the guest, away, gets its stake back too ({a0} -> {gold(A)})", eventually(lambda: gold(A) == a0, timeout=60))
        check("the notes are gone", host_table("DuelStakes", ctx["a"][1]) == "none" and host_table("DuelStakes", ctx["h"][1]) == "none")
    finally:
        move(A, HOME)
        both_joined(H, A, HOME)
        time.sleep(4)
        dismiss_dialogs(A)


def s5(ctx):
    """everything: the stake is what the poorer one has"""
    prepare(ctx)
    a0 = gold(A)
    # the guest keeps 300 (its own game spends the rest), the host has more
    if a0 > 300:
        ev(A, f'EClass.pc.ModCurrency(-{a0 - 300}); "ok"')
    if gold(H) < 1000:
        ev(H, 'EClass.pc.ModCurrency(1000); "ok"')
    eventually(lambda: gold(H, ctx["a"][1]) == 300, timeout=15)
    all_text = ev(H, 'return "emp_duel_stake_all".lang();')
    if not stake_challenge(ctx, "h", all_text):
        return
    check(f"the guest's box says 300 ({box_text(A)[:120]})", "300" in box_text(A))
    click_no(A)
    ended()


def s6(ctx):
    """a guest that says it paid and did not: the duel is called off, nothing changes hands"""
    prepare(ctx)
    h0, a0 = gold(H), gold(A)
    if not stake_challenge(ctx, "h", "100 "):
        return
    duel = ev(A, 'return ElinTogether.Helper.PlayerDuel.View.DuelId.ToString();')
    # its game is kept from paying (as if modified), then says "paid" by hand
    ev(A, f'HarmonyLib.Traverse.Create(typeof(ElinTogether.Helper.PlayerDuel)).Field("_paidDuel").SetValue({duel}); "ok"')
    click_yes(A)
    time.sleep(2)
    ev(A, f'var d = new ElinTogether.Models.DuelIntentDelta {{ Kind = 4, DuelId = {duel} }}; '
          'ElinTogether.Net.NetSession.Instance.Connection.Delta.AddRemote(d); "ok"')
    check(f"the host's stake is down meanwhile ({h0} -> {gold(H)})", eventually(lambda: gold(H) == h0 - 100, timeout=10))
    check(f"no duel is fought: called off for unpaid stakes ({describe(H)})",
          eventually(lambda: describe(H).endswith("emp_duel_unpaid"), timeout=30))
    ended()
    check(f"the host's stake came back ({h0} -> {gold(H)}), the guest's gold did not move ({a0} -> {gold(A)})",
          eventually(lambda: gold(H) == h0, timeout=10) and gold(A) == a0)
    check("no note left", host_table("DuelStakes", ctx["h"][1]) == "none" and host_table("DuelStakes", ctx["a"][1]) == "none")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    a = ap.parse_args()
    from datetime import datetime, timezone
    t0 = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")
    ctx = {"a": (A, state(A)["pc"]["uid"]), "h": (H, state(H)["pc"]["uid"])}
    was = rule(H)
    arena = ev(H, 'return ElinTogether.Net.NetSession.Instance.Rules.DuelArena.ToString();')
    to_home()
    set_option("DuelArena", False)
    set_option("DuelBets", True)
    check("the box DuelBets reaches the guest's game", eventually(lambda: rule(A) == "True", timeout=10))
    steps = [s1, s2, s3, s4, s5, s6]
    if a.only:
        steps = [s for s in steps if s.__name__ in a.only.split(",")]
    try:
        for step in steps:
            log(f"--- {step.__name__.upper()}: {step.__doc__}")
            try:
                step(ctx)
            except Exception as ex:  # noqa: BLE001
                check(f"{step.__name__} interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
            for name, port in (("host", H), ("guest", A)):
                try:
                    print(f"    shot {name}: {shot(f'bets7-{step.__name__}-{name}', port)}")
                except Exception:  # noqa: BLE001
                    pass
    finally:
        set_option("DuelBets", was == "True")
        set_option("DuelArena", arena == "True")
    scan_logs(t0)
    done()


if __name__ == "__main__":
    main()
