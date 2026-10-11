# Netcode 2.0 — N4 shot proof and authority resource history

N4 builds on merged N3/main `c9da1be3a12d2a07dd1c39b41929131eac5f74e9`.
Branch: `codex/net2-04-shot-proof`; draft PR #454.

**Shadow remains the default. Full enforcement readiness is not established.**
`PRIME_SHOT_PROOF=off` disables observations; `shadow` records disagreements
without changing combat decisions. Experimental `enforce` refuses unverified
accepted discrete emissions and unmatched damage claims. It can acknowledge an
exact existing authority payment without accepting any claimed combat fields.
Continuous and ability simulation retain their existing authority paths.

## Wire and compatibility

Live protocol advances **43 → 45** across this PR. Protocol 44 appended a
32-bit emitted component ID to hit claims (59 → 63 bytes). Protocol 45 adds
33 bytes to each compact live intent: a count and up to 32 newest-first physical
input samples, each identifying weapon, shoot, physical zoom and eligibility.
It does not send a derived charge counter or scope state as input proof.
Maximum live datagram size is **1,197 bytes**, below the 1,200-byte budget.
Clients and servers must update together. Canonical intent/replay widths remain
unchanged; historical claims lacking component identity cannot gain that proof.

## Source and input proof

Authority source state starts at native spawn, before client intents overwrite
live ammo and equipment. A separate bounded journal records actual ammo grants,
resolved weapon pickups, canonical Prime changes, an independent double-damage
timer and successful shot expenditures. It never copies a reported ammo balance.
Pickup operations begin after the last applied owner input; they cannot fund an
earlier shot. Chronological replay enforces caps, native refill-toward-60 rules,
ownership, charge permission, cost, affinity and cooldown. Reordered shots cannot
overdraft before a grant or invalidate an already committed later expenditure.
Checkpoints retain spent ammo and the cooldown crossing the retained boundary.

The journal retains eight lives per slot and 512 operations per life with a
256-source-frame reorder window. Bounded input history retains 512 frames per
life. Missing/conflicting evidence, unsupported state changes or capacity limits
remain explicit unavailable proof. Admission certificates are immutable: later
grants or ring eviction cannot refund or retroactively authorize an earlier shot.

Input validation checks ordinary trigger edges, charge hold/release and physical
scope toggles against native weapon rows and independent resource history.
Charge time begins no earlier than the first carrier actually observed in that
life; backfilled inputs cannot create charge time before a respawn. A native
charge-counter-1 tap remains uncharged. Prime swaps the emission row, while
charge progression and cooldown still use the hunter's ordinary equipped row.
Both presence and absence of the affinity bit must match the authority context.
Scope history survives ring compaction, but missing resets/transitions stay
unavailable rather than trusting reported `ZoomedState`.

Source poses retain separately carried intent aim and accepted body position.
The emitted ray must be finite/unit, agree with that aim, stay within the current
three-unit muzzle envelope and have a clear historical body-to-muzzle path.
The fixed stream clock permits 32 frames of lead for jitter/batching, not
unbounded acceleration. These checks corroborate admitted client movement;
they do not replace later server movement validation. The world ACK is the
opponents' read point, not the shooter's source clock.

## Components, claims and acknowledgements

Every shot uses epoch/match/slot/generation/source-life/shot identity. Root
projectiles get ordinals 1–15; children append a lineage nibble, up to eight
levels. Missing/exhausted lineage becomes zero and cannot acquire proof.
Payment identity also includes victim generation/life, component and damage
kind, so direct and splash can remain distinct while retries cannot double-pay.
The ledger bounds 256 shots, 512 components and 512 payments per slot, retained
for 1,024 authority frames. Evidence overflow is counted and fails validation.

Actual collisions create witnesses only after authority health decreases.
Straight-shot checks use the emitted ray, historical victim body/head rig,
weapon reach and historical walls, doors and force fields. Moving, homing,
splash and ricochet/child impacts require the exact physical witness. A reported
body point or knockback vector is never reused as a shot ray.

Client/server collision positions can produce different knockback even when
both agree on the hit. Strict mode now separates acknowledgement of an already
paid hit from admission of new damage. Exact paid identity returns the immutable
native damage, health, flags and damage sequence; mismatched claimed impulse,
damage, headshot or effects are never applied. Diagnostic evaluation still
rejects those payloads. This works at arrival, after pending settlement and after
legacy ledger expiry/respawn, without a second payment or head correction.
Shadow preserves its existing correction and trade ordering.

The earlier N4 work also fixes the first native ACK missing its shot ID. Missing
IDs use the admitted claim identity, while explicit identities remain intact.
The broader N5 arbitration work is unchanged.

A lost-ACK control also exposed two pre-existing recovery bugs. Claim retries
now measure elapsed time since the previous send instead of requiring an exact
age multiple of a changing RTT interval. A due retry runs at the next send opportunity. Visual prediction still releases
after 60 frames, while the bounded receipt identity remains available for up to
424 frames (six maximum-spaced sends, the maximum authority pending window and
a final round trip). Replies after prediction release retire only the receipt;
they cannot reinstate health, damage or death prediction. The six-send cap and
server admission/arbitration cutoffs are unchanged. The test drain now waits
for client receipt outboxes as well as authority pending claims.
Retired transport connections retain receive/dedup state for the existing
15-second reliable lifetime, allowing a repeated Bye to recover a lost close
ACK. Their 2-second graceful disposal deadline is unchanged. Deterministic
regressions cover skipped send opportunities, changing RTT, dropped close ACKs,
bounded retirement expiry and replacement incarnation fencing.

## Remaining enforcement gates

This is still a shadow rollout. In particular:

- Authority pickup history proves authority permission, but a shared grant
  receipt/source boundary is still needed to establish honest client equivalence
  around pickup transitions. Client-local test-rig refills are deliberately not
  authority grants.
- Recovered shots can lack their original source pose; incomplete charge or
  scope history stays unavailable. Full honest-player rejection rates are not
  established by the scripted controls.
- Omega slot-replacement history and PowerBeam's ramping sustained autofire are
  unavailable. Balanced mode, One in the Chamber, Gun Game and enhanced hunters
  need their own trusted inventory/modifier transitions.
- Continuous, turret, bomb and melee validators retain explicit conservative
  unavailable policies. This PR is not a complete anti-cheat system.

`ClaimRadius` stays **2.0**. Repeated claim-stage observations are not independent
honest-shot distributions. No cutoff was copied from Fruity. Do not enable
strict mode globally or treat these controls as rendered projectile agreement.
N5 has not been started.

## Validation and evidence

Validation is Mac arm64 only; no Windows-specific change required another
Windows run. No Linux/Android testing, CI run or long soak was used.

The prior shadow slice remains fully preserved in
`tools/nettest/baselines/net2-n4-20261010.json` and `artifacts/net2-n4`.
Its final engine was `ac8a1072`, SHA-256
`e9164a8b1ed030e27dabe8cf7020948ff2190a94ee876ed44379d37757cd2d71`.
It passed 241 proof and 204 combat assertions. The former final extreme run had
no hit claims, so it did not establish impaired claim/ACK coverage.

The continuation ledger is `tools/nettest/baselines/net2-n4-completion-20261010.json`;
raw attempts are separate under `artifacts/net2-n4-completion`. The final tested
engine is `42b9fbe0`, SHA-256
`a6863be7146ae3c6138af1f9277520028a118e295b056f603d47a8b498c0267b`.
The ledger maps each focused check to its immutable binary and source revision.

- Release build: 41 existing warnings, zero errors.
- 324 asset-backed shot-proof assertions and 43 physical-input proof assertions;
  204 combat assertions; 84 source-journal assertions; packet/codec/lifecycle,
  architecture and claim-stress checks passed. The final client receipt change
  also passed 3,338,739 health/shot assertions.
- The evidence/drain Python suite has 108 passing checks.
- Final eight-player LAN evidence control (`f5c02520`): 90 shots accepted,
  committed and fired; 17,888 health and 2,371 slow-lane comparisons; eight
  claims with matching nonzero-shot client ACKs and one native payment each.
- Final two-player extreme evidence control (`f5c02520`): 28 shots; 1,196 health
  and 637 slow-lane comparisons; no delivery/event/score failures. It generated
  no claims, so impaired claim coverage is not attributed to this run.
- Final eight-player extreme receipt control (`42b9fbe0`): **PASS**, 100 shots,
  12 claims, 19,080 health and 2,512 slow-lane comparisons; no timeouts, shot gaps
  or semantic gaps. All 12 claims received terminal verdicts; four arrived
  after prediction release, at ages 68–104 frames. Accepted answers matched
  their single payment. Ten reductions were native physical hits and two
  were legacy shadow rescues; this is not strict verified-rescue coverage.
  This used 25 seconds active input plus 7.77 seconds of
  bounded drain, at 400 ±80 ms RTT, 5% loss, 3% reorder and 1% duplication.
- New regression tests against previous engines fail at the intended assertions
  for missed retries, lost close ACKs and prematurely expired receipts. The same
  tests pass with their corresponding fixes; these negative controls are
  sensitivity evidence, not passing gameplay runs.

All failed attempts remain preserved. The first impaired duel exposed an audit
identity error: cached slow scoreboard fields were attributed to the fast health
frame. Schema 2 records exact slow-lane epoch/match/revision/payload assignments,
compares fast health at its own frame, and separately requires terminal score
convergence. Bootstrap helper validation is excluded; a cache reset has its own
context. Missing, forged, same-context duplicate or stale slow evidence fails.
Schema 1 and its original failed result remain unchanged.

Two subsequent eight-player impaired runs exposed the actual recovery defects:
one missed claim retry and lost disconnect ACK, then a correctly retried answer
arriving after the old shared prediction/receipt deadline. Their FAIL results,
exact timelines, fixes, old-engine negative controls and final positive control
are retained in the ledger. Build/test invocation errors and their corrections
are retained too. No failed attempt was overwritten or repeated unchanged to
obtain a passing result.

These are shadow correctness controls, not a before/after performance benchmark
or an honest-player rejection calibration. Scripted loadouts/ammo can conflict
with the authority journal; source pose, geometry and impulse disagreements are
still reported even when a previously paid hit can be acknowledged safely.

Run `nettest --shot-proof-scene PROFILE 'MP1 SANCTORUS'`, `--shot-inputs` and
`--shot-source-journal` for focused checks. `run-net2.py --shot-proof shadow`
clears inherited experiment flags and supports a bounded quiescent drain.
`net4_evidence.py CAMPAIGN` audits proof instrumentation independently of gameplay
parity. An instrumentation PASS does not imply enforcement readiness.

Rollback: `PRIME_SHOT_PROOF=off` disables the experimental observer/enforcement;
it does not downgrade protocol 45. A binary rollback must keep client/server
protocol versions together. Preserve the component identity and ACK fixes when
selectively revising source validation.
