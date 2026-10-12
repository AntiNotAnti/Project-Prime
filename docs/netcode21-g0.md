# Netcode 2.1 G0: evidence contract and implementation audit

Date: 2026-10-11. Gameplay source: `ed51bc90abb9d29b019620ce70146c8833d590d1`,
verified against freshly fetched `origin/main`. The supplied plan was written
against `ec9d63f7afa7ddd1220cf9441f619c3576294014`. This slice changes evidence
tooling only. **Netcode 2.1 gameplay is not implemented by G0.**

## Contract and baseline drift

- Current live protocol is **51**, not the plan's 50. G0 does not change it.
  `run-net2.py` now reads the checkout's protocol instead of silently expecting
  50. Historical binaries require explicit `--protocol`; their adapters remain.
- The frozen Release game and nettest engine SHA-256 is
  `3172a329dc843f636836d982482b837c16a7a9184cea041a369411cc1a72049b`.
  Its informational version names the source above. Build SDK is 10.0.401;
  build completed with 33 warnings and zero errors on macOS arm64.
- Protocol18's current worst-case fast datagram is **1,172 bytes**. The live
  budget remains 1,200 including the 24-byte header; the separate 1,472-byte
  rare-control limit is not a live packet allowance.
- PR #467 already corrected the native Sylux muzzle obstruction and Android
  release-between-fixed-steps defect. Do not reapply those changes for M7/M1.
- Preserve the existing OpenGL/GLES renderer and launcher integration; G0 adds
  no rendering backend, UI replacement, gameplay flag or wire layout.

The machine-readable record is
[`net21-g0-20261011.json`](../tools/nettest/baselines/net21-g0-20261011.json).
Raw logs, private profiles and recordings remain under ignored
`artifacts/net21-g0/`; extracted game assets are not committed. A baseline result
is evidence for its recorded source and workload, not for a later gameplay slice.

## Fresh execution results

| Check | Result and scope |
| --- | --- |
| Release nettest/game build | PASS; 33 warnings, zero errors |
| 13 network/asset fixture routes | PASS, including source journal 84, shot proof 324, native form 59, bomb scene 70 and Sylux muzzle 27 assertions |
| Input/replay/mechanics fixtures | PASS: pointer 1,788, replay format 3,013, mechanics 406; Android source-linked touch tapes pass |
| Source guards / generator | PASS: no game assets, shipped map sources, tracked-source OpenGL/GLES gate, packet generator |
| Python campaign/evidence suite | 359 tests: 358 passed, one pre-existing optional local-campaign test skipped |
| 2-client LAN tour, 25 s action | Legacy PASS; **zero** component payments, so combat coverage NOT_RUN |
| 4-client LAN tour, 25 s action | Legacy PASS; **zero** component payments, so combat coverage NOT_RUN |
| 2-client LAN duel, 30 s action | Legacy PASS; two authoritative component payments; full G0 INCOMPLETE |
| 8-client LAN tour, 30 s action | FAIL: final 22 peer-3 shots unaccepted/uncommitted, bounded drain failed, all eight clients exited 1; 15 component payments were observed |
| New protocol-51 recording seek/hash check | FAIL at frame 901: gameplay hashes equal, presentation hashes differ; no successful hash file emitted |
| Full main CI | PASS on `ed51bc90`; all 25 build jobs and the separate semantic workflow completed successfully. G0 draft PR checks are separate and were queued at capture. |

The eight-client failure is more specific than a slow network tail: all 22
missing events are slot 3/life 2 Shock Coil `ContinuousTick` events with
`ContinuousPhase=0`. IDs 147–158, 206–210 and 244–248 correspond to source frames
1581–1592, 1652–1656 and 1696–1700. Adjacent events were accepted. No queue drop
was observed, and the owner remained alive in that life.

`NetFireEvents.cs:487` rejects zero or mismatched phases; the compact decoder
therefore refuses their carriers. `NetHooks.cs:579` resets the current continuous
tick each input step; target capture restores it (`NetContinuousTargeting.cs:132`),
but commit overwrites the authored phase from that field
(`NetFireEvents.cs:676`). The evidence supports a baseline event-authoring defect
candidate, not increasing the drain timeout. A follow-up must reproduce the
native successful-spawn branch without phase capture before changing gameplay.

All UDP campaigns used seed 8128, `PRIME_SHOT_PROOF=shadow`, native extracted
AMHE1 assets, isolated profiles and a 20-second maximum drain. The eight-client
roster is Samus, Kanden, Trace, Sylux, Noxus, Spire, Weavel, Samus. These are
scripted HitRig clients, not G1 AI clients. Hosting, bots, spectators, pickups,
grants, freeze, physical touch, impaired final gameplay, long soak and platform
device acceptance are not established by these runs.

The first eight-client staging attempt exhausted disk before producing a campaign.
That incomplete attempt is retained separately. Only redundant `bin/` copies
created by this task were removed after recording their hashes; the original
frozen binary, raw JSON, logs and recordings remain. The subsequent eight-client
gameplay failure is a separate result, not the disk incident.

N14's final replay results are historical source `01292af8597a2bb1b88438390c4c9f87d953f2ef`,
protocol 50. Their committed checksums are retained, but the old local artifacts
are unavailable for rehashing. Do not use those passes to override the fresh
protocol-51 replay failure. N2's historical 88.02% compact relay-byte reduction
is a category-specific reference, not a present total UDP throughput comparison.

## Evidence tooling

`tools/nettest/net21_evidence.py` supplies a per-slice manifest and a fail-closed
audit. It records source/binary identity, configuration, seed, effective faults,
expected and observed counts, counting units, checks, provenance and exclusions.
Offered/accepted claims, hits, pickups, grants, freeze start/end, touch sample
pairs, alternate contacts, death, packet loss, queue occupancy and replay facts
are separate denominators. Missing instrumentation stays `null`/`NOT_RUN`.
Positive but insufficient coverage stays `INCOMPLETE`; an explicit failure wins.
Zero required events cannot pass. Measured zero loss/queue occupancy can pass a
declared zero minimum. An exclusion cannot waive a missing required metric.

The audit validates evidence structure and declared coverage; it cannot certify
the truth of operator-provided measurements. Compare against independently
recorded source/binary hashes and retain the raw evidence. Historical results
cannot be promoted to fresh reproduction by reusing their PASS labels.

```sh
python3 tools/nettest/net21_evidence.py report.json \
  --source-sha SOURCE_SHA --binary-sha256 ENGINE_SHA256
python3 tools/nettest/net21_evidence.py --campaign CAMPAIGN_DIR
python3 -m unittest discover -s tools/nettest -p 'test_*.py'
```

The build workflow now runs the network Python contracts as well as generated
packet checks. One pre-existing Python test needs a developer's immutable local
campaign and skips when absent; this is recorded separately, never device or
live acceptance.

Named fault profiles retain their original meanings. Independent overrides make
the proposed 150/250/400 ms and 5/10/25% campaigns expressible without renaming an
old profile or altering control runs:

```sh
python3 tools/nettest/run-net2.py --build BUILD_DIR --paths PROFILE/paths.txt \
  --source SOURCE_SHA --out NEW_OUTPUT_DIR --players 8 --seconds 90 \
  --profile lan --rtt-ms 250 --jitter-ms 40 --loss-percent 10 \
  --reorder-percent 3 --duplicate-percent 1 --seed 8128 --server-operator
```

These are existing client-side NetLag faults in both directions. Jitter adds
nonnegative hold in each direction; it is **not** a symmetric plus/minus RTT
distribution. Effective values and seed derivation are written to the manifest.
Nonfinite durations/rates, invalid protocol bytes and overflowing client seeds
are refused before starting processes. Burst loss, generic reconnect/rebind,
authority restart and old-life/nonfinite packet injection remain unimplemented
campaign controls; do not label those faults exercised by random loss.

## A1: authority-owned grants

1. Add a bounded ledger keyed by authority epoch, match, monotonic spawn
   generation, slot generation/life, grant sequence and source/expiry frames.
   Double Damage, Cloak and Death Alt are separate types. The ledger must not
   depend on whether N4 shot proof is Off, Shadow or Enforce.
2. Preserve the native 1,800-simulation-frame effects at
   `PlayerProcess.cs:1497` and offline pickup behavior. Currently
   `NetHealthSync.OwnsPickup` restricts health only (`NetHealthSync.cs:24`), while
   non-health pickups immediately set timers and call `item.OnPickedUp(this)`
   (`PlayerProcess.cs:1592`). A1 establishes ownership before A3 changes UX.
3. Add a monotonic spawn identity instead of treating `_spawnCount` as one:
   `ItemSpawnEntity.cs:186` decrements that count on deactivation. Reserve before
   deferred item removal so two contacts cannot consume the same incarnation.
   Respect actual moving-parent item positions (`ItemSpawnEntity.cs:99,199`).
4. Retire active grants on death, disconnect, replacement and room/match reset;
   retain bounded old-life history for delayed admitted shots. Integrate with
   `NetPlayerLifecycle.cs:78` and session reset paths, rather than another player
   identity system.
5. Preserve N4 ammo expenditure (`NetShotSourceJournal.cs:195`). Its Double
   journal operation (`:211`) is not a pickup admission, is disabled when proof
   is off (`:29`), and funds the next input source frame (`:55`). Later integration
   must remove the owner bit as authorization: currently
   `PlayerEntityNetAim.cs:1814` holds or clears puppet Double timers from it.

Tests must cover renewal, two-player races, expiry, frame rollover, old-life
claims, death before delayed delivery, slot reuse, modes and eight clients.

## A2: verified pickup claims

1. Add bounded claim/verdict contracts and an exact terminal-result cache.
   Bind the player identity to the authenticated peer; never trust an arbitrary
   slot in the payload. The enum currently uses IDs through 71 with voice IDs
   24/25 reserved (`NetProtocol.cs:24`). Re-audit before allocating IDs or bumping
   the actual protocol; 72/73 are only candidates.
2. Check epoch/match/life, alive state, finite coordinates, incarnation and
   historical item availability. Reuse native biped/alt contact geometry at
   `PlayerProcess.cs:1405`; a generic radius does not reproduce both.
3. Use life-aware historical player pose (`NetUnlagged.TryHistoricalPose`),
   plus **new** bounded moving-item position/availability history. Existing
   history is player/alt/turret geometry, not item history. Current history is
   128 frames, rewind default 45/ceiling 120; choose and test a pickup policy.
4. Historical proximity alone does not establish independent movement authority:
   N12 movement shadow remains advisory and observes owner poses. Document that
   residual trust rather than promising anti-teleport guarantees.
5. Retransmit cached terminal verdicts through existing reliability. Duplicate
   claims cannot consume a newer item incarnation or issue a second grant.
   Cover hosted/dedicated dispatch, capacity, expiry and unavailable history.

A3 must add a bounded grant delta/bootstrap lane and prediction reconciliation,
not six extra bytes per player on every fast snapshot. Replay already records
spawners (`ReplayAuthorityWorld.cs:92`) and native timers
(`ReplayWorldSchemas.cs:242`), but lacks admitted grant identities/history.
Checkpoint version 8 and historical protocol 41/50 adapters must retain absent
field semantics. A4 then replaces `NetHitClaims.MaxDamageFor`'s broad maximum
damage ×5+1 cap (`NetHitClaims.cs:2025`) using source-specific components and
grant-at-fire proof, preserving ShotKey, CombatAck and exact payments.

## Freeze dependency

Current `PlayerState.FlagFrozen` (`NetProtocol.cs:1874`) drives boolean
`ModSetFrozen` (`PlayerReplicationBridge.cs:845`), which chooses local 150/30
frame timers and holds them at two while the flag persists
(`PlayerEntityNetAim.cs:2058`). A5 needs authoritative remaining frames and one
transition per freeze, coordinated with A3's wire/replay changes. Preserve native
SFX, ice visuals and re-freeze rules; do not simply keep resetting a local timer.

## M0/M1: native touch producer before consumer changes

M0 should extend existing controls and reference tests, not introduce gameplay:

- `MouseFlick.cs:65` uses five 60 Hz samples, 120 degrees, 3-degree rest rearm,
  0.86 coherence and 21-frame cooldown. That is ModernFlick, not the proposed DS
  four-delta sum. Preserve its defaults and sensitivity range 0.25–4.
- `TouchSamplingChecks.cs:6` already tests 30 Hz pointer tapes / 60 Hz simulation
  at 30/60/120/144 presentation. Extend to 240 and integrated roll/boost before
  claiming native parity; existing conservation checks do not prove it.
- Add modern control tapes for Zoom-held flick, dedicated Boost suppression,
  backward/diagonal motion, momentum projection, capture and scene reset.
  Native boost currently forces full charge and locks aim for 18 frames on swipe
  (`PlayerInput.cs:2256`); ClassicTouch needs a distinct path.

M1 should add pure per-player native clock/state/sample types, including four
signed bounded deltas and sum, contact state/duration, sequence, sibling and
generation/life. Define overflow, release and sequence wrap. Accumulate input
at simulation cadence, produce one logical decision per two simulation steps,
and allocate the two roll half-impulses without consuming twice.

The Android adapter must classify the aim surface independently of accumulated
camera delta (`TouchControls.cs:1137`, `GameView.cs:1602`). Dialog and weapon-wheel
capture (`GameView.cs:1729,1764`), focus/suspend, hunter/life and profile changes
must reset eligibility. Exclude stick/buttons/extra fingers. Desktop relative
mouse normalization is profile-specific; never globally change camera aim.

Native boost consumption belongs to M2. M3 must coordinate live compact intents,
canonical replay and checkpoints: `CompactIntentCodec.TryRead` requires exact
length; live fire capacity is 14 while canonical capacity is 16. Existing
Roll/Zoom bits and swipe checkpoint fields do not prove a physical touch contact.
Legacy recordings must never synthesize one.

Required tapes include 600/700/1350 DS units per second, four-delta sums,
reversal, first/continued contact, saturation, one/two-finger capture, reset,
life/sequence fencing and 60/120/144/240 presentation invariance. Synthetic
tapes are separate from physical Android and real DS traces.

## Advancement and rollback

G0 remains a gate until full baseline CI, required combat coverage and the
missing workload/fault dimensions are resolved. The plan's G1 AI clients must
use actual independently connected gameplay AI; server bots and scripted HitRig
clients are not substitutes. No A1/A2/M1 gameplay change is included here.

Rollback removes the evidence-tool/workflow changes together. No live protocol,
save, replay or server/client artifact migration is needed for G0. Later wire
slices must deploy and roll back matching server/client artifacts together.
