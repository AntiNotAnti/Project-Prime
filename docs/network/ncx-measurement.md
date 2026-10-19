# NCX-01 measurement contract and qualification

NCX-01 is **complete for its measurement-foundation exit gate**. Latest-main integration
checks pass; checkpoint-09 baseline observations retain their original provenance. The manifest
separates successful instrumentation/control checks from retained comparator failures,
unsupported comparisons and later release gates. No visual-fidelity percentage or CPU
improvement follows from these measurements.

The first timeline integration base was `73d7586bd9b8560bce699f032241dfc320fd6799`: it includes
the intervening replay audio ownership fix (#728) and custom weapon payload foundation
(#729). The custom payload foundation does not enable a networked M7 or make an unknown
custom source eligible for native proof. Native protocol remains 59.

An earlier complete 27-route baseline is `062f868fdda0fc9b302616cc13745c0bfde98b3b`, integrating main
`2e3eeef10` through CW-04b/CW-04c and the intervening movement setup/replay-label work.
Its Release build has zero warnings/errors and all 27 focused off/on/native routes pass.
The [validation manifest](../../tools/nettest/baselines/ncx-measurement.json) records the
completed scoped gate and retains prior failures and later qualification limits.

## Opt-in timeline

Use the existing campaign controller with `--shot-timeline`. It sets
`PRIME_NCX_TIMELINE=1` alongside the existing `PRIME_NET2_EVIDENCE` output. Both are
required; ordinary play and existing campaigns leave this new observation buffer off.
The campaign report adds `ShotTimeline`; the runner writes a privacy-safe
`ncx-timeline.json` with a separate coverage status. Gameplay/drain success and timeline
completeness are different results.

Storage is a fixed array bounded by the configured evidence row limit. Overflow and
unkeyed observations make evidence incomplete; neither can grow a gameplay queue or
authorize/refuse a shot. Detached JSON export allocates at report time. The observation
buffer is not a payment ledger, replay format, packet extension or alternate authority.

| Observation | Exact meaning and limit |
| --- | --- |
| `source-authored` | Provisional native Begin; emission may still fail |
| `source-emitted` | Existing successful emission seam; each local/authority/remote copy is an observation of the same full key |
| `intent-offered` / `claim-offered` | Scheduled into the existing transport; not observed physical socket transmission |
| `intent-received` | Lifecycle-accepted carrier before fire-event admission, including relayed clients |
| `source-admitted` | Accepted discrete source ledger entry; not resource/collision certification or payment |
| `source-rejected` / `source-expired` | Existing source disposition; no invented damage result |
| `native-contact` | Actual native contact seam before payment; immunity may prevent damage; `Result` is head=1/body=0, `Value` is native channel |
| `claim-declared` / `claim-received` | New prediction claim versus every received attempt, including retries; `Value` is requested damage |
| `payment-attempt` / `payment-refused` | Existing component/continuous reservation; refusal result 1=duplicate, 2=capacity, continuous 3=invalid identity |
| `payment-committed` | Existing ledger committed an actual body/turret outcome; `Value` is body damage only, not total turret damage or a kill |
| `terminal-decided` / `terminal-delivered` | Authority Answer versus a receipt matched to the exact live outbox identity; repeated answers are separate observations |
| `predicted-feedback` | Tentative local damage after the existing remote lethal clamp; not an authoritative hit or visible photon |
| `replay-captured` | Existing recorder accepted the authoritative resolved-shot fact |
| `render-submitted` | Exact projectile-anchor or contact-cue submission; **not visible pixels** |

Rows retain epoch/match/shooter generation/life/ShotId, component, continuous phase,
channel, victim generation/life, claim ID and the full uint32 damage sequence. Scoped
modifier bits are not interpreted as a continuous phase. The continuous ledger's phase
identifier is distinct from the original emitted ShotId. Claim-funded commit observations
preserve the separately supplied claim ID without modifying the gameplay outcome.
For turret quantities, retain the existing `TimedCaseJoins`/continuous outcome evidence;
zero body damage alone does not mean no turret payment.

The exporter hashes the complete source key into a versioned token and excludes raw
source keys, peer names, endpoints, account IDs and arbitrary unknown stage strings.
It counts uniquely emitted sources, received claims/retries, exact component/phase/victim
payments and delivered receipts separately. A missed shot need not have a claim/payment;
missing stages describe observation coverage, not proof of an illegal hit or payment bug.
The legal-hit denominator and actual pixels remain explicitly unavailable here.

## Clock domains

Each process records Stopwatch ticks and its measured frequency. LocalFrame is its own
simulation frame; AuthorityFrame is its local authority tick or latest received snapshot.
Authored SourceFrame/AckFrame/AckSubFrame are retained where known; zero means absent.
Frames, frame wrap and clock origin are not elapsed milliseconds.

Only same-process intervals use timestamp subtraction. Optional cross-process alignment
reports causal bounds on server-clock minus client-clock: earliest offer/receipt per exact
source bounds one direction; earliest terminal decision/delivery per exact source+claim
bounds the other. This assumes constant clock offset/rate within the capture, reports
interval width as uncertainty, and never invents a one-way latency estimate or pairs
arbitrary retry indices. Missing/inconsistent bounds stay unavailable/incomplete.

## Current local evidence

Raw artifacts are under `artifacts/ncx/NCX-01/`.

- `timeline-build-04.log`: refreshed Release game/nettest build, zero warnings/errors.
- `timeline-focused-results-04.json`: seven content-free routes in both off/on modes,
  including the new identity/clock/storage checks, architecture, archival protocol,
  claim stress, source lifecycle/input and existing netcode-performance correctness.
- `timeline-native-results-04.json`: combat arbitration, continuous combat and native
  impact authority fixtures pass in both modes. These are actual asset-backed damage
  fixtures, not rendered victim/observer evidence.
- Python timeline/dispatch checks: 28 pass; existing runner route tests: 50 pass.
- The first two-client moderate UDP smoke passes gameplay and bounded drain: 12 uniquely
  emitted sources, two committed payments, two delivered terminal receipts, and one
  observed claim retry. Coverage is INCOMPLETE, with no rendered pixels/independent
  legal-hit oracle. Its initial `--source` label hashed tracked changes only; retain
  that label and do not use this run for matched source certification. A closed-source
  manifest and repeat are required. Concurrent fixture work also disqualifies its
  timing values from performance comparison.

Retained diagnostic failures: the first compile used the wrong confirmed-impact field
name; an intermediate test build raced a newly added helper signature. Both logs remain.
The original Tiered allocation loop measured a fixed 128 bytes, while its tiering-disabled
control measured zero. The final tests compile measurement loops directly as hot methods
and measure zero routine observation allocation. This is a focused storage/disabled-hook
result, not whole-game allocation or frame-time certification.

## Pinned Fruity preparation

Native source is `12ff677b9700bc8dabde37eb9a3fdd10c62b87b6`, protocol 34. Qt 6.12 development
dependencies were installed locally; Release native compilation succeeds. Python 3.9
failed upstream generators using `Path.write_text(newline=...)`; selecting the available
Python 3.12.14 runtime resolves that setup failure without editing the generators.

The unmodified-source executable is retained with SHA256
`32814f934f0c9e72e203702fdf40d287c11cbdda278d9a640beb12d850605a8e`.
The local comparison build has an explicitly hashed, task-private profile adapter:
`FRUITY_NCX_USER_DATA` selects an absolute private data directory; absent preserves the
upstream path. It changes no combat, movement, weapon, transport, timing or rendering
policy. Preserve both its patch and binary hash in comparison manifests; do not label it
an unmodified-source binary or silently use the installed older app as current master.
No comparator code is copied into Prime's combat implementation.
The exact [profile-only patch](../../tools/nettest/comparators/fruity-private-profile.patch)
is retained for reproduction. Apply it to the pinned native source before building the
comparison app, and copy it beside the private qualification manifest as
`fruity-private-profile.patch`; the adapter checks its exact SHA256 before launch.

Native smoke initially failed because the development app lacked the GLFW runtime path.
Local staging uses symlinks to the already linked, versioned Homebrew GLFW/OpenAL libraries
to avoid duplicate Objective-C classes from loading a second copied GLFW image. Runtime
libraries and adapter must be hashed. Smoke and real-client controls still need final
package-qualified records. The native combat fixture currently reaches an OpenGL-session
requirement without a current context after its earlier assertions; its nonzero exit is
retained as FAIL, not promoted from its many preceding PASS lines.

## Original qualification checklist (retained implementation history)

The following checklist describes the earlier incomplete state. Completed checkpoint-09
observations and latest-main qualification are recorded below; unsupported comparator
capabilities stay unavailable.

Qualify the implemented same-prepared-frame exclusion/readback probe on a passing
campaign, with exact keyed projectile/cue ownership, saved RGB/PNG hashes, positive/negative
controls and unchanged gameplay clocks/RNG/lifecycles. Repeated draw submissions or a
relocated clear camera do not satisfy this gate. Flight samples are captured before an
impact receipt arrives, then joined to exact authority facts; ambiguous component/shot
attribution remains incomplete.

Retain the narrow frozen-contact oracle's unsupported/legal-damage limits. Run declared
matched stationary, lateral and
jump-pad fixtures with an explicit no-fire observer and the same supported geometry,
hunters, weapons and rules on both engines. Prime currently lacks the beam-transparent,
invisible brush flags used by Fruity's wells, so copying those JSON maps verbatim would
create a non-equivalent fixture. Measure actual 30/60/120/144/240/360+ Hz observations,
both binaries' controls and A/A variance before any comparative conclusion.

The native `NetCheckClient` does not use the normal `RenderWindow` frame-cap/accumulator
path. Its `-drawrate` repeats interpolated draws within each 60 Hz callback; it does not
establish the corresponding wall-clock refresh cadence. Do not translate Prime's
`--presentation-hz` to a native `-fpscap` or repeated-draw count and label that matched.
Native presentation-rate comparison remains unsupported by the current adapter.

The historical [PR #109 report](https://github.com/liveteklol/Fruity-Prime/pull/109#issuecomment-6062360519)
uses headless `-netcheck -headless -hitlog` clients. Its 94–95% victim and 93% observer
figures come from simulation contact/synthesized-effect event joins, not framebuffer
readback. The parser pairs shooter/victim/beam/launch within three frames, and nearby
damage/impact events within 750 ms; it lacks the full lifecycle identity used by this
probe. These are contact/presentation-event diagnostics. They cannot serve as an
actual-pixel baseline for the proposed 96%/95% release targets. Saved primary-source
responses and historical parser/logger files are retained under `artifacts/ncx/NCX-01/`.

No new gameplay default, fast settlement, movement enforcement, visual-fidelity percentage,
performance improvement or superiority claim is enabled by this measurement work.

## Rendered controls retained during implementation

`visibility-oracle-build-04.log` builds game/nettest with zero warnings/errors. The pure
pixel attribution, frozen Imperialist oracle and architecture routes pass. An attempted
`--frame-timing` route is unsupported and exits 2; it is retained as a command error,
not a timing test result.

`source-inputs-04.json` closes tracked and untracked C#/project/shader/JSON inputs for
that build. Python runner/exporter hashes are recorded separately by each invocation.
The input SHA256 is `5b9b7136f9cefd7ab84c2a327643a35a661e6df9d1bc79be9c2d1ba88d16e74b`.
Subsequent source edits require a new build/input manifest; they cannot relabel this run.

`prime-pixels-lan-04` captures actual native Power Beam trail/SingleParticle and exact
confirmed-cue items in three real clients on MP3 PROVING GROUND. The actual camera is
unchanged. Baseline/excluded/restored RGB and mask PNGs are retained; all 192 captured
samples have identical baseline/restored RGB, unchanged guarded gameplay/camera/lifecycle
state and zero new GL errors. Raw producer positives are 60/64, 5/64 and 49/64 by launch
order, including the spectator. The exporter must independently verify those PNGs and
join exact authority keys before any positive same-shot claim. These are capped samples,
not a camera-visibility or legal-hit percentage.

The campaign **FAILS**: the victim view refuses to spawn four authority-accepted sources.
Health/score convergence and bounded drain pass, but cannot override that failure.
Capture is intrusive: achieved rates are approximately 30.6/28.8/41.1 Hz with dropped
fixed steps, below the requested 60 Hz. No 60 Hz or performance certification follows.
The source-equivalent `prime-pixels-control-lan-04` without the exclusion probe **PASSES**
the campaign and bounded drain, has zero dropped fixed steps, and observes roughly
42.0/41.3/56.4 Hz. It still does not achieve every requested rate.

Investigation found repeated hashing of the whole engine assembly for every pixel sample.
The next build caches that diagnostic identity once per scene. Preserve the failed run
and re-run both capture and controls; do not patch gameplay or hide failed source rows.

`fruity-native-qualification-03.json` is the final successful private-profile native smoke
with symlink runtime staging and non-system runtime hashes. `fruity-udp-lan-01` records
two current-source native peers joining and receiving snapshots, with exit 0 and upstream
RESULT PASS. Overall comparison remains INCOMPLETE: the upstream harness lacks Prime's
bounded-drain contract, and screenshots alone do not establish exact same-shot pixels.

`visibility-fixtures-build-05.log` passes with zero warnings/errors. Pixel, independent
oracle, fixture-role/aim and architecture checks pass. `oracle-scene-05` retains 36 actual
native first-segment observations: 31 source-qualified historical contacts (body and head),
one miss, and four explicitly unavailable cases. The reused native fixture's 949 assertions
also pass. Its resources and placements are declared internal-admission setup, not naturally
acquired resources or authenticated UDP. The oracle consumes frozen source/body/obstacle
values, never a contested claim verdict; it does not certify damage eligibility or promote
those 31 geometric contacts into a legal-hit denominator.

The correct game route `-frametimingcheck` passes on the frozen build-04 binary, including
fixed-step, presentation/camera and RNG checks. This is chosen-time arithmetic validation,
separate from the measured achieved rates above.

## Shared-fixture controls and setup failures

All three native emulated fixtures now launch from hashed raw recipes staged in the
private app resource map directory before C++ metadata initialization. They confirm
actual slots 0/1 firing and slot 2 contributing no shots, shooting frames or deaths.
Their upstream harness still reports **FAIL**: its generic reference-position checks
reject the declared initial observer placement, and lateral/jump cases also expect
the untargeted observer to take damage. UDP and role controls pass separately; those
native failures remain recorded in `fruity-emulated-controls-results-02.json`. Do not
suppress them or claim equivalent passing gameplay from role checks alone.

The retained compiled-06 Prime cache and native control-02 map outputs share byte-exact
spawn/jump-pad entities and empty animation data. Decoded collision planes, vertices,
directed polygon edges, flags/layers and face multiplicities across all 385 grid cells
match the six-solid-box recipes. Collision serialization, model and node bytes differ.
`fruity-prime-retained-fixture-geometry-01.json` binds the files, parser and hashes.
This qualifies static encoding only; it does not prove runtime movement, contacts,
camera framing or visual parity.

`prime-ncx-static-pixels-06` **FAILS** with three lobby timeouts and no gameplay/pixel
reports. The custom room correctly requires the lobby and exact installed map package;
loose recipes alone cannot satisfy that package identity. The follow-up harness setup
uses the existing mapbundle CLI to cook each declared recipe once, copies identical
archives to every private process profile, and uses ordinary authenticated readiness
and owner-start commands. No map-availability or readiness bypass is authorized.

The first existing-controller A/A attempt, `aa-integrated-06-01`, is
**BLOCKED_CONCURRENT_TEST**. Its guard observed other builds/tests on the host and ran
zero of eighteen cases. No variance was measured; retry on a quiet host without
terminating independent work.

`fixture-lobby-build-07b.log` passes with zero warnings/errors after reducing build-tool
concurrency to four processors and serial MSBuild. The earlier build was interrupted
under memory pressure, without compiler diagnostics; its log and interruption manifest
remain. Game processes receive no processor-count override. Five focused routes pass,
including actual-slot/readiness guards. Checkpoint `c71986dd9` contains those setup changes.
Main `a3a73617e` is subsequently integrated at `bc83ad395`; its refreshed build and checks
are recorded separately rather than relabeling the frozen capture.

`prime-ncx-static-pixels-07b` uses the frozen 07b binary and identical once-cooked packages
for server and all three peers. Ordinary authenticated lobby start succeeds. Its pixel
controls pass independently in both PNG decoders: 192 samples, positives 64/64/2 and
negatives 0/0/62. Visible exact-paid victim cues occur in both participating cameras;
the connected observer has two uniquely paid-source flight samples and no visible
confirmed cue in its capped samples. Flight remains distinct from confirmation.
All 55 uniquely paid campaign sources are a separate denominator from those samples.
Full-source coverage remains INCOMPLETE due to the cap and ambiguous associations.

The campaign remains **FAIL** because the new no-fire observer inherits a local-shot
activity requirement. Both participant processes supply local evidence, and the
cross-process gameplay/drain checks have no additional failures or gaps. The corrective
observer guard requires both participants' observed alive lifecycles to join exact
committed source emissions, rejects every observer offer/emission and any relevant
overflow, and never substitutes received packets for activity. Rerun after compilation;
do not retroactively change the failed result. Requested 60 Hz achieved approximately
48.2/47.2/49.5 Hz with dropped fixed steps; intrusive capture remains disqualified for
CPU comparison. Retained postprocessing mistakes (a refused duplicate audit output and
an invalid summary-field lookup) do not alter raw images; the separately saved decoder
control proof and both matching audits establish the pixel checks.

## Clean checkpoint and passing capture

Clean source `928350a5ed7359c50d5e25e9453c0eab759d92e5` integrates main `a3a73617e`.
`checkpoint-build-09.log` passes with zero warnings/errors; all 31 off/on/native routes
pass. The native fidelity pack retains exactly the same 46 passing hashes and the
known missile-splash tick-177 Z difference, expected 52814 versus actual 52815.
No golden is regenerated. `native-fidelity-audit-09.json` checks the actual divergence
format; the initial diagnostic parser's incorrect FAIL-prefix assumption is retained.

`prime-ncx-static-pixels-09` **PASSES** the campaign and bounded drain with no failures
or incomplete cross-process evidence. Both PNG decoders independently agree on all
192 controls: positives 62/64/2 and negatives 2/0/62. Both participant cameras contain
visible exact-paid victim cues. The observer contributes no authored/emitted sources
and has two visible uniquely paid-source flight samples, distinct from confirmed cues.
The 55 uniquely paid sources do not become a 55-shot visibility denominator. Sampling
caps and ambiguous associations keep full-source pixel coverage INCOMPLETE.
Requested 60 Hz achieves approximately 50.0/49.4/51.0 Hz, with dropped fixed steps;
the intrusive probe remains unsuitable for CPU comparisons.

`aa-checkpoint-09-01` executes eight of eighteen planned synthetic ServerSim cases
before another test starts and the existing guard stops the matrix. Its raw paired
workload/variance records remain; the whole 2/4/8-player calibration is INCOMPLETE.
No real-network or performance-improvement claim follows from those partial cases.

The three native rendered custom-fixture controls fail during rendering startup,
before usable shot/frame/image reports. The native headless map generator omits
texture payloads but retains 128x128 texture descriptors; the generated 3488-byte
model cannot satisfy their 8192-byte reads. Headless controls do not exercise that
read path. A stock-room rendered control opens and saves real images, isolating this
failure to the custom generation/render combination. Retain all failed outputs and
qualify a full-data preparation route before accepting these rendered fixtures.

## Completed checkpoint-09 observations

All three rendered Prime emulated controls (static, lateral, jump) pass the existing
campaign and bounded drain without the intrusive pixel probe. All participants use
stock uncharged Power Beam and Samus; actual slots 0/1 fire and slot 2 observes.
The six additional static requested-rate cases also pass campaign/drain. Their source
is frozen `928350a5e`, not the subsequent main integration. The rate denominator is
presented frames / measured process elapsed seconds, including setup/drain overhead.
It measures render/swap-loop cadence, not monitor scanout.

| Requested cap Hz | Achieved loop Hz, slots 0 / 1 / 2 |
| --- | --- |
| 30 | 28.49 / 28.87 / 28.99 |
| 60 | 55.53 / 56.23 / 56.97 |
| 120 | 96.94 / 98.13 / 96.97 |
| 144 | 110.02 / 111.55 / 109.05 |
| 240 | 157.40 / 165.61 / 159.43 |
| 360 | 193.03 / 203.02 / 206.72 |
| 500 | 240.75 / 259.40 / 266.35 |

Every case records dropped fixed steps (6–20 per peer) and stalls (0–2). The observations
do not establish achieved 360+ Hz, identical combat hashes across all rates, or a CPU
benchmark. `prime-presentation-matrix-results-09.json` retains the six new commands;
the 60 Hz case and all three scenarios are in `prime-emulated-controls-results-09.json`.

`aa-checkpoint-09-02` completes all eighteen existing synthetic asset-backed ServerSim
runs: three alternating identical-binary pairs at each of 2/4/8 players, 300 warmup
and 3600 measured steps. All nine paired workload comparisons match; binary hashes
remain unchanged and validation has no errors. The raw driver exits with
**PAIRED_P99_REGRESSION_FLAG**. Paired p99 changes are approximately −3.41/+3.81/+1.75%
(2 players), −0.44/+5.03/−0.18% (4), and −6.43/−6.07/+6.65% (8). This records host
A/A variation, not a code regression or improvement. No pooled confidence interval,
real-UDP CPU comparison, native competitor A/A, or dedicated-host qualification exists.
Retain the prior blocked/partial attempts and their distinct statuses.

`fruity-rendered-controls-results-04.json` qualifies ordinary full-data native map
preparation before server/headless startup. Each 19872-byte model contains two complete
8192-byte texture payloads. All nine native peers now complete 1080 rendered loop frames
and save 129 hashed actual-view screenshots across three scenarios, with no observed
render-start exception. Assigned slots, UDP snapshots and no-fire observer controls
pass. Participant own-shot counts are 39/35 (static), 82/78 (lateral) and 47/44 (jump);
observer own shooting/shots/events/deaths remain zero. Loop FPS is about 52.4–54.8.

The native upstream harness still **FAILS**, including generic observer position-drift
and no-hit assertions. The run exit remains 1 and raw failure lines remain in the
manifest. Successful setup, rendering and role checks do not override that result.
These native images are unkeyed actual camera views, without an exact-shot exclusion
pass. Native drain, independently paced high-Hz comparison, legal-hit denominator and
competitor CPU A/A remain unavailable. This is a characterized comparator baseline,
not matched passing gameplay or superiority evidence.

Seed scope is fixed transport fault seeds, fixed default gameplay RNG startup words
`0x3DE9179B / 0`, and a deterministic frame/slot fixture schedule. `--netseed` configures
fault queues only. Authority snapshots subsequently supply gameplay RNG words; script
reset does not reseed gameplay RNG. Stock uncharged Power Beam has one projectile and
zero spread in both engines, skipping randomized spread. Idle sway, camera shake and
presentation effects still consume RNG; equal startup words do not prove equal pixels
or combat. The source audit is retained as `seed-scope-audit-01.json`.

Private extracted assets use independent writable APFS copy-on-write clones on macOS,
and ordinary copies elsewhere. Distinct-inode and mutation-isolation tests pass. No
hardlinks are used. Failed output trees and generated-before files remain preserved.

## Latest-main integration qualification

Main `46c101cdb7a4a25e2e8fb651181610d733b1fe11` includes CW-04d/e custom provenance/payment
boundaries and camera/replay/movement changes. Integration `cde48609f` retains those
boundaries: custom contacts clear the native witness before timeline logging, and the
segment scope still clears a previous native witness for custom projectiles. Existing
native-source observation remains opt-in. Checkpoint-09 evidence stays bound to its
original source; build10 and follow-up checks receive separate records.

Build11 at `4ca528ac0` additionally excludes typed custom sources before native oracle
buffer work. Its Release build has zero warnings/errors; all 31 off/on/native routes,
frame-timing checks and both 156-assertion custom rewind scenes pass. The new custom
regression deliberately forges matching native metadata and verifies that a custom
first segment changes no oracle row, overflow or coverage. Both actual timeline modes
are exercised. Fidelity again retains all 46 passing hashes and only the known missile
divergence; no goldens or engine hash change during qualification.

`prime-ncx-static-pixels-11` passes campaign and bounded drain on that integrated binary.
The unchanged exporter independently verifies 192 PNG controls: visible samples 64/64/1,
negative samples 0/0/63. Each participant view has 31 visible exact-paid confirmed cues.
The observer's one positive is flight, not a confirmed cue. Its 57 uniquely paid sources
remain a separate denominator; full-source coverage remains INCOMPLETE (12 association
limits). Requested 60 Hz achieves roughly 50.9/51.7/52.1 loop Hz, with 25/21/44 dropped
fixed steps; intrusive capture remains disqualified for CPU comparison. The first
attempt used an unrecognized runner argument and exited 2 before launching any game;
that command error log is retained separately from the corrected passing invocation.

## Acceptance interpretation

NCX-01's exit is a reproducible measurement foundation: baseline both binaries and
controls, honest denominators, exporter, positive visual exercise, measured A/A variance
and no added gameplay change. It does not require the later release's 96%/95% visibility,
15% CPU improvement, native refresh equivalence or every family to be certified here.
The characterized native FAILs are part of its baseline and remain FAIL. They provide
no passing comparative gameplay conclusion and must be resolved or explicitly excluded
from any later comparison. The complete A/A run measures variation even though its
performance threshold flags noise; it cannot qualify an optimization.

The scoped positive pixel controls and independent geometric oracle validate their
measurement mechanisms. Their caps, unavailable legal-damage denominator, unsupported
families and lack of native shot-keyed pixels are retained limitations. They authorize
no fast-payment family. NCX-02 may strengthen source/payment identity only after the
latest-main integration checks below pass; later certification must supply its own
source-qualified evidence.

The pre-existing [N13 live settlement coverage failure](../netcode2-n13.md) remains an
explicit remediation item for NCX-02/09/11. The short opt-in NCX campaign/drain controls
do not replace that exact workload or prove it fixed. Reproduce the original accepted
source/claim/payment/receipt accounting with this keyed exporter, classify every missing
terminal or bounded timeout, and retain its original FAIL/INCOMPLETE record until an
equivalent source-qualified live run passes. Synthetic A/A cannot close live settlement.

The latest-main probe-off `prime-ncx-static-control-11` also passes campaign and bounded
drain. NCX-01's scoped acceptance is complete; native comparison failures, unavailable
denominators, A/A noise, unachieved high refresh rates and the N13 remediation item remain
explicit later qualifications. `code-stability-11.json` verifies all snapshotted code/data
inputs remain unchanged except the noncompiled measurement-result manifest itself.
