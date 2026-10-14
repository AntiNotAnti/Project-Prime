# Asset-backed world ownership fixture

Hypothesis: the eligible LIFE-02/LIFE-03 handoff remains correct when production
room data, player models, AI, entities and collision grids are loaded, rather
than only constructing content-free Scene owners. This diagnostic slice changes
the nettest fixture and dispatch only. Its integration parent is
`266c6fe12171e8bd3c41ba5829ef9f9c501c7e4a`.

The previous [controlled soak](lifecycle-controlled-soak.md) passes 610
constructed-owner save comparisons, but does not call AddRoom/OnLoad or advance
the native world. Preserve that evidence and its explicit missing gates. The new
fixture closes those particular workload gaps without reclassifying its own
headless results as rendered or launcher acceptance.

## Reproducible command

Build the exact integrated source once, then run its qualified DLL. Use a fresh
isolated PROJECT_PRIME_USER_DATA directory and the user's licensed extracted
AMHE1 filesystem; assets, recordings and raw logs remain outside the repository.
The recording argument is a genuine application recording for the simultaneous
detached-replica case. `-` explicitly records that case as NOT_RUN.

```sh
dotnet /absolute/qualified/bin/nettest.dll --asset-lifecycle \
  /absolute/licensed/AMHE1/filesystem 10 \
  /absolute/positive-recording.ppdemo /absolute/evidence/life-10.json
```

Repeat independently with counts 100 and 500. The output must be fresh: the
fixture refuses to overwrite evidence. Record source SHA/dirty status, runtime,
asset/recording identity, nettest/client DLL hashes and the command in the
external campaign manifest. The fixture JSON also records the actual loaded
client assembly SHA-256/MVID, per-owner generation/request/slot stamps,
room/player/entity/step counts, actual save byte lengths/hashes, failed/retried
writes, eligible weak observations and bounded value diagnostics.

## Positive and negative workload

The fixture constructs eight actors through AddPlayer (the local actor and all
seven hunter bots), alternates MP3 PROVING GROUND and MP9 CRYOCHASM, calls the
production AddRoom/OnLoad and advances sixty actual OnSimulationFrame steps per
load. Missing actors, entities, initialization observations or frame advancement
fail the run. This is an ownership workload with native steps; it does not
establish all seven hunters' movement/combat correctness.

The actual loader-created StorySave is serialized by the independent frozen
legacy writer in PostMatchSaveTests. PrepareBeforeTeardown captures a save whose
generation and request version must match the owner. The fixture then mutates
the original save's health, progression Stats and numeric byte array. Actual
post-cleanup save files must still equal the independent original bytes. Every
seventeenth load obstructs Savedata with a real file: IOException must retain
the immutable payload and prohibit owner release; a retry must write the
original bytes. Terminal Commit/Release cannot duplicate a successful write.
The released contexts remain retained through the final weak observation.

With a recording, two fully loaded live worlds and a production
PassiveReplayScene exist together. Replica construction, actual stepping and
disposal must preserve all newer foreground facade identities. Three genuine
nonempty production collision leases coexist and dispose in nesting order;
inner disposal must preserve the outer candidate identity/count. A worker
completion with a foreign generation and an ended old room's completion are
rejected. Old callbacks, queries and teardown must preserve the newer world's
gameplay hash. An old released commit also cannot clear the next loaded owner.

After the final eligible owner release, one diagnostic observation performs
explicit collections outside all world-creating methods, while value snapshots,
weak references and released save contexts remain alive. Any surviving loaded
scene fails and requires an actual retained-root investigation; the fixture
does not manufacture a GC-root path from weak liveness. Normal production
Scene.OnLoad/EndScene GC policies are unchanged.

## Coordinated native recipes

Use the same immutable integrated client build and fresh external evidence
directories. Run these serially in the parent-owned native campaign slot.

```sh
# Existing real GL/map/SFX and Studio renderer/cache ownership fixture.
python3 tools/native-render-check/run_resource_arms.py \
  --binary /absolute/qualified/bin/native-render-check.dll \
  --output /absolute/fresh/evidence/native-life \
  --arms combined map-gl studio audio --cycles 10 --frames 120 \
  --quiescent-samples 6 --quiescent-ms 1000 -- -resource-audio-wait

# Rendered detached world/checkpoint/seek comparison from an actual recording.
dotnet /absolute/qualified/bin/ProjectPrime.dll \
  -replayreplicacheck /absolute/positive-recording.ppdemo \
  -shots /absolute/fresh/evidence/replay-pictures

# Existing real file/video export, queue completion and cancellation checks.
dotnet /absolute/qualified/bin/ProjectPrime.dll \
  -replayexportcheck /absolute/positive-recording.ppdemo \
  -output /absolute/fresh/evidence/export

# Existing 120 native NCSF/MiniAudio playback, pause, retirement and close cycles.
dotnet /absolute/qualified/bin/music-lifecycle-check.dll \
  --native /absolute/licensed/AMHE1/filesystem
```

The resource runner already verifies actual failure-stage execution, exact
RGBA/float-depth fallback parity, owned GL closure and audio-task drain; an
audio stub cannot PASS the native audio arm. It accepts at most 200 cycles.
Separate batches beyond that cap must remain separate contexts/processes and
must not be presented as one 500-load same-context campaign. Studio renderer
cache rebuilds do not exercise Studio UI interactions. None of these commands
by itself covers lobby/reconnect, all custom assets, power interruption, every
failure stage or all platform gates.

## Validation and disposition

Initial source delivery: diff check PASS. Build and asset campaign NOT_RUN
until the coordinated qualified build/run. Native GL/audio, lobby UI/reconnect,
rendered Studio/editor routes and media acceptance remain NOT_RUN until their
actual receipts exist. The new fixture writes those scope labels explicitly.
No production behavior, protocol, replay format, save serializer or settings
default is changed. Roll back this test/dispatch/document slice independently;
retain all prior negative or incomplete native evidence.

## Serial acceptance executor

`tools/nettest/run_life_campaign.py` wraps the existing commands above and the
current OpenGL shell/respawn routes. Supply qualified tool DLLs that resolve the
same immutable ProjectPrime.dll; compiling a standalone harness must preserve
that qualified physical client, for example through BuildProjectReferences=false
and a generated external project with its reference HintPath set to the
qualified DLL. A freshly rebuilt client alias is not interchangeable with the
qualified DLL. The executor rejects mismatched client copies. It also requires
the integrated build receipt whose sourceBefore/sourceAfter match the clean
source head and whose actualLoadedClient includes the qualified SHA and MVID.

```sh
python3 tools/nettest/run_life_campaign.py \
  --nettest /absolute/qualified/bin/nettest.dll \
  --client /absolute/qualified/bin/ProjectPrime.dll \
  --native /absolute/qualified/native/native-render-check.dll \
  --music /absolute/qualified/music/music-lifecycle-check.dll \
  --filesystem /absolute/licensed/AMHE1/filesystem \
  --replay /absolute/positive-recording.ppdemo \
  --paths-template /absolute/fixture-template/paths.txt \
  --qualification-receipt /absolute/exact-head-build/receipt.json \
  --output /absolute/fresh/evidence/life-campaign \
  --counts 10 100 500 --native-cycles 10 \
  --operations asset native replica export music shell respawn
```

The runner normalizes only paths.txt into fresh isolated user data, does not copy
player settings or credentials, and clears every ambient PRIME/MPH/PROJECT
environment control before setting its explicit user-data path. It records and
checks source state, DLL/dependency/runtime-config hashes, replay, paths,
qualification receipt, runner/helper scripts before and after workloads. The
existing asset-identity helper hashes the actual licensed input files and
verifies their identity at campaign completion. Loaded fixture SHA/MVID must
match the qualification receipt. Raw logs remain external and retain failures.

Each executable runs in an owned POSIX process group. Timeout or executor
SIGTERM/SIGINT terminates the group, escalates if a descendant ignores SIGTERM,
reaps its leader and records remaining members. Success requires no live
descendants left before cleanup. This ownership implementation is qualified on
POSIX only; executable Windows campaigns are explicitly NOT_RUN until a
Windows process-tree owner is validated. The lightweight Python suite exercises
success, a real SIGTERM-resistant descendant, executor termination, environment
isolation, exact qualification rejection, PNG integrity/black controls and
positive/missing shell and respawn denominators without starting game processes.

`shell` uses `-shellshot OUTPUT -shelllifecycle REPLAY -noupdate -debuglog`,
including the full standard fullscreen/rematch/map-rotation script. It never
adds the obsolete modern-backend flags described in historical documentation.
The current standard script has 31 captures; its two added cycles require twelve
more. Both exact PASS cycle markers, logged actual asset readiness, no missing
steps, every expected nonblack PNG and matching OpenGL final-composite evidence
are required. Inspect the pictures independently as well. This covers actual
rendered menus, rematch, Map Studio open/close and replay UI seek/return; it does
not author maps or establish editor playtest, lobby network reconnect or native
memory boundedness.

Optional `respawn` reuses `-respawnrendercheck 'MP3 PROVING GROUND' -cycles 9
-timeout 300` and validates its actual nine-case PASS summary, deaths/respawns,
positive checked frames, fourteen control comparisons and zero reported
invariant/GL/failure counts. A failure or incomplete route remains FAIL and stops
the serial campaign; future operations remain NOT_RUN rather than disappearing
from evidence. Native runs remain coordinated separately from ordinary timing
benchmarks. No executor run has yet qualified these broader native/UI gates.

The first integrated build at `b58377ab` failed because the fixture applied LINQ
to the foreach-only native entity iterator. The explicit native traversal fix
was followed by a full successful build at `ff16b44d`. Its first real 10-load
smoke then failed before sequential loads: three actual simultaneous worlds
initialized and the original server recording's detached replica stepped, but
the fixture's diagonal line-segment query yielded no candidates in MP3. That
point query is not a bounding-box query. The corrected fixture derives limits
from the actual active native room grids and borrows the existing production
GetCandidatesForLimits lease. All nonempty and outer identity/count assertions
remain mandatory. Preserve both failed receipts; neither failure is a native
world/ownership PASS.

At corrected integrated head `38ae3ceb`, the qualified full client/nettest passed
all three actual asset campaigns: 10, 100 and 500 loads, totaling 610 real room
loads, 36,600 native frames and 610 exact save comparisons. Each campaign also
loaded three simultaneous worlds including the original authority recording's
detached replica and finished with zero sequential/simultaneous weak targets
alive after eligible release. The physical client SHA-256 was
`2238516d0eee2976c49cff8f3e2eb59b180d0bb704b50ad63561235d9272a517`, MVID
`0727be91-ff32-463e-b0b8-431bcbb272e8`. These source-qualified external receipts
establish loaded-world/save ownership in this headless workload. GL/audio, shell
UI and media campaigns remain separate; their positive receipts cannot be
inferred from this pass.
