"""Teams keep their own story journal, commit 9 of dev/PLAN_teams_knowledge.md. Needs 3 windows: host + 2 guests
already in game, same map, independent travel on; two teams (the host and guest A in one, guest B in another):

    cd dev && set PYTHONPATH=_tools/pylib
    python _tools/mp_test.py --clients 2
    python ../bench/team_story_suite.py

  J0 box on: both guests are told the rule; the host founds a team, A joins it, B founds its own (the team menus'
     own entries); nothing is in the journal yet
  J1 B travels alone to Vernis; the host moves the main quest on, on its map: the host's team took part (the host
     and A), B's did not
  J2 B, alone in Vernis, moves the main quest on in its own game: B's team took part, the host's team keeps its
     chapter (B was alone)
  J3 B comes back; A moves the main quest on, on the host's map: both teams took part (everyone was there)
  J4 the guests' team cards read the same journal as the host's; the world's quest is one for everyone
  J5 box off: a step is written nowhere, the guests are told no journal

Not played like a player: the steps are QuestMain.ChangePhase called by C# in the game that makes them (what a
story dialog calls), B travels by MoveZone (no walk to the map's edge). The main quest is put back where it was.
Proves: who took part (the stepper, the players on its map, a player alone counts alone), the per-team chapter,
no world effect (the quest's phase is the same in every game).
"""
import sys
import time

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import RESULTS, VERNIS, check, client_settled, ev, eventually, move, wait, zone_uid  # noqa: E402

H, A, B = 27551, 27552, 27553
BF = 'System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public'
T = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams")'
HOST = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.ElinNetHost")'
CFG = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpConfig+Server")'
BOOK = f'((System.Collections.Generic.Dictionary<int, int>){HOST}.GetProperty("TeamStoryBook", {BF}).GetValue(null))'
TOLD = f'((System.Collections.Generic.Dictionary<int, int>){T}.GetProperty("ToldStory", {BF}).GetValue(null))'
MAIN = 'EClass.game.quests.Get<QuestMain>()'


def uid(port):
    return int(ev(port, 'return EClass.pc.uid.ToString();'))


def config(name, value=None):
    """A host setting: read it, or set it (then the guests are told the rules)"""
    get = f'var e = {CFG}.GetProperty("{name}", {BF}).GetValue(null); var p = e.GetType().GetProperty("Value");'
    if value is None:
        return ev(H, f'{get} return p.GetValue(e).ToString();') == "True"
    ev(H, f'{get} p.SetValue(e, {str(value).lower()}); return "";')
    ev(H, 'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("UpdateRemoteSessionRules").GetValue(); return "";')
    return value


def guest_rule(port):
    return ev(port, 'return ElinTogether.Net.NetSession.Instance.Rules.TeamStory.ToString();') == "True"


def team_of(charauid):
    return int(ev(H, f'return System.Convert.ToString({T}.GetMethod("TeamOf", {BF}).Invoke(null, new object[] {{ {charauid} }}));'))


def menu(port, who, pick):
    """Runs the first entry of the team menu of that player's row whose label `l` passes `pick`, as a click does"""
    return ev(port, f'var m = (System.Collections.IEnumerable){T}.GetMethod("MenuFor", {BF}).Invoke(null, new object[] {{ {who} }}); '
                    'foreach (var e in m) { var l = (string)e.GetType().GetField("Item1").GetValue(e); '
                    f'if ({pick}) {{ ((System.Action)e.GetType().GetField("Item2").GetValue(e))(); return l; }} }} return "";')


def chapter(team, port=H):
    """The phase that game's journal holds for that team, -1 for none"""
    book = BOOK if port == H else TOLD
    return int(ev(port, f'return {book}.TryGetValue({team}, out var p) ? p.ToString() : "-1";'))


def phase(port):
    return int(ev(port, f'return {MAIN} == null ? "-1" : {MAIN}.phase.ToString();'))


def step(port, to):
    """The main quest moves on in that game, as a story dialog moves it"""
    ev(port, f'{MAIN}.ChangePhase({to}); return "ok";')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a, b = uid(H), uid(A), uid(B)
    was = {name: config(name) for name in ("Teams", "TeamStory", "TeamApproval")}
    p0 = phase(H)
    if not check(f"the world has a main quest (phase {p0})", p0 >= 0):
        return
    home = zone_uid(H)
    book0 = ev(H, f'return string.Join(",", {BOOK}.Select(z => z.Key + ":" + z.Value));')
    try:
        print("J0 box on, two teams")
        config("Teams", True)
        config("TeamApproval", False)
        config("TeamStory", True)
        ev(H, f'{BOOK}.Clear(); return "ok";')
        check("both guests are told the rule", eventually(lambda: guest_rule(A) and guest_rule(B), timeout=15))
        menu(H, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("the host leads a team", eventually(lambda: team_of(h) != 0, timeout=10))
        red = team_of(h)
        time.sleep(2)
        menu(A, f'EClass.game.cards.globalCharas.Find({h})', 'true')
        check("A joined the host's team", eventually(lambda: team_of(a) == red, timeout=15))
        menu(B, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("B leads a team of its own", eventually(lambda: team_of(b) not in (0, red), timeout=15))
        blue = team_of(b)
        check("nothing in the journal yet", chapter(red) == -1 and chapter(blue) == -1)

        print("J1 B away, the host moves the story on")
        move(B, VERNIS)
        wait(client_settled(B, VERNIS, True), "B alone in Vernis", timeout=120)
        step(H, p0 + 1)
        check(f"the host's team took part ({chapter(red)})", chapter(red) == p0 + 1)
        check(f"B's team did not ({chapter(blue)})", chapter(blue) == -1)

        print("J2 B, alone, moves the story on")
        check("B's game follows the world's quest", eventually(lambda: phase(B) == p0 + 1, timeout=20))
        step(B, p0 + 2)
        check("the host's quest moved on", eventually(lambda: phase(H) == p0 + 2, timeout=30))
        check(f"B's team took part ({chapter(blue)})", eventually(lambda: chapter(blue) == p0 + 2, timeout=10))
        check(f"the host's team keeps its chapter ({chapter(red)})", chapter(red) == p0 + 1)

        print("J3 B comes back, A moves the story on")
        move(B, home)
        wait(client_settled(B, home, False), "B back on the host's map", timeout=120)
        time.sleep(5)
        check("A's game follows the world's quest", eventually(lambda: phase(A) == p0 + 2, timeout=20))
        step(A, p0 + 3)
        check("the host's quest moved on", eventually(lambda: phase(H) == p0 + 3, timeout=30))
        check(f"the host's team took part ({chapter(red)})", eventually(lambda: chapter(red) == p0 + 3, timeout=10))
        check(f"B's team too, it was there ({chapter(blue)})", chapter(blue) == p0 + 3)

        print("J4 the guests read the same journal, one world quest")
        for port, name in ((A, "A"), (B, "B")):
            check(f"{name}'s team card: both teams at {p0 + 3}",
                  eventually(lambda: chapter(red, port) == p0 + 3 and chapter(blue, port) == p0 + 3, timeout=15))
        check("the world's quest is one phase in the three games",
              eventually(lambda: phase(H) == phase(A) == phase(B) == p0 + 3, timeout=15))

        print("J5 box off")
        config("TeamStory", False)
        step(H, p0 + 4)
        check(f"a step is written nowhere ({chapter(red)})", chapter(red) == p0 + 3)
        ev(H, 'HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Method("TellTeams").GetValue(); return "ok";')
        check("the guests are told no journal", eventually(lambda: chapter(red, A) == -1 and chapter(red, B) == -1, timeout=15))
    finally:
        config("TeamStory", False)
        # (the quest put back where it was, in every game: a phase written directly triggers nothing)
        for port in (H, A, B):
            ev(port, f'if ({MAIN} != null) {MAIN}.phase = {p0}; return "ok";')
        if zone_uid(B) != home:
            move(B, home)
        ev(H, f'{BOOK}.Clear(); return "ok";')
        for pair in [z for z in book0.split(",") if z]:
            team, at = pair.split(":")
            ev(H, f'{BOOK}[{team}] = {at}; return "ok";')
        for who in (h, a, b):
            ev(H, f'{T}.GetMethod("Leave", {BF}).Invoke(null, new object[] {{ EClass.game.cards.globalCharas.Find({who}) }}); return "";')
        for name, value in was.items():
            config(name, value)
        print(f"{sum(ok for _, ok in RESULTS)}/{len(RESULTS)}")


if __name__ == "__main__":
    main()
