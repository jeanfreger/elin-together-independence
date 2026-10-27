"""World time, stage 4 (council 2026-10-26, PLAN_world_time_mmo.md): an absent player's own timers are frozen and resumed,
behind the host setting "Quests wait for absent players" (AbsentQuestsWait, on). Host + 1 guest (27552) open at the
Prairie, personal quests on; a second guest (27553) is used as a control when it is open.

    python bench/world_time_stage4.py [on,off]

ON   the guest takes a personal quest (7 days left), leaves the game (title); the host plays on 3 days; the guest joins
     again: its quest has the same time left (within 1 h, the join's own minutes) and its hunger is unchanged. The
     second guest, present all along with a quest of its own, has 3 days less (the world's time is its time).
OFF  the same with the box off: the absent guest's quest comes back 3 days shorter, as before stage 4.
Red before stage 4 (ON): the quest came back 3 days shorter. Red without the box (OFF): the quest kept its time.

Not played like a player: the quests are taken with quests.Start and their deadline set by eval; the host's 3 days are
GameDate.AdvanceMin by eval (the host playing on alone, as a long trip or nights would); leaving is ResetSession +
title (chara_suite.leave). The host's own player is never absent from its own world (it is the world); a host that
reopens a world copy after a takeover is not covered.
"""
import sys
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import emp  # noqa: E402
from hunt5_suite import BF, CONFIG, HOSTT, get_rule, rule_on_both, set_rule  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A, B = 27551, 27552, 27553
DAY = 24 * 60
WEEK = 7 * DAY
GEN = 'System.Collections.Generic.'


def is_open(port):
    try:
        return emp.call(port, "state", {}, timeout=5) is not None
    except Exception:  # noqa: BLE001
        return False


def left(port, q):
    return int(ev(port, f'var x = EClass.game.quests.list.Find(y => y.uid == {q}); '
                        'return x == null ? "-99999" : (x.deadline - EClass.world.date.GetRaw()).ToString();'))


def kept(chara, q):
    """The time left of quest q in what the host keeps for that player."""
    return int(ev(H, f'var logs = ({GEN}Dictionary<int, {GEN}Dictionary<int, byte[]>>)'
                     f'{HOSTT}.GetProperty("PersonalQuestLogs", {BF}).GetValue(null); '
                     f'{GEN}Dictionary<int, byte[]> log; byte[] b; '
                     f'if (!logs.TryGetValue({chara}, out log) || !log.TryGetValue({q}, out b)) return "-99999"; '
                     'var x = new ElinTogether.Models.LZ4Bytes { Bytes = b }.Decompress<Quest>(); '
                     'return (x.deadline - EClass.world.date.GetRaw()).ToString();'))


def take_quest(port, label):
    from quest_suite import accept, in_log, offer  # noqa: PLC0415
    quest, qid = offer(port)
    accept(port, quest)
    check(f"{label} took quest {qid} ({quest})", eventually(lambda: in_log(port, quest), timeout=15))
    ev(port, f'var q = EClass.game.quests.list.Find(x => x.uid == {quest}); q.deadline = EClass.world.date.GetRaw() + {WEEK}; return "ok";')
    chara = int(ev(port, 'return EClass.pc.uid.ToString();'))
    check(f"{label}: the host keeps it with about 7 days left", eventually(lambda: abs(kept(chara, quest) - WEEK) <= 60, timeout=60))
    return quest


def drop_quest(port, quest):
    if quest:
        ev(port, f'var q = EClass.game.quests.list.Find(x => x.uid == {quest}); if (q != null) EClass.game.quests.list.Remove(q); return "ok";')


def round_trip(box):
    from chara_suite import leave  # noqa: PLC0415
    from fixtures import cleanup, preflight  # noqa: PLC0415
    from mp_test import join_client, state  # noqa: PLC0415
    from quest_suite import in_log  # noqa: PLC0415
    tag = "ON" if box else "OFF"
    print(f"\n== {tag} a guest away for 3 days, quests wait {box}", flush=True)
    control = is_open(B)
    preflight(ports=(H, A, B) if control else (H, A))
    saved = (get_rule("PersonalQuests"), get_rule("AbsentQuestsWait"))
    quest = other = None
    try:
        set_rule("PersonalQuests", "true")
        set_rule("AbsentQuestsWait", str(box).lower())
        check(f"{tag} personal quests on both games", rule_on_both("UsePersonalQuests", True))
        got = ev(H, f'var e = {CONFIG}.GetProperty("AbsentQuestsWait", {BF}).GetValue(null); return e.GetType().GetProperty("Value").GetValue(e).ToString();')
        check(f"{tag} the host's box is {box} ({got})", got == str(box))
        quest = take_quest(A, f"{tag} the guest")
        if control:
            other = take_quest(B, f"{tag} the second guest")
        left0, hunger0 = left(A, quest), int(ev(A, 'return EClass.pc.hunger.value.ToString();'))
        other0 = left(B, other) if other else 0

        leave()
        ev(H, f'EClass.world.date.AdvanceMin({3 * DAY}); return "ok";')
        join_client(H, A, "guest")
        players = 3 if control else 2
        check(f"{tag} the guest is back", eventually(lambda: len(state(H).get("players", [])) == players and state(A).get("sceneMode") == "Zone", timeout=120))
        check(f"{tag} its quest is back in its log", eventually(lambda: in_log(A, quest), timeout=60))
        left1 = left(A, quest)
        if box:
            check(f"ON its quest kept its time while it was away ({left0} -> {left1} min; before stage 4: 3 days less)", abs(left1 - left0) <= 60)
            hunger1 = int(ev(A, 'return EClass.pc.hunger.value.ToString();'))
            check(f"ON its hunger is as it left it ({hunger0} -> {hunger1})", abs(hunger1 - hunger0) <= 2)
        else:
            check(f"OFF its quest ran out with the world ({left0} -> {left1} min, 3 days less)", abs(left0 - left1 - 3 * DAY) <= 60)
        if other:
            other1 = left(B, other)
            check(f"{tag} the second guest, present, lived the 3 days ({other0} -> {other1} min)", abs(other0 - other1 - 3 * DAY) <= 60)
    finally:
        drop_quest(A, quest)
        if other:
            drop_quest(B, other)
        set_rule("AbsentQuestsWait", saved[1].lower())
        set_rule("PersonalQuests", saved[0].lower())
        cleanup(ports=(H, A, B) if control else (H, A))


STEPS = {"on": lambda: round_trip(True), "off": lambda: round_trip(False)}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    names = sys.argv[1].split(",") if len(sys.argv) > 1 else list(STEPS)
    for name in names:
        try:
            STEPS[name]()
        except Exception as ex:  # noqa: BLE001
            check(f"{name} interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} checks OK")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
