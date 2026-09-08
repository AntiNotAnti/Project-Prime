# Collision query isolation measurement — 2026-09-08

Query ownership increased allocation in this controlled workload, while p95 execution time remained comparable. Keep the isolated implementation and the conservative one-match density default. This does not establish Worker capacity or whole-match tick cost.

## Reproduction and evidence

- Host: Apple M4 Pro, 12 logical CPUs, 24 GiB RAM, macOS; .NET SDK 10.0.400, Release build.
- Historical collision algorithm: commit `9ef8cb5d572837c7d1efcc821ea4e3ae1bf7c615`; SHA-256 `eb91ddaee8eb6ae63846fe9f20a7a11cd679ceabf6929ebf749db778a10db5e5`.
- Current collision source SHA-256: `56cc32d97c7f202f063d4b0e101a6033c3ce15a2de6f4357c8da9500be14ab7b`.
- The script extracts historical source into a new `/tmp/codex-collision-bench.*` directory. Only its class name and duplicate public type declarations change. Both implementations use the **current** Scene/content/model/runtime code. No checkout reset occurs.
- The unsafe historical static workspace is exercised serially only. It is not a viable concurrent implementation.
- One or two real `MP1 SANCTORUS` Battle scenes contain eight fixed bots each. Warmup: 300 actual simulation ticks. The query stage then holds those positions fixed, with 1,000 samples per actor.
- Each sample performs a segment, swept-sphere and radius query at the actor position. Reported percentiles describe that **three-query batch**, not one query or a simulation tick.
- Allocations use `GC.GetAllocatedBytesForCurrentThread`, excluding scene construction, warmup, sample-array allocation and JSON serialization. This is not process-wide allocation or Worker aggregate GC data.
- Tiered compilation is disabled for the reported run to prevent compilation tier changes between variants. An exploratory tiered run showed changing medians and is not used for the comparison.

```sh
COLLISION_BASELINE_REVISION=9ef8cb5d572837c7d1efcc821ea4e3ae1bf7c615 \
DOTNET_TieredCompilation=0 GAME_DATA_DIRECTORY=/absolute/path/to/AMHE1 \
  tools/collision-bench/prepare-and-run.sh 1000
```

`--prepare-only` extracts and builds without running the workload. The script prints the exact source manifest path. It creates no copies of game assets.

## Results

| Total bots / scenes | Variant | Queries | Bytes/query | Batch p50 µs | Batch p95 µs | Batch p99 µs | Batch max µs |
|---|---|---:|---:|---:|---:|---:|---:|
| 8 / 1 | Historical shared scratch, serial | 24,000 | 2,302.67 | 8.833 | 20.958 | 24.916 | 942.917 |
| 8 / 1 | Current query owned | 24,000 | 5,736.33 | 8.959 | 21.000 | 26.625 | 602.209 |
| 16 / 2 | Historical shared scratch, serial | 48,000 | 2,302.67 | 8.875 | 21.292 | 25.041 | 793.917 |
| 16 / 2 | Current query owned | 48,000 | 5,736.33 | 9.000 | 21.417 | 25.834 | 584.209 |

Raw rows: [collision-query-2026-09-08.jsonl](measurements/collision-query-2026-09-08.jsonl).

Historical/current checksums match: `2060682203744077400` for eight bots and `15128720966987285680` for sixteen. The checksum covers segment distance, swept-sphere result counts/distances and radius result counts; it is not a comprehensive proof of every collision field. Existing collision isolation tests remain necessary.

The allocation increase is 3,433.67 bytes/query (2.49× total). The p95 changes are +0.042 µs and +0.125 µs per three-query batch. Individual maxima are susceptible to GC and OS scheduling; this single controlled run does not establish tail guarantees.

## Decision and next measurement

No optimization was applied. Measure full Worker CPU, process allocations, GC pauses and missed/catch-up ticks under real clients, observers, replay and telemetry before deciding whether this allocation increase is a practical bottleneck. If those measurements identify collision scratch as the cause, consider an explicitly caller-owned reusable workspace with lifetime enforcement; keep returned candidate lists owned by their query. Do not restore global scratch or publish higher Worker density based on this microbenchmark.
