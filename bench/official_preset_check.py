"""The "Public server (official)" preset and the official server's fixed rules (plan_official_server section 1).
Needs 1 window (host 27551), no guest. usage: python bench/official_preset_check.py (or copied to dev/_tools)

What it proves:
- PRESET: ApplyPreset(3) (the "official" preset, index 3 after custom) writes the listed values: 12 spot-checked
  (WorldCopy, Takeover, MaxCharacters, KickKeepOutMinutes, WorldMapTime, NightPasses, IdleMinutes, TeamMaxSize,
  LandsPerTeam, AbandonDays, SourceValidationSet, GlobalChat) and SettingsTable.Preset reads 3 afterwards
- FIXED: with OfficialServer.BenchForce on (the bench has no Elin Together account), the rows of the fixed rules
  report Requires() = false (greyed), a "classic" preset applied then leaves the fixed boxes (fallbacks, PlayerKill
  off, Teams) alone while a non-fixed box (HostManagesBase) still changes, and ForceOfficial writes a changed fixed
  box (WorldCopy) back
The host's rules are put back with the recommended preset at the end (0). Not tested: the page drawing (the menu is
another worker's).
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import RESULTS, check, ev  # noqa: E402

H = 27551
ST = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Components.SettingsTable")'
SRV = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.EmpConfig+Server")'
OFF = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.Steam.OfficialServer")'


def entry(name):
    return f'HarmonyLib.AccessTools.Property({SRV}, "{name}").GetValue(null)'


def rd(name):
    e = entry(name)
    return ev(H, f'var e = {e}; return e.GetType().GetProperty("BoxedValue").GetValue(e).ToString();')


def wr(name, value):
    e = entry(name)
    ev(H, f'var e = {e}; e.GetType().GetProperty("BoxedValue").SetValue(e, {value}); return "ok";')


def st(method, args=""):
    return ev(H, f'var r = HarmonyLib.AccessTools.Method({ST}, "{method}").Invoke(null, new object[] {{ {args} }}); return r == null ? "" : r.ToString();')


def greyed(row_id):
    """True when the row's Requires() says false (the page greys it out)"""
    code = (f'var rows = (System.Collections.IEnumerable)HarmonyLib.AccessTools.Method({ST}, "Rows").Invoke(null, null); '
            'foreach (var row in rows) { var t = row.GetType(); '
            f'if ((string)t.GetProperty("Id").GetValue(row) == "{row_id}") {{ var f = t.GetProperty("Requires").GetValue(row); '
            'return f == null ? "none" : ((System.Func<bool>)f)().ToString(); } } return "missing";')
    return ev(H, code)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    flag = f'HarmonyLib.AccessTools.Field({OFF}, "BenchForce")'
    try:
        # PRESET (not hosting as the official server: a plain host)
        ev(H, f'{flag}.SetValue(null, false); return "ok";')
        time.sleep(1.3)
        st("ApplyPreset", "3")
        want = {"WorldCopy": "False", "Takeover": "False", "MaxCharacters": "3", "KickKeepOutMinutes": "60",
                "WorldMapTime": "PersonalPace", "NightPasses": "Half", "IdleMinutes": "5", "TeamMaxSize": "10",
                "LandsPerTeam": "2", "AbandonDays": "30", "SourceValidationSet": "sources,plugins", "GlobalChat": "True"}
        for name, value in want.items():
            got = rd(name)
            check(f"PRESET: {name} = {value} (got {got})", got == value)
        preset = ev(
            H, f'var cur = HarmonyLib.AccessTools.Property(HarmonyLib.AccessTools.TypeByName("ElinTogether.Components.RuleSource"), "Current").GetValue(null); '
               f'return HarmonyLib.AccessTools.Method({ST}, "Preset").Invoke(null, new object[] {{ cur }}).ToString();')
        check(f"PRESET: the rules read back as preset 3 (got {preset})", preset == "3")

        # FIXED
        ev(H, f'{flag}.SetValue(null, true); return "ok";')
        time.sleep(1.3)  # (StoryOwner.OfficialHosting is read once a second)
        for row in ("world_copy", "takeover", "auto_host", "team_own_story", "teams", "player_kill", "personal_quests"):
            g = greyed(row)
            check(f"FIXED: row {row} is greyed out (Requires() = {g})", g == "False")
        check(f"FIXED: a free row (max_characters) is not greyed ({greyed('max_characters')})", greyed("max_characters") in ("True", "none"))
        st("ApplyPreset", "1")  # classic: fallbacks off, host-led building
        check(f"FIXED: classic leaves a fixed fallback box on (OwnJournal {rd('OwnJournal')})", rd("OwnJournal") == "True")
        check(f"FIXED: classic leaves Teams on ({rd('Teams')})", rd("Teams") == "True")
        check(f"FIXED: classic still changes a free box (HostManagesBase {rd('HostManagesBase')})", rd("HostManagesBase") == "True")
        wr("WorldCopy", "true")
        check("FIXED: a fixed box changed behind the page's back (WorldCopy true)", rd("WorldCopy") == "True")
        st("ForceOfficial")
        check(f"FIXED: ForceOfficial writes it back (WorldCopy {rd('WorldCopy')})", rd("WorldCopy") == "False")
    finally:
        ev(H, f'{flag}.SetValue(null, false); return "ok";')
        time.sleep(1.3)
        st("ApplyPreset", "0")


if __name__ == "__main__":
    main()
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    sys.exit(1 if failed else 0)
