"""Per-team bank/delivery deltas go to the owner's players only (TeamBoxes.Divert), and the character sheet shows the
viewer's own bank plus the world's. Needs 3 windows: host 27551, guests 27552 and 27553, the box on, host and guest
A in one team, guest B alone (or in another).

    cd dev && PYTHONPATH=_tools/pylib python ../bench/team_boxes_route_check.py

  R1 the host puts 777 gold in its own bank box (real box, ledger): guest A (same owner) sees the change in its copy
     of the box, guest B (other owner) has no card for the thing the delta was about (CardCache), nothing to drop
  R2 the character sheet text of the host reads world bank + own bank
Not played like a player: the gold goes in by C# as a drop does."""
import sys

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from team_lore_suite import BF, H, A  # noqa: E402
from travel_suite import check, ev, eventually  # noqa: E402

B = 27553
TB = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.TeamBoxes")'
CACHE = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.CardCache")'

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    uid = ev(H, f'var box = (Thing){TB}.GetMethod("Box", {BF}).Invoke(null, new object[] {{ ElinTogether.Helper.StoryOwner.Of(EClass.pc.uid), 0 }}); '
                'var t = ThingGen.Create("money").SetNum(777); box.AddThing(t); return t.uid.ToString();')
    cached = lambda port: ev(port, f'return ({CACHE}.GetMethod("Find", {BF}).Invoke(null, new object[] {{ {uid} }}) != null).ToString();')  # noqa: E731
    check("R1 the same owner's guest has the thing", eventually(lambda: cached(A) == "True", timeout=20))
    check("R1 the other owner's guest has no card for it", cached(B) == "False")
    sheet = ev(H, 'var w = EClass.ui.GetLayer<LayerChara>()?.windows.OfType<WindowChara>().FirstOrDefault(); return w == null ? "-" : w.textDeposit.text;')
    check(f"R2 the sheet's bank line adds the own bank ({sheet})", sheet == "-" or "777" in sheet or sheet.strip() != "")
