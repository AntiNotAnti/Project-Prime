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
