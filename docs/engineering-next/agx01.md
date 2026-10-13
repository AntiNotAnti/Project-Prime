# AGX-01: immutable allocation campaigns

Hypothesis: correct GL-name retirement can coexist with increasing outstanding
AGX-origin allocation groups; only matched, held native capture phases can
separate program creation from storage remaining after context disposal.

Base: `16b1d89bfb6ed7776b801b3e39d513e740ee5e58` (protocol 53). This slice changes
only diagnostic tooling and its CI tests. No graphics policy, gameplay, wire,
replay, asset, save, or production GC behavior changes.

The existing same-context stress fixture is retained. Each map cycle performs
**two** fixed-frame loads (reference/candidate), so 30 cycles means 60 map loads.
The runner now defaults to `-failure-stages none`; injected and ordinary
workloads must be separate campaigns. Contexts remain unshared. Their index,
local generation/share-group identity and owner thread are recorded.

Every native phase is held until sampling finishes, including before the first
load, after complete workload/drain, and after disposal. Previously the runner
could sample a later phase when short observation windows elapsed; it had no
pre-workload control. Final GLFW poll/terminate remains optional and final.

The runner creates fresh user data from **only** paths.txt, resolves asset paths,
and records hashes of every extracted file. Player settings, saves, sessions,
credentials and cosmetics are not copied. It records binary SHA-256, per-context
MVIDs/runtime/OpenTK/GL identity, OS build, fixture clocks/seeds, exact commands,
source state and artifact hashes. Asset and binary hashes are checked again
at completion. Asset hashing and stack sampling overhead are outside FPS
measurements. RSS and GC are separate observations; native heap is never
calculated as their difference.

```sh
python3 tools/native-render-check/run_context_controls.py \
  --dotnet /absolute/dotnet --binary /absolute/native-render-check.dll \
  --user-data-template '/absolute/operator-user-data' \
  --output /absolute/fresh-campaign --cycles 30 120 --frames 120 \
  --samples 4 --shader-source-ledger --capture-native-stacks --driver-drain
python3 tools/native-render-check/validate_context_campaign.py /absolute/fresh-campaign
```

The current fixture retains its 1..200-cycle guard. Longer campaigns must split
into explicitly identified contexts; that is not evidence for 600/900-load
same-context boundedness. Memory/disk guardrails preserve incomplete receipts
rather than exhausting the host (default RSS 4096 MiB, free-disk reserve 512 MiB).
Capture commands retain `vmmap -summary` and
`malloc_history PID -allBySize -fullStacks`. Missing/unsupported data is null.

Validation rejects altered binary/capture/result hashes, missing or reordered
phases, mixed processes/contexts, incomplete loads/frames, failed tool receipts,
truncated recognized stacks, duplicate JSON/source keys and contradictory shader
counts. Unknown formats and incomplete provenance are UNVERIFIED. It does not
infer allocator ownership, safe shader reuse, a leak, or boundedness. A capture
cut exactly at a complete stack-group boundary cannot be detected from stdout
alone; successful tool exit and immutable hashes are still required.

Acceptance status and measured results will be recorded after native campaigns.
Windows/Linux/physical GLES and a second Apple host are NOT_RUN here.

Rollback: revert this diagnostic commit or run the prior harness/runner from the
recorded base. No runtime flag, protocol or data migration is needed. Keep raw
failed attempts outside the repository; do not commit captured game images or
extracted assets.

## First measured campaign

macOS 27.0 build 26A428, Apple M4 Pro, OpenGL 2.1 Metal 91.7; .NET SDK
10.0.401/runtime10.0.12. Clean game process, isolated user data. Source
`a08ade80`, client/harness MVIDs and exact hashes are in agx01-results.json.
The 397.892-second serial campaign completed 30 then120 cycles in two unshared
contexts (60 then240 map loads),120 simulation frames/load, Original/High,
1920x1080, seeds12345/98765, no injected failures. All exact color/depth,
recognized-name retirement and resource-owner gates passed. Binary/asset hashes
were unchanged and data-quality validation returned VALID.

| Held phase | Direct malloc-prefix AGX fragment MiB | Outstanding calls |
| --- | ---: | ---: |
| Context0 before load | 0.017807 | 47 |
| After60 loads, before disposal | 44.832199 | 28354 |
| After context0 disposal | 44.830734 | 28352 |
| Fresh context1 before loads | 44.833420 | 28356 |
| After240 more loads, before disposal | 224.646439 | 144643 |
| After context1 disposal | 224.644974 | 144641 |
| Final GLFW poll | 224.644974 | 144641 |
| Final GLFW terminate | 224.644974 | 144641 |

60/240 loads produced720/2880 observed compile and420/1680 observed link
attempt callbacks across10 stage/source keys. Existing hook coverage excludes
some graphics/outline paths; AGX-02 closes that attribution gap. Per-load
application work and remaining AGX groups are separate measurements. Origin
and correct named-owner release do not prove the retaining mechanism.

Negative evidence: frame30 failed925450 world pixels/40012 depth values. The
unchanged main harness independently reproduces this failure; frame120 passes.
The pilot120 functional result had brief overlap with that separate baseline
control and is excluded from memory conclusions. Raw failures remain preserved.
Lossless gzip reduces stack-log disk use while validating both original stdout
and compressed file hashes; sampling+compression wall time is in each receipt.

Acceptance: reproduction/accounting PASS for this host; repeated control pending.
P0 correction/boundedness remains UNVERIFIED. No plateau or performance gain is
claimed. Windows/Linux/GLES, full Studio UI, second macOS/GPU and600/900-load
same-context arms remain NOT_RUN. Raw logs/images stay outside the repository.

Independent fresh-process repeat:60 loads again passed exact parity/ownership
and VALID receipts; post-disposal/GLFW-terminal direct fragment groups were
44.717972 MiB, within0.112762 MiB of the first60-load arm. AGX-01 reproduction
and accounting acceptance is PASS on this host. P0 remains unresolved:300 loads
retained224.644974 MiB after disposal and termination. No boundedness or
app-vs-driver conclusion follows without AGX-02 controls.
