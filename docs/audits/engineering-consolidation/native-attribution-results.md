# Native-memory attribution results

October 13, 2026. Diagnostic PR #493; baseline source
`91b1c0c6c44e8c90effd250729709f791a84765e`. These are macOS 27 arm64,
Apple M4 Pro lifecycle measurements with .NET SDK 10.0.401. They are not FPS,
cross-platform acceptance, or global memory-boundedness evidence. No merge is
requested. The historical report keeps its original fixture scope.

The [evidence index](native-attribution-results.json) records 61 raw artifact
paths and SHA-256 hashes, the full-arm matrix, exact across-build parity, and
the final graphics profiling statistics. Raw data remains in the named local
worktrees; captures, game assets and bulky allocation stacks are not committed.
The central baseline/report PR #489 records additional preserved artifacts.

## Full 60-cycle arms and visual parity

All eight arms passed: baseline order combined/map-gl/studio/audio, lifecycle
candidate order audio/studio/map-gl/combined. Each arm used the same invariant
settings: eight players, 1920x1080, 120 exact simulation steps per map load,
fixed RNG, High shadows, all five failure stages, and 30 quiescent samples at
one-second intervals. Only `PROJECT_PRIME_GL_FORCE_LEGACY=0` was explicitly set
among GL policies; other policies used production defaults. Each map arm had
120 loads; each Studio arm had 60 renderer sessions with three cache checks;
each audio arm had 120 SFX loads/updates/shutdowns and no GL context.

| Arm | Baseline quiescent RSS mean / final MiB | Candidate quiescent RSS mean / final MiB |
| --- | ---: | ---: |
| combined | 809.939 / 809.812 | 954.027 / 886.625 |
| map-gl | 570.022 / 569.984 | 572.910 / 572.875 |
| studio | 338.176 / 338.188 | 337.007 / 337.016 |
| audio | 581.227 / 581.188 | 535.322 / 535.281 |

The candidate combined arm has materially changing quiescent RSS. Its final
sample is not its mean, and these first campaigns do not establish an audio
gain or a graphics plateau. GC/LOH states varied between processes, motivating
the matched retention controls below.

The root-authored `lifecycle-crossbuild-parity.json` compares the exact cycle,
room, reference/injected selection, stage, dimensions, simulation frame, RGBA
hash and depth hash. Both map-gl and combined had 120 pairs and zero mismatches
across baseline/candidate builds. This proves parity for those frozen fixture
inputs, not all gameplay/replay/full editor UI. Baseline owner program fields
were zero but a current program could remain recognized and deletion-pending;
the candidate additionally unbinds it and reports zero recognized/pending/current
programs. Pending deletion is never described as fully released native storage.

## Matched audio workload and weak retention controls

The opt-in probe returns a `NoInlining` workload helper before collecting. It
weakly observes every actual SFX instance, candidate resource owner, bank lists,
SoundData, sample/stream objects and every already-created encoded/decoded
sample/channel/stream payload. It never evaluates an uncreated Lazy. Returned
telemetry is scalar-only; observations contain weak references. A separately
marked second full GC is diagnostic-only. Existing default fixtures/schema and
production GC policy remain unchanged.

One immutable harness DLL was used against both client copies, with API/build
checks against each client. Forward order was baseline then candidate; reverse
order was candidate then baseline. Each trial completed 60 cycles/120 native
loads and all shutdown tasks, without faults, cancellation or backlog.

| Order/build | RSS after second GC MiB | Managed estimate MiB | LOH MiB |
| --- | ---: | ---: | ---: |
| forward baseline | 735.344 | 464.580 | 383.877 |
| forward candidate | 440.781 | 464.511 | 383.877 |
| reverse baseline | 589.750 | 148.240 | 68.251 |
| reverse candidate | 294.594 | 148.109 | 68.251 |

Both matched pairs repeat an approximately 295 MiB RSS difference while their
managed/LOH state matches within each pair. The between-pair managed state is
different and unresolved by these counters. All 120 actual instances and
observed banks/objects/nonempty payloads were dead after drain. Surviving shared
empty arrays had zero payload bytes; repeated weak observations are not unique
object counts. This excludes the observed bank graph as the retained owner in
these captures, not every possible application root.

The reverse pair has successful live `vmmap -summary` captures: physical
footprint 538.8M to 243.5M, Malloc Small resident 306.7M to 32.1M, and
DefaultMallocZone allocated 283.8M to 5842K (about 5.705 MiB), resident 346.0M
to 69.5M. OS units are rounded. These allocator categories alone do not identify
an allocation owner or classify the entire RSS gap as a live leak. The forward
candidate VM sampling window was missed; it is not reported as a paired capture.

## Audio allocation-origin proof

A separate matched baseline/candidate audio60 profiling pair used the same
immutable harness and `MallocStackLogging=1`. Both passed and were sampled at
least three seconds after the final cycle while still quiescent. Both
`vmmap -summary PID` and `malloc_history PID -allBySize -fullStacks` succeeded.
Installed tool help/man text is preserved: `-allBySize` without
`-highWaterMark` or `-allEvents` reports currently outstanding allocation-site
groups, not cumulative historical traffic.

| Outstanding non-mmap stack groups | Baseline MiB / calls | Candidate MiB / calls |
| --- | ---: | ---: |
| stacks containing OpenAL | 347.285 / 68,691 | 1.181 / 9,484 |
| OpenAL BufferInfo source vectors | 337.437 / 23,070 | 0 / 0 |

The dominant source stack is OpenAL `std::vector<BufferInfo>::reserve` through
`OALSource::OALSource`, `OALContext::AddSource`, and `alGenSources`. That proves
the surviving source-vector allocation population in the baseline and its
absence in the candidate control. It does not factorially isolate source
deletion from close serialization/library cleanup, or equate all RSS savings
with live vectors. Explicit CLR mmap groups are separate reserved/mapped sizes,
not resident bytes. Stack logging perturbs allocator/CPU behavior; macOS logged
lite mode and a failure to exclude its pages from footprint. No FPS or direct
comparison against unprofiled RSS is claimed.

## Managed heap confirmations

The candidate audio120 probe completed 240 loads and shutdowns with every
nonempty observed payload dead. After first/second diagnostic GC, LOH was
6.5 MiB; a subsequent marked external `dotnet-gcdump` collection requested an
extra Gen2 GC. The final RSS was 257.969 MiB. The captured live graph was
66,534,838 bytes/1,318,030 objects, with no byte array above 1 MiB. This is a
profiling confirmation with an extra collection, not a plain plateau campaign.

A candidate audio60 capture reproduced the lower 68.251 MiB LOH state. Its
extra-GC live graph was 113,335,904 bytes/912,240 objects. Offline root-path
inspection found six byte arrays of 1, 2, 4, 8, 16 and 32 MiB: 63 MiB of payload
plus 144 bytes of headers. Each path went from local roots through Object arrays
to `System.Buffers.SharedArrayPoolThreadLocalArray[]`. This proves pool retention
for those six arrays in that capture. It does not prove the pool caused the
additional roughly 315.6 MiB in the earlier high-LOH state, which did not recur
in the captured heap. Exact roots of that high state remain unproven. Gcdumps,
the small offline reader and root paths are hashed in the evidence index.

## Graphics 120-cycle allocation capture

The existing immutable candidate graphics binary ran map-gl120 with 240 map
loads, all five stages, one GL context, fixed settings and 120 one-second
quiescent samples. It passed; every unload reported zero owned resources and
zero recognized/deletion-pending/current programs. Audio was the stub and
Studio was absent. Native stack logging was enabled only for this separate
profiling run. Live VM and outstanding stack capture both succeeded.

Quiescent RSS mean/final was 781.778/781.781 MiB, range 781.156–782.234;
OLS slope was 0.000209 MiB/s (R² 0.00479) over 121.66 seconds. GC committed
112.8125 MiB and LOH 31.822 MiB stayed constant during this idle interval.
During repeated loads, however, latter-half RSS still increased by
2.247 MiB/cycle at the reference cleanup point and 2.261 MiB/cycle at the
injected cleanup point (R² 0.995/0.989). Quiet stability does not establish
repeated-load boundedness.

| Surviving allocation-site partition | MiB | Interpretation |
| --- | ---: | --- |
| AGX FragmentProgramVariant non-mmap stacks | 185.224 | Mainly C++ string allocations through AGX shader variant compilation |
| Other graphics stack groups | 71.154 | AGX/Metal/OpenGL/IOGPU origin; includes platform resource records |
| IOSurface stack groups | 39.616 | Drawable/surface creation and lookup origin |
| CLR non-mmap stack groups | 21.231 | Native runtime allocation sites |
| Explicit mmap/VM groups | 5035.219 | Reserved/mapped lengths; not live malloc or resident bytes |

The disjoint partition checks explicit mmap/VM in the first two frames, then
fragment variants, vertex variants, IOSurface, other graphics modules, CLR and
other. The raw stacks and partition are preserved. Platform resource records
must not be summed into malloc-zone live bytes. The largest individual
fragment-variant string groups were 15.398, 9.188, 9.188 and 8.141 MiB. Drawable
groups were 23.766 and 15.844 MiB, and an AGX heap resource group was 9 MiB.
There were no OpenAL stack groups in this map-only capture.

VM residency included IOAccelerator(graphics) 59.2M, IOSurface 39.6M and
owned-unmapped(graphics) 104.6M; Untagged was 176.6M. Stack-logging lite zone
allocated 232.1M and resident 252.9M; profiling tool data was 63.9M resident.
Physical footprint was 742.9M. VM categories, stack groups and RSS have distinct
accounting and capture times. The driver-origin variant/drawable allocations
are now identifiable; this single outstanding capture cannot distinguish a
bounded driver cache from a native leak or identify all loader/managed roots.

## Remaining acceptance gates and reproduction

P0 graphics memory boundedness remains open. A follow-up can compare surviving
AGX groups over increasing fixed repeated-load counts and across explicit
context disposal/recreation, with unchanged workload identity and managed
loader-root inspection. Acceptance needs a demonstrated plateau or an
identified owner/lifetime fix; an idle plateau and zero owned IDs alone do not
supply it. Full Studio UI, replay-only operation, loader/model retention roots,
Windows and Android remain outside these fixtures. No production GC policy
change or general native leak claim is supported here.

Use the [tool README](../../../tools/native-render-check/README.md) for invariant
campaign, opt-in probe, VM and outstanding-stack commands. Release harness
compilation/API checks passed against both clients; the device-free weak
selftest passed. All 19 Python tests passed, and the existing OpenGL CI job
passed at tooling commit `5190124a434fd40ef32fdb8260d0389f1efc6057`. The final
documentation commit requires its own CI status; unrelated checks are not
claimed here. Initial failed pilots, incomplete capture attempts and profiler
markers remain preserved.

Immutable DLL identities:

- Baseline client: `22c7eb0fb0e59c7e40fa656c4bda1c20a58c42b5628e3fdd5f47fb79fb6ff0b2`.
- Lifecycle candidate client: `86f075e7c8dd7a06277b4050feedc796680b97a2f259615e2aa81e1faab37328`.
- Matched retention harness: `0be05bd547efffbba64afae93646fc427733b1bc21cc3f822d1b37fa28aeb5b1`.
- Final map120 harness: `9567c96848b3adf02146043da840560fc4fded71a365b16ee6266b3850b461f7`.

Runner source identity describes the runner checkout, not an inferred rebuild
of an external immutable DLL. DLL hashes/MVIDs and embedded commands govern
binary identity. Profiling environmental additions live in the separate
profiling manifests, alongside the ordinary invariant runner manifest.
