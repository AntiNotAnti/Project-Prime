# Intermission voting and rematches (G5.7)

The server offers at most eight numbered choices. A vote contains only an offered byte ID, match ID, phase revision, and option-set revision; clients cannot request a map path or inject rules. The server resolves the ID through its current admitted rotation. Public servers offer Rematch, Next map, and at most six other rotation entries. Private servers offer Rematch, Next map, and Return to lobby. A one-map rotation does not imply a private session.

`-votepolicy public|private` (or `PRIME_VOTE_POLICY`) selects the policy. Without an override, Duel and `PRIME_PRACTICE=1` use private rematches; other presets use public rotation. This is server session policy, separate from immutable match rules.

## Authority and ordering

Only connected, ready human player slots may vote. Observers and server bots are excluded before dispatch and again by the ballot adapter. Connection identities fence slot reuse; a disconnect clears that identity's vote before resolution. A player has one immutable choice per ballot. Retrying that choice is idempotent; changing it is rejected. Duplicate human identities in separate slots are rejected before state changes.

Option-set `Revision` remains constant during voting, allowing concurrent players to submit from the same advertised options. Separate `UpdateRevision` advances whenever eligibility or counts change. Clients accept only a newer option set or strictly newer publication for the same set, so reordered count snapshots cannot erase a confirmed vote. All revision/tick comparisons use the existing wrap-safe sequence comparison.

Intermission voting closes at the authoritative phase deadline. The server freezes the winner and electorate then, even if report durability or an admin hold delays the actual transition. The highest count wins. Ties consume one draw from a dedicated seeded server RNG; a unique winner consumes none. No votes choose ordinary Next map without consuming RNG. Repeated resolution returns the cached result.

Admin-selected rules override the vote winner. Automatic transition remains subject to the existing admin rotation hold and durable terminal-report gate. Rematch preserves the exact current rules; a map or Next choice uses the server's normal rules resolver. Return to lobby starts a fresh match identity in WaitingForPlayers with an independent `VoteLobbyHold`. It never manufactures an unfinished-match result. This lobby waits indefinitely until its first Rematch/Next vote starts a 300-tick window. Rematch releases only the vote hold; admin and reporting readiness remain independent. Next creates the next admitted match. A held lobby has no terminal result requiring a second report before leaving it.

## Live protocol 8 extension

Reliable event 13 remains ObserverTransition. Event 14 is server-to-client IntermissionBallot; event 15 is client-to-server IntermissionVote. Frozen historical demo codecs retain their historical event bounds.

Ballot: 28-byte header followed by 1–8 fixed 36-byte options (maximum 316 bytes).

| Offset | Type | Meaning |
|---|---|---|
| 0 / 4 / 8 | u32 LE | Match / phase revision / option-set revision, nonzero |
| 12 | u32 LE | Deadline tick |
| 16 / 17 / 18 / 19 | u8 | Phase / count / viewer's acknowledged ID / eligible humans |
| 20 | u8 | Deadline present (0 or 1) |
| 21–23 | zero | Reserved |
| 24 | u32 LE | Publication UpdateRevision, nonzero |
| option +0 / +1 / +2 | u8 | Unique ID 1–8 / choice kind / votes |
| option +3 | zero | Reserved |
| option +4–35 | ASCII | Printable, nonempty label with canonical zero padding |

Vote: exactly 16 bytes; u32 LE match at 0, phase revision at 4, option-set revision at 8; ID at 12; bytes 13–15 zero. Unknown IDs, malformed lengths, reserved bytes, future/wrong revisions, wrong phases, and expired votes are rejected. Ballot vote totals cannot exceed eligible humans.

## Verification

The combined replay/voting filter passed **51/51** tests with AMHE1 content (`/tmp/codex-re-prime-g5-vote-replay-final.log`). Focused tests exercise strict codecs, concurrent votes, duplicate retries, phase/identity/deadline fences, tick wrap, deterministic ties, disconnect/reuse, frozen resolution, and bounded rotation selection. A real UDP loopback test submits event 15 through authenticated server dispatch and delivers a confirmed event 14 before an older publication; the older publication is acknowledged but cannot replace confirmation. A real AMHE1 simulation test holds WaitingForPlayers, resolves a lobby rematch, and proves admin/reporting gates still prevent countdown until independently released.

Keyboard, mouse, gamepad, and Android touch UI integration belongs to the shared client presentation path. Compilation and focused input tests do not establish an actual rendered desktop or Android device session; that remains a separate live validation gate.
