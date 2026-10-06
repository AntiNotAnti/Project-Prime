# Roadmap measurement evidence

These numerical artifacts were recorded on macOS 27 Arm64 / Apple M4 Pro in the isolated engineering checkout on October 6, 2026. They are generated fixtures or diagnostic summaries, not game assets or private player recordings. Sources and limits are described in each JSON and the [implementation tracker](../roadmap-implementation-2026-10-06.md).

- `replay-decode-30.json` / `replay-decode-120.json`: independent one/three-reader decode and 100 random seeks; warm filesystem after writing synthetic eight-player packets. Initial reader-only runs predate the later addition of a valid full bootstrap for private-world fixture output.
- `replay-private-world.json`: synthetic six-minute archive, real built-in MP1 SANCTORUS asset-backed private scenes, identical linear/seek hashes. Indexed baseline is the existing checkpoint index, not a new optimization. Scope totals include the initial cold construction, followed by warm workloads. The original operation name `cold seek` means a new player/checkpoint cache, not cold OS assets.
- `replay-private-world-staged.json`: the production detached preparation/adoption path and staged seeks preserve the synchronous indexed/unindexed/linear hashes. Staged seeks reuse a warmed player, while synchronous cold-seek samples create a fresh one. This is correctness and owner-work evidence, not a controlled speed comparison. Median staged startup was 17.78 ms with 13.81 ms total owner work; the maximum opening callback across samples was 22.64 ms. Indexed staged frame 18,000 took median 17.05 ms wall, with an 11.05 ms maximum owner callback across samples. Scene/restore still exceed a 1 ms step label.
- `production-frame-trace-summary.json`: real native Metal `-shellshot` lifecycle, 562 production frames; loads/resizes/pause/rematches/screenshots, one missed UI step. Return timestamps are CPU/driver observations, not scanout or GPU durations.
- `logging.json`: actual DebugLog.Line measured against its bounded memory ring, warm local files and a clearly artificial 5 ms delayed-flush sensitivity model.

Reproduce with the repository's .NET 10 SDK and extracted assets only where noted:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet run --project src/MphRead -c Release --no-build -- -replaydecodebenchmark -minutes 30
dotnet run --project src/MphRead -c Release --no-build -- -replaydecodebenchmark -minutes 120
dotnet run --project tools/logging-benchmark -c Release
dotnet run --project src/MphRead -c Release --no-build -- -replaydecodebenchmark -minutes 6 -fixtureoutput /tmp/synthetic-prime.ppdemo
# With valid paths.txt in the working directory and isolated user data:
dotnet /absolute/path/to/ProjectPrime.dll -replaybenchmark /tmp/synthetic-prime.ppdemo -output /tmp/private-world-results
# Native surface run; choose a supported backend and fresh paths/output:
PROJECT_PRIME_FRAME_TRACE=/tmp/production-frames.json dotnet /absolute/path/to/ProjectPrime.dll -shellshot /tmp/shellshots -renderer metal
```

The optional fixture output refuses to replace an existing recording. Benchmark scenes, checkpoint/clip leases and temporary reader archives are released deterministically. The fixed trace budget is 8,192 frames and files are written only on shutdown.

For final validation use `tools/check-engineering-contracts.sh` and the separately frozen Deno function checks documented in [Edge acceptance](../../../tools/edge-check/README.md). Physical platform and relay acceptance remain release gates; a local source/type/fixture check does not replace them.
