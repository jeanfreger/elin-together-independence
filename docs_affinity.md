# Affinity per player (design note)

Elin keeps one number per character, `Chara._affinity` (`_cints[5]`), for how much it likes "the player". Gifts,
chats, quest rewards, love potions, the divorce deed all move it (`Chara.ModAffinity(EClass.pc, a)`); talk options
(invite, marry, sleep beside, become mama, the card), shop prices, lunches, the brush and the people list read it.
In multiplayer that one number belongs to the whole group: a resident the host courted loves the guest too, and a
guest's rudeness makes it hate the host.

Host option **Affinity per player** (`affinity_per_player`, rules key 135, off by default): each player has its own
affinity with each resident. Off: one value for the group, as before.

## Players

- What a resident thinks of you is what *you* did: your gifts, your talks, your quests. The host's love for a
  resident and a guest's dislike of it live side by side.
- Every place the game reads or changes "affinity with the player" follows the player concerned: the dialog of the
  one who talks (invite, marry, sleep beside, mama, card), the price of the one who buys, the gift of the one who
  gives. Nothing is asked of anyone: the game knows who acts.
- A guest starts at 0 with a resident it never met, except its own companions and its spouse, which keep the value
  they had (the group's, from before the option): turning the option on takes nothing from anyone's companions.
- Marriage (Shared marriages): to propose, the proposer's own affinity must reach Love, in its own dialog. The
  spouse stays the one who married it; the divorce deed costs the divorcing player's own affinity.

## Data and saves

- The world owner's (the host's) value stays where the game keeps it, `_cints[5]`. A save opened without the mod
  is the host's world with the host's affinities, exactly as vanilla.
- The other players' values are a table in the host's save (`affinity_per_player`: resident uid -> player chara uid
  -> value), next to the spouse list. A value is written there the first time it changes, and, when a player joins,
  for its companions and spouse (frozen at what they were).
- A guest's game keeps only its own column, sent by the host when it joins and after each change. It is never saved
  by the guest.
- Reading is one getter: `_affinity` returns the column of `EClass.pc` when that is not the world owner (on the
  host: a guest standing in, see below; on a guest's game: always its own). So every vanilla line follows
  without patching each caller.

## Network and anti-cheat

- The player who acts rolls the dice in its own game (as before); a guest sends what changed (`CharaAffinityDelta`,
  relative). The host adds it to *that guest's* column only and sends the new value back to that guest only. The
  host's own changes are no longer broadcast: nobody else reads them.
- When the host computes something for a guest (the existing stand-ins: `RemoteCraft.AsCrafter`, `PlayerStandIn`,
  the quest receiver of `QuestRewardPatch`, a `ModAffinity` aimed at a guest's character), `EClass.pc` is that
  guest, so the change lands in its column and goes to it. A replay of another player's act changes nothing.
- Checked on the host (`OnHostPermit`, refused when trust is enforced, logged otherwise): a guest only sends
  relative changes; a gain needs the resident on the sender's map; one act never brings more than a precious gift
  can (400). Losses are always accepted: they only hurt the sender.
- The worst a hacked guest can do is make a resident love *itself*: it cannot touch the host's value or another
  player's.

## Not covered

- A guest alone on a map it holds (away) changes its own column locally; the host learns it only through the
  existing paths (as before the option, where such changes were lost as well).
- A world handed over to another player (takeover) keeps the old owner's values in `_cints[5]`.
- Turning the option on in the middle of a session: guests get their column at their next join.
