# Netcode 2.0 — N4 shot-proof shadow admission

N4 starts at merged N3/main `c9da1be3a12d2a07dd1c39b41929131eac5f74e9`.
Branch: `codex/net2-04-shot-proof`. Live protocol advances **43 → 44**;
clients and server must update together. Hit claims append a 32-bit emitted
component ID (59 → 63 bytes). Historical 59-byte decoding retains the original
shot ID and supplies component zero; it is not independent component proof.

**Shadow admission is the default. Enforcement readiness is not established.**
`PRIME_SHOT_PROOF=off` disables observations; `shadow` records disagreements
without changing health, emission, headshot correction or legacy matching.
`enforce` is an experimental conservative control: unverified discrete emission
is refused, unmatched claims cannot rescue damage without proof, and a verified
physical witness acknowledges an existing hit rather than paying it again.
Continuous/ability simulation retains its existing authority path; this switch
is not a complete anti-cheat system. Do not enable it as the normal server mode
until the remaining source-history and honest-player gates below are met.

## Proof and identity

The observer is bounded per slot: 256 admitted shots, 512 emitted components,
512 component payments, eight source lives and 128 source poses. Retention is
1,024 authority frames. Saturation refuses new evidence explicitly rather than
overwriting a live payment. Campaign capture separately bounds claim and
emission rows to 16,384 each and counts overflow. Match reset clears ownership;
process-lifetime diagnostic counters survive teardown.

Every shot uses the existing epoch/match/slot/generation/source-life/shot key.
Actual root projectiles receive ordinals 1–15. Spawned children append a lineage
nibble; eight levels are representable. Exhausted or missing lineage becomes
zero and cannot acquire proof. An in-place wall bounce keeps the same component;
a newly spawned ricochet child receives a new component. Direct, splash and
turret damage kinds are separate within the full shot/victim-life/component key.
A new claim ID does not create new payment eligibility.

Source facts come from the authority's native spawn loadout before client
inventory is applied. Ownership, charge permission, ammo cost, minimum cooldown
and captured affinity/modifier consistency are checked against those facts.
A tap can retain charge counter 1 without entering a native charged tier; this
remains an ordinary uncharged shot, including without a charge upgrade.
Both source-time orderings are checked: an older retained event arriving late
cannot bypass a newer admitted shot's cooldown or spend its ammo again. Once
retained expenditure history is incomplete, the result becomes unavailable.
Authority-observed pickup changes invalidate the source model rather than
silently treating client-reported inventory as authority evidence.

Source-frame poses retain the independently carried intent aim and accepted
body position. The original event ray must be finite/unit, agree with that aim,
stay within the current three-unit muzzle envelope and have a clear historical
body-to-muzzle path. A fixed stream clock anchor permits 32 frames of lead for
jitter/batching; it cannot follow an ever-increasing client offset. The claim
ACK remains an opponents' world-read point, not the shooter's source clock.
These are corroboration checks against admitted client movement; they do not
replace the later server movement work or establish a calibrated muzzle cutoff.

Actual authority collisions record a witness only after health or turret
health decreased. Denied/protected collisions do not produce one. Claims must
match component weapon, damage ceiling, head classification, afflictions and
quantized knockback. A physical hit cannot legitimize an invalid source or
forged muzzle. Strict old-life acknowledgements require exact physical
component/weapon/kind identity, preserve the original cached identity and never
rescue damage or perform a new head correction against a present-life body.
Their immutable physical outcome survives the legacy ledger reset on shooter
respawn, retaining original shot ID, damage, health, flags and damage sequence.

The impaired control also exposed an existing first-ACK identity bug: a native
physical outcome omitted the claim's shot ID, causing the owner's exact-ID check
to discard it. The terminal answer now stamps the admitted claim's shot ID on
both first and cached results while preserving native damage, health and sequence.
Retry timing and the broader N5 acknowledgement policy are unchanged.

Straight-shot inspection uses the emitted ray, historical victim rig/head band,
weapon reach and historical walls, doors and force fields. Claim `HitPoint` is
a reported victim body origin; `Direction` is knockback. Neither is repurposed
as a shot ray. Even a clear historical ray does not certify damage modifiers:
a missing physical result remains unavailable. Moving projectiles, homing,
splash and ricochet/child claims need their own exact physical witness. Server
component families take precedence over client flags; continuous, turret and
bomb/melee admission have explicit unavailable reasons.

## Rollout limits

The trusted model deliberately does not infer independent charge-hold/zoom
history, timed Prime/double-damage state, ordered pickup grants or mode-specific
inventory transitions. Balanced mode, One in the Chamber, Gun Game and enhanced
hunters remain unavailable. Charged/scoped source input remains unavailable even
when the authority projectile really collides. Missing source poses, history,
component identity or damage/effect results also remain unavailable. The minimum
cadence check is not a reconstruction of all native autofire state.

The normal shadow path therefore preserves current combat, including legacy
head/body correction and trade ordering. Experimental strict mode can suppress
legal recovered shots and legacy correction when independent evidence is absent.
No global strict-mode gameplay-parity or anti-exploit completion claim is made.
The plan's enforcement gate remains open until honest legal variants can be
admitted with acceptable rejection rates; N5 is not implicitly approved.

`ClaimRadius` stays **2.0**. Captured radius buckets count observations at multiple
claim stages, not independent honest shots. The short scripted UDP controls
change test loadouts and do not establish an honest-player false-reject cutoff.
No value was copied from Fruity's independent implementation.

The final eight-player scripted control recorded ten unique physical-hit claims.
All agreed on damage, head classification and effect bits; eight differed in
knockback by more than the experimental 0.001 vector tolerance. Differences
exceeded packet rounding alone. Four had otherwise verified source proof. This
is a concrete strict-mode rejection gap, not grounds to loosen the tolerance
without independent physical validation. Shadow preserves the authority result.

## Validation and evidence

The durable ledger is `tools/nettest/baselines/net2-n4-20261010.json`.
Raw immutable attempts are under `artifacts/net2-n4`; the final ledger records
source and binary hashes, all failures and the evidence archive identity.
Validation uses Mac arm64. Windows is not required for this platform-independent
slice. No Linux/Android testing, CI run or long soak is part of this change.

The asset-backed scene exercises real accepted emission and native collision,
not injected collision witnesses. It covers forged shot IDs, weapon ownership,
weapon mismatch, ammo/rate, wrong head/effects/knockback, invalid aim/ray, stale
ACK, source clock, victim life, protected targets, walls, component retries,
lineage limits, real direct/splash/affinity/ricochet impacts, old-life exact
acknowledgements and off/shadow/strict controls. Charged homing has an explicit
unavailable source-input expectation; it is not reported as fully verified.

Run the proof scene with `nettest --shot-proof-scene PROFILE 'MP1 SANCTORUS'`.
`tools/nettest/run-net2.py --shot-proof shadow` records the explicit mode and
clears inherited game experiment flags. `tools/nettest/net4_evidence.py CAMPAIGN`
checks row/counter completeness and reports source, geometry and stage summaries.
Its instrumentation PASS is deliberately separate from enforcement readiness.

The original short impaired run remains a failure: semantic event 38 was still
reliable-pending when the peer exported and closed, while event 39 was buffered.
The one-second comparison tail did not cover retransmission backoff. The final
headless controls use an opt-in bounded drain, with scripts quiesced while real
simulation and transport continue. Every offered shot must reach authority;
flights, bombs and claims must settle; and every peer must present the saved
semantic/award frontier for the full claim grace. Reliable queues must be healthy
throughout and empty at authority preparation and client release. Periodic
SessionState/Roster publications continue, so continuous queue emptiness is not
a valid stability condition. A server-first release handshake records the
frontier before clients disconnect.
Offline checks compare the entire baseline-to-frontier payload interval without
relaxing existing tail guards. Timeout is a failure. Default runner shutdown and
ordinary gameplay are unchanged; rendered/soak drain combinations are unsupported.

Primary reference reviews: [Fruity #109](https://github.com/liveteklol/Fruity-Prime/pull/109)
and [Fruity #112](https://github.com/liveteklol/Fruity-Prime/pull/112). No retired
Project Prime PRs #410–#423 were merged or treated as dependencies.
