"""Teams learn on their own, commits 1-7 of dev/PLAN_teams_knowledge.md. Host + 2 guests already in game, same map:

    cd dev && set PYTHONPATH=_tools/pylib
    python _tools/mp_test.py --clients 2
    python ../bench/team_lore_suite.py

  K0 box on: every team (the Wanderers too) is given what the world knows, the guests are told the rule
  K1 the host founds a team, guest A joins it, guest B founds its own (the team menus' own entries)
  K2 A reads a recipe scroll: A and its teammate (the host) learn it, B (another team) does not; the host's book
     has it for A's team and not for B's
  K3 B reads another one: B learns it, the host and A do not; the host's book has it for B's team only
  K4 the save written now (the vanilla game.txt) knows both recipes, and the host plays on with its team's only
  K5 B leaves and joins again (a real world copy): it still knows its own recipe and not A's
  K6 box off: the world knows everything again, in the three games; the book is emptied

Not played like a player: the scrolls are handed over by the host's C# (then read with the game's AI_Read, as a
click on "read" does), the save is asked by C#, the boxes are set by C# (the settings window is not clicked).
Not tested here: the codex and kills by team, a zone kept by a guest, a story reward.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
import emp  # noqa: E402
from guest_suite import first_id, give  # noqa: E402
from mp_test import join_client, state  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually, wait  # noqa: E402

H, A, B = 27551, 27552, 27553
BF = 'System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public'
T = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.Teams")'
K = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamKnowledge")'
CFG = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpConfig+Server")'


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
    return ev(port, 'return ElinTogether.Net.NetSession.Instance.Rules.TeamKnowledge.ToString();') == "True"


def team_of(charauid):
    return int(ev(H, f'return System.Convert.ToString({T}.GetMethod("TeamOf", {BF}).Invoke(null, new object[] {{ {charauid} }}));'))


def menu(port, who, pick):
    """Runs the first entry of the team menu of that player's row (who: C# chara) whose label `l` passes `pick`, as a
    click on it does; the label, empty when none"""
    return ev(port, f'var m = (System.Collections.IEnumerable){T}.GetMethod("MenuFor", {BF}).Invoke(null, new object[] {{ {who} }}); '
                    'foreach (var e in m) { var l = (string)e.GetType().GetField("Item1").GetValue(e); '
                    f'if ({pick}) {{ ((System.Action)e.GetType().GetField("Item2").GetValue(e))(); return l; }} }} return "";')


def knows(port, rid):
    return ev(port, f'return EClass.player.recipes.knownRecipes.ContainsKey("{rid}").ToString();') == "True"


def book_knows(team, rid):
    """The host's book: that team knows that recipe (its Player object's for the host's own team)"""
    return ev(H, f'var lore = (ElinTogether.Models.TeamLore){K}.GetMethod("Of", {BF}).Invoke(null, new object[] {{ {team} }}); '
                 f'return lore.Known.ContainsKey("{rid}").ToString();') == "True"


def lore_state():
    return ev(H, f'return System.Convert.ToString({K}.GetMethod("Describe", {BF}).Invoke(null, null));')


def fresh_recipes(n):
    """Recipes none of the three games knows, without pillar or bridge variants"""
    ids = ev(H, 'return string.Join(",", RecipeManager.list.Where(r => r.NeedFactory && !r.isBridge && !r.isBridgePillar && !r.noListing '
                '&& RecipeManager.Get(r.id + "-p") == null && RecipeManager.Get(r.id + "-b") == null '
                '&& !EClass.player.recipes.knownRecipes.ContainsKey(r.id)).Select(r => r.id).Take(60));').split(",")
    return [i for i in ids if i and not knows(A, i) and not knows(B, i)][:n]


def read_scroll(port, charauid, rid):
    """The host hands that player a recipe scroll for that recipe; its game reads it (AI_Read)"""
    scroll = give({"p": (port, charauid)}, "p", first_id("Recipe"), extra=f't.SetStr(53, "{rid}");')
    ev(port, f'var t = EClass.pc.things.Find(x => x.uid == {scroll}); EClass.pc.SetAI(new AI_Read {{ target = t }}); return "ok";')


def saved_knows(rid):
    """The vanilla save just written (game.txt) knows that recipe"""
    return ev(H, 'var p = GameIO.pathCurrentSave + "game.txt"; var text = IO.IsCompressed(p) ? IO.Decompress(p) : System.IO.File.ReadAllText(p); '
                 f'var k = Newtonsoft.Json.Linq.JObject.Parse(text).SelectToken("player.recipes.knownRecipes"); return (k != null && k["{rid}"] != null).ToString();') == "True"


def game_id(port):
    return ev(port, "return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(EClass.game).ToString();")


def rejoin(port, name):
    """That guest leaves and joins again: the host sends it a new copy of the world"""
    gid = game_id(port)
    ev(port, 'ElinTogether.Net.NetSession.Instance.ResetSession(); return "ok";')
    time.sleep(3)
    emp.call(port, "eval", {"code": 'EClass.scene.Init(Scene.Mode.Title); "ok"'}, timeout=60)
    wait(lambda: state(port).get("sceneMode") == "Title", f"{name} at the title", timeout=120)
    join_client(H, port, name)
    wait(lambda: state(port).get("sceneMode") == "Zone" and len(state(H).get("players", [])) == 3, f"{name} back in", timeout=180)
    time.sleep(5)
    return check(f"{name} got a new copy of the world", game_id(port) != gid)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a, b = uid(H), uid(A), uid(B)
    was = {name: config(name) for name in ("Teams", "TeamKnowledge", "TeamApproval")}
    r1 = r2 = None
    try:
        print("K0 box on")
        config("Teams", True)
        config("TeamApproval", False)
        config("TeamKnowledge", True)
        check("both guests are told the rule", eventually(lambda: guest_rule(A) and guest_rule(B), timeout=15))
        check(f"every team was given the world's knowledge ({lore_state().splitlines()[0]})",
              eventually(lambda: "seeded 1" in lore_state(), timeout=10))
        world = int(ev(H, 'return EClass.player.recipes.knownRecipes.Count.ToString();'))
        check(f"the Wanderers' book has the world's {world} recipes",
              any(line.startswith(f"0 recipes {world} ") for line in lore_state().splitlines()))

        print("K1 teams")
        menu(H, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("the host leads a team", eventually(lambda: team_of(h) != 0, timeout=10))
        red = team_of(h)
        time.sleep(2)
        menu(A, f'EClass.game.cards.globalCharas.Find({h})', 'true')
        check("A joined the host's team", eventually(lambda: team_of(a) == red, timeout=15))
        menu(B, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("B leads a team of its own", eventually(lambda: team_of(b) not in (0, red), timeout=15))
        blue = team_of(b)
        time.sleep(3)

        r1, r2 = (fresh_recipes(2) + [None, None])[:2]
        if not check(f"two recipes nobody knows ({r1}, {r2})", r1 and r2):
            return

        print("K2 A reads a recipe")
        read_scroll(A, a, r1)
        check("A learnt it", eventually(lambda: knows(A, r1), timeout=60))
        check("its teammate the host learnt it", eventually(lambda: knows(H, r1), timeout=20))
        time.sleep(8)
        check("B (another team) did not", not knows(B, r1))
        check("the host's book: A's team knows it", book_knows(red, r1))
        check("the host's book: B's team does not", not book_knows(blue, r1))

        print("K3 B reads another")
        read_scroll(B, b, r2)
        check("B learnt it", eventually(lambda: knows(B, r2), timeout=60))
        check("the host's book: B's team knows it", eventually(lambda: book_knows(blue, r2), timeout=20))
        time.sleep(8)
        check("the host (another team) did not", not knows(H, r2))
        check("A (another team) did not", not knows(A, r2))

        print("K4 the save keeps everything")
        ev(H, 'EClass.game.Save(false, true); return "ok";')
        check("the vanilla save knows A's recipe", saved_knows(r1))
        check("the vanilla save knows B's recipe", saved_knows(r2))
        check("the host still plays with its team's only", knows(H, r1) and not knows(H, r2))

        print("K5 B rejoins")
        if rejoin(B, "B"):
            check("B still knows its own recipe", eventually(lambda: knows(B, r2), timeout=20))
            check("and still not A's", not knows(B, r1))

        print("K6 box off")
        config("TeamKnowledge", False)
        check("the host knows both again", eventually(lambda: knows(H, r1) and knows(H, r2), timeout=15))
        check("A knows both", eventually(lambda: knows(A, r1) and knows(A, r2), timeout=15))
        check("B knows both", eventually(lambda: knows(B, r1) and knows(B, r2), timeout=15))
        check(f"the book is emptied ({lore_state().splitlines()[0]})", "seeded 0" in lore_state())
    finally:
        # (box off first: the recipes are the world's again, taken back from every game)
        config("TeamKnowledge", False)
        time.sleep(3)
        for rid in (r1, r2):
            if rid:
                for port in (H, A, B):
                    ev(port, f'EClass.player.recipes.knownRecipes.Remove("{rid}"); return "";')
        for who in (h, a, b):
            ev(H, f'{T}.GetMethod("Leave", {BF}).Invoke(null, new object[] {{ EClass.game.cards.globalCharas.Find({who}) }}); return "";')
        for name, value in was.items():
            config(name, value)


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
