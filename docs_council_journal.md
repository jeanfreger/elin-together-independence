# Council: per-player journal (user request 2026-10-10, "for later")

Design notes, 2026-10-28. Belongs in dev/ (not tracked): move it there on merge.

## What the journal is in Elin

The journal (J) is one per world: it reads the `Player` object of the game it is drawn in. Its tabs and where they
read from (decompiled sources):

| Tab | Reads | Today in the fork |
|---|---|---|
| Top (home name, schedule, notices) | `world.schedule`, `player.notices` | world |
| Quests | `game.quests` | per player already (PersonalQuests: random quests, fame, karma); story quests shared |
| Locations / factions / population / religion | `game.factions`, zones | world |
| Key items | `player.keyItems` | world (DialogFlagSync, story) |
| Hall of fame | `stats.kills`, `stats.digs`, `stats.taxBillsPaid`, `stats.timeElapsed`, days, turns | **the host's**, for everyone |
| Chronicle / log | `game.log` (last 50 messages) | **the host's**, for a guest after every world copy |
| Gallery | `player.sketches` | per player already (OwnSettings) |
| Codex | `player.codex` | world, shared on purpose (CodexDelta) |
| Notes (memo widgets) | `player.memo`, `memo2` | per player already (OwnSettings) |

A guest gets the whole `Player` of the host again with every world copy (OwnSettings.Carry puts its own parts back).
So what is not carried shows the host's life: after a trip, the guest's message history is the host's last 50 lines,
and its hall of fame says how much the HOST dug.

## Viewpoint 1: player experience

- A player opening the journal expects to read about itself: its quests, its notes, what it saw happen, how much it
  dug, how often it died. Reading the host's lines ("You dig...", "Nardole fell asleep") after a trip is confusing,
  and losing one's own last messages at every trip is a host/guest inequality (the host never loses its own).
- What the group built together stays one: the base, the codex (a shared collection: the cards in it are items, one
  set for everyone), the schedule, the factions and towns, the story and its key items, the world's date.
- No new window, no new tab: the same journal, with the right numbers (vanilla look, PLAN_vanilla_ui.md).

## Viewpoint 2: data and saves

- The host's own journal is vanilla: it lives in the world save as always. Nothing new goes into the save, so a world
  played with the mod still loads without it (the save has no new field, no new type).
- A guest's own parts already live in a file of its machine, per world seed and character (OwnSettings, the same file
  as its notes, sketches, depth reached). The message history and its personal records join that file. A guest who
  plays from another PC starts these from zero, as it does today for notes and sketches (same limit, written down).
- Kills stay a world count: the game counts every kill of the player's faction (host, guests and their pets), and a
  story quest reads it (QuestDefense, 100 kills). Per player it would break that quest for a guest. Days, turns, time
  played in this world, taxes and shipping are the world's too (the world clock, the base's bills).
- Personal records: `digs` (only the player's own digging counts, `TaskMine` with `owner.IsPC`), `death` and `death2`
  (its own deaths; a dialog reads death2), `slept` (its own nights; also seeds what recipe it dreams of, which is then
  its own roll instead of a replay of the host's). `allyDeath` counts any faction member's death: world.

## Viewpoint 3: network and anti-cheat

- No new message. Each game keeps its own history and records locally; the host never trusts a guest's number, so
  there is nothing to check (no OnHostPermit needed). A guest editing its file changes only what its own journal shows,
  one dialog line about its deaths and its own dream recipe roll: nothing it could not already do in its own game.
- The host's rule reaches guests in NetSessionRules (key 150). An older host does not send it: false, the guest keeps
  the old behaviour (the host's history and records).

## Verdict

"Per-player journal" means: every tab that tells about a player tells about THAT player; what the group shares stays
one. Concretely:

1. Quests, fame, karma: per player already (PersonalQuests). Unchanged.
2. Notes (memo), gallery: per player already (OwnSettings). Unchanged.
3. **Message history (chronicle tab, `game.log`)**: each player keeps its own across world copies and rejoins.
4. **Personal records (hall of fame and what the game reads from them)**: digs, deaths, nights slept are the player's.
5. World: codex, key items and story, kills, days/turns/time played, taxes, shipping, schedule, factions, base log.

Host setting "Each player has their own journal" (`OwnJournal`), in the fallbacks group (off brings back the
host-centred journal), **on by default**: it only makes guests equal to the host, who already has all of this.

Parts (one commit each): the setting; the message history; the records. Tests: `bench/journal_check.py`
(2 windows): marker messages and records on the guest survive a world copy, the host's own do not leak, and with
the box off the guest gets the host's again.

Not done: a guest who becomes the host by takeover keeps the old host's journal parts; the records are not
mirrored to the host (another PC = start from zero, like notes today).
