# Combat impact implementation and acceptance

The implementation is delivered through stacked draft PRs, with independent safe
rollout defaults. Release acceptance is **partial**: the victim/spectator
same-projectile target is unmet, sustained eight-player continuous fire exhausts
semantic event history in both off/on controls, and some source/variant/platform cohorts remain
unverified, and CI review was explicitly skipped. No merge or release occurred.
The current machine-readable record is [after.json](after.json); the original
baseline remains [baseline.json](baseline.json). The earlier checkpoint is preserved
as [after-slice6.json](after-slice6.json), including failures later superseded by
separately recorded repairs. A failed raw run is never rewritten as passed.

## Local contracts and builds

The final dedicated impact runner passes **21/21** groups: existing 3,338,739 health/
shot assertions, claim stress, 2,352 lag profiles, 1,620 weapon profiles, transport,
lifecycle and historical replays; plus 569 live codec/identity/queue, 614 transport
(includes those 569), 30 presentation, 574 settlement safety, 18 kill-ticket and
three profiling assertions. Asset-backed checks pass 57 authority/resource,
156 firing-context and 178 native early-settlement assertions. Overlapping suites
are not added into an inflated total. See [contracts](rendered/contracts/results.json).

All runtime checks in `bash tools/check-engineering-contracts.sh` pass, including
SQL migration fixtures, renderer/lifecycle/pacing, updater recovery, replay timeline,
lobby, map roadmap and replay preparation. Its final server build caught a missing
graphics preprocessor guard in the netcheck harness. The guard was repaired and the
server recompile passes with 40 warnings/zero errors. The original failed script and
successful repaired build remain separate logs. Desktop/nettest and Android ARM64
builds also pass. These are local results, not CI status.

The production transport/codec fault matrix still passes all 45 scenarios at
2/4/8 peers, 0/50/150/250/350 ms RTT, 0/40/80 ms positive FIFO jitter, 2% loss,
3% reorder and 1% duplication. Its >=98% unique-delivery result at 10 facts/s/peer
is a synthetic queue result, not a pixel-visibility or eligible-UDP-delivery SLA.

## Native combat and rendered views

The authority-issued loopback fixture fixes the earlier invalid weapon-acquisition
assumption. It requires an unlisted dedicated server and local peers, issues legal
inventory, and retains native ammunition, cadence, source and collision proof.
Charged cases require actual charged native emissions and intended-weapon damage.

- **Nine-weapon Metal campaign:** 9/9 arms with shooter, victim and spectator,
  250±40 ms RTT, 2% loss, 2% reorder and 1% duplicates. The retained classifications
  are 644/644 authority-backed; all 587 first draw submissions are authority-backed.
  There are 37,941 projectile-state comparisons and zero mutations.
- **Variants:** 11/14 original arms pass. Two Omega arms and one Judicator arm
  failed an old prediction-only counter despite native authority-only damage.
  Separate final reruns pass; the original failure summaries remain intact.
- **Four/eight players:** four-player 50/150/350 ms and eight-player 0/250 ms runs
  exercise scoped duel and continuous ticks, including a 500 ms peer suspension.
  A trigger-window counter and headless cosmetic-consumption issue were repaired.
  The final four-player duel, two eight-player coil and two Omega reruns all pass;
  the Judicator recheck makes six affected rechecks passing in total.
- **Backends:** native OpenGL views and a three-view MoltenVK Missile arm render.
  Metal and MoltenVK offscreen backend probes also pass. OpenGL's initial pilot
  failed an inappropriate symmetric-shooting gate; later paired owner/observer
  counters require remote observations only for participants that actually fire.
- **Refresh:** requested 120/144/240/360 FPS Metal arms pass, with measured rates
  approximately 117 FPS. Physical 240/360 Hz output is not claimed. The separate
  virtual replay cadence test verifies identical gameplay at 60/120/144/240/360/540 Hz.
- **Kill tickets:** three impaired arms start 13 tickets and confirm all 13, with
  zero rejection or expiry. The short 350 ms/5% loss arm misses a remote shot
  observation; its 60-second ±80 ms rerun passes. This sample remains too small
  for default enablement or a population reversal-rate estimate.

Per-weapon raw denominators, stage classifications, ingress-to-draw p90/p99 and
within-process hit-to-ingress samples are in [the report](rendered/metal-nine/report.json).
No timestamps are subtracted across processes. Ring overwrite, readiness, lifecycle
and shutdown censoring are explicit limitations. Classifications and accepted draw
submissions are **not pixel visibility**. Spot inspections show actors, weapons and
cues; screenshots remain local and are inventoried by hash because game imagery must
not be committed. See [reproduction and evidence](rendered/README.md).

The proposed >=90% same-projectile gate is not met in victim/spectator views; most
use truthful fallback effects. The visibility-qualified >=98% cue gate is unverified.
The retained 100% authoritative backing is encouraging but does not make those other
gates pass. Live impact publication/correction stays off by default.

## Authority and settlement

The native early-settlement fixture uses ordinary accepted firing, resource-backed
native path evidence and final damage application. It proves exact component payment
and later duplicate suppression, ordering blockers, lifecycle fences and lethal parity.
At simulated 250 ms RTT, an isolated Imperialist claim settles 18 frames (300 ms)
earlier than grace. This is a deterministic native fixture, **not** a measured UDP
latency win. Sustained combat is deliberately likely to block eligibility. The default
is shadow; unsupported/ambiguous hits keep the existing grace path.

No arbitrary grace reduction, shooter-authoritative damage, skip-rewind branch or
speculative gameplay death was introduced. Predicted lethal feedback changes only a
render pose; actual health, collision, score, respawn and killcam retain authority.

## Performance

The original matched baseline/default-feature comparison retains eight-player p99
0.9848 -> 0.9666 ms (−1.85%) and identical allocations/step. Four-player p99 increases
6.07%, so this is not a universal performance win. These are synthetic intent workloads
with live impacts and predicted-kill visuals off, not enabled native acceptance.

The opt-in server scratch feature saves **320 bytes/native emission**, and the median
batch of 1,024 repeated historical-pose reads improves **0.0279 -> 0.0069 ms** (75.3%).
Its exact native/parity suite passes 1,925 top-level assertions plus nested authority,
fire, combat and early-settlement checks. Native-emission microbenchmark p99 increases
13.5%; keep the feature opt-in. The three-pair synthetic server-frame medians change
+3.79% / +3.09% / −1.90% at 2/4/8 players, with unchanged allocations/frame. See
[paired frames](server-scratch/frames/comparison.json) and
[native parity](server-scratch/expanded-parity.json).

The later native UDP experiment alternates live delivery off/on on one frozen binary,
with identical diagnostics, eight real peers, 250±40 ms RTT, 2% loss and tiering off.
It reuses bounded server histograms and captures p50/p95/p99/p99.9 and allocation at
teardown. Whole-match samples include startup, staggered departure and random combat;
they cannot prove the controlled steady-state allocation or <=5% p99 release gate.
Both initial and repeated controls disconnect peers for **semantic history overrun**.
An owner-shutdown hypothesis was tested with eight extra owner seconds and did not
resolve it. Both live off/on arms hit the failure, so no live-impact-specific cause
or pristine-baseline origin is asserted. All six repeated arms are invalid for
performance acceptance, even though the earlier off-arm harness returned zero.
The final harness now fails explicit server timeout/reliable/semantic disconnects;
its off-mode reproducer confirms this stricter result. Raw timing medians are retained
only for diagnosis in [native-performance/comparison.json](native-performance/comparison.json)
and [disconnect-audit.json](native-performance/disconnect-audit.json).

For diagnosis only, the median run p99 is 1.76 ms off versus 1.80 ms on;
allocation is 39095 versus 38946 bytes/frame. Disconnects
change the simulated population, so these values do not meet the matched-load contract.

This is a release blocker. Weakening required semantic delivery, dropping authoritative
match events or expanding retention without a bounded transport design would not be a
safe way to make the test pass. The live cosmetic lane remains independent and disabled
by default; its safe default does not resolve this sustained-load acceptance failure.

No broad historical-world/line-of-sight cache or rewind replacement is claimed.
The pose cache keys exact fractional frame, lifecycle and history revision and
invalidates on every relevant history mutation. Dynamic geometry remains native.

## Replay, bots and platform scope

A real recorded match passes theatre, player/POV changes, seeks, pause, 720p/4K clean-
HUD export, repeated 24..144 FPS samples, serialized jobs/reels and render-state
invariance. A generated eight-actor replay passes all game modes, checkpoint and
frozen-clip restoration, 2,354 killcam checks, and high-refresh virtual drawing.
Seven-bot simulation and mixed human/bot online replication pass, as do 995 alternate-
contact and 62 continuous-target scene assertions. These are functional checks, not
matched mixed-bot optimization benchmarks.

Protocol-44 fixtures created by the original baseline open and seek under the new
build. Strict repeat export at 30 FPS differs by one/two pixels within both baseline
and current builds. Every corresponding baseline/current frame is pixel-identical;
the pre-existing repeatability failure is retained in
[pixel-comparison.json](rendered/historical-replay/pixel-comparison.json).

Android ARM64 builds with 129 warnings and zero errors, and the installed debug APK
passes three fresh Vulkan surface/readback/present/UI-overlay Home/resume cycles on
the ARM64 emulator. This does not verify physical Android combat, touch or focus loss.
Windows DX12/Vulkan, Linux Vulkan and physical Android acceptance require those hosts.
Controller/touch/audio quality, moving platforms/dynamic cover, repeated-child lineage,
affinity/homing variants and unsupported turret-only/bomb/alt/burn fact sources remain
explicit release/fidelity work. CI review is skipped by user request.

## Defaults and rollout

Live impacts, predicted-kill visuals, server scratch and detailed diagnostics remain
off; claim fast settlement remains shadow. Roll back independently with
`-noliveimpacts`, `-claimfastpath off`, or by omitting the respective opt-in flag.
Protocol 45 clients and servers must ship together if later released; replay fact v1
and historical checkpoint compatibility remain unchanged. No deployment, merge or
automatic configuration migration occurred.
