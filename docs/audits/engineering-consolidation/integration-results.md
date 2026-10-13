# Engineering consolidation: measured integration and remaining gates

Updated October 13, 2026 UTC. All changes remain proposals in open PRs. No PR has
been merged, and the primary checkout remains clean on `main`. A fresh fetch
still resolves `origin/main` to `91b1c0c6c44e8c90effd250729709f791a84765e`.
The original [baseline](baseline.md), its [manifest](baseline-evidence.json), and
historical performance reports are preserved. This report adds later evidence;
it does not reinterpret earlier failed trials as passes.

## Source and review boundaries

Each PR branches from the pinned main. The local integration branches are
validation assemblies of proposed commits, not replacements for focused review:

- Lifecycle bundle `749aa12dc89d41dc83823eafe0929c13b1ebfb99` combines the
  attribution tools with SFX ownership `587918a1`, shader ownership `e0430a2f`,
  and failed music-startup cleanup `48b00564`. Its measured client SHA-256 is
  `86f075e7c8dd7a06277b4050feedc796680b97a2f259615e2aa81e1faab37328`.
- Initial feature bundle `164990e60244ecdb21fdcc4e98e91f3ea097090b` adds updater staging,
  Enhanced Hunters retirement, collision query ownership including the corrected
  8,192-candidate retention budget, and all three lifecycle CI steps. The workflow
  integration preserves both audio and music startup tests at their shared
  insertion point. The corrected nettest binary SHA-256 is
  `8bc3701ba6d37db067da1b86be6168153a58b0edc3610fca15da86f3c7b133c3`.
- Final feature bundle `87617f47dd67d4cae77931c43f9dce6af87b3fd4` applies the
  independently reviewed test-fixture repair `81ef736f` from #499. Production
  combat behavior is unchanged; the final compiled checks run the combined
  source, not just repaired fixtures against a prebuilt library.
  Nettest/client SHA-256 are
  `273294e1185e234560f0f1021900a73f41dafe294884f8c7c9b9873afbcffd7d` /
  `2edee59479183f3599ae4fded663be2c357176388743167c10ab1acf18cf2a40`.
  Release compilation passes with 31 existing warnings and zero errors.

Full commands, binary/source identities, results, and SHA-256 of machine-local
raw evidence are in [integration-evidence.json](integration-evidence.json).
The environment is SDK 10.0.401/runtime 10.0.12, macOS 27 ARM64, Apple M4 Pro,
OpenGL 2.1 Metal - 91.7. No retail data is committed.

| PR | Concern | Dependency and review gate |
| --- | --- | --- |
| [#488](https://github.com/AntiNotAnti/Project-Prime/pull/488) | Protocol/replay documentation | Phase 0; source is protocol 53, replay formats 1–5 |
| [#489](https://github.com/AntiNotAnti/Project-Prime/pull/489) | Baseline, owner inventory, measured integration | Phase 0; no runtime changes |
| [#493](https://github.com/AntiNotAnti/Project-Prime/pull/493) | Native attribution tools and measured isolation | Phase 0; tooling evidence precedes lifecycle conclusions |
| [#492](https://github.com/AntiNotAnti/Project-Prime/pull/492) | Shader ownership and rollback | Attribution/negative control; exact failure recovery and visual parity |
| [#494](https://github.com/AntiNotAnti/Project-Prime/pull/494) | SFX ownership and serialized close | Attribution/partial acquisition evidence; reload/task/native adapter gates |
| [#495](https://github.com/AntiNotAnti/Project-Prime/pull/495) | Failed MiniAudio startup cleanup | Focused startup negative control; broader loader remains separate |
| [#498](https://github.com/AntiNotAnti/Project-Prime/pull/498) | Retire live Enhanced Hunters | Phase 0; packet/replay fingerprint and Balanced Mode remain unchanged |
| [#497](https://github.com/AntiNotAnti/Project-Prime/pull/497) | Scoped collision workspace | Phase 0; nested/independent scene, exact traversal, allocation gates |
| [#496](https://github.com/AntiNotAnti/Project-Prime/pull/496) | AI/HUD allocation evidence | Merged AI #481–#485; negative evidence rejects speculative rewrites |
| [#490](https://github.com/AntiNotAnti/Project-Prime/pull/490) | Updater rejection/cancellation cleanup | Independent ingress slice; hash/rollback contracts preserved |
| [#491](https://github.com/AntiNotAnti/Project-Prime/pull/491) | Tracked Finder metadata | Independent hygiene; 63,516 repository bytes, no install-size claim |
| [#499](https://github.com/AntiNotAnti/Project-Prime/pull/499) | Repair inherited gameplay test setup | Independent baseline defect; settled spawn, current projectile identity, explicit telemetry window; needed for complete content-backed integration checks |

These are evidence dependencies, not permission to merge. Recheck latest main,
resolve any overlapping workflow hunks, and rerun integration before future
integration. Do not duplicate upstream UI #424 or authority/pickup #472–#474.

## Native isolation and proven lifetime changes

Baseline and lifecycle candidate each completed four 60-cycle isolation arms:
audio, Studio, map/GL without audio, and combined. The baseline arms ran in
combined/map/Studio/audio order; the candidate ran audio/Studio/map/combined.
Within each binary the arms use the same settings and assets. Every arm includes
diagnostic drain followed by 30 one-second quiescent observations. Drain,
readbacks, and forced diagnostic collections make these lifecycle measurements,
not FPS measurements. Audio-only still has the common runtime/read-cache harness
initialization; it does not mean an otherwise empty process.

| Arm | Baseline mean quiescent RSS MiB | Candidate mean quiescent RSS MiB | Baseline/candidate last RSS MiB | Baseline/candidate post-warmup RSS slope MiB/cycle |
| --- | ---: | ---: | ---: | ---: |
| Audio | 581.23 | 535.32 | 581.19 / 535.28 | 4.775 / -0.480 |
| Studio | 338.18 | 337.01 | 338.19 / 337.02 | 0.775 / 0.776 |
| Map/GL | 570.02 | 572.91 | 569.98 / 572.88 | 1.189 / 1.083 |
| Combined | 809.94 | 954.03 | 809.81 / 886.62 | 2.834 / 5.868 |

Slopes use post-candidate-unload samples, or post-Studio-unload for Studio-only,
excluding the first five cycles. The combined candidate's falling quiescent RSS
shows why last RSS and the 30-sample mean must be distinguished. The audio arm's
managed live/LOH differs substantially between these first trials: about
112.40/66.25 MiB baseline versus 428.76/381.88 MiB candidate after drain.
Consequently this table alone neither establishes a native-memory improvement
nor attributes the growth. Matched retention/VM follow-up is reported in #493.

The same retention harness then ran four 60-cycle audio trials in baseline /
candidate and candidate / baseline order. All 120 shutdown tasks per trial
completed; every observed nonempty SFX instance, bank, sample, channel payload and
stream graph was collected after diagnostic drain. Managed live/LOH matched
within each pair (about 464.5/383.88 MiB forward and 148.2/68.25 MiB reverse),
while post-second-GC RSS was 294.56/295.16 MiB lower for the candidate in the two
orders. Reverse-order VM evidence places most of this difference in native
default malloc allocated bytes (about 283.7 versus 5.7 MiB) and malloc-small
residency. These are measured subsystem/allocator differences, not an inferred
RSS-minus-managed native estimate.

A further candidate confirmation completes 120 cycles/240 loads and shutdowns,
with zero incomplete/faulted/cancelled tasks and no observed nonempty SFX graphs
retained. RSS reaches about 257 MiB after drain with 120 seconds of quiescence.
External gcdump collection adds one diagnostic Gen-2 collection, explicitly
recorded; it is not a production policy change or FPS result. The 60-cycle heap
capture identifies six live byte arrays of 1/2/4/8/16/32 MiB, totaling 63 MiB,
rooted in .NET's SharedArrayPool thread-local arrays. They account for most of
the lower 68.25-MiB LOH state. The 120-cycle graph has no such large byte arrays
and LOH is about 6.5 MiB, including diagnostic observation arrays. The previous
383.88-MiB LOH state did not recur in the captured heap; its additional roots
remain unproven, and are not extrapolated from the smaller state.

Native malloc stack logging is supported. The baseline audio capture, after
drain, identifies surviving Apple OpenAL source-creation allocations:
`vector<BufferInfo>::reserve` → `OALSource::OALSource` → `OALContext::AddSource`
→ `alGenSources`. The installed tool's documentation confirms `-allBySize`
reports currently live allocations without `-highWaterMark` or `-allEvents`.
The paired profiled baseline/candidate records 347.285 / 1.181 MiB in currently
live non-mmap allocation groups with OpenAL frames. Within those groups,
source `BufferInfo` vector allocations fall from 337.437 MiB to zero. Both use
the same immutable retention harness, 60 cycles/120 loads, stack logging enabled,
and sampling at least three seconds after the final cycle/drain. This attributes
the surviving baseline population to OpenAL source creation and demonstrates its
removal in the lifecycle candidate. Anonymous VM/runtime reservations are
classified separately. Profiler overhead and the difference between live
allocations and unreachable leaks remain explicit; deletion versus serialization
versus library-specific cleanup is not isolated by this bundle. The 120-cycle
map/GL source-stack follow-up is maintained in #493's report and manifest.

The map/GL and combined arms have **240 across-build pairs with exact RGBA and
depth hashes**, matching cycle, room, stage, frame count, and dimensions. All
tracked owned GL/resource counters are zero after candidate teardown. Baseline
left one deleted-but-still-bound program recognized by GL; the shader fix
explicitly unbinds an owned current program, yielding zero recognized/pending
program names and current program zero. Another scene's binding is preserved.

The independent real GL regression passes 120 repetitions and 1,680 injected
shader/link failure scenarios, with 120 exact recovery frames. Linux Mesa CI also
passes the new 120-repetition shader step. SFX partial-acquisition, context
rebinding, duplicate unload, serialized worker-close, stalled-close fallback,
and linked Android adapter checks pass 750 assertions and 120-cycle stresses.
Owned SFX buffer acquisition falls from 129 to 65 because the original extra 64
buffers were unused. The Android adapter previously accumulated native mixer
entries because its context destroy was a no-op; explicit deletion removes owned
sources before buffers and leaves the next instance's voices intact. Android
Release/AOT compilation passes; physical audible/device-loss/pause-resume testing
remains NOT_RUN. Failed music startup now releases the acquired engine, with four
passing failure-path checks and 120 fake engine cycles.

No production GC policy changed. Graphics/Studio RSS still increases while
tracked owner counters reach zero. The current boundedness gate remains open
until allocation/VM evidence distinguishes live native allocation from driver or
allocator retention and confirms sufficient-cycle behavior. Music.Load still
needs request ownership, private construction, stale-request fencing, publication,
cancel/removal coordination, and callback quiescence; startup cleanup does not
solve that larger source-backed problem.

## Frame and simulation regression evidence

The unprofiled lifecycle bundle was compared with pinned main in A/B/B/A order.
All four runs use eight bots, MP3 PROVING GROUND, 1920×1080, High shadows,
VSync Off, uncapped presentation, 24 seconds total, 240 simulation steps of warmup,
and approximately 20 seconds measured. Statistics below are the median of two
per-run statistics per arm, not aggregate percentiles.

| Metric | Baseline | Candidate | Change |
| --- | ---: | ---: | ---: |
| Mean completed frame FPS | 406.68 | 404.71 | -0.48% |
| Frame interval p50 ms | 2.0502 | 2.0531 | +0.14% |
| Frame interval p95 ms | 4.7101 | 4.7797 | +1.48% |
| Frame interval p99 ms | 7.3620 | 7.3893 | +0.37% |
| 1% low estimate FPS | 135.96 | 135.48 | -0.36% |
| 0.1% low estimate FPS | 106.84 | 110.46 | +3.39% |
| Allocated bytes/rendered frame | 30,214.89 | 30,215.86 | +0.003% |

All runs have zero dropped simulation steps and zero stalls, with measured
simulation cadence 59.940–59.981 Hz. These results support the scoped regression
check; two runs per arm do not support a major FPS improvement claim. Frame
intervals include presentation and are not CPU render submission timings. No GPU
time, transition responsiveness, cross-platform gain, or complete p99.9/simulation
CPU-tail acceptance is claimed.

Paired eight-second presentation smoke also passes at caps 60/120/144/240 with
solo Off/two-player Low/eight-player High/eight-player Ultra settings respectively,
and zero dropped steps/stalls. Actual baseline/candidate FPS: 57.64/57.53,
110.08/102.03, 141.03/141.28, 234.62/234.96. Caps are upper bounds; neither
120-Hz run attained the cap. Concurrent headless compilation/diagnostic work may
affect this short smoke. It does not replace repeated cap accuracy, input latency,
suspend/focus, physical-device, or thermal gates.

## Integration checks and superseded invocations

The first integration attempt used `--enhanced-retirement-replay` and `--advanced`,
which were not registered, and omitted the required fixture directory from
`--enhanced-retirement-scene`. The harness fell through to its hostname mode and
failed DNS resolution. These are invocation errors, not introduced engine bugs.
The original logs and `artifacts/integration-checks/manifest.json` remain intact;
the corrected, hashed manifest explicitly supersedes them.

| Check | Status | Evidence and practical limit |
| --- | --- | --- |
| Retirement eligibility/settings/wire/archive contracts | PASS | 80 checks, `--enhanced-retirement` |
| Frozen main-generated v9 archive and continuation | PASS | 46 checks, `--enhanced-retirement-scene <assets> <fixtureDir>`; synthetic headless fixture |
| Forced retired rules versus supported live/Balanced world | PASS | 20 checks, `--enhanced-scene <assets>` |
| Advanced rule contracts | PASS | 130 checks, `--advanced-rules` |
| Game modes / replay formats | PASS | Existing correct integration invocations; 2,198 / 3,034 checks |
| Protocol / lobby / waitlist / transport | PASS | Protocol 18 regression, 1,172-byte fast packet and zero decode allocation; 7,747 lobby and 309 waitlist assertions; eight impaired peers, 1,784 intents, 128 exact-once controls, 400-ms client pump stall |
| Collision integration | PASS | 618 exact golden rows; 32,000 concurrent queries, 2,000 independent-scene checks, nested leases/dedup, references cleared |
| Warm collision allocation | PASS | 40,000 normal queries and 100 queries each at 4,225/8,192 candidates allocate zero bytes |
| Above-budget collision query | PASS with limit | 8,193 candidates completes and clears references, discards its workspace; universal allocation neutrality not claimed |
| Full combat and advanced content-backed scene suites | PASS after fixture repair | 204 combat / 347 Balanced scene assertions; failures reproduced on main and fixed in test setup only by #499 |
| Broad seven-Hunter rendered historical replay/UI/input/export/physical updater matrix | NOT_RUN | Headless/failure-path coverage does not substitute for these gates |

The collision PR's first 2,048-candidate retention cap caused 43,387,200 bytes
of allocation per 100 warm 4,225-candidate queries. That candidate was rejected.
The current explicit 8,192-candidate budget restores zero warmed allocations in
the characterized range, retaining at most eight workspaces and 65,536 candidate
objects per simulation thread/lane. Very large valid maps can exceed this budget;
queries still complete but their scratch is discarded. The original 2,048-cap
performance timing does not validate the corrected candidate and is preserved as
historical/contaminated evidence, with no FPS gain claim.

The content-backed failures had two concrete fixture causes. A preceding recovery
fixture left the shooter at Y=32.341797; fall-damage receipts removed 26 and then
75 HP while the fired-shot count stayed zero. Restoring the original settled pose
through actual `Spawn` before the independent held-input section fixes the combat
test without clearing retained projectile flights. The affinity fixture supplied
unstamped synthetic projectiles, so the current launch-identity gate correctly
rejected their status effects. Capturing owner Hunter metadata and using the
production lifecycle stamp restores the original disrupt/freeze/burn assertions.
Finally, room setup had already emitted Hunter-pick telemetry before the seven
explicit fixture spawns. Resetting only this fixture's observation window gives
one pick per Hunter, now asserted individually as well as by total.

The same repairs pass combat 204 and Balanced 347 against both the pristine-main
and integrated libraries. Original failures, invalid invocations, controlled arms,
and exact log hashes remain in #499's durable report. The final combined source
passes all 12 final integration invocations after a rebuild; no production damage/status/telemetry rule is
changed to make an assertion pass.

## Phase disposition and next implementation gates

| Phase | Implemented/proposed or evidence disposition | Remaining gate before acceptance/next dependent work |
| --- | --- | --- |
| 0 baseline | Source/PR/architecture/owner inventory, protocol correction, manifests | Full representative workload matrix and warning classification; primary source still pinned |
| 1 native/lifecycle | Isolation tools; proven shader/SFX/music-startup ownership fixes | Finish allocator/native attribution and longer confirmation; Music loader ownership; physical audio/EGL; GC transition/residency A/B |
| 2 obsolete systems | Live Enhanced Hunters retirement and Finder cleanup | Seven-Hunter rendered historical compatibility/UI parity; archive type deletion needs schema migration; Avalonia remains until UI #424 route/platform parity |
| 3 GL efficiency | Existing 25 policies retained; exact scoped visuals/frame regression | CPU/GPU/present draw attribution after lifecycle gate; repeated speed evidence; experimental depth/procedural flags stay opt-in |
| 4 collision/scene | Explicit nested reusable collision query owner with bounded pool | Current-head CI/integration, physical parity and above-budget allocation limits; broader seams only after this gate |
| 5 network/server | Existing authoritative/bounded lanes preserved; impairment/contracts checked | Recheck #472–#474; scoped profiling, 2/4/8-peer gameplay/JIP/reconnect/rotation and shutdown ownership before refactor |
| 6 replay/Studio/editor | Existing schema/checkpoints preserved; exact scoped replay/hash continuation | Studio/export/audio/cancel/HD identity and long-session memory; editor/import/playtest profiling |
| 7 AI/HUD/input | Profiled actual navigation at 1/4/8 bots; tested stack-promoted scratch allocates zero; event-only HUD rewrite rejected | Other runtimes/AOT/maps, full cost/behavior and physical input/thermal evidence; no unjustified rewrite |
| 8 build/CI | Platform/AOT/startup matrix preserved; native regressions added to CI | Current-head CI/package smoke, warning/native archive inventory; no install-size claim without bytes |
| 9 robustness | Updater failure/cancellation stage cleanup, existing bounds reused | Large-entry cancellation, physical install/relaunch, expansion compatibility and broader malformed ingress matrix |
| 10 acceptance | Focused local integration and evidence-linked PR queue | All applicable final-head checks, unresolved P0 attribution/Music loader and full platform/gameplay gates |

Full consolidation acceptance is **not claimed**. No repository protection or
release settings were mutated. Review rollback is per focused PR/commit; no new
protocol, replay layout, simulation rate, graphics backend, gameplay optimization
default, or production collection policy is introduced by the lifecycle fixes.

## CI evidence

CI is checked against each PR's actual current head, including workflow run head
identity, rather than an older green commit. The snapshot at 05:29:23 UTC has no
failed current-head checks; #488–#492, #495 and #496 are fully green, with other
platform jobs pending/queued. Linux Mesa's new shader failure step is confirmed
PASS, resolving the earlier report's PENDING remote status. A refreshed compact
snapshot and final PR descriptions record later heads/status; queued checks are
not passes. This is a timestamped snapshot, including #489's previous head;
subsequent documentation pushes require their own CI. Final current-head status
is recorded in each PR description/checks tab. Green hosted builds do not
establish physical-device acceptance.
