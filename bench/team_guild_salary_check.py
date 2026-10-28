"""Guild salary per owner (ElinTogether/Helper/TeamBills.cs GuildSalaries). Needs 2 windows (host 27551 + guest 27552).

    python bench/team_guild_salary_check.py

The host's character is made a member of the fighters' guild at rank 3 (salary 300 a month, the game's rank * 100).
TeamBills.PaySalary is called by reflection for the home base with the box on.
  G1 TeamOwnGuilds on: the host is paid 300 more than with the guilds off (its own guild ranks)
  G2 TeamOwnGuilds off: the guild salary is the world's, never one team's: the host gets none of it
Not tested: the guest's own stored ranks (needs a second story bundle with guild ranks), the bank policy path.
"""
import sys

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from team_lore_suite import BF, H, config, menu, team_of, uid  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

TBILL = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamBills")'


def month():
    """The host's purse change, and the new parcels in the world's list, for one month's salary"""
    out = ev(H, f'var f = EClass.pc.faction; var m = {TBILL}.GetMethod("PaySalary", {BF}); '
                'var before = EClass.pc.GetCurrency(); var list = EClass.game.cards.listPackage.Count; '
                'm.Invoke(null, new object[] { f, f.GetChildren() }); '
                'return (EClass.pc.GetCurrency() - before) + "," + (EClass.game.cards.listPackage.Count - list);')
    gain, parcels = (int(x) for x in out.split(","))
    return gain, parcels


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h = uid(H)
    was = {n: config(n) for n in ("Teams", "TeamApproval", "TeamBoxes", "TeamOwnStory", "TeamOwnGuilds")}
    try:
        for n in ("Teams", "TeamBoxes", "TeamOwnStory"):
            config(n, True)
        config("TeamApproval", False)
        if team_of(h) == 0:
            menu(H, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("the host is in a team", eventually(lambda: team_of(h) != 0, timeout=10))
        ev(H, 'var g = EClass.game.factions.Fighter.relation; g.type = FactionRelation.RelationType.Member; g.rank = 3; return "ok";')

        config("TeamOwnGuilds", False)
        off = month()
        config("TeamOwnGuilds", True)
        on = month()
        print(f"    guilds off {off} on {on} (purse gain, new parcels)")
        check(f"G1 the host is paid its own guild salary with the guilds per owner (+{on[0] - off[0]})", on[0] - off[0] == 300)
        check("G2 guilds off: the host gets nothing of the world's guild salary beyond its lands' share", off[0] <= on[0] - 300)
        check("G2 the world's share was sent to the world (parcel) or its bank, not to a team", off[1] >= on[1])
    finally:
        ev(H, 'var g = EClass.game.factions.Fighter.relation; g.type = FactionRelation.RelationType.Default; g.rank = 0; return "ok";')
        for n, v in was.items():
            config(n, v)
    ok = sum(1 for _, c in RESULTS if c)
    print(f"{ok}/{len(RESULTS)} verifications OK")


if __name__ == "__main__":
    main()
