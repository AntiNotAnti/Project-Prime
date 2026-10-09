# PR109-inspired combat presentation

Base: `0127fab5ffd411529a5c69a40e5467e140f50a60`, protocol 44.
Upstream Fruity PR109: `1756af902f960c82edde28bd31e6895f8bcb8198`, open and conflicting when checked on 2026-10-08 (America/Chicago).

**Delivery status: gated partial implementation, not release acceptance.** Seven draft PRs
implement transport, conservative cosmetics, shadow decisions, kill tickets, profiling and
reproducible evidence. The follow-up claim proof adds opt-in early settlement for isolated Imperialist direct hits;
other categories keep existing grace arbitration. Full visual acceptance and a collision optimization win are outstanding.

This implementation retains dedicated-server combat authority, existing movement ownership,
claim grace/arbitration, shot/lifecycle identity and replay v1 facts. No release or merge is authorized.

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
| 0 Baseline | done | exact baseline, coverage audit, bounded diagnostics, original and matched benchmark records |
| 1 Live transport | partial | canonical seam, protocol 45, independent bounded delivery and native ingress verified; child identities and complete fact coverage remain limited |
| 2 Presentation | partial | pure endpoint cues, full primary-shot matching and fallback contracts; no corrected trails or weapon-specific effect campaign |
| 3 Fast claims | partial | opt-in Imperialist path with exact component suppression and conservative admission/order proof; native deterministic parity verified, broader impaired campaign pending |
| 4 Predicted kills | partial | opt-in Imperialist render-only tickets; authored animation and impaired rendered reversal campaign outstanding |
| 5 Collision performance | partial | bounded profiling, native and synthetic samples; no new collision optimization or parity win |
| 6 Acceptance | unverified | local contracts and real UDP evidence retained; full platform/rendered/bot/weapon matrix not accepted |

## Reviewable PR chain

All are drafts, explicitly stacked on the previous branch, with #410 based on the
verified main SHA above. None has been merged. Exact base/implementation SHAs and
per-slice changed files are in [pr-chain.json](pr-chain.json). Slice 6 adds the final
evidence/docs after its implementation audit; its PR exposes the current complete diff.

| Slice | PR | Branch | Implementation head |
| --- | --- | --- | --- |
| 0 | [#410](https://github.com/AntiNotAnti/Project-Prime/pull/410) | `codex/impact-00-baseline` | `85a376fa` |
| 1 | [#411](https://github.com/AntiNotAnti/Project-Prime/pull/411) | `codex/impact-01-authority-facts` | `ed3a5234` |
| 2 | [#412](https://github.com/AntiNotAnti/Project-Prime/pull/412) | `codex/impact-02-live-presentation` | `4bf31597` |
| 3 | [#413](https://github.com/AntiNotAnti/Project-Prime/pull/413) | `codex/impact-03-claim-shadow` | `cb495dd8` |
| 4 | [#414](https://github.com/AntiNotAnti/Project-Prime/pull/414) | `codex/impact-04-predicted-visuals` | `c939be7b` |
| 5 | [#415](https://github.com/AntiNotAnti/Project-Prime/pull/415) | `codex/impact-05-collision-profile` | `41179600` |
| 6 | [#416](https://github.com/AntiNotAnti/Project-Prime/pull/416) | `codex/impact-06-acceptance` | `c463c14a` |

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

The live client submits depth-tested weapon-colour endpoint particles through the same
pure drawing helper as Replay Studio. A fixed 256-entry projectile index matches full
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
There is no live corrected tracer, synthetic projectile or gameplay transform mutation.
These conservative thresholds are provisional, not empirically tuned recommendations.

`--impact-crossview` currently means 22 **asset-free presentation contracts**, not an
observed multi-camera success rate. Existing replay format/control checks pass after
sharing the drawing helper. The pinned macOS libktx runtime builds and the remaining
engineering contracts passed after installation. Rendered first-person/spectator/high-refresh and
weapon-specific particles/audio remain unverified; default stays off.

Slice 1 PR: https://github.com/AntiNotAnti/Project-Prime/pull/411 (depends on #410).

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
contradiction before a verdict is handled by expiry; extensive high-loss reversal and
trade/render acceptance remains unverified, so no default enablement is justified.

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

No additional historical-collision optimization is enabled or claimed as a win.
The baseline already has a per-shot, fractional-frame/lifecycle pose cache; replacing
rewind or adding a geometry cache without an exercised claim/dynamic-world parity
campaign would not satisfy the requested safety gate. This slice is **partial**:
instrumentation is implemented and measured; optimization/parity and mixed-bot load
remain unverified. Three profiler contracts cover zero disabled allocation, bounds and
scope invalidation. Slice 4 PR: https://github.com/AntiNotAnti/Project-Prime/pull/414.

## Acceptance evidence and reproduction

See [after.json](../../validation/live-impact/after.json) and
[COMPARISON.md](../../validation/live-impact/COMPARISON.md) for the measured outcomes,
failed native arms, benchmark limits, CI snapshot, and raw evidence paths. No percentage
from a synthetic queue test or headless ingress join is labeled visual success.
The final local runner passed 20 checks, including 3,338,739 health/shot assertions,
614 transport assertions, 57 real authority/resource assertions and 156 fire-context
assertions. Visual contracts inspect unchanged gameplay state; they do not inspect pixels.

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
| `-claimfastpath off\|shadow` | shadow | `off` removes the bounded observer and retains original arbitration; `enabled` fails with exit code 2 |
| `-predictedkillvisuals` | off | omit, or use `-nohitprediction`; actual predicted remote death remains hardcoded off |
| `-impactprofile` | off | omit to disable timing/allocation probes |
| Historical collision replacement | absent | original server native/rewind validation remains active |

Protocol 45 is a strict client/server boundary. Rebuild paired binaries together if this
chain is eventually released. ReplayShotFact v1 and historical replay/checkpoint layouts
are unchanged. No automatic configuration migration, release or deployment is performed.

## Prioritized remaining work

- **Security/correctness (blocker):** design and verify an exact native-component paid/
  suppression ledger and a closed admission/order frontier before enabling early claim
  settlement. Add trade, delayed earlier shot, continuous multiplicity and consumed-component
  adversarial parity fixtures. Keep the original grace/arbitration until these pass.
- **Fidelity:** carry stable repeated-child lineage, extend source-backed facts for turret-only,
  bomb/alt/burn paths, and distinguish every charged/affinity/homing/ricochet case. Never guess
  component identity from proximity. Correct the native fixture's legal weapon acquisition
  through ordinary authority rules before treating all-weapon runs as acceptance.
- **Visuals:** implement and inspect weapon-specific impacts/trails, observer/first/third-person
  views, moving-pose offsets and duplicate splash behavior; tune provisional hold/cone/range
  thresholds from measured views. Run 60–360 Hz, seeks/export, dynamic cover/custom maps,
  physical Android focus/resume and repeated high-loss predicted-kill reversals. Features
  remain off until the requested denominators and false-impact gates are demonstrated.
- **Performance:** use the retained native profiles to isolate catch-up work, then parity-test
  a bounded cache or read-only query on supported shots. Demonstrate an actual hotspot win
  and the enabled eight-player <=5% p99 gate with mixed bots/humans. Current default-off
  microbenchmarks and fixed bandwidth ceilings cannot establish that enabled release gate.
