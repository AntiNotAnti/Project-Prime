# Context teardown and replay memory controls

October 13, 2026. Follow-up diagnostic PR #505, stacked on #493. No production
GC/cache policy changes or merges. Hardware: macOS 27 arm64, Apple M4 Pro,
.NET SDK 10.0.401. Results identify allocation origins and owner retirement;
they do not establish all-platform acceptance or a native leak classification.

## Context workload and identity

One process ran 120 cycles in an actual window/context, disposed it, then ran
180 cycles in a freshly created unshared window/context: 600 total map loads.
Each load advanced 120 exact simulation steps. Both contexts completed their
ordinary exact color/depth reference/failure comparisons and scoped ownership
checks. All five allocation-failure stages were exercised. Actual windows were
alive/current before disposal and `Exists=false` after disposal. The helper
returns before post-disposal sampling; no additional GC is requested after
context teardown. Managed window weak-alive observations without another GC
are not retaining-root evidence.

Inputs were eight players (Samus and seven Spire), bots false, fixed RNG
12345/98765, 1920x1080, Original preset, requested uncapped rate, High shadows,
red outlines, and alternating MP9 CRYOCHASM / MP3 PROVING GROUND. This differs
from the performance ABBA fixture's eight bots. User data was the existing
operator-owned `/Users/jarrett/Library/Application Support/Project Prime`;
it was not an isolated settings copy. No full immutable asset/settings-tree
hash was captured. Only GL FORCE_LEGACY=0 was explicitly set among GL policies.
The separate profiling run added MallocStackLogging=1.

- Context harness SHA-256: `de525d7fa93bb768a3d8f8cc08941f51a48c897e8a7e56417fe1230d4c801b35`.
- Candidate client SHA-256: `86f075e7c8dd7a06277b4050feedc796680b97a2f259615e2aa81e1faab37328`.
- Runner source at launch: `12de2149`; harness source was the earlier context-control slice.

The runner rechecked both binary hashes after the entire campaign. Four live
VM/malloc captures succeeded: at least three seconds after the final cycle in
each still-live context and after each disposal. Commands were `vmmap -summary
PID` and `malloc_history PID -allBySize -fullStacks`, without highWaterMark or
allEvents. The installed tool's outstanding-group semantics were verified in
the earlier #493 evidence. Stack logging changes allocator/CPU behavior; no
FPS comparison against unprofiled runs is made. Native work was serial.

## Surviving graphics allocation sites

| Capture | AGX fragment groups MiB | Direct malloc-prefix portion MiB / calls |
| --- | ---: | ---: |
| context 0, after 120 cycles, before disposal | 207.862 | 167.783630 / 109,124 |
| context 0, after disposal | 180.096 | 167.783630 / 109,124 |
| context 1, after 180 additional cycles, before disposal | 435.426 | 423.113419 / 274,657 |
| context 1, after disposal | 435.285 | 422.972794 / 274,649 |

Categories are disjoint stack-origin partitions. Direct malloc-prefix requires
libsystem_malloc in the first three frames and excludes explicit mmap/VM;
it is an observed allocation mechanism, not an exhaustive heap census.
Platform resource records can carry AGX compiler callers. The first category
decrease of 27.766 MiB was mainly IOSurface 23.766 MiB and IOGPU 4 MiB records,
not retirement of the fragment-variant heap strings. Those heap groups stayed
exactly unchanged across first context disposal and then grew by about
255.33 MiB with another 180 cycles. They remain after second context disposal.
These actual source groups show no repeated-workload plateau.

The stacks identify AGX fragment-variant compilation/string allocation sites.
They do not identify every retaining root, prove a bounded driver cache, or
justify a program-sharing cache. Shader uniforms are scene mutable; any future
sharing experiment needs separate ownership, state restoration and parity
proof. No speculative driver-cache patch is part of this slice.

## Paging makes RSS unsuitable as a release witness here

The first context's VM capture reported writable swapped 405.5M before disposal
and 605.9M afterward. Its stack-logging allocator zone was allocated 231.6M to
228.2M, but resident 83.1M to 33.9M with 168.7M to 220.5M swapped. The same
context's quiescent RSS moved from about 492.5 MiB to 111.5 MiB while GC committed
116.3125 MiB and LOH about 32.3 MiB stayed constant. Raw VM values are rounded
OS units and differ from process RSS accounting/capture time.

Consequently the negative latter-half RSS slopes (-1.714/-1.739 MiB per cycle
in context 0 and -0.812/-0.797 in context 1) are not memory improvements or a
plateau. Allocated AGX groups grew despite paging. The 1191-second wall time is
profiling/lifecycle evidence, not throughput or FPS evidence.

## Legacy display-list and wider program audit

The earlier diagnostic's zero set did not directly enumerate retail
`Mesh.ListId` names or call native IsList. Retained-list/VBO byte counters were
not a legacy-list census. Its direct program queries covered four primary
fields, omitting outline, graphics pipeline and inactive world variants.
Those earlier captures retain this narrower scope.

Source retirement exists for all three legacy creation paths:

| Creation/owner | Retirement path | New name witness |
| --- | --- | --- |
| Renderer.GenerateLists / Scene._modelLeases | Scene.UnloadGl → SharedModelResources.Release → DeleteLists and Mesh.ListId=0 | Capture leased model names; IsList after unload plus zero mesh IDs/leases |
| CharacterModelRuntime scene resources | Release(scene) → each rigid/weighted segment Release → DeleteLists; owner dictionaries removed | Current CWT owner segment names; IsList after unload |
| Editor mesh/grid lists | DisposeEditorMeshes → part fill/edge and grid DeleteLists | Current editor mesh/grid names; IsList after unload |

The csproj's global GL alias routes SharedModelResources.DeleteLists through
GraphicsApi, which deletes native lists and invalidates retained/combined
geometry. This is not a direct OpenTK deletion that bypasses retained buffers.
The new ledger also queries captured retained/combined VBO/IBO and APPLE/core
VAO names, all named scene/variant programs and attached shaders, texture/FBO/RBO
names. Recognized deletion-pending programs/shaders remain separate; no pending
name is labeled fully released driver storage. Optional procedural/depth owners
retain their existing dedicated counters; no global context-object census is
claimed.

The expanded ledger passed an actual five-cycle map/Studio witness against the
unchanged candidate client. Fresh immutable harness SHA-256 was
`1a530c6e79c0f77ab35ec2736303abff3ce5a9dc19c83222d776385c7ac3e8eb`, built from
`22764624`. Both arm manifests matched these immutable binary hashes before and
after their run. The map arm completed ten real loads, all five requested
failure stages and exact color/depth comparisons; Studio completed five cycles
of three visible geometry/material cache rebuilds each. Native work was serial,
without stack logging, with three 100ms post-drain samples. The unrelated
operator-owned app PID32937 remained running; this is ownership evidence, not a
performance comparison. No runtime ownership patch was needed for these owners.

| Captured name set | Native recognized before unload | Recognized after unload |
| --- | --- | ---: |
| Map legacy lists | 270 or 454 | 0 |
| Map retained/combined buffers | 208, 542 or 544 | 0 |
| Map APPLE/core VAOs | 103, 104, 271 or 272 | 0 |
| Map scene/variant/outline/pipeline programs | 8 or 9 | 0 |
| Studio mesh/grid lists | 11 | 0 |
| Studio programs | 7 | 0 |

All captured names were positively recognized before unload. Programs had zero
recognized/deletion-pending names afterward; textures/FBO/RBO checks, mesh list
fields and model leases also returned zero. Studio did not allocate retained
VBO/VAO names on this editor path, so its zero before/after counts do not cover
that path. Attached shader counts were zero before and after on this candidate;
there was no live attached-shader retirement witness. These observations close
the identified legacy-list name-audit gap for this workload, not a global
context-object census or an all-platform retirement guarantee.

The opt-in bounded source ledger observed 120 compilation and 70 link callbacks
for the ten map loads, and 60/35 for Studio. Each arm had the same ten stage/hash
keys and nine distinct GLSL hashes, with zero overflow. Repeated compilation
therefore occurred with unchanged observed canonical/world-variant source.
The ledger excludes graphics pipeline, outline and HUD/UI callbacks. Attempts
are not success counts; equal GLSL does not prove identical driver pipeline keys,
identify retaining roots, or establish safe program sharing. The complete source
hash ledger and positive/retired native-name sets are preserved in the raw JSON.

## Actual headless replay isolation

The separate replay tool completed pilot 2 and confirmation 120 cycles against
an actual October 11 recording from the existing net21-a1 artifacts. Every
cycle opened the passive player and sought 120 → 30 → 240 → 120: 480 exact seeks,
with identical repeated frame-120 presentation-state hashes within and across
cycles. Player disposal was repeated twice; active readers, checkpoint counts
and bytes were zero. After NoInlining helpers returned and diagnostic drain,
all 120 player objects and 360 replaced/current scenes, sessions and readers
were weak-dead. No GL context or native SFX instance was acquired.

The existing Headless.Enter contract suppresses presentation. This covers
reader/replica/seek/checkpoint ownership, not rendered replay, full Studio UI,
audio playback or export. Full diagnostic GC/compaction ran after each helper,
with no production GC policy change. Declining RSS in this run is also affected
by paging and is not a global boundedness conclusion. Managed assets/cache roots
outside observed owners remain separately attributable work.

## Reproduced high-LOH roots

One further controlled audio60 attempt reproduced the previously missing high
managed state. It reused immutable harness
`0be05bd547efffbba64afae93646fc427733b1bc21cc3f822d1b37fa28aeb5b1` and unchanged
candidate client `86f075e7`, with the earlier NoInlining probe, first/second GC,
120 loads of 120 SFX updates, and 120 one-second quiescent observations. No
malloc stack logging was enabled. A temporary observer used already-cached
DiagnosticsClient/TraceEvent libraries and requested GCHeapStats events without
requesting GC; a separate managed-only 8 MiB target validated its LOH readings.

After the fixture's final drain, the live observer reported LOH 403,048,464 bytes
(384.377 MiB), above the predeclared 350 MiB threshold. Exactly one conditional
gcdump succeeded in 1.13 seconds and one live VM capture succeeded. The receipt
marks the extra Gen2 collection induced by gcdump. LOH stayed 403,048,464 bytes
before and after that extra collection, including the final fixture sample.
The one run completed PASS in 145.13 seconds with both binary hashes unchanged.
No second retry was run.

The captured graph contains 450,425,114 bytes and 1,046,463 objects. Breadth-first
paths from the graph root account for 403,047,288 bytes in 36 rooted objects of
at least 85,000 bytes. That differs from observed LOH by 1,176 bytes; the fixture
reported 1,152 bytes of LOH fragmentation. The remaining 24 bytes are not assigned
to a fabricated object or owner.

| Captured root population | Object bytes, including headers |
| --- | ---: |
| Thread-local shared byte/char array pools | 397,935,048 |
| Diagnostic weak-observation list backing array | 2,097,176 |
| Current Scene byte buffers | 294,960 |
| Eight current registry/player radar arrays | 1,835,200 |
| Static map-definition arrays | 786,552 |
| Static integer lookup table | 98,352 |
| Total rooted objects at least 85,000 bytes | 403,047,288 |

The dominant arrays have actual paths
`[.NET Roots] → [local vars] → Object[] → Object[] → SharedArrayPoolThreadLocalArray[] → Byte[]/Char[]`.
Large payloads alone include seven byte arrays totaling 127 MiB and seven char
arrays totaling 251 MiB; smaller pool arrays and object headers bring their
captured pool total to 379.500 MiB. This is actual high-state root evidence,
not an extrapolation from the earlier 63 MiB byte-pool capture or a five-thread
bucket hypothesis. Other paths explicitly identify diagnostic ownership and
the current Scene/registry/static map roots. They do not show a chain of earlier
SFX instances. All 120 SFX instances, candidate owners, bank structures and
observed nonempty decoded bank payloads were weak-dead. Shared zero-length arrays
remain alive and account for zero payload bytes.

The tool warns that .NET 10 lacks its non-lossy Block buffering and that gcdump
may be incomplete on large heaps. The close numerical LOH match is independent
coverage evidence, not permission to remove that warning or claim a complete
small-object/root census. Captured paths establish retaining roots; allocation
callers and subsystem workload contributions have not been established by
allocation stacks. No pool/GC policy change, leak classification or FPS claim
follows from this profiling-only attempt.

## Validation and preserved artifacts

Release builds against candidate client passed with zero warnings/errors;
context parser/NoInlining and weak-retention selftests passed. All 26 Python
tests passed, including streaming origin mechanism separation and failed native
capture preservation. Context selftest and replay-tool compilation were added
to the existing OpenGL CI job. The final retirement/source witness and both
device-free selftests passed against the fresh candidate harness; its build had
zero warnings/errors. Final-head CI remains pending; #493's successful CI does
not imply this PR's pass.

Raw evidence is preserved in the managed native-context-controls worktree:

- `artifacts/context-controls/pilot-2x2/`: original successful context pilot.
- `artifacts/context-controls/candidate-120-180-stacks/`: preserved pre-native Python3.9 hashlib compatibility failure; no native process started.
- `artifacts/context-controls/candidate-120-180-stacks-retry/`: completed context workload, four VM/malloc captures, v1 and v2 origin summaries, manifests and exact per-context fixtures.
- `artifacts/context-controls/retirement-source-5cycles-v2/`: actual five-cycle map/Studio retirement and bounded shader-source witness, exact output hashes and immutable binary manifest.
- `artifacts/replay-memory/pilot-2.json` and `candidate-120.json`: actual headless replay results/logs.
- `artifacts/audio-high-loh/one-candidate-60/`: the single reproduced high-state run, live observer source/log/selftest, gcdump and induced-GC receipt, VM capture, graph-reader source, type report and complete captured large-object root paths.

Source markers distinguish the running immutable DLL from later tool/source
changes. New origin summaries have new filenames; earlier #489/#493 referenced
artifacts were never overwritten. The reproduced high-state root accounting now
closes the earlier missing high-LOH attribution gate within the capture scope.

P0 graphics boundedness/retaining-root classification remains open despite the
passing physical list/program retirement witness. The next gate is targeted
driver pipeline-key/retaining-root attribution or a bounded ownership correction
with a demonstrated repeated-workload plateau. Headless replay ownership passed;
rendered replay/full Studio UI, pool allocation callers and hardware gates remain open. Any engine
lifetime/cache or GC policy change requires its own attributable evidence and
matched parity/measurement controls.
