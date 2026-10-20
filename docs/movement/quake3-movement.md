# Quake III movement

Choose **Movement: Quake III** in normal practice Match Rules or in the host's
multiplayer Match Rules. The movement row cycles **Classic → Momentum → Quake
III → Traversal → Classic**. Apply the draft to save practice rules or ask the lobby server
to accept them; Cancel leaves the previous rules intact. Guests see the accepted
selection and cannot change it. The server freezes the profile when the match
starts. It also applies to authority-owned bots.

**Classic remains the default.** Old/missing settings still use `Native` (the
Classic configuration name). Existing saved Momentum selections remain Momentum
v1; they do not silently change to Quake III. Campaign always uses Classic and
retains the saved practice preference for later. Momentum and Quake III require
Native Hunter abilities and cannot combine with Balanced or Enhanced Hunters.

For a standalone server, use `-server -movement Quake3`. All players and the
server need the new build: live protocol 68 requires an exact protocol match.
Return to Classic before the next match to roll back the movement choice.

## Technique and fixed profile

Run forward and jump. Hold forward plus a strafe direction while smoothly
turning toward that direction; alternate sides across subsequent jumps. This
profile permits air-strafe speed building from running speed and removes the
Momentum v1 1.5× total-speed retention ceiling. A fixed-facing diagonal still
reaches its projection limit; turning technique creates further acceleration.
Hold Jump to chain legal hops on landing. Each held hop requires a new physical
floor contact; there is no airborne jump buffer or added grace period. Release
Jump to land and apply ground friction. Fresh presses still invoke native Hunter
jump abilities, including Space Jump; holding never spends an airborne jump.
Alternate forms, Hunter abilities, collision, pads and platforms retain their
native ownership and exclusions.

The immutable Quake III profile v2 is independent of Momentum v1 and of private
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

The horizontal coefficients are unchanged from Quake III profile v1. Profile v2
adds the following techniques through the existing native input/combat seams:

- **Circle jump:** build a curved approach with forward plus strafe, turn smoothly
  toward the strafe side, then jump. Acceleration follows the turning wish
  direction; this is earned movement, with no launch-speed bonus.
- **Strafe-jump chain:** hold Jump with forward plus strafe, turn to keep the wish
  direction ahead of current travel, and alternate sides across hops. A legal
  landing hop bypasses ground friction; airborne acceleration retains existing
  perpendicular velocity and has no artificial total-speed cap.
- **Rocket jump:** use Missiles, aim at nearby floor, jump and fire. A valid
  self-missile explosion adds a full radial impulse to existing velocity,
  including upward velocity. An angled floor/wall blast can push sideways;
  subsequent legal blasts add further impulse. Self-damage and lethal self-hits
  remain enabled. Spawn protection, blast radius and line of sight still apply.

Rocket impulse uses finalized native splash damage multiplied by 0.025 native
velocity units per point and linear distance attenuation, bounded to 1.2 per
explosion. Direction points away from the blast toward the body plus 0.5 world
units of height. The existing conversion is 30 world units/second per native
velocity unit. Health damage retains its weapon curve and modifiers; the
physical attenuation is separate. These are Project Prime tuning values.
Only admitted Quake III ordinary eligible bipeds use this self-missile policy;
Classic, Momentum, alternate forms and native special-state impulse paths keep
their existing behavior. No extra ammo, invulnerability or speed is granted.

This targets Quake III / Quake Live-style strafe-jumping while preserving Hunter
jump timing and the game's native collision/gravity architecture. It is not an
exact Quake Live physics port. High-speed traversal and human feel still need
playtesting; existing maps were authored around original movement speeds.
The [reference attribution](reference-attribution.md) records reviewed behavior
and parameter facts. Existing independent C# math is reused; no third-party
movement code or new dependency is imported.

## Network and recordings

Quake III is a distinct movement model (`Quake3`, enum 2), currently profile version 2.
Protocol 60 assigned it modifier bit 16 and widened the MatchState rule field to
32 bits. Protocol 62 selects profile v2's held hops and missile impulses without
changing packet sizes or movement bits. All live participants must update. Classic remains enum 0 and Momentum remains enum 1 with the original
bit 15 and immutable profile v1. Session and MatchState must agree; unknown
bits, conflicting movement bits, stale/unauthorized edits and mid-match changes
are refused. Human ownership, authority-owned bots and advisory-only movement
validation retain their existing architecture.

Normal Quake III matches can be recorded. Labels, replay checkpoints, seeking
and replay-library caches retain the distinct recorded model. Historical
protocol 56–59 checkpoints keep their actual 105-byte match field, while 60 uses
107 bytes, retained by protocols 61/62. Recorded Quake III protocols 60/61 retain
profile version 1, while 62 identifies version 2. Passive replay applies recorded
poses and damage presentation without rerunning current jumps or blast physics.
No new persistent jump state or checkpoint appendix is needed. Historical packets cannot introduce new Quake III bits; pre-59 data
cannot introduce Momentum either. Conversion occurs only when reading old
recordings. The new cache version resubmits older presentation entries for scan.

The CLI `-momentumq3dev` and its optional JSON file remain a separate offline
experiment. They never override these fixed menu/network rules and remain
unrecordable. Use [private tuning](quake3-practice-tuning.md) to experiment; any
future shared coefficient change needs a separately identified compatible profile.

The original menu integration is documented in [integration results](quake3-menu-results.md).
Profile v2 techniques and verification are tracked in [advanced movement results](quake3-advanced-results.md).

Performance optimization remains deferred under the accepted
[v1 tradeoff](pr09h-results.md); this integration makes no new timing-budget claim.
