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
