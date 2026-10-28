"""Parcels per team (ElinTogether/Helper/TeamMail.cs). Needs 2 windows: host 27551 + guest 27552, one team each.

    python bench/team_parcels_check.py

  P1 label: a parcel sent for a player shows "for <Name>" (always); an untagged parcel does not
  P2 box on, no base for the teams: a parcel for the guest goes (at the next delivery, FactionBranch.ReceivePackages)
     into the guest's team's delivery box, not the host's; the parcel list holds none after
  P3 mail box owner: a mail box installed by the host's player gets the host as its owner (TeamMail.Claim); the
     guest may not take it over (MayChange false), the owner may hand it to the guest
  P4 box off: the game's own delivery empties the list as before (no team delivery box is involved)
All the internal types are reached by reflection (the script engine cannot see them). Not tested: the menu entry
itself, a team with a real base (needs a claimed land).
"""
import sys

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from team_lore_suite import BF, H, A, config, menu, team_of, uid  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

TM = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamMail")'
TB = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamBoxes")'


def send(target_uid, tagged=True):
    tag = (f'{TM}.GetMethod("Send", {BF}, null, new System.Type[] {{ typeof(int), typeof(Thing) }}, null)'
           f'.Invoke(null, new object[] {{ {target_uid}, p }});')
    return ev(H, 'var p = ThingGen.CreateParcel(null, ThingGen.Create("money").SetNum(7)); ' +
              (tag if tagged else 'EClass.world.SendPackage(p);') + ' return p.Name;')


def deliver():
    ev(H, 'EClass.pc.homeBranch.ReceivePackages(null); return "ok";')


def delivery_count(owner):
    return int(ev(H, f'return (({TB}.GetMethod("Box", {BF}).Invoke(null, new object[] {{ {owner}, 1 }})) as Thing).things.Count.ToString();'))


def pending():
    return int(ev(H, 'return EClass.game.cards.listPackage.Count.ToString();'))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    h, a = uid(H), uid(A)
    was = {n: config(n) for n in ("Teams", "TeamApproval", "TeamBoxes", "TeamBases")}
    try:
        config("Teams", True)
        config("TeamApproval", False)
        config("TeamBases", True)
        config("TeamBoxes", True)
        if team_of(h) == 0:
            menu(H, 'EClass.pc', 'l == "emp_team_found".lang()')
        if team_of(a) in (0, team_of(h)):
            menu(A, 'EClass.pc', 'l == "emp_team_found".lang()')
        check("two teams", eventually(lambda: team_of(a) not in (0, team_of(h)), timeout=15))
        red, blue = team_of(h), team_of(a)

        print("P1 label")
        name = ev(H, f'return EClass.game.cards.globalCharas.Find({a}).NameSimple;')
        tagged = send(a)
        check(f"the tagged parcel shows its recipient ({tagged!r})", name in tagged)
        plain = send(0, tagged=False)
        check(f"an untagged parcel does not ({plain!r})", name not in plain)
        ev(H, 'EClass.game.cards.listPackage.Clear(); return "ok";')

        print("P2 delivery box of the recipient's team")
        send(a)
        before_blue, before_red = delivery_count(blue), delivery_count(red)
        deliver()
        check("the guest's team got the tagged parcel in its delivery box", delivery_count(blue) == before_blue + 1)
        check("the host's team got none", delivery_count(red) == before_red)
        check(f"the parcel list is empty after the delivery ({pending()})", pending() == 0)

        print("P3 mail box owner")
        res = ev(H, 'var row = EClass.sources.things.rows.First(r => r.trait.Length > 0 && r.trait[0] == "MailPost"); '
                    'var m = ThingGen.Create(row.id); '
                    f'{TM}.GetMethod("Claim", {BF}).Invoke(null, new object[] {{ m, EClass.pc.uid }}); '
                    'var own = m.GetInt("emp_mail_owner"); '
                    f'var guest = EClass.game.cards.globalCharas.Find({a}); '
                    f'var g = (bool){TM}.GetMethod("MayChange", {BF}).Invoke(null, new object[] {{ m, guest, 0 }}); '
                    f'var h2 = (bool){TM}.GetMethod("MayChange", {BF}).Invoke(null, new object[] {{ m, EClass.pc, {a} }}); '
                    'return own + "," + g + "," + h2;')
        check(f"the installer owns it, the guest cannot take it over, the owner may hand it on ({res})",
              res == f"{h},False,True")

        print("P4 box off")
        config("TeamBoxes", False)
        before_blue = delivery_count(blue)
        send(a)
        deliver()
        check(f"the game's own delivery took it ({pending()} left)", pending() == 0)
        check("no team delivery box got it", delivery_count(blue) == before_blue)
    finally:
        for n, v in was.items():
            config(n, v)
    ok = sum(1 for _, c in RESULTS if c)
    print(f"{ok}/{len(RESULTS)} verifications OK")


if __name__ == "__main__":
    main()
