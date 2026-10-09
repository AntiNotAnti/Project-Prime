# PR109-inspired combat presentation

Base: `0127fab5ffd411529a5c69a40e5467e140f50a60`, protocol 44.
Upstream Fruity PR109: `1756af902f960c82edde28bd31e6895f8bcb8198`, open and conflicting when checked on 2026-10-08 (America/Chicago).

**Delivery status: implementation and local validation delivered behind independent gates;
release acceptance remains partial.** Eleven stacked draft PRs implement diagnostics,
canonical transport, presentation, proof-driven claims, optional kill tickets, bounded
server optimization, native fixtures and rendered acceptance. CI review is skipped by
user request. No PR is merged and no feature is promoted to an unproven default.

The chain retains dedicated-server combat authority, existing movement ownership,
shot/lifecycle identity and replay v1 facts. Missing supported-source identities use
truthful fallback cues or retain existing behavior. See the source-backed status table
and remaining acceptance gates below; this is not a claim that every proposed target passed.

## Coverage audit at the baseline

`PlayerEntity.TakeDamage` creates a fact only after a positive health/turret delta,
with a nonzero damage event, nonzero shot, valid weapon (PowerBeam through Omega),
and current authority/match identity. It does not publish a fact for every kind of damage.

| Source | Baseline evidence and limits |
| --- | --- |
| Normal/charged beams, Imperialist body/head, Omega | Native direct collision calls `TakePlayerDamageAt` with the collision point and sets `EnhancedDirectHit` around the call. Despite its name this flag DOES cover normal direct beams. |
| Splash | `TakePlayerDamageAt` uses the explosion origin. Not a victim body contact; do not derive a body offset from it. |
| Ricochet/multi-pellet, enhanced children | Parent ShotKey retained; no stable pellet/child component ID in the v1 fact. Exact component matching requires new evidence. |
| Shock Coil | Phase-aware positive damage ticks can publish; v1 contains a continuous flag but no tick component. Zero-damage ticks are not facts. |
| Halfturret | Beam source/target flags and turret delta fields exist. Turret-only damage is excluded by the precondition `damage > 0` in some paths. |
| Balanced weapons | Positive native beam deltas use the same final-damage seam; child identity remains a gap. |
| Bombs/Lockjaw, alt contact | Native sources usually have no beam ShotId/weapon; excluded by the fact precondition. Never infer exact projectile identity from proximity. |
| Burn, self/environment | Burn/environment commonly lack a valid beam/shot identity. A beam self-hit can publish when it has a legal identity. Do not advertise general coverage. |
| Rescued claims | Uses independently validated claim witness at final application; body-relative collision pose is not retained in v1. World point must remain the fallback. |

At the baseline, the live receive case only called `ReplayCapture.AcceptedShotFact`, and
authority publication returned when recorder admission failed. Slice 1 adds an independent
bounded live lane; the replay lane remains background priority.

## Measurement contract

`NetImpactDiagnostics` is opt-in (`-liveimpactdebug`), a fixed 4096-event ring, and has
no simulation-time file I/O. Its join key includes the full ShotKey, victim slot/generation/life,
damage event, resolution tick (wrap discrimination), and component. Component zero means
unknown. Identity is session-local; no account names, endpoint addresses or tickets are logged.
Exports use process-monotonic timestamps; joining files must not subtract clocks from different
processes. `tools/live-impact/join.py` joins exact event keys into JSON/CSV.

Run `tools/live-impact/run-validation.py --output <directory> [--assets <data directory>]`.
A .NET 10 SDK is required. The local installed SDK is at `~/.dotnet/dotnet`; the default PATH
runtime has no SDK. Asset-backed commands take **data directory, then room**, correcting
the handoff examples. Headless synthetic-intent performance is labeled separately from
rendered multiplayer acceptance. Before/after records never imply unexecuted platform tests.

## Slice status

| Slice | Status | Evidence |
| --- | --- | --- |
| 0 Baseline | done | exact baseline, coverage audit, bounded diagnostics, matched controls and raw failure retention |
| 1 Live transport | done for documented fact sources | canonical seam, protocol 45, bounded independent delivery, exact native ingress and adversarial contracts; unsupported source classes explicitly excluded |
| 2 Presentation | partial | configurable draw-only endpoints, authored sprites and nine-weapon rendered three-view campaign pass; victim/spectator same-projectile target remains unmet |
| 3 Fast claims | partial | enabled isolated Imperialist path, exact paid-component ledger and admission/order fence pass 178 native assertions; real-UDP early-settlement eligibility/latency is not established |
| 4 Predicted kills | partial | opt-in render-only fall/tickets, 18 contracts, 13/13 impaired tickets confirmed; sample and authored animation quality insufficient for default enablement |
| 5 Collision performance | done within narrow measured scope | bounded pose/emission reuse passes parity and saves 320 bytes/native shot and 75.3% repeated pose-read time; broad geometry/rewind replacement intentionally deferred |
| 6 Acceptance | partial / platform work blocked | local contracts, macOS backends, Android build/emulator, bots, replay and impaired populations exercised; physical Windows/Linux/Android and complete experience gates remain unverified; CI review skipped |

## Reviewable PR chain

All are drafts, explicitly stacked on the previous branch, with #410 based on the
verified main SHA above. None has been merged. Exact base/implementation SHAs and
per-slice changed files are in [pr-chain.json](pr-chain.json). The final acceptance follow-up consolidates later proof, benchmark and rendered evidence.
Per-campaign manifests preserve their actual build hashes; later evidence never relabels earlier failures.

| Slice | PR | Branch | Implementation head |
| --- | --- | --- | --- |
| 0 | [#410](https://github.com/AntiNotAnti/Project-Prime/pull/410) | `codex/impact-00-baseline` | `85a376fa` |
| 1 | [#411](https://github.com/AntiNotAnti/Project-Prime/pull/411) | `codex/impact-01-authority-facts` | `ed3a5234` |
| 2 | [#412](https://github.com/AntiNotAnti/Project-Prime/pull/412) | `codex/impact-02-live-presentation` | `4bf31597` |
| 3 | [#413](https://github.com/AntiNotAnti/Project-Prime/pull/413) | `codex/impact-03-claim-shadow` | `cb495dd8` |
| 4 | [#414](https://github.com/AntiNotAnti/Project-Prime/pull/414) | `codex/impact-04-predicted-visuals` | `c939be7b` |
| 5 | [#415](https://github.com/AntiNotAnti/Project-Prime/pull/415) | `codex/impact-05-collision-profile` | `41179600` |
| 6 | [#416](https://github.com/AntiNotAnti/Project-Prime/pull/416) | `codex/impact-06-acceptance` | `c463c14a` |
| 7 Claim proof | [#417](https://github.com/AntiNotAnti/Project-Prime/pull/417) | `codex/impact-07-claim-proof` | `b0cd8821` |
| 8 Server scratch | [#419](https://github.com/AntiNotAnti/Project-Prime/pull/419) | `codex/impact-08-server-scratch` | `d7bd5749` |
| 9 Native fixtures | [#421](https://github.com/AntiNotAnti/Project-Prime/pull/421) | `codex/impact-09-native-fixtures` | `c47f05c1` |
| 10 Rendered reconciliation | [#422](https://github.com/AntiNotAnti/Project-Prime/pull/422) | `codex/impact-10-rendered-reconciliation` | `9b739b8f` |

## Slice 1: independent live fact transport

`NetCombatFactPublisher.Publish` receives the single final applied-damage fact. Its
server-only live fanout runs before optional recorder admission. A separate packet
71, protocol 45, carries the unchanged 61-byte replay fact plus version, kind,
component and a 1/4096-unit body offset descriptor (74-byte payload, 98-byte framed
datagram). Unknown offsets are encoded explicitly; claims and splash cannot pretend
to have a certified body offset. No snapshot payload grows.

`-liveimpacts` opts in on server and client. `-noliveimpacts` overrides it. Default is
off. Per ready peer: 64 retained live events, two unreliable attempts three simulation
frames apart, 12-frame sender expiry, at most four transmissions per simulation frame.
The ceiling is 240 datagrams/23,520 framed bytes per second per peer, excluding UDP/IP;
8 peers at saturation add at most 188,160 framed bytes/s. Pressure drops cosmetics;
no reliable retry window, replay history or match state waits for these events.
The receiver has a separate 64-packet cosmetic allowance, preserves the full original
gameplay/critical capacity, and drains ordinary realtime gameplay before cosmetics.
Saturation of cosmetics cannot consume the gameplay receive reserve.

The deterministic matrix uses the production outbox/codec over the fault scheduler:
2/4/8 peers, 0/50/150/250/350ms RTT, 0/40/80ms positive FIFO jitter, 2% loss, 3%
reorder and 1% duplication. It is **not** a symmetric-jitter UDP/rendered campaign.
All 45 scenarios pass >=98% unique delivery at 10 events/s/peer. Saturation explicitly
drops expired cosmetics. `--impact-transport` now passes 614 assertions (569 codec/identity/queue + 45 scenarios); baseline contracts
and both asset-backed authority checks remain green. See `transport.json` and `slice1/`.

The initial engineering attempt stopped at authored-mip because this new macOS worktree
lacked pinned libktx. The documented native runtime was then built and the remaining
contracts resumed successfully. Final verification outcomes are recorded in `after.json`.

PR chain: slice 0 https://github.com/AntiNotAnti/Project-Prime/pull/410, base main.

## Slice 2: conservative presentation

The live client submits depth-tested authored weapon sprites through the same pure
drawing helper as Replay Studio, using the bounded per-draw single-particle pool. No
simulation effect, random-number update or duplicate sound is produced. Supported exact
primary beams additionally read a frame/lifecycle-fenced endpoint in their draw methods. A fixed 256-entry projectile index matches full
ShotKey, weapon and primary projectile ordinal. Native child emissions can reuse spawn
ordinals across calls, so children use an authority-only component (high bit set) and
truthful fallback cues; they are **not** falsely matched by proximity. Rescued claims
also fall back when their client-correlatable component is unknown. This is an explicit
fidelity limitation, not full child correlation coverage.

Native direct contacts capture a quantized offset before the historical victim pose is
restored. Splash, turret, continuous and claims retain the authoritative world point.
The presenter holds missing relays for three simulation frames, displays cues for six,
and expires after 90. Existing matching native impact points suppress duplicate cues;
known direct/splash pairs can share a blast cue. Continuous events are not blast-coalesced.
A backwards, >60-degree, >4-unit or blocked correlation becomes an endpoint-only fallback.
The endpoint changes the drawn tip while older trail history stays fixed; no simulation
transform/history/velocity is changed. The actual smoothed endpoint and both beam/
trail-origin segments are checked against geometry on every draw. `-impactdistance`
(0..8), `-impactangle` (0..60) and `-impacthold` (0..8) configure these defaults.
The conservative thresholds are configurable, not a claim of optimal tuning.

`--impact-crossview` passes 30 asset-free contracts. The later native Metal campaign
covers all nine weapons with shooter, victim and spectator at 250±40 ms RTT, 2% loss,
2% reorder and 1% duplicate packets: 644 classified events and 587 first draw submissions
have exact server backing, with 37,941 projectile-state comparisons and zero mutations.
Classifications/submissions do not establish pixel visibility; the same-projectile ratio
is low in victim/spectator views and the proposed 90% gate remains unmet. Spot-inspected
local images show rendered actors and weapon cues. Full raw reports are in
[rendered/](../../validation/live-impact/rendered/README.md).

## Slice 3 and follow-up: proof-driven early settlement

`-claimfastpath off|shadow|enabled` defaults to shadow. The enabled path is restricted to
independently reserved Imperialist direct/head hits. It uses the same `ApplyOne` damage,
death/lifecycle, spawn protection, resource and CombatAck path as ordinary arbitration.
It never shortens the grace constant. Unsupported or ambiguous hits retain existing grace.

`TryReserveClaim` now retains the native witness component. The rescue index keys full
ShotKey + victim generation/life + native component + direct/splash + turret category.
An exact paid marker survives repeated native callbacks for the existing 720-frame
retention. Unknown component zero preserves the legacy aggregate fixture contract.
Capacity is checked after proof reservation and before applying damage.

An order proof requires all of the following:

- The fractional launch admission horizon has expired, including one conservative guard frame.
- No other earlier/equal accepted attack remains in the full 512-frame retention window,
  including already emitted or paid attacks that could still fund a delayed claim/head upgrade.
- No older deferred launch, native flight, retained alternate attack, bomb or active burn exists.
- This is the oldest currently pending claim, with an exact native witness already reserved.

The authority seals that admission frontier before early application. Raising the rewind
budget later cannot re-admit a new attack behind a settled frontier. Existing admitted
claims still use their ordinary validation; the frontier is not a blanket claim rejection.
Anonymous alternate claims require the producer's zero launch frame, preventing a forged
beam launch time from bypassing alternate-contact ordering. Match/room/session reset clears
the frontier. `off` returns subsequent claims to grace; the already sealed expired frontier
remains until reset. No additional wire or replay format change is needed.

The native fixture uses actual resource-backed emissions and detached historical bodies,
then the production claim receive/reservation/arbitration path. Off, shadow and enabled
converge on one damage application, including lethal shots and repeated native callbacks.
At simulated 250 ms RTT, an isolated claim arriving 30 frames after its launch settles at
launch + 47 instead of arrival + 35: **18 saved frames (300 ms)**. An older retained attack
blocks that saving. This deliberately conservative path is expected to have low eligibility
in sustained combat; the fixture is not a claim of measured real-network latency improvement.
The follow-up passes 570 asset-free safety/component assertions, 178 native early-settlement
assertions and all 21 local validation checks. Raw checks and build identity are in
`validation/live-impact/claim-proof/`.

## Slice 4: opt-in render-only kill tickets

`-predictedkillvisuals` (off by default; disabled by `-nohitprediction`) allows a local
Imperialist lethal prediction against a biped to rotate only its render matrix around
the feet. The victim's health, simulation pose, collision, animation state, score,
respawn and killcam are untouched. `NetHitPrediction.DeathEnabled` remains false.
Other weapons/alt forms keep ordinary immediate hit feedback.

Tickets fence full ShotKey, victim generation/life and claim ID. Repeated speculative
hits cannot restart a fall. Exact lethal CombatAck or server fact confirms once;
denial/other-shooter lethal facts recover over six simulation frames. A missing verdict
expires at RTT + 18 frames, clamped to 24–60 frames; focus/time gaps cannot extend the
absolute simulation deadline on the next pose read. A confirmed ticket also has a bounded
18-frame wait for the real death state. Respawn/epoch/occupant changes discard it.
This is a provisional visual pose, not an authored fall animation. Snapshot-only
contradiction before a verdict is handled by expiry; the three rendered impaired arms total only 13 tickets (13 confirmed, zero rejected/
expired). The short 5% loss arm misses one remote shot-observation gate; its 60-second
350±80 ms/5% loss rerun passes. Broader trade/reversal and animation quality acceptance
remain unverified, so no default enablement is justified.

18 ticket and integration assertions pass (including idempotence, wrong identities,
expiry/wrap, double hits, smooth recovery and unchanged snapshot health/pose/flags).
The existing 3,338,739 health/shot assertions still pass. The claim fast path has separate proof and rollout controls from this cosmetic prototype.
Slice 3 PR: https://github.com/AntiNotAnti/Project-Prime/pull/413.

## Slice 5: bounded historical-combat profiling

`-impactprofile` measures BeginShot/EndShot, native catch-up, historical geometry,
accepted-fire emission and path Supports. Disabled scopes allocate nothing; enabled
scopes use fixed 2048-entry timing rings with all-call allocation/time totals. Reset
invalidates open scopes. Exported percentile windows overlap/nest and are not additive.
The asset-backed server benchmark now records p99.9, peak working set and these samples
with `--impact-profile` after its duration argument.

On macOS ARM64, 1800 measured steps after 300 warm-up steps in MP1 SANCTORUS yielded
p99 0.2772/0.5345/0.9795ms for 2/4/8 synthetic-intent players. In the eight-player run,
BeginShot totaled 6.068ms, EndShot 29.236ms (including 29.046ms catch-up), historical
geometry 1.682ms and EmitPending 20.244ms. Supports had **zero calls**: this workload
cannot establish claim-validation cost. Raw samples/build identity are in `profile/`.

The later server-scratch follow-up implements a measured narrow improvement, detailed
below. No read-only replacement of broad world rewind, native trajectory or dynamic
geometry is introduced. Those changes require their own parity evidence.

## Acceptance evidence and reproduction

See [after.json](../../validation/live-impact/after.json) and
[COMPARISON.md](../../validation/live-impact/COMPARISON.md) for the measured outcomes,
failed native arms, benchmark limits and raw evidence paths. CI review is skipped;
old CI observations are historical evidence only, never the current acceptance result. No percentage
from a synthetic queue test or headless ingress join is labeled visual success.
The final local runner passed 21 checks, including 3,338,739 health/shot assertions,
614 transport assertions, 57 real authority/resource assertions, 156 fire-context
assertions and 178 native early-claim assertions. Visual contracts inspect unchanged gameplay state; they do not inspect pixels.

```sh
dotnet build tools/nettest -c Release
python3 tools/live-impact/run-validation.py --dotnet dotnet \
  --output /tmp/impact-contracts --assets /absolute/path/to/game-data
python3 tools/hitrig/run-networking-slices.py \
  --runtime /absolute/path/to/frozen-runtime --data /absolute/path/to/game-data \
  --dotnet /absolute/path/to/dotnet --output /tmp/impact-native \
  --players 2 --seconds 30 --impacts --modes powerbeam \
  --profiles rtt0-loss0,rtt250-loss2,rtt350-loss2 --require-combat
python3 tools/live-impact/report-native.py /tmp/impact-native --output /tmp/impact-native/report.json
python3 tools/live-impact/benchmark-pair.py \
  --before /absolute/path/to/baseline/nettest.dll --before-commit BASE_SHA \
  --after /absolute/path/to/current/nettest.dll --after-commit HEAD_SHA \
  --assets /absolute/path/to/game-data --output /tmp/impact-benchmark \
  --dotnet /absolute/path/to/dotnet --seconds 30 --repeats 3
```

The native runner isolates preferences, disables master registration and freezes its
runtime. It retains failures and checks source hashes after execution. Weapon admission
must stay intact: a locally armed rig weapon is not automatically a legal server attack.
Exports use `PRIME_IMPACT_LOG` with `-liveimpactdebug` at native harness/server teardown.
The diagnostic ring is 4096 events; any overwrite invalidates a full-run denominator.
Monotonic timestamp subtraction is only within one process. Missing peer facts can be
outside readiness/lifecycle windows, so the join cannot assert an eligible-delivery SLA.

## Gates and rollback

| Gate | Default | Rollback and scope |
| --- | --- | --- |
| `-liveimpacts` | off | `-noliveimpacts` overrides; disables live publication/consumption; ordinary visuals and replay remain |
| `-liveimpactdebug` | off | omit to remove detailed hot-path logging; exports happen only on explicit diagnostic paths |
| `-claimfastpath off\|shadow\|enabled` | shadow | `off` retains original arbitration; `enabled` permits only independently proven, order-safe Imperialist direct hits |
| `-predictedkillvisuals` | off | omit, or use `-nohitprediction`; actual predicted remote death remains hardcoded off |
| `-impactprofile` | off | omit to disable timing/allocation probes |
| `-impactserverscratch` | off | omit for uncached pose reads/fresh emission equipment; native geometry, rewind and collision remain active |

Protocol 45 is a strict client/server boundary. Rebuild paired binaries together if this
chain is eventually released. ReplayShotFact v1 and historical replay/checkpoint layouts
are unchanged. No automatic configuration migration, release or deployment is performed.

## Prioritized release and fidelity work

- **Security/correctness (release blocker):** sustained eight-player Shock Coil causes
  semantic-history overrun disconnects in live-impact off and on controls. The owner-
  grace experiment does not fix this. Keep required match-event delivery intact;
  no weakened authority, dropped semantic events or unbounded retention is justified.
  Broader bounded transport work and sustained-load acceptance remain open.
  Keep ambiguous claims on existing arbitration. The paid ledger
  and admission/order fence are implemented and tested; broader claim categories must
  independently prove multiplicity, lifecycle and trade parity before expansion. No
  shooter-authoritative damage, changed grace constants or speculative gameplay death.
- **Fidelity:** stable repeated-child lineage and source-backed turret-only/bomb/alt/burn
  facts remain unsupported. Affinity/homing/ricochet variants need dedicated cohorts.
  Unknown components must continue to fall back instead of nearest-projectile matching.
- **Visuals:** the 90% same-projectile target and a visibility-qualified 98% cue denominator
  are unproven. Complete moving-platform/door/force-field and physical-device/input/audio
  QA, plus a larger predicted-kill reversal campaign before rollout. macOS live 240/360
  targets do not exceed the host's roughly 117 FPS throughput; virtual cadence invariance
  does pass 60/120/144/240/360/540 Hz.
- **Performance:** narrow hot-path wins are established, not a broad server speedup.
  Native UDP off/on timing retains startup and varying actual combat and is invalidated
  by semantic-history disconnects in both modes; no enabled performance gate passes. Mixed-bot checks are functional, not matched optimization benchmarks.
- **Platforms:** Windows DX12/Vulkan, Linux Vulkan and physical Android combat/focus
  acceptance require those executors. Android emulator Vulkan lifecycle is not a
  physical-device combat result. CI review is intentionally outside this request.

## Server scratch follow-up

`-impactserverscratch` (off by default) enables two bounded optimizations:

- One emission equipment object per current player/pool replaces per-shot equipment and ammo-delegate allocations. Native spawned beams retain only its stable pool reference; charge, scope, weapon, smoke and payment state are reset for each spawn. Slot/lifecycle and pool replacement discard scratch.
- An 8 × 128 cache reuses immutable historical player poses between history mutations. Exact fractional frame, victim generation/life and history revision fence each entry, including unavailable samples. Record, same-frame replacement, slot reset and room reset invalidate it. No world collision or line-of-sight result is cached, so doors/force fields retain their existing native queries.

The feature retains native emission, rewind, catch-up and independent claim proof. Omit
`-impactserverscratch` to use fresh equipment and uncached claim pose reads. No wire change.

Three alternating native benchmark pairs for each 2/4/8-player population use identical
positions and disabled tiered compilation to avoid tier transitions contaminating comparisons.
The initial exploratory run with tiered compilation and changing spawn positions is retained
as `preliminary-warmup.json`, excluded from conclusions. In the corrected run, eight-player
emission allocation drops from 2,170,880 to 1,515,520 bytes over 2,048 shots: **320 bytes/shot**.
The median batch of 1,024 repeated historical-pose reads falls from **0.0279 to 0.0069 ms**
(75.3%); both allocate zero bytes. This deliberately stresses reuse and is not a whole-match
speedup. Native emission medians improve 1.2%, but its microbenchmark p99 rises 13.5%; the
optimization remains opt-in.

A separate 18-run matched server-frame campaign (three alternating 30-second synthetic
runs per population, same binary with flag off/on) reports median run p99 changes of
+3.79% / +3.09% / −1.90% for 2/4/8 players, and unchanged allocation per frame. Eight-player
p99 is 0.1788 → 0.1754 ms. These are medians of run percentiles, not pooled percentiles or
confidence intervals. The workload has no live UDP peers. Raw paired profiler samples,
commands, assembly hashes and frame results are in `validation/live-impact/server-scratch/`.
CI review was skipped as requested; these are local results only.

The expanded native suite passes 1,925 top-level cache/benchmark/parity assertions, including
57 authority-policy, 156 firing-context, 162 native combat, alternate-form history and
178 early-settlement assertions in its nested suites. All 21 local live-impact regression
checks also pass. Both features remain off by default pending impaired live acceptance.

## Native/rendered acceptance follow-ups

The explicit `-hitrigloadout` fixture works only on unlisted loopback dedicated servers;
normal authority issues/refills the requested weapon and continues to validate resource,
cadence, source identity and collision proof. Packaged `TEST ARENA` map identity is used,
not an uninstalled loose recipe. Charged cases require actual charged native emissions
and the intended weapon's authoritative damage facts. Raw earlier false-negative arms
remain failed in their original summaries; separate rechecks demonstrate the counter fixes.

Native 2/4/8-player tests cover 0/50/150/250/350 ms buckets, jitter/loss/reorder/duplicates,
continuous ticks and a 500 ms peer suspension. The three-way nine-weapon Metal campaign,
OpenGL pilot, MoltenVK spectator test and high-refresh arms retain exact commands and
runtime hashes. The runner uses full event-identity authority joins rather than assuming
all legitimate hits appear in local prediction counters. Headless peers drain cosmetic
inboxes without recording a draw. `--owner-grace` can hold the first peer through staggered shutdown. In the longer
continuous-fire campaign, the actual reset follows semantic-history disconnects in
both live off/on controls, not ordinary owner departure; that experiment is retained
as failed and excluded from performance acceptance. The harness now explicitly
fails unexpected server disconnects even when timed client processes return zero.

Real recorded-match theatre and export pass, including seeks, player/POV changes, clean
HUD, native 720p/4K targets, repeatable 24..144 FPS exports, serialized queue/reel and
render-state invariance. A generated eight-actor replay passes all modes, checkpoint/
frozen-clip seek/restore, 2,354 killcam checks and 60..540 Hz virtual draw invariance.
Protocol-44 fixtures created by the original baseline open and seek under this build.
Their strict 30 FPS export-repeat test fails by the exact same one/two pixels on baseline
and current; corresponding output images match pixel-for-pixel. This existing repeatability
limitation remains visible, not silently waived.

Bot AI, seven-bot scene, mixed human/bot replication, 995 alternate-contact checks and
62 continuous-target scene assertions pass. Android ARM64 builds with 129 warnings and
zero errors; three fresh emulator Vulkan surface/readback/UI-overlay resume cycles pass.
Local engineering diagnostics and the dedicated-server compilation repair are retained
with final acceptance. No CI status was reviewed in this continuation.
