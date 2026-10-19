# NCX-01 measurement contract and work in progress

NCX-01 is **in progress**. Keyed instrumentation, a narrow independent collision oracle,
stationary/lateral/jump fixture definitions and actual-view pixel attribution are
implemented. Passing matched workload controls, achieved presentation-rate observations
and A/A variance are still required before this slice can pass its exit gate. Do not
start NCX-02 or certify a fast-pay family from these checks.

The prepared-frame probe and narrow frozen-contact oracle are now implemented. They
remain qualification work: constructed tests pass, actual pixel controls have been
captured, and matched fixtures/A/A plus a passing capture workload remain pending.

The first timeline integration base was `73d7586bd9b8560bce699f032241dfc320fd6799`: it includes
the intervening replay audio ownership fix (#728) and custom weapon payload foundation
(#729). The custom payload foundation does not enable a networked M7 or make an unknown
custom source eligible for native proof. Native protocol remains 59.

The current tested code is `062f868fdda0fc9b302616cc13745c0bfde98b3b`, integrating main
`2e3eeef10` through CW-04b/CW-04c and the intervening movement setup/replay-label work.
Its Release build has zero warnings/errors and all 27 focused off/on/native routes pass.
The [validation manifest](../../tools/nettest/baselines/ncx-measurement.json) remains
IN_PROGRESS and retains every incomplete gate and prior failure.

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

Native smoke initially failed because the development app lacked the GLFW runtime path.
Local staging uses symlinks to the already linked, versioned Homebrew GLFW/OpenAL libraries
to avoid duplicate Objective-C classes from loading a second copied GLFW image. Runtime
libraries and adapter must be hashed. Smoke and real-client controls still need final
package-qualified records. The native combat fixture currently reaches an OpenGL-session
requirement without a current context after its earlier assertions; its nonzero exit is
retained as FAIL, not promoted from its many preceding PASS lines.

## Remaining NCX-01 gate

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
performance improvement or superiority claim is enabled by this work in progress.

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
