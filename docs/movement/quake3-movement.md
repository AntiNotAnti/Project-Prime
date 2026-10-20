# Quake III movement

Choose **Movement: Quake III** in normal practice Match Rules or in the host's
multiplayer Match Rules. The movement row cycles **Classic → Momentum → Quake
III → Classic**. Apply the draft to save practice rules or ask the lobby server
to accept them; Cancel leaves the previous rules intact. Guests see the accepted
selection and cannot change it. The server freezes the profile when the match
starts. It also applies to authority-owned bots.

**Classic remains the default.** Old/missing settings still use `Native` (the
Classic configuration name). Existing saved Momentum selections remain Momentum
v1; they do not silently change to Quake III. Campaign always uses Classic and
retains the saved practice preference for later. Momentum and Quake III require
Native Hunter abilities and cannot combine with Balanced or Enhanced Hunters.

For a standalone server, use `-server -movement Quake3`. All players and the
server need the new build: live protocol 60 refuses protocol 59 and older peers.
Return to Classic before the next match to roll back the movement choice.

## Technique and fixed profile

Run forward and jump. Hold forward plus a strafe direction while smoothly
turning toward that direction; alternate sides across subsequent jumps. This
profile permits air-strafe speed building from running speed and removes the
Momentum v1 1.5× total-speed retention ceiling. A fixed-facing diagonal still
reaches its projection limit; turning technique creates further acceleration.
Ground contact applies friction. Release and repress jump for native legal jumps.
No auto-hop, buffered jump, grace timer, extra jump or separate gravity system
is added. Alternate forms, Hunter abilities, collision, pads, platforms and
external impulses retain their native ownership and exclusions.

The immutable Quake III profile v1 is independent of Momentum v1 and of private
JSON experiments. It runs once per eligible 60 Hz simulation tick:

| Coefficient | Value |
| --- | --- |
| Ground speed | 983/4096 × 30 = 7.199707 world units/second |
| Ground acceleration | 10 per second |
| Ground friction | 6 per second |
| Stop speed | Ground speed × 100/320 |
| Air acceleration | 1 per second |
| Air wish projection limit | Full ground speed |
| Artificial total-speed cap | None |

This targets Quake III / Quake Live-style strafe-jumping while preserving Hunter
jump timing and the game's native collision/gravity architecture. It is not an
exact Quake Live physics port. High-speed traversal and human feel still need
playtesting; existing maps were authored around original movement speeds.
The [reference attribution](reference-attribution.md) records reviewed behavior
and parameter facts. Existing independent C# math is reused; no third-party
movement code or new dependency is imported.

## Network and recordings

Quake III is a distinct movement model (`Quake3`, enum 2), profile version 1.
Protocol 60 assigns it modifier bit 16 and widens the MatchState rule field to
32 bits. Classic remains enum 0 and Momentum remains enum 1 with the original
bit 15 and immutable profile v1. Session and MatchState must agree; unknown
bits, conflicting movement bits, stale/unauthorized edits and mid-match changes
are refused. Human ownership, authority-owned bots and advisory-only movement
validation retain their existing architecture.

Normal Quake III matches can be recorded. Labels, replay checkpoints, seeking
and replay-library caches retain the distinct recorded model. Historical
protocol 56–59 checkpoints keep their actual 105-byte match field, while 60 uses
107 bytes. Historical packets cannot introduce new Quake III bits; pre-59 data
cannot introduce Momentum either. Conversion occurs only when reading old
recordings. The new cache version resubmits older presentation entries for scan.

The CLI `-momentumq3dev` and its optional JSON file remain a separate offline
experiment. They never override these fixed menu/network rules and remain
unrecordable. Use [private tuning](quake3-practice-tuning.md) to experiment; any
future shared coefficient change needs a separately identified compatible profile.

Exact test scopes, source/binary identities, hosted-CI status and remaining
limits are in the [integration results](quake3-menu-results.md).

Performance optimization remains deferred under the accepted
[v1 tradeoff](pr09h-results.md); this integration makes no new timing-budget claim.
