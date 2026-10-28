"""Several lands on one world-map tile (council 2026-10-28 section 4). Needs 2 windows (host 27551 + guest 27552) in a
throwaway world, both standing on a land of the host (a claimed base). ~5 minutes. usage: python bench/sibling_lands_check.py
(it leaves a claimed plot behind: use a test world). The C# sent through ev() cannot see internal types: reflection only.

Host team Red holds the base the host stands on, the guest founds team Blue. Box "Several lands per tile" on.
What it proves, step by step (everything the player does goes through the game: the deed is read with TraitDeed.OnRead,
the question is answered by clicking its Yes, the tile is entered with Player.EnterLocalZone, the list is a click):
- OFF (control): with the box off, reading a deed on the occupied tile is the game's own refusal and no plot appears
- DEED: with the box on the guest reads its deed on Red's tile, clicks Yes: the host makes ONE more Zone_Field at the same
  x,y (a plot, not claimed), the guest steps into it by itself
- CLAIM: both in the plot, the guest reads the deed again (the game's own claim, ClaimZoneDelta): the plot is Blue's;
  the host's book holds it, the tile now holds 2 lands (Red's and Blue's)
- ICON: the map cell of the tile still holds Red's land (the first), in both games
- LIST: the host walks onto the tile (EnterLocalZone) and a list of 2 lines opens (team names); clicking Blue's enters
  it; the guest does the same and enters Red's
- BACK: leaving the Blue plot for the world map puts the player on the same spot of the map (the tile)
- CLOSED: Red's land closed to others (box "Land rules"): its line is refused for the guest, who ends on the map
  (not on Red's land), the line of Blue's own land still works
- RELOCATE: Blue's plot moved to a free tile (BaseRelocateDelta.Relocate, what the deed does): the tile keeps Red's land
  and its icon, the plot is on the new tile
- OFF AGAIN: box off, walking onto a tile: no list opens
Not tested: the deed with an away guest alone on its world map (the delta is the same, sent through SendWhileAway), a
guest claiming without the host in the plot (refused, as every claim by a guest off the host's map), reconnection.
"""
import sys
import time

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from team_lib import *  # noqa: F403,E402
from travel_suite import host_goto, zone_uid  # noqa: E402

CLICK_YES = ('var d = EClass.ui.layers.OfType<Dialog>().LastOrDefault(); if (d == null) return "no dialog"; '
             'var b = d.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(x => x.interactable && x.gameObject.activeInHierarchy && '
             'x.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text == "Yes" || t.text == "はい" || t.text == "是")); '
             'if (b == null) return "no yes"; b.onClick.Invoke(); return "yes";')
LIST_LINES = ('var l = EClass.ui.layers.OfType<LayerList>().LastOrDefault(); if (l == null) return ""; '
              'return string.Join("|", l.GetComponentsInChildren<ItemGeneral>(false).Where(x => x.gameObject.activeInHierarchy).Select(x => x.button1.mainText.text));')
CLICK_LINE = ('var l = EClass.ui.layers.OfType<LayerList>().LastOrDefault(); if (l == null) return "no list"; '
              'var it = l.GetComponentsInChildren<ItemGeneral>(false).FirstOrDefault(x => x.gameObject.activeInHierarchy && x.button1.mainText.text.Contains("%s")); '
              'if (it == null) return "no line"; it.button1.onClick.Invoke(); return "click";')
# the lands of a tile as the host holds them: "uid:claimed" for every zone of the region at x,y
LANDS_AT = ('var r = EClass.game.spatials.Find(__R__); return string.Join(",", r.children.OfType<Zone>().Where(z => z.x == __X__ && z.y == __Y__ && !z.destryoed)'
            '.Select(z => z.uid + ":" + z.IsPCFaction));')
CELL = 'var c = EClass.scene.elomap.GetCell(__X__, __Y__); return c == null || c.zone == null ? "0" : c.zone.uid.ToString();'


def fmt(code, **k):
    for key, v in k.items():
        code = code.replace(f"__{key}__", str(v))
    return code


def read_deed(port):
    ev(port, 'var t = EClass.pc.things.Find(x => x.id == "deed"); if (t == null) return "no deed"; t.trait.OnRead(EClass.pc); return "read";')
    time.sleep(2)


def stand_on(port, x, y):
    """The player at that world-map tile (a shortcut for walking there: the walk itself is not what is tested)"""
    ev(port, f'var em = EClass.scene.elomap; EClass.pc.Teleport(new Point({x} - em.minX, {y} - em.minY), false, false); return "ok";')
    time.sleep(1)


def enter_tile(port, x, y):
    ev(port, f'var em = EClass.scene.elomap; EClass.player.EnterLocalZone(new Point({x} - em.minX, {y} - em.minY)); return "ok";')
    time.sleep(2)


def lands(x, y, region):
    return ev(H, fmt(LANDS_AT, R=region, X=x, Y=y))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    base = int(ev(H, 'return EClass._zone.uid.ToString();'))
    check("the guest starts on the host's land", zone_uid(A) == base)
    x, y = (int(v) for v in ev(H, 'return EClass._zone.x + "," + EClass._zone.y;').split(","))
    region = int(ev(H, 'return EClass._zone.ParentZone.uid.ToString();'))
    box("Teams", True)
    box("TeamBases", True)
    box("SiblingLands", False)
    clear_book()
    h, a = uid(H), uid(A)
    plot = None
    try:
        ask(H, "Found", text="Red")
        ask(A, "Found", text="Blue")
        check("both teams exist", eventually(lambda: team_of(H, h) > 0 and team_of(H, a) > 0, timeout=15))
        teams(H, "OnClaimed", "EClass._zone")
        check("Red holds the base", teams(H, "TeamOfZone", "EClass._zone") == str(team_of(H, h)))
        # the guest's deed, given by the host to the guest's character (it reaches the guest's bag)
        ev(H, f'var c = EClass.game.cards.globalCharas.Find({a}); c.AddThing(ThingGen.Create("deed")); return "ok";')
        check("the guest holds a deed", eventually(lambda: ev(A, 'return EClass.pc.things.Find(x => x.id == "deed") != null ? "1" : "0";') == "1", timeout=15))

        host_goto(H, A, region)
        check("both on the world map", zone_uid(H) == region and eventually(lambda: zone_uid(A) == region, timeout=60))
        stand_on(A, x, y)

        # OFF (control)
        before = lands(x, y, region)
        read_deed(A)
        check(f"OFF: no question and no plot ({lands(x, y, region)} vs {before})",
              ev(A, CLICK_YES) in ("no dialog", "no yes") and lands(x, y, region) == before)

        # DEED
        box("SiblingLands", True)
        check("the guest's game knows the rule", eventually(lambda: ev(A, 'return ElinTogether.Net.NetSession.Instance.Rules.SiblingLands.ToString();') == "True", timeout=15))
        read_deed(A)
        check("DEED: the question opens", ev(A, CLICK_YES) == "yes")
        check("DEED: the host made ONE more field on the tile, not claimed",
              eventually(lambda: len(lands(x, y, region).split(",")) == 2, timeout=30) and lands(x, y, region).endswith(":False"))
        plot = int(lands(x, y, region).split(",")[-1].split(":")[0])
        check(f"DEED: the guest stepped into the plot {plot}", eventually(lambda: zone_uid(A) == plot, timeout=60))

        # CLAIM (the host is in the plot too: a guest claims only on a map the host keeps)
        host_goto(H, A, plot)
        read_deed(A)
        ev(A, CLICK_YES)
        check("CLAIM: the plot is Blue's in the host's book",
              eventually(lambda: teams(H, "TeamOfZone", f"EClass.game.spatials.Find({plot}) as Zone") == str(team_of(H, a)), timeout=30))
        check("CLAIM: the tile holds two claimed lands", eventually(lambda: lands(x, y, region).count(":True") == 2, timeout=15))

        # back to the world map, both
        host_goto(H, A, region)
        check("ICON: the cell still shows Red's land (host)", ev(H, fmt(CELL, X=x, Y=y)) == str(base))
        check("ICON: and the guest's game too", ev(A, fmt(CELL, X=x, Y=y)) == str(base))

        # LIST
        stand_on(H, x, y)
        enter_tile(H, x, y)
        lines = ev(H, LIST_LINES)
        check(f"LIST: two lines, Red's and Blue's ({lines})", "Red" in lines and "Blue" in lines and lines.count("|") == 1)
        ev(H, CLICK_LINE % "Blue")
        check("LIST: clicking Blue's enters the plot", eventually(lambda: zone_uid(H) == plot, timeout=60))
        ev(H, 'EClass.pc.MoveZone(EClass._zone.ParentZone); return "ok";')
        check("BACK: leaving the plot puts the host on the tile",
              eventually(lambda: zone_uid(H) == region, timeout=60) and
              ev(H, 'var p = EClass.pc.pos; var em = EClass.scene.elomap; return (p.x + em.minX) + "," + (p.z + em.minY);') == f"{x},{y}")
        host_goto(H, A, region)
        stand_on(A, x, y)
        enter_tile(A, x, y)
        check(f"LIST: the guest sees the lines too ({ev(A, LIST_LINES)})", "Red" in ev(A, LIST_LINES))
        ev(A, CLICK_LINE % "Red")
        check("LIST: and enters Red's land", eventually(lambda: zone_uid(A) == base, timeout=60))
        host_goto(H, A, region)

        # CLOSED
        box("LandRules", True)
        ask(H, "LandRule", teams(H, "PackRule", "3", "0", "false"), base)
        check("CLOSED: Red's land is closed to others", eventually(lambda: teams(A, "RuleOf", str(base)) == teams(H, "PackRule", "3", "0", "false"), timeout=15))
        stand_on(A, x, y)
        enter_tile(A, x, y)
        ev(A, CLICK_LINE % "Red")
        time.sleep(6)
        check("CLOSED: the guest is not on Red's land", zone_uid(A) != base)
        host_goto(H, A, region)
        enter_tile(A, x, y)
        ev(A, CLICK_LINE % "Blue")
        check("CLOSED: Blue's own land still opens", eventually(lambda: zone_uid(A) == plot, timeout=60))
        host_goto(H, A, region)
        box("LandRules", False)

        # RELOCATE (what the relocation deed does in the world the host keeps)
        spot = ev(H, 'var z = EClass.game.spatials.Find(%d); var r = EClass._zone.Region; foreach (var d in new[] { 3, 4, 5, 6, 7 }) foreach (var o in new[] { new[] { d, 0 }, '
                     'new[] { 0, d }, new[] { -d, 0 }, new[] { 0, -d } }) { var w = new Point(z.x + o[0], z.y + o[1]); if (r.GetZoneAt(w.x, w.z) == null && r.CanCreateZone(w)) '
                     'return w.x + "," + w.z; } return "";' % base)
        if spot:
            nx, ny = (int(v) for v in spot.split(","))
            ev(H, 'var t = HarmonyLib.AccessTools.TypeByName("ElinTogether.Models.BaseRelocateDelta"); var m = HarmonyLib.AccessTools.Method(t, "Move"); '
                  f'm.Invoke(null, new object[] {{ EClass.game.spatials.Find({plot}) as Zone, {nx}, {ny} }}); return "ok";')
            check(f"RELOCATE: the tile keeps Red's land alone ({lands(x, y, region)})", lands(x, y, region) == f"{base}:True")
            check("RELOCATE: and its icon", ev(H, fmt(CELL, X=x, Y=y)) == str(base))
            check("RELOCATE: the plot is on the new tile", ev(H, fmt(CELL, X=nx, Y=ny)) == str(plot))
        else:
            print("    [SKIP] no free tile near the base to move the plot to")

        # OFF AGAIN
        box("SiblingLands", False)
        enter_tile(H, x, y)
        check("OFF AGAIN: walking onto a tile opens no list", ev(H, LIST_LINES) == "")
    finally:
        clear_book()
        box("SiblingLands", False)
        box("LandRules", False)
        box("TeamBases", False)
        box("Teams", False)


if __name__ == "__main__":
    main()
    report()  # prints "N/M verifications OK"
