"""Host/guest audit item 3: the stolen-fish roll (1 in 1000) is made by the fishing player's game only. Needs 2 windows
(host 27551 + guest 27552). usage: python bench/fish_stolen_check.py

What it proves: on the host and on the guest, AI_Fish.ProgressFish.OnProgressComplete carries the mod's transpiler
that turns the roll into "no theft" while a progress is replayed for another player (CharaProgressCompleteDelta
.IsReplaying). It does not fish 1000 times: the rule is read off the patched method, and the transpiler compiled
(a failed match would have thrown at patch time and left no transpiler on the method).
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import check, ev  # noqa: E402

CODE = ('var m = typeof(AI_Fish.ProgressFish).GetMethod("OnProgressComplete"); '
        'var info = HarmonyLib.Harmony.GetPatchInfo(m); '
        'return string.Join(",", info.Transpilers.Select(t => t.PatchMethod.Name));')


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ok = total = 0
    for port, who in ((27551, "host"), (27552, "guest")):
        got = ev(port, CODE)
        good = "OnProgressCompleteStolen" in got
        total += 1
        ok += good
        check(f"{who}: the stolen-fish transpiler is on OnProgressComplete ({got})", good)
    print(f"{ok}/{total} verifications OK")


if __name__ == "__main__":
    main()
