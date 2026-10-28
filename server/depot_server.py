"""Elin Together depot server, with no window: the "without Elin" half of ElinTogetherServer.exe, for a machine that
has no Windows desktop (a NAS, a Raspberry Pi, a small Linux box, a Docker container). Python 3.8+, standard library
only, one file.

It keeps the world (an archive of an Elin save) and the lock that says who hosts it. No game runs here: the first
player who comes takes the world and hosts it with the mod, the others join that player, every save comes back here.
Players type `<address of this machine>:55557` in "Join by address" (or in the "Depot" setting).

    python3 depot_server.py --depot /volume1/elin-depot [--port 55557] [--password secret] [--import <save>]

The same settings from the environment (for Docker): DEPOT_DIR, DEPOT_PORT, DEPOT_PASSWORD.

The protocol is the one of ElinTogetherServer.exe and ElinTogether/Helper/SaveDepot.cs (AskServer), byte for byte:
request  = password \\n command \\n id \\n name \\n length \\n <length bytes>
answer   = status \\n length \\n <length bytes>
commands = WHO, TAKE, PUT, BEAT, RELEASE. The folder is the same too (world.zip, world.1-3.zip, replaced-*.zip): a
depot folder moves between the two servers as it is. Not encrypted: the password crosses the network in clear, keep
the port on a trusted or private network (LAN, Tailscale, ZeroTier).
"""
import argparse
import io
import os
import shutil
import socketserver
import sys
import threading
import time
import zipfile
from datetime import datetime

MAX_WORLD = 300 * 1024 * 1024
LOCK_LIFE = 180.0  # seconds without a BEAT before the lock is free again
BACKUP_EVERY = 30 * 60.0  # the hosting player's saves keep the three previous ones, one per half hour at most


class Depot:
    def __init__(self, folder, password):
        self.folder = folder
        self.password = password or ""
        self.gate = threading.Lock()
        self.holder_id = None
        self.holder_name = None
        self.beat = 0.0
        os.makedirs(folder, exist_ok=True)

    @property
    def world(self):
        return os.path.join(self.folder, "world.zip")

    def held(self):
        return self.holder_id is not None and time.monotonic() - self.beat < LOCK_LIFE

    def hold(self, id_, name):
        self.holder_id, self.holder_name, self.beat = id_, name, time.monotonic()

    def handle(self, command, id_, name, body):
        """(status, answer bytes or None). Called under the gate."""
        held = self.held()
        mine = held and self.holder_id == id_
        if command == "WHO":
            return "OK " + (self.holder_name if held and not mine else ""), None
        if command == "TAKE":
            if held and not mine:
                return "NO held " + self.holder_name, None
            if not os.path.exists(self.world):
                return "NO empty", None
            with open(self.world, "rb") as f:
                answer = f.read()
            self.hold(id_, name)
            return "OK", answer
        if command == "PUT":
            # the hosting player saves; when nobody hosts, a player may put another world (a new game): the old one
            # is kept aside and that player becomes the one who hosts
            if held and not mine:
                return "NO held " + self.holder_name, None
            if not body:
                return "NO empty", None
            self.store(body, self.holder_id != id_)
            self.hold(id_, name)
            return "OK", None
        if command == "BEAT":
            if not held or mine:
                self.hold(id_, name)
                return "OK", None
            return "NO held " + self.holder_name, None
        if command == "RELEASE":
            if mine:
                self.holder_id = None
            return "OK", None
        return "NO unknown", None

    def store(self, world, replaces):
        """Written beside, then swapped in one move: a cut never leaves the folder without world.zip"""
        incoming = self.world + ".new"
        with open(incoming, "wb") as f:
            f.write(world)
        if os.path.exists(self.world):
            newest = os.path.join(self.folder, "world.1.zip")
            if replaces:
                stamp = datetime.now().strftime("%Y%m%d-%H%M%S-") + f"{datetime.now().microsecond // 1000:03d}"
                os.replace(self.world, os.path.join(self.folder, f"replaced-{stamp}.zip"))
            elif not os.path.exists(newest) or time.time() - os.path.getmtime(newest) > BACKUP_EVERY:
                for i in range(3, 0, -1):
                    older = os.path.join(self.folder, f"world.{i}.zip")
                    newer = self.world if i == 1 else os.path.join(self.folder, f"world.{i - 1}.zip")
                    if os.path.exists(newer):
                        shutil.copyfile(newer, older)  # (a fresh date: the half hour counts from this copy)
        os.replace(incoming, self.world)

    def import_save(self, path):
        """A save of this machine becomes the world: a save folder (with game.txt), a Steam Cloud save folder (its
        cloud.zip) or an archive. None, or what prevents it"""
        if os.path.isdir(path) and not os.path.exists(os.path.join(path, "game.txt")) and os.path.exists(os.path.join(path, "cloud.zip")):
            path = os.path.join(path, "cloud.zip")
        if os.path.isfile(path):
            with open(path, "rb") as f:
                world = f.read()
        elif os.path.exists(os.path.join(path, "game.txt")):
            data = io.BytesIO()
            with zipfile.ZipFile(data, "w", zipfile.ZIP_DEFLATED) as z:
                for root, _, files in os.walk(path):
                    for name in files:
                        full = os.path.join(root, name)
                        z.write(full, os.path.relpath(full, path))
            world = data.getvalue()
        else:
            return "This is not an Elin save (no game.txt)."
        try:
            with zipfile.ZipFile(io.BytesIO(world)) as z:
                if "game.txt" not in z.namelist():
                    return "This archive holds no Elin save (no game.txt)."
        except zipfile.BadZipFile as ex:
            return str(ex)
        with self.gate:
            if self.held():
                return f"{self.holder_name} is hosting the world right now: wait until they leave."
            self.store(world, True)
        return None


def read_line(f):
    line = f.readline(4097)
    if not line.endswith(b"\n"):
        raise IOError("incomplete line")
    return line[:-1].decode("utf-8")


class Handler(socketserver.StreamRequestHandler):
    timeout = 60

    def handle(self):
        depot = self.server.depot
        try:
            password, command, id_, name = (read_line(self.rfile) for _ in range(4))
            length = int(read_line(self.rfile))
            # the password before anything is read or reserved: nothing waits for the bytes of a stranger
            if password != depot.password:
                return self.reply("NO password", None)
            if length < 0 or length > MAX_WORLD:
                return self.reply("NO too big", None)
            body = self.rfile.read(length)
            if len(body) < length:
                return
            with depot.gate:
                status, answer = depot.handle(command, id_, name, body)
            log(f"{name} : {command} -> {status}")
            self.reply(status, answer)
        except (OSError, ValueError, UnicodeDecodeError):
            pass  # a request cut or malformed: the next one

    def reply(self, status, body):
        self.wfile.write(f"{status}\n{0 if body is None else len(body)}\n".encode("utf-8"))
        if body:
            self.wfile.write(body)


class Server(socketserver.ThreadingTCPServer):
    daemon_threads = True
    allow_reuse_address = True


def log(text):
    print(datetime.now().strftime("%H:%M:%S") + "  " + text, flush=True)


def main():
    parser = argparse.ArgumentParser(description="Elin Together depot server (no window, no game)")
    parser.add_argument("--depot", default=os.environ.get("DEPOT_DIR", "depot"), help="folder that keeps the world")
    parser.add_argument("--port", type=int, default=int(os.environ.get("DEPOT_PORT", "55557")))
    parser.add_argument("--password", default=os.environ.get("DEPOT_PASSWORD", ""))
    parser.add_argument("--import", dest="save", help="make this save the world first (refused while someone hosts)")
    args = parser.parse_args()

    depot = Depot(os.path.abspath(args.depot), args.password)
    if args.save:
        problem = depot.import_save(args.save)
        if problem:
            print(problem, file=sys.stderr)
            sys.exit(1)
        log(f"{args.save} is the world now")
    with Server(("", args.port), Handler) as server:
        server.depot = depot
        log(f"depot {depot.folder} on port {args.port}" + (", with a password" if depot.password else ", no password"))
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
