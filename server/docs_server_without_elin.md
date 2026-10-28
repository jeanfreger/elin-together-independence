# "A server without Elin": what it can mean for this mod (2026-10-28)

## The honest part

The world of Elin is the game's own code: hours, days and months passing, weather, quests, factions, the
expedition, NPC life, every map's turns. None of it exists outside `Elin.exe` (Unity + Mono, Windows, and Steam for the
sessions). A program without the game can **keep** a world (files) and **arbitrate** who holds it (a lock). It
cannot **run** it, fix it, or merge two copies of it. So "server without Elin" can only mean one of two things:

1. **A depot without the game**: a small program keeps the save and the lock; a *player's* game simulates the world
   while that player hosts it. That is what exists (shared folder, ElinTogetherServer.exe "without Elin", GitHub).
   The world lives only while someone plays it, and when the hosting player leaves, the others go back to the title
   screen and one of them takes the world again (no seamless relay).
2. **The game, with nobody playing it**: `Elin.exe -empserver <save>` (EmpServer.cs), started by the server app
   with `-batchmode -nographics` (no window, no GPU), restarted up to 5 times if it stops, optional daily restart.
   This is the only way to have the world always on and joinable. It needs a Windows PC (or Proton/Wine, never
   tried) with Elin installed and a Steam account; it cannot run on a NAS.

A world with nobody connected is frozen anyway on new installs (`TimeStopsWhenEmpty`, on by default since stage 3, EmptyWorld.cs), so an always-on server does not
"live" while empty either: it only waits, ready, holding the world.

## Cost of the always-on game

Measured: a played Elin window uses 1.7 to 2.9 GB of working set (dev/_shots/memwatch.log, 2026-10-23). Headless
(`-batchmode -nographics`) has no GPU textures, so the figure should be lower, but nobody has measured it. CPU: the
game runs at 60 frames a second (Core.cs) or the player's own setting, even with nobody connected, so an empty server
keeps part of a core busy all day. `bench/server_rest_check.py` prints the numbers (CPU seconds per 30 s, working set)
for: box off and empty, box on and empty, one player.

## What was the most useful achievable step, and what was done

Plan steps 3-4 (arrive without passing through the host's map; the session survives the host leaving) are the real
"relay without a cut". They are big: about 35 places still make the host "the world" (quests, expedition, date,
karma, base resources) plus 15 for the save and the session, and `PLAN_serveur_depot.md` says to decide them with the
user after a two-PC test. Not started here.

Two small steps that help now, both done:

1. **The always-on game rests while empty** (`ServerRestsWhenEmpty`, host setting, off by default, card "repair"):
   with no peer connected, a `-empserver` game drops to 5 frames a second and pauses its sound; the next frame a
   peer is there, it gets its own rate back (checked every frame, so a join does not crawl). Nothing else changes:
   the world is already frozen while empty (with `TimeStopsWhenEmpty` off, an empty world just runs about 12 times
   slower, nobody being there to see it), saves and orders (`server.cmd`) go on.
2. **A depot server with no window** (`server/depot_server.py` + `server/Dockerfile`): the "without Elin" half of the
   server app as one Python file, standard library only, same protocol and same folder layout, for a NAS (Synology:
   Docker or Python package), a Raspberry Pi or any Linux box. Players type `address:55557` as with the Windows app.

## Bench notes for the main session

- `python bench/depot_server_py_check.py`: 28/28 here, no game, ~15 s. `--exe` runs the same checks against
  ElinTogetherServer.exe (opens its window; not run here, the user plays on this PC).
- Not yet proven: a real game against the Python server. `DEPOT_SERVER=1 depot_suite.py` starts the exe itself
  (`depot_suite.py` line 246, `[SERVER_EXE, "--depot", ..., "--port", PORT, *more]`); the Python server takes the
  same arguments, so replace `str(SERVER_EXE)` there by `sys.executable, "<worktree>/server/depot_server.py"` for one
  run (2 windows, ~2 min).
- `bench/server_rest_check.py` (needs 2 windows, ~6 min, launches its own games): R1-R5 as in its docstring; run
  once in a window and once with `SERVER_ARGS="-batchmode -nographics"`, and copy the printed numbers here.
- Then, if the numbers are good: consider the box on by default for `-empserver` only (a game nobody plays), and
  make `Server.bat` (dev/_release/template/server.ps1) start with `-batchmode -nographics` like the app.
