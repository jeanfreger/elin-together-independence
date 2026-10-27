"""Official server entry (user, 2026-10-27). Host open on the title screen or in a game, port 27551.
What it proves on the bench:
 1. OfficialServer.IsOfficial refuses a lobby that carries no / a forged signature (the title entry would then say
    "not open right now" and show no Join).
 2. OfficialServer.Hosting is false on this PC (no key): ImportCharacter is not forced off, the settings row is not
    greyed; with the setting ticked, the host still reads it as on.
NOT testable on the bench: a really signed official lobby (needs the Elin Together account and its key, and Debug
lobbies are private so they never reach the list), so the Join click and the player count are checked by reading the
code path (same ConnectLobby as the list's Join), and the greyed import row with Hosting true needs the real host PC.
usage: python bench/official_entry_check.py   (from the worktree; needs dev/_tools on the path)"""
import sys
from pathlib import Path

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import check, ev  # noqa: E402

H = 27551


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    forged = ev(H, 'var l = new HeathenEngineering.SteamworksIntegration.LobbyData();'
                   'return ElinTogether.Net.Steam.OfficialServer.IsOfficial(l).ToString();')
    check(f"a lobby with no signature is not official ({forged})", forged == "False")
    ok = ev(H, 'return ElinTogether.Net.Steam.OfficialServer.Verify(1, "AAAA").ToString();')
    check(f"a forged signature is refused ({ok})", ok == "False")
    hosting = ev(H, 'return ElinTogether.Net.Steam.OfficialServer.Hosting.ToString();')
    check(f"this PC does not host the official server ({hosting})", hosting == "False")
    was = ev(H, 'return ElinTogether.EmpConfig.Server.ImportCharacter.Value.ToString();')
    ev(H, 'ElinTogether.EmpConfig.Server.ImportCharacter.Value = true; return "ok";')
    now = ev(H, 'var row = System.Linq.Enumerable.First(ElinTogether.Components.SettingsTable.Rows(), r => r.Id == "import_chara");'
                'return row.Requires().ToString();')
    check(f"the import row is not greyed without the key ({now})", now == "True")
    ev(H, f'ElinTogether.EmpConfig.Server.ImportCharacter.Value = {was.lower()}; return "ok";')
    sys.exit(0)


if __name__ == "__main__":
    main()
