# Source-qualified grant presentation microbenchmark

This isolated diagnostic builds the friend assembly `nettest` against an explicitly selected frozen **physical** client. It has no project reference, production change, UDP route or native graphics/audio dependency. `ExternallyResolved` references copy only the selected client and mathematics DLL with their own harness metadata; do not use a symlinked entry DLL or copy an entire runtime tree. Other unchanged dependencies may be symlinked if actually required by the chosen client.

Build and run templates (substitute actual absolute paths and full source/hash receipts):

```
dotnet build tools/grant-presentation-benchmark/grant-presentation-benchmark.csproj -c Release -p:FrozenClientDirectory=/absolute/frozen-client -o /absolute/benchmark-bin -m:1 -p:UseSharedCompilation=false
dotnet /absolute/benchmark-bin/nettest.dll --client-source CLIENT_SHA --expected-client-sha256 CLIENT_SHA256 --tool-source TOOL_SHA --tool-root /absolute/repository/tools/grant-presentation-benchmark --output /absolute/evidence/self-check.json --self-check
dotnet /absolute/benchmark-bin/nettest.dll --client-source CLIENT_SHA --expected-client-sha256 CLIENT_SHA256 --tool-source TOOL_SHA --tool-root /absolute/repository/tools/grant-presentation-benchmark --output /absolute/evidence/microbenchmark.json
```

`--self-check` constructs/validates the same fixed inputs and canonical roundtrip without timing loops. The measured mode uses eight complete warmup batches then seventeen measured batches per workload, in the listed fixed order. It never forces GC. A batch sample measures elapsed time and thread-managed allocation around the actual loop; source hashing, JSON, array conversion, fixture construction and sample formatting occur outside the window. All seventeen samples are retained. Quantiles use nearest rank across **batch-average nanoseconds per iteration**; with seventeen samples, both p95 and p99 equal the maximum. They are neither per-call latency distributions nor native frame tails.

| Workload | One reported iteration | Iterations per batch |
| --- | --- | ---: |
| empty-24-views | 24 `View` calls: eight complete lives × three timed types, zero grant/pending cells | 4096 |
| populated-24-views | 24 `View` calls with 24 confirmed facts and no pending predictions | 4096 |
| duplicate-24-accepts | 24 byte-identical `Accept` calls on the populated immutable state, all refused as duplicates | 4096 |
| worst-write | One canonical `Write` using a reused preallocated stream/writer | 128 |
| worst-read | One canonical `Read`, including a fresh stream, reader and decoded state | 32 |

The fixed simulation frame is101. Populated grants have contact100, authority101, end1900; each view has remaining/confirmed1799. Frames do not advance inside a workload. Worst checkpoint state has exactly24 fact cells **and24 grant-ACK cells**,16 independent pending predictions,512 distinct unexpired consumed incarnations. Loaded bounds must be16/512/32768; unexpected ABI fails instead of silently changing the workload. Canonical serialized length/counts and SHA are reported; full `Read`→`Write` bytes must match. Checksums must be stable across samples; populated and worst states remain byte-equal after all workloads. Duplicate refusal and view/checkpoint input validation are real production helper operations, not substituted no-ops.

The report records actual loaded client location/MVID/SHA, tool and mathematics identities, three tool-source file hashes, reflected client protocol, runtime/architecture/Stopwatch frequency and tiered-JIT environment. The selected client SHA is enforced. Source commit arguments identify operator-supplied frozen-build receipts; a source SHA alone does not prove binary provenance. Paired arms require source-qualified build receipts, identical tool/workload hashes, fixed settings, actual client identity, unchanged checksum/shape and an idle serialized CPU measurement slot. Retain setup failures and all samples. Build separately per frozen client to ensure dependency metadata describes that client.

This is a managed CPU/allocation microbenchmark. It supplies no native grant, real UDP delivery, GPU, image/depth, gameplay, full-frame or rollout performance claim. An original/current rehearsal is not final reconciled-source acceptance. Do not promote a local median or these coarse sample tails into a native frame-time improvement.
