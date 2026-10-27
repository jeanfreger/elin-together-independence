"""H5-13: a wedding brings the base's people to their seats (ZoneEventWedding.OnVisit): every character of the player's
faction but "the player". Another player's character was taken too: moved to a seat, sent back where it stood when the
wedding ended. Now no player's character is an attendee, the host's or a guest's, in whichever game runs the wedding.

W1  the host's game runs a wedding's arrival on the base where the guest stands: the guest is not among the attendees
    (its position is not noted for the way back), the base's other people are. Red before: the guest was one.

Read, no change (see ZoneEventPlayersPatch): a siege (debug menu only) and a survival raid come from the map's edge or
the core and go for the base's people; a quest zone's waves go for "the player's party", which holds every player of
the map where it runs; a subdue counts the enemies of that party. The vampire feast of a wedding (feat 1250) now reads
the player whose wedding it is when the host came along to it (ElinNetHost.AccompaniedTaker): NOT tested, a real
wedding needs a marriage and the zone of the quest.

Not played like a player: no marriage, no wedding zone. The wedding's arrival (OnVisit) is called by eval on the base
with a wedding quest made by Quest.Create and 60 seats laid on the map; within the same eval everything it changed is
put back (attendees to their tiles, the map's things NPC property flags, the food's price and mark, the seats, the
field effect, the quest).

Host + 1 client open (ports 27551 / 27552), both on the base (a map of the player's faction).
usage (from dev/, PYTHONPATH=_tools/pylib): python <worktree>/bench/h513_wedding.py
"""
import sys
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
sys.path.insert(0, str(TOOLS))
import hunt5_suite as h5  # noqa: E402
from travel_suite import RESULTS, check, ev  # noqa: E402

H, A = 27551, 27552

WEDDING = """
var g = EClass.game.cards.globalCharas.Find(__GU__);
var npc = CharaGen.Create("farmer");
var q = Quest.Create("wedding", null, npc, false);
EClass.game.quests.list.Add(q);
var chairs = new System.Collections.Generic.List<Thing>();
for (int i = 0; i < 60; i++) { var p = EClass._map.bounds.GetRandomSurface(false, true, false); if (p == null || p.HasThing || p.HasObj) continue; var t = ThingGen.Create("1321"); EClass._zone.AddCard(t, p).Install(); chairs.Add(t); }
var npcProp = EClass._map.things.ToDictionary(t => t, t => t.isNPCProperty);
var food = EClass._map.things.Where(t => t.HasElement(10)).Select(t => (t, t.c_priceFix, t.GetBool(128))).ToList();
var w = new ZoneEventWedding(); w.uidQuest = q.uid;
string res = "-";
try {
    w.OnVisit();
    res = w.attendees + "|" + w.transfer.list.Any(d => d.uid == g.uid) + "|" + w.transfer.list.Any(d => d.uid == EClass.pc.uid) + "|" + chairs.Count;
} finally {
    foreach (var d in w.transfer.list) { var c = EClass.game.cards.globalCharas.Find(d.uid); if (c != null && c.currentZone == EClass._zone) { c.MoveImmediate(new Point(d.x, d.z), false, true); c.noMove = d.noMove; } }
    foreach (var kv in npcProp) if (!kv.Key.isDestroyed) kv.Key.isNPCProperty = kv.Value;
    foreach (var f in food) { f.Item1.c_priceFix = f.Item2; f.Item1.SetBool(128, f.Item3); }
    foreach (var t in chairs) if (!t.isDestroyed) t.Destroy();
    EClass._zone.RemoveFieldEffect(10001);
    EClass.game.quests.list.Remove(q);
    npc.Destroy();
}
return res;
"""


def main():
    from fixtures import cleanup, preflight  # noqa: PLC0415
    sys.stdout.reconfigure(encoding="utf-8")
    print("W1 a wedding's attendees are the base's people, not the players")
    preflight()
    try:
        if not check("W1 set up: the host is on a map of the player's faction",
                     ev(H, "return EClass._zone.IsPCFaction.ToString();") == "True"):
            return
        gu = h5.guest_uid()
        out = ev(H, " ".join(WEDDING.replace("__GU__", str(gu)).splitlines()))
        print(f"    attendees|guest|host|seats: {out}")
        attendees, guest, host, seats = out.split("|")
        check(f"W1 set up: seats were laid ({seats}) and the base's people came ({attendees})", int(seats) > 0 and int(attendees) > 0)
        check("W1 the guest's character is not an attendee", guest == "False")
        check("W1 nor the host's", host == "False")
    finally:
        cleanup()


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
