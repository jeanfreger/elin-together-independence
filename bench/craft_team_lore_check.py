"""With "Teams learn on their own" on, a craft the host runs for a guest of ANOTHER team (RemoteCraft.AsCrafter) reads
that team's recipe levels, not the host team's (Recipe.GetQualityBonus). Host 27551 + guest 27552 (2 players), the box
on, the host and the guest in different teams (e.g. right after K1 of team_lore_suite; the script says so and stops
otherwise). A recipe is given level 9 in the host's book and level 1 in the guest team's; inside AsCrafter(guest) the
host's Player object must show 1, after it 9 again; the guest team's ledger keeps what it had. Same team: untouched.
Not played like a player: the stand-in scope is entered by C#, as AIUseCrafterPatch does for a station craft."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")))
sys.path.insert(0, str(Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\bench")))
from team_lore_suite import K, BF, H, A, config, team_of, uid  # noqa: E402
from travel_suite import RESULTS, check, ev  # noqa: E402

RC = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.RemoteCraft")'


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if not check("the box is on", config("TeamKnowledge")):
        sys.exit(1)
    th, ta = team_of(uid(H)), team_of(uid(A))
    if not check(f"host and guest are in different teams ({th} / {ta}): found them first", th != ta):
        sys.exit(1)
    rid = ev(H, 'return RecipeManager.list.First(r => r.NeedFactory).id;')
    # the host's book (its Player object) says 9, the guest team's ledger says 1
    ev(H, f'EClass.player.recipes.knownRecipes["{rid}"] = 9; var lore = (ElinTogether.Models.TeamLore){K}.GetMethod("Of", {BF}).Invoke(null, new object[] {{ {ta} }}); '
          f'lore.Known["{rid}"] = 1; {K}.GetMethod("Store", {BF}).Invoke(null, new object[] {{ {ta}, lore }}); return "ok";')
    guest = f'EClass.game.cards.globalCharas.Find({uid(A)})'
    scope = (f'var s = (System.IDisposable){RC}.GetMethod("AsCrafter", {BF}).Invoke(null, new object[] {{ {guest}, false }}); '
             f'var inside = EClass.player.recipes.knownRecipes["{rid}"]; s.Dispose(); ')
    out = ev(H, scope + f'return inside + "," + EClass.player.recipes.knownRecipes["{rid}"];')
    check(f"inside the guest's craft the recipe is at its team's level 1, after it back at 9 ({out})", out == "1,9")
    own = ev(H, f'var s = (System.IDisposable){RC}.GetMethod("AsCrafter", {BF}).Invoke(null, new object[] {{ EClass.pc, false }}); '
                f'var v = EClass.player.recipes.knownRecipes["{rid}"]; s.Dispose(); return v.ToString();')
    check(f"the host's own craft sees 9 ({own})", own == "9")
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
