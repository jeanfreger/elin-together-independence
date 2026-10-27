"""H5-09, the base's mail made by the world keeper (the host's game) for each player, not for the host alone.

M1  a random parcel's creature (GameDate.ShipRandomPackages: SpawnList.Select(pc.LV + 10), and the furniture that comes
    with it, TraitBaseContainer.PutChara) is drawn for a player in turn. Host level 1, guest level 60: 400 random
    parcels by the host's date; some creatures come from the guest's level (source level >= 30), some from the host's
    (<= 20). Red before: every one from the host's level (none >= 30; the furniture <= 11).
M2  a guest's game with a backer reward code asks for its own gift box (BackerGiftDelta, union 1040): the host puts one
    "gift" with the base's parcels, marks the guest's character, and a second ask gives nothing. Red before: no such
    message, the box came only for the host's own code.
M3  a guest's game WITHOUT a reward code asks nothing when a day passes (the bench has no code: if it has one, skipped).

Not played like a player: the levels are set by eval; the parcels are made by calling ShipRandomPackages on the host
(the date does it once a day); M2 sends the guest's ask by eval (the bench has no valid reward code, ElinEncoder), the
parcels are read from the world's list on its way (game.cards.listPackage), not taken from the mailbox. Lutz's letters
(GameDate.ShipLetter) are left as they are: the world's day count, the shared mailbox, the same for every player.

Host + 1 client open (ports 27551 / 27552), same map.
usage (from dev/, PYTHONPATH=_tools/pylib): python <worktree>/bench/h509_mail.py
"""
import sys
import time
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import hunt5_suite as h5  # noqa: E402
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A = 27551, 27552
GIFT = "emp_backer_gift"
ASK = ('var t = HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.BackerGiftDelta"); '
       'HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.PersonalQuests"), "TellHost")'
       '.Invoke(null, new object[] { System.Activator.CreateInstance(t) }); return "ok";')


def count():
    return int(ev(H, "return EClass.game.cards.listPackage.Count.ToString();"))


def m1(gu):
    print("M1 a random parcel's creature is drawn for a player in turn (host level 1, guest level 60)")
    lv_h = int(ev(H, "return EClass.pc.LV.ToString();"))
    lv_g = int(ev(A, "return EClass.pc.LV.ToString();"))
    n = count()
    try:
        ev(H, 'EClass.pc.LV = 1; return "ok";')
        ev(A, 'EClass.pc.LV = 60; return "ok";')
        check("M1 the host sees the guest at level 60",
              eventually(lambda: ev(H, f"return EClass.game.cards.globalCharas.Find({gu}).LV.ToString();") == "60", timeout=20))
        ev(H, 'for (int i = 0; i < 400; i++) EClass.world.date.ShipRandomPackages(); return "ok";')
        out = ev(H, f"var l = EClass.game.cards.listPackage; return string.Join(\",\", l.Skip({n})"
                    ".Where(p => !string.IsNullOrEmpty(p.c_idRefCard) && EClass.sources.charas.map.ContainsKey(p.c_idRefCard))"
                    ".Select(p => EClass.sources.charas.map[p.c_idRefCard].LV));")
        levels = [int(x) for x in out.split(",") if x]
        check(f"M1 set up: creatures were shipped ({len(levels)})", len(levels) >= 6)
        check(f"M1 some from the guest's level (source level >= 30: {sum(v >= 30 for v in levels)}; {levels})",
              any(v >= 30 for v in levels))
        check(f"M1 and some from the host's (<= 20: {sum(v <= 20 for v in levels)})", sum(v <= 20 for v in levels) >= 2)
    finally:
        h5.drop_packages_since(n)
        ev(H, f'EClass.pc.LV = {lv_h}; return "ok";')
        ev(A, f'EClass.pc.LV = {lv_g}; return "ok";')


def m2(gu):
    print("M2 a guest with a backer reward code gets its own gift box, once")
    n = count()
    marked = f'return EClass.game.cards.globalCharas.Find({gu}).GetBool("{GIFT}").ToString();'
    try:
        ev(H, f'EClass.game.cards.globalCharas.Find({gu}).SetBool("{GIFT}", false); return "ok";')
        ev(A, ASK)
        check("M2 the host sends a gift box to the base's mail",
              eventually(lambda: ev(H, f'return EClass.game.cards.listPackage.Skip({n}).Any(p => p.id == "gift").ToString();') == "True",
                         timeout=20))
        check("M2 and marks the guest's character", ev(H, marked) == "True")
        ev(A, ASK)
        time.sleep(5)
        gifts = int(ev(H, f'return EClass.game.cards.listPackage.Skip({n}).Count(p => p.id == "gift").ToString();'))
        check(f"M2 a second ask gives nothing more (gift boxes: {gifts})", gifts == 1)
    finally:
        h5.drop_packages_since(n)
        ev(H, f'EClass.game.cards.globalCharas.Find({gu}).SetBool("{GIFT}", false); return "ok";')


def m3():
    print("M3 a guest without a reward code asks nothing when a day passes")
    if ev(A, "return Steam.HasDLC(ID_DLC.BackerReward).ToString();") == "True":
        print("    (skipped: this bench guest has a reward code)")
        return
    n = count()
    try:
        h5.to_midnight(H)
        time.sleep(8)
        gifts = int(ev(H, f'return EClass.game.cards.listPackage.Skip({n}).Count(p => p.id == "gift").ToString();'))
        check(f"M3 no gift box (got {gifts})", gifts == 0)
    finally:
        h5.drop_packages_since(n)


def main():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    sys.stdout.reconfigure(encoding="utf-8")
    preflight()
    gu = h5.guest_uid()
    try:
        m1(gu)
        m2(gu)
        m3()
    finally:
        cleanup()


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
