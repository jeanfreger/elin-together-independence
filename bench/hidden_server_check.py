"""The hidden server for a real player (plan dev/PLAN_no_host_launch.md, tests T1-T3): host box "Play as a guest of a
hidden server (beta)" ON, the visible game hosts world_lab, a second, hidden Elin (-empserver, -empparent) starts, the
visible game joins it over the loopback as an ordinary guest.
Launches its own games (the Steam game folder's Elin.exe, DEBUG build, bridge on): the visible one, then the hidden one
through the product's own code (HiddenServer.TryStart, what the "Start Server" button calls). Windows minimized
(the hidden one has none), -empmute; closes only by exact pid. Needs the game closed (nothing else running), Steam
running, Dev.Listener on in the mod's config. About 25 minutes (two start-ups of the visible game, two of the server).

    python bench/hidden_server_check.py [--hide-appid] [--skip-quit-variant]

--hide-appid  moves steam_appid.txt out of the game folder for the run (put back at the end): a real player has none,
              the env SteamAppId/SteamGameId the game sets for the server is then the only thing that says which game it is
              (T1, "SteamAPI_Init without steam_appid.txt"). Do not use while another session needs the file.
The visible game is started with the same env: it stands in for a game started by Steam.

T1  the visible game lets go of its single-instance mutex and the real Elin.exe starts as a hidden server: it is not
    blocked on the mutex, loads the world, opens its session, makes its Steam lobby (so SteamAPI_Init worked), and the
    visible game joined it in time (no fallback to classic hosting)
T2  identity: the server's host id is the reserved one, the visible game keeps its real Steam id and its own character,
    and the server lists it as the only player; the server holds the only listen socket (the visible game has none),
    on 127.0.0.1 only; the owner seat is accepted with the token and an admin, refused without it, and accepted again with
    it; the owner is denied a map of its own (a zone lease) and stays at home; the lobby id is in ElinMP/server
T3  the visible game dies (killed by pid): the server saves and stops within 15 s by itself, nothing left running.
    Then (unless --skip-quit-variant) a second start, and the visible game quits cleanly: server.stop stops the server
"""
import argparse
import os
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

DEV_TOOLS = Path(os.environ.get("ELIN_DEV_TOOLS", r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools"))
sys.path.insert(0, str(DEV_TOOLS))
sys.path.insert(0, str(DEV_TOOLS / "pylib"))
import emp  # noqa: E402
from gamepath import GAME  # noqa: E402
from mp_test import (BEHIND, CONTINUE, EMBARK, GAME_EXE, PICK_CHARA, PRISTINE, SAVES, SHOTS, WINDOW, bridge_for, log, shot,  # noqa: E402
                     state, wait)
from resync_suite import lines, now  # noqa: E402
from travel_suite import HOME, RESULTS, VERNIS, check, dismiss_dialogs, ev, eventually, move, scan_logs  # noqa: E402

ELINMP = SAVES.parent / "ElinMP"
SERVER_DIR = ELINMP / "server"
STATUS = ELINMP / "server.txt"
APP_ID = "2135150"
RESERVED = 76561190000000000 + 999  # SteamNetPeer.DevIdentityBase + ServerAvatar.ServerIdentity
PORT = 55556

T = 'HarmonyLib.AccessTools.TypeByName'
SERVER_CFG = f'{T}("ElinTogether.EmpConfig+Server")'
HOST_TYPE = f'{T}("ElinTogether.Net.ElinNetHost")'
AVATAR = f'{T}("ElinTogether.Helper.ServerAvatar")'


def prop(type_expr, name):
    return f'HarmonyLib.AccessTools.Property({type_expr}, "{name}").GetValue(null)'


def set_box(port, on):
    got = ev(port, f'var e = {prop(SERVER_CFG, "HiddenServer")}; '
                   f'HarmonyLib.AccessTools.Property(e.GetType(), "Value").SetValue(e, {str(on).lower()}); return "ok";')
    check(f"host box HiddenServer set to {on} ({got})", got == "ok")


def safe(fn, default=None):
    try:
        return fn()
    except (OSError, RuntimeError, KeyError, ValueError, TypeError):
        return default


def status():
    if not STATUS.exists():
        return {}
    return dict(line.split("=", 1) for line in STATUS.read_text(encoding="utf-8", errors="replace").splitlines() if "=" in line)


def alive(pid):
    out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/NH"], capture_output=True, text=True).stdout
    return f" {pid} " in out


def kill(pid):
    subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)


def children_of(pid):
    """Elin.exe processes started with -empparent <pid>: the hidden servers of that visible game."""
    cmd = ("Get-CimInstance Win32_Process -Filter \"Name='Elin.exe'\" | "
           f"Where-Object {{ $_.CommandLine -like '*-empparent {pid} *' }} | ForEach-Object {{ $_.ProcessId }}")
    out = subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True).stdout
    return [int(x) for x in out.split()]


def working_set_mb(pid):
    out = subprocess.run(["powershell", "-NoProfile", "-Command", f"(Get-Process -Id {pid}).WorkingSet64 / 1MB"],
                         capture_output=True, text=True).stdout.strip()
    return int(float(out.replace(",", "."))) if out else 0


def udp_binding(pid):
    """Local addresses the process listens on for UDP at the session port."""
    out = subprocess.run(["netstat", "-ano", "-p", "udp"], capture_output=True, text=True).stdout
    return [parts[1] for parts in (line.split() for line in out.splitlines())
            if len(parts) >= 4 and parts[-1] == str(pid) and parts[1].endswith(f":{PORT}")]


def start_visible(appid_env=True):
    """The visible game: the Steam folder's Elin.exe, world_lab loaded and ready to host."""
    shutil.rmtree(SAVES / "world_lab", ignore_errors=True)
    shutil.copytree(PRISTINE, SAVES / "world_lab")
    for stale in ("server.txt", "server.stop", "server.cmd"):
        (ELINMP / stale).unlink(missing_ok=True)
    env = dict(os.environ)
    if appid_env:
        env["SteamAppId"] = env["SteamGameId"] = APP_ID
    proc = subprocess.Popen([str(GAME_EXE), *WINDOW], cwd=GAME_EXE.parent, startupinfo=BEHIND, env=env)
    log(f"visible game started (pid {proc.pid})")
    port = wait(lambda: bridge_for(proc.pid), "bridge of the visible game", timeout=600)
    ev(port, 'Game.TryLoad("world_lab", false, () => Game.Load("world_lab", false)).ToString()')
    wait(lambda: state(port)["gameStarted"] and state(port)["sceneMode"] == "Zone", "world_lab loaded", timeout=600)
    emp.call(port, "eval", {"code": CONTINUE}, timeout=180)
    wait(lambda: safe(lambda: state(port)["sceneMode"] == "Zone"), "visible game in the zone", timeout=120)
    time.sleep(5)
    return proc, port


def host_hidden(visible_port):
    """What the "Start Server" button does with the box on. True when the hidden server took over."""
    got = ev(visible_port, f'var m = HarmonyLib.AccessTools.Method({T}("ElinTogether.Helper.HiddenServer"), "TryStart"); '
                           'return m.Invoke(null, new object[] { new System.Action(() => UnityEngine.Debug.Log("emp-bench-classic")) }).ToString();')
    return got == "True"


def settle(port):
    """The visible game, joined to the server: through the character screens if they come, until it plays."""
    def playing():
        s = safe(lambda: state(port))
        if s and s["sceneMode"] == "Zone" and s["connected"]:
            return True
        safe(lambda: emp.call(port, "eval", {"code": PICK_CHARA}, timeout=180))
        safe(lambda: emp.call(port, "eval", {"code": EMBARK}, timeout=180))
        return False

    wait(playing, "the visible game plays on the server", timeout=300, every=3.0)
    time.sleep(4)
    dismiss_dialogs(port)


def cycle(proc, V, t0):
    """Host with the box on, wait for the hidden server and the join. Returns (server pid, server bridge port, own chara uid)."""
    before_uid = state(V)["pc"]["uid"]
    set_box(V, True)
    check("the hidden server took over from the classic hosting", host_hidden(V))
    kids = []
    eventually(lambda: bool(kids.extend(children_of(proc.pid)) or kids), timeout=60)
    check("a second Elin.exe was started with -empparent (not blocked by the single-instance mutex)", bool(kids))
    server_pid = kids[0] if kids else 0
    S = wait(lambda: bridge_for(server_pid), "bridge of the hidden server", timeout=600) if server_pid else None
    check("the hidden server reached running (the session is open)",
          eventually(lambda: status().get("state") == "running", timeout=180))
    check("the visible game lets go of its mutex (log)", bool(lines(t0, "let go of its mutex")))
    check("the hidden server opened its session (log)", bool(lines(t0, "opening the session")))
    check("a Steam lobby was made by the hidden server (lobby.txt: SteamAPI_Init worked there)",
          eventually(lambda: (SERVER_DIR / "lobby.txt").exists() and (SERVER_DIR / "lobby.txt").read_text().strip().isdigit(), timeout=30))
    check("the visible game joined in time: no fallback to the classic hosting",
          eventually(lambda: safe(lambda: state(V)["role"]) == "Client", timeout=90) and not lines(t0, "hosted the usual way"))
    settle(V)
    if server_pid:
        mb = working_set_mb(server_pid)
        log(f"hidden server memory: {mb} MB")
        check(f"the hidden server stays under 4 GB ({mb} MB)", 0 < mb < 4096)
    return server_pid, S, before_uid


def identity_checks(proc, V, S, server_pid, uid, t0):
    own = int(ev(V, 'return ((ulong)HeathenEngineering.SteamworksIntegration.UserData.Me).ToString();'))
    host_user = ev(S, f'return {prop(AVATAR, "HostUser")}.ToString();')
    check(f"the server's host id is the reserved one ({host_user})", host_user == str(RESERVED))
    check("the server's own character is the hidden stand-in",
          ev(S, f'return HarmonyLib.AccessTools.Method({AVATAR}, "Is").Invoke(null, new object[] {{ EClass.pc }}).ToString();') == "True")
    ids = status().get("ids", "")
    check(f"the visible game is the server's only player, under its real Steam id ({ids})", ids == str(own))
    check("it plays the character it hosted with (same uid)", state(V)["pc"]["uid"] == uid)

    # sockets
    check("the owner seat is the real account (server OwnerSeat)", ev(S, f'return {prop(HOST_TYPE, "OwnerSeat")}.ToString();') == str(own))
    check("and an admin of its world",
          ev(S, f'return ((System.Collections.IDictionary){prop(HOST_TYPE, "Admins")}).Contains(ulong.Parse("{own}")).ToString();') == "True")
    check("the owner seat was accepted with its token (log)", bool(lines(t0, "is the owner's seat")))
    sock = lambda port: ev(port, 'var s = HarmonyLib.Traverse.Create(ElinTogether.Net.NetSession.Instance.Transport).Field("Socket"); '
                                 'return s.Property("IsListening").GetValue().ToString() + "|" + s.Field("_listenSocket").GetValue() + "|" '
                                 '+ s.Field("_steamListenSocket").GetValue() + "|" + HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("Steamworks.HSteamListenSocket"), "Invalid").GetValue(null);')
    server_sock = sock(S).split("|")
    visible_sock = sock(V).split("|")
    check(f"the server listens, with the Steam (P2P) socket ({server_sock})",
          server_sock[0] == "True" and server_sock[2] != server_sock[3])
    check(f"the visible game opens no listen socket of any kind ({visible_sock})",
          visible_sock[0] == "False" and visible_sock[1] == visible_sock[3] and visible_sock[2] == visible_sock[3])
    bound = udp_binding(server_pid)
    check(f"the server's port is bound on 127.0.0.1 only ({bound})", bool(bound) and all(b.startswith("127.0.0.1:") for b in bound))
    lobby = (SERVER_DIR / "lobby.txt").read_text().strip() if (SERVER_DIR / "lobby.txt").exists() else ""
    check(f"the visible game sees the server's lobby id for invites ({lobby})",
          lobby.isdigit() and ev(V, f'return HarmonyLib.AccessTools.Method({T}("ElinTogether.Helper.HiddenServer"), "ServerLobby").Invoke(null, null).ToString();') == lobby)

    # the owner holds no map of its own
    t1 = now()
    move(V, VERNIS)
    check("the owner is denied a map of its own (log)", eventually(lambda: bool(lines(t1, "holds no map")), timeout=60))
    time.sleep(3)
    dismiss_dialogs(V)
    check("and stays at home", (safe(lambda: state(V)["zone"]["uid"]) == HOME))

    # token: without it the seat is put out, with it it is accepted again
    token = SERVER_DIR / "owner.token"
    check("owner.token holds 32 bytes", token.exists() and len(token.read_bytes()) == 32)
    aside = SERVER_DIR / "owner.token.aside"
    t2 = now()
    ev(V, 'ElinTogether.Net.NetSession.Instance.ResetSession(); return "ok";')
    time.sleep(3)
    token.rename(aside)
    try:
        safe(lambda: emp.call(V, "command", {"cmd": "emp.connect_udp"}, timeout=120))
        check("the same account on the loopback without the token is put out (log)",
              eventually(lambda: bool(lines(t2, "came without the owner's token")), timeout=60))
        check("and does not play", eventually(lambda: not safe(lambda: state(V)["connected"], True), timeout=30))
    finally:
        aside.rename(token)
    ev(V, 'ElinTogether.Net.NetSession.Instance.ResetSession(); return "ok";')
    time.sleep(3)
    if safe(lambda: state(V)["sceneMode"]) != "Title":
        safe(lambda: emp.call(V, "eval", {"code": 'EClass.scene.Init(Scene.Mode.Title); "ok"'}, timeout=60))
    wait(lambda: safe(lambda: state(V)["sceneMode"] == "Title" and not state(V)["connected"]), "title screen", timeout=90)
    safe(lambda: emp.call(V, "command", {"cmd": "emp.connect_udp"}, timeout=120))
    settle(V)
    check("with its token again it plays its own character", state(V)["pc"]["uid"] == uid)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--hide-appid", action="store_true")
    ap.add_argument("--skip-quit-variant", action="store_true")
    a = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    SHOTS.mkdir(exist_ok=True)
    t0 = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")
    appid = GAME / "steam_appid.txt"
    hidden_appid = appid.with_suffix(".txt.bench_aside")
    pids = []
    V = None
    try:
        if emp.live_ports():
            raise RuntimeError("Elin is already running: close it")
        if a.hide_appid and appid.exists():
            appid.rename(hidden_appid)
            log("steam_appid.txt moved aside")

        log("--- T1 + T2")
        proc, V = start_visible()
        pids.append(proc.pid)
        server_pid, S, uid = cycle(proc, V, t0)
        pids.append(server_pid)
        if server_pid and S:
            identity_checks(proc, V, S, server_pid, uid, t0)

            log("--- T3 the visible game dies")
            stamp = (SAVES / "world_lab" / "game.txt").stat().st_mtime
            t3 = time.time()
            set_box(V, False)
            kill(proc.pid)
            gone = eventually(lambda: not alive(server_pid), timeout=40)
            took = time.time() - t3
            log(f"server gone after {took:.1f} s")
            check(f"the server stops by itself within 15 s of its game dying ({took:.1f} s)", gone and took <= 15)
            check("it saved the world before it stopped", (SAVES / "world_lab" / "game.txt").stat().st_mtime > stamp)
            check("no Elin.exe of this run is left", not any(alive(p) for p in pids))

            if not a.skip_quit_variant:
                log("--- T3 the visible game quits cleanly")
                t4 = now()
                proc, V = start_visible()
                pids.append(proc.pid)
                server_pid, S, uid = cycle(proc, V, t4)
                pids.append(server_pid)
                set_box(V, False)
                safe(lambda: emp.call(V, "eval", {"code": "UnityEngine.Application.Quit(); return \"ok\";"}, timeout=30))
                check("the clean quit writes server.stop (log)", eventually(lambda: bool(lines(t4, "asked to stop, this game leaves")), timeout=60))
                check("the server saves and stops after it", eventually(lambda: not alive(server_pid), timeout=60))
                check("the visible game is gone", eventually(lambda: not alive(proc.pid), timeout=60))
    except Exception as ex:  # noqa: BLE001
        check(f"interrupted: {type(ex).__name__}: {str(ex)[:300]}", False)
        for h in emp.live_ports():
            safe(lambda: print(f"    capture: {shot('fail-hidden-' + str(h['port']), h['port'])}"))
    finally:
        if V:
            safe(lambda: set_box(V, False))
        for pid in pids:
            if pid and alive(pid):
                kill(pid)
        if hidden_appid.exists():
            hidden_appid.rename(appid)
            log("steam_appid.txt put back")

    scan_logs(t0)
    failed = [label for label, good in RESULTS if not good]
    print(f"\n{len(RESULTS) - len(failed)}/{len(RESULTS)} verifications OK")
    for label in failed:
        print(f"  FAILED: {label}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
