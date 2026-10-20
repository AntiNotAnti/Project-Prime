# CW-00 ADR D: physics pilot stays outside gameplay

Status: experimental decision deferred until CW-09 measurements.

Existing DeathPresentationRuntime provides lifecycle and procedural whole-model fall/fade,
not articulated physics. Retain it as mandatory fallback. Define an independently owned render-only
corpse palette and optional backend only after the headshot fact path is accepted.

BEPU v2 is a candidate, not an accepted dependency. CW-09 must inspect current upstream license,
package/TFM, Android AOT/SIMD, native dependencies and actual target benchmarks before selection.
No physics dependency or unsupported compatibility claim is introduced in CW-00.

Corpse collision may read validated static world geometry only. Never register corpses in gameplay
queries or lend gameplay ModelInstance/node state. Fixed-step, capped bodies/segments/contacts,
finite impulses, sleeping, bounded lifetime, disposal and headless zero-allocation are required.
Compare 0/1/2/4/8 corpses and 60/120/240 Hz. Missing physical Android evidence keeps physics disabled.
Approximate replay recreation is labeled; exact poses require a separately versioned recording.
Rollback disposes the corpse pool and resumes existing cosmetic rendering without combat changes.

## CW-09 decision (2026-10-20)

Accepted as a non-shipping technical pilot only. Exact BEPU2.5.0-beta.33 pins and
notices live in a new leaf assembly which the game/server do not reference.
The prototype demonstrates fifteen constrained primitive parts, private static
collision, accepted-direction inputs, pure interpolation and independent pose
ownership. The standalone procedural comparison does not replace CW-08.

Desktop45-workload/128-world evidence supports continued research; desktop
outliers and missing full-game/physical Android thermal and graphics evidence do
not support enabling physics. The default remains the accepted native/procedural
presentation. Android AOT build or emulator CPU success alone cannot remove the
physical-device gate. See CW-09 evidence for exact build/run status. CW-10 proceeds
with safe fallback/profile work; unsupported Hunters, moving surfaces and missing
static mesh/renderer qualification must select fallback rather than approximate
gameplay collision. Exact physical replay poses remain unsupported.
