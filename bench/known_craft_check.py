"""A station craft the host runs for a guest teaches the GUEST the crafting idea (TraitCrafter.Craft adds it to "the
player"): the host's own knownCraft stays as it was, the guest's gets the id (KnownCraftDelta).
Host 27551 + guest 27552 (2 players), same map. A cooking-pot style recipe id is not needed: the guest's player action
is replayed by the host through AI_UseCrafter, here driven by the guest's own C# starting the craft on a station of the
map (a stove, built by the host if absent). Then: host's set lacks the new id, guest's has it.
Not played like a player: the station and the two ingredients are handed over by C#."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")))
from travel_suite import RESULTS, check, ev, eventually  # noqa: E402

H, A = 27551, 27552
MARK = 'return string.Join(",", EClass.player.knownCraft.OrderBy(i => i));'


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    host_before = ev(H, MARK)
    guest_before = ev(A, MARK)
    # the game's own path: the guest uses a mixing station with two ingredients, the host runs the craft
    ev(H, 'var t = ThingGen.Create("stove"); EClass._zone.AddCard(t, EClass.pc.pos.GetNearestPoint(false, false) ?? EClass.pc.pos); '
          'ThingGen.Create("apple"); return t.uid.ToString();')
    stove = ev(A, 'var t = EClass._map.things.Find(x => x.id == "stove"); return t == null ? "" : t.uid.ToString();')
    if not check("a stove stands on the shared map", stove != ""):
        sys.exit(1)
    ev(A, 'var stove = EClass._map.things.Find(x => x.id == "stove"); var ai = new AI_UseCrafter { crafter = stove.trait as TraitCrafter, '
          'ings = new System.Collections.Generic.List<Thing> { ThingGen.Create("apple"), ThingGen.Create("apple") } }; '
          'EClass.pc.SetAIImmediate(ai); return "ok";')
    check("the guest's game learns the new idea (KnownCraftDelta)",
          eventually(lambda: ev(A, MARK) != guest_before, timeout=30))
    check(f"the host's own ideas did not grow ({ev(H, MARK)})", ev(H, MARK) == host_before)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
