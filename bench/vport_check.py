"""No-host decision 2026-10-28, T1 (solo): two processes on one Steam account listen on different Steam virtual
ports, and a connect to virtual port 1 reaches the one that listens on 1, not the one on 0.
Needs 3 windows (host 27551 = the "server" on vport 0, guest A 27552 = the owner's zone session on vport 1,
guest B 27553 = a third instance that dials). All three share one Steam account on the bench.
usage: python bench/vport_check.py   (~1 minute; no map travel, the windows may stand anywhere)

What it proves:
  1. the host opens a SteamNetManager listen socket on vport 0 and A one on vport 1 (StartServerSdr(vport)): both
     calls succeed (no "Failed to create listen socket")
  2. B calls Connect(account, 1) (SteamNetManager.Connect(UserData, int vport)): A's log shows the connection
     request (AcceptIfHost) and the host's does not
  3. CONTROL: B calls Connect(account, 0): the host's log shows it, A's does not
Reads the Player.log of each window (the mod's session log is shared by all the games, it cannot tell them apart).
If step 2 fails because Steam routed the connection to the wrong process (or to none), that is the "unproven" part
of the decision: then (e) a game-server identity, or (c) a relay, see dev/COUNCIL_2026-10-28_no_host.md.
Leaves the three games running; the raw listen sockets are disposed at the end.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from benchlib import A, H, check, ev, eventually, finish  # noqa: E402
from travel_suite import LOCALLOW  # noqa: E402
from mp_test import SHOTS  # noqa: E402

B = 27553
LOGS = {H: LOCALLOW / "Player.log", A: SHOTS / "elin2-player.log", B: SHOTS / "elin3-player.log"}
ASK = "connection request from"

# SteamNetManager is public but the script engine may not see the mod's assembly: reflection only
SM = 'HarmonyLib.AccessTools.TypeByName("ElinTogether.Net.Steam.SteamNetManager")'
UD = 'typeof(HeathenEngineering.SteamworksIntegration.UserData)'


def make(slot):
    return f'var m = System.Activator.CreateInstance({SM}, new object[] {{ null }}); System.AppDomain.CurrentDomain.SetData("vp_{slot}", m); '


def listen(port, vport):
    return ev(port, make(f"l{vport}") +
              f'HarmonyLib.AccessTools.Method({SM}, "StartServerSdr", new System.Type[] {{ typeof(int) }}).Invoke(m, new object[] {{ {vport} }}); '
              f'return "listening";')


def dial(port, vport):
    return ev(port, make(f"d{vport}") +
              f'var me = (HeathenEngineering.SteamworksIntegration.UserData)HeathenEngineering.SteamworksIntegration.UserData.Me; '
              f'HarmonyLib.AccessTools.Method({SM}, "Connect", new System.Type[] {{ {UD}, typeof(int) }}).Invoke(m, new object[] {{ me, {vport} }}); '
              f'return "dialled";')


def dispose(port):
    return ev(port, f'foreach (var k in new[] {{ "vp_l0", "vp_l1", "vp_d0", "vp_d1" }}) {{ '
                    f'if (System.AppDomain.CurrentDomain.GetData(k) is System.IDisposable d) d.Dispose(); '
                    f'System.AppDomain.CurrentDomain.SetData(k, null); }} return "ok";')


def size(port):
    return LOGS[port].stat().st_size if LOGS[port].exists() else 0


def asked_since(port, offset):
    if not LOGS[port].exists():
        return False
    with open(LOGS[port], "rb") as f:
        f.seek(offset)
        return ASK in f.read().decode("utf-8", errors="replace")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        check("1 host listens on vport 0", listen(H, 0) == "listening")
        check("1b A listens on vport 1", listen(A, 1) == "listening")
        time.sleep(3)

        h0, a0 = size(H), size(A)
        dial(B, 1)
        check("2 a connect to vport 1 is logged by A (AcceptIfHost)", eventually(lambda: asked_since(A, a0), timeout=30))
        check("2b and not by the host", not asked_since(H, h0))
        dispose(B)

        h0, a0 = size(H), size(A)
        dial(B, 0)
        check("3 CONTROL: a connect to vport 0 is logged by the host", eventually(lambda: asked_since(H, h0), timeout=30))
        check("3b and not by A", not asked_since(A, a0))
    finally:
        for port in (B, A, H):
            dispose(port)


if __name__ == "__main__":
    main()
    finish()
