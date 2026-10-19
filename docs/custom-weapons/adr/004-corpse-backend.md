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
