"""Host/guest audit item 4: OwnSettings keeps the guest's own counters across world copies. Needs 2 windows
(host 27551 + guest 27552). usage: python bench/ownsettings_counters_check.py

What it proves: with distinctive values in the guest's Player (stats.mama, stats.escapeSilverBell, questRerollCost,
safeTravel, staminaRecovery, tutorialStep), OwnSettings.Keep() (what runs before a new world replaces the game) notes
all six in the kept record.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import check, ev  # noqa: E402

A = 27552
OS = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Helper.OwnSettings")'
FIELDS = {"Mama": ("stats.mama = 4242L", "4242"), "EscapeSilverBell": ("stats.escapeSilverBell = 7", "7"),
          "QuestRerollCost": ("questRerollCost = 33", "33"), "SafeTravel": ("safeTravel = 5", "5"),
          "StaminaRecovery": ("staminaRecovery = 11", "11"), "TutorialStep": ("tutorialStep = 9", "9")}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ok = total = 0
    sets = " ".join(f"EClass.player.{s};" for s, _ in FIELDS.values())
    ev(A, f'{sets} HarmonyLib.AccessTools.Field({OS}, "_kept").SetValue(null, null); '
          f'HarmonyLib.AccessTools.Method({OS}, "Keep").Invoke(null, null); return "ok";')
    for name, (_, want) in FIELDS.items():
        got = ev(A, f'var k = HarmonyLib.AccessTools.Field({OS}, "_kept").GetValue(null); '
                    f'if (k == null) return "no kept"; '
                    f'return HarmonyLib.AccessTools.Field(k.GetType(), "{name}").GetValue(k).ToString();')
        total += 1
        good = got == want
        check(f"{name} kept ({got})", good)
        ok += good
    print(f"{ok}/{total} verifications OK")


if __name__ == "__main__":
    main()
