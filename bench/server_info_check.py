"""Council 2026-10-28 gaps 1, 2 and 10: message of the day and rules, personal ignore list, admin announcements.
Needs 2 windows (host 27551 + 1 guest 27552), both in game. usage: python bench/server_info_check.py

What it proves:
- MOTD: the host writes ElinMP/motd.txt and rules.txt, publishes them (UpdateRemoteSessionRules, what the page's button
  runs) and the guest's rules carry the same text
- IGNORE: the guest ignores the host (PlayerSocial.SetIgnored, what the player menu runs); a whisper the host then
  writes does not reach the guest's pop text, while an unignored one does; the ignore list is saved in the guest's
  ElinMP/social_<id>.txt
- ANNOUNCE: the host's Announce reaches the guest as a "[Server]" line; the guest made an admin asks for a line with
  AdminCommandDelta (verb "say") and the host shows it; a guest that is not an admin is refused
Everything C# is reached by reflection (the game's script engine cannot see the mod's internal types).
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import LOCALLOW, ev, eventually  # noqa: E402
from permit_suite import M, send_raw  # noqa: E402

H, A = 27551, 27552
T = 'HarmonyLib.AccessTools.TypeByName'
HT = f'{T}("ElinTogether.Net.ElinNetHost")'
CONN = 'ElinTogether.Net.NetSession.Instance.Connection'
SOC = f'{T}("ElinTogether.Helper.PlayerSocial")'
BUB = f'{T}("ElinTogether.Patches.ChatBubbleEvent")'
GUEST = 'ElinTogether.Net.NetSession.Instance.CurrentPlayers.First(p => p.Index != 0)'
HOST = 'ElinTogether.Net.NetSession.Instance.CurrentPlayers.First(p => p.Index == 0)'
ok = total = 0


def check(label, cond):
    global ok, total
    total += 1
    ok += bool(cond)
    print(("OK   " if cond else "FAIL ") + label)


def shown(port, text):
    """True when the pop-text queue of that game holds a line with the text"""
    return ev(port, f'var q = (System.Collections.IEnumerable)HarmonyLib.AccessTools.Field({BUB}, "Shown").GetValue(null);'
                    f'foreach (var s in q) {{ if (((string)s).Contains("{text}")) return "True"; }} return "False";') == "True"


def main():
    global ok, total
    sys.stdout.reconfigure(encoding="utf-8")
    folder = LOCALLOW / "ElinMP"
    folder.mkdir(parents=True, exist_ok=True)
    old = {n: (folder / n).read_text(encoding="utf-8") if (folder / n).exists() else None for n in ("motd.txt", "rules.txt")}
    (folder / "motd.txt").write_text("Welcome-bench-motd", encoding="utf-8")
    (folder / "rules.txt").write_text("Rule one\nRule two", encoding="utf-8")
    try:
        ev(H, f'HarmonyLib.AccessTools.Method({HT}, "UpdateRemoteSessionRules").Invoke({CONN}, null); return "";')
        rules = 'ElinTogether.Net.NetSession.Instance.Rules'
        check("MOTD: the guest reads the host's message of the day",
              eventually(lambda: ev(A, f'return {rules}.Motd;') == "Welcome-bench-motd", timeout=15))
        check("MOTD: the guest reads the rules, line breaks kept", ev(A, f'return {rules}.RulesText.Replace("\\n", "/");') == "Rule one/Rule two"
              or ev(A, f'return {rules}.RulesText.Length.ToString();') == "17")

        # IGNORE
        guest_uid = ev(H, f'return {GUEST}.CharaUid.ToString();')
        host_user = ev(A, f'return ((ulong){HOST}.User).ToString();')
        ev(A, f'HarmonyLib.AccessTools.Method({SOC}, "SetIgnored").Invoke(null, new object[] {{ {host_user}UL, "bench host", true }}); return "";')
        saved = [p for p in folder.glob("social_*.txt") if f"ignore {host_user}" in p.read_text(encoding="utf-8")]
        check("IGNORE: the guest's list is saved in its ElinMP folder", bool(saved))
        whisper = ('var host = ElinTogether.Net.NetSession.Instance.CurrentPlayers.First(p => p.Index != 0).FindChara();'
                   f'HarmonyLib.AccessTools.Method({T}("ElinTogether.Helper.PlayerWhisper"), "Send").Invoke(null, new object[] {{ host, "{{0}}" }}); return "";')
        ev(H, whisper.replace("{0}", "ignored-line"))
        time.sleep(4)
        check("IGNORE: an ignored player's whisper is dropped on my side", not shown(A, "ignored-line"))
        ev(A, f'HarmonyLib.AccessTools.Method({SOC}, "SetIgnored").Invoke(null, new object[] {{ {host_user}UL, "bench host", false }}); return "";')
        ev(H, whisper.replace("{0}", "heard-line"))
        check("IGNORE: after stopping, the whisper arrives again", eventually(lambda: shown(A, "heard-line"), timeout=15))

        # ANNOUNCE
        ev(H, f'HarmonyLib.AccessTools.Method({HT}, "Announce").Invoke({CONN}, new object[] {{ "bench-notice" }}); return "";')
        check("ANNOUNCE: the host's line reaches the guest as a server notice", eventually(lambda: shown(A, "bench-notice"), timeout=15))
        send_raw(A, f'new {M}AdminCommandDelta {{ Verb = "say", Target = 0UL, Reason = "forged-notice" }}')
        time.sleep(4)
        check("ANNOUNCE: a guest that is not an admin is refused", not shown(H, "forged-notice"))
        guest = ev(H, f'return ((ulong){GUEST}.User).ToString();')
        ev(H, f'HarmonyLib.AccessTools.Method({HT}, "MakeAdmin").Invoke({CONN}, new object[] {{ {guest}UL, true }}); return "";')
        time.sleep(2)
        send_raw(A, f'new {M}AdminCommandDelta {{ Verb = "say", Target = 0UL, Reason = "admin-notice" }}')
        check("ANNOUNCE: an admin guest's line is shown by the host", eventually(lambda: shown(H, "admin-notice"), timeout=15))
        send_raw(A, f'new {M}AdminCommandDelta {{ Verb = "restart", Target = 0UL, Reason = "1" }}')
        check("ANNOUNCE: an admin's restart warning reaches the host", eventually(lambda: shown(H, "restarts in 1 min"), timeout=15))
        ev(H, f'HarmonyLib.AccessTools.Method({HT}, "MakeAdmin").Invoke({CONN}, new object[] {{ {guest}UL, false }}); return "";')
    finally:
        for n, text in old.items():
            if text is None:
                (folder / n).unlink(missing_ok=True)
            else:
                (folder / n).write_text(text, encoding="utf-8")
    print(f"{ok}/{total} verifications OK")


if __name__ == "__main__":
    main()
