# Zone sessions on a public server (security audit 2026-10-27, item 8)

A player who keeps a map away from the host (the "holder") hosts the players who walk into it: its game simulates
the map, the visitors' characters, bags, lives and companions, and uploads them to the host at checkpoints, when it
leaves and when it drops (ElinNetHostZoneSession, ReplaceGuestCharas). The visitor's own game mirrors what the
holder sends it, and uploads that when it leaves.

## Security

- A modified holder is a host for its visitors: it can rewrite a visitor's character (level, bag, gold, death),
  ignore or invent the visitor's actions, and the result reaches the real host through its uploads or the visitor's
  own. The host's OnHostPermit checks run on the holder's game, which is the attacker's.
- Item 3 already limits the holder's uploads to its own visitors; that only bounds who can be harmed.
- No check on the host can tell an honest holder's simulation from a forged one: the holder IS the authority for
  that map. The only real fix is not to put a player under a stranger's authority.

## The players' experience

- Visiting each other's map is the heart of independent travel; on a dedicated server the server never moves, so
  zone sessions are the only way two players play together away from its map.
- Players who chose to play together (a team) trust each other already: they share a base, bills, the story.
- A stranger refused at the door must not get a question (seamless): it stays where it is and is told why, as with
  the existing "another player is already there".
- The host of a public server can vouch for regulars: admins and players let in with "allow" are trusted.

## What breaks

- On a public or dedicated server, two players who are in no team together cannot share a map away from the host's;
  a town held by a stranger is closed to the other until it leaves (it is "occupied", as a busy holder already was).
- When the host leaves a map where players stand, the first stays as holder; a player that holder may not host now
  comes along with the host (as a player still loading the map always did) instead of staying behind.
- When a holder leaves or drops, visitors that the next holder may not host go back to the host's map.
- Private and friends' games (TrustEnforced off): nothing changes, a refusal is only logged ("Would have refused").

## Verdict

Behind TrustEnforced, no new setting: a player is placed under another player's map only when the holder is
trusted by the host's own lists: same team (not the Wanderers), or the holder is an admin or was let in with
"allow". Otherwise the visit is refused at the three places that make a visitor (a request to join, the host leaving
players behind, a holder handing its map over), with a new reason text, logged; off a public server only logged.
