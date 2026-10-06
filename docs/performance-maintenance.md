# Performance and maintenance

Project Prime keeps player data across versions. That is intentional, but it also
means settings and reproducible caches can outlive the build that created them.
The maintenance layer keeps those two ideas separate: preserve player data, prune
only data that Project Prime can recreate.

## What an update may delete

Desktop release archives contain `.project-prime-files.json`. During an in-app
update, Project Prime removes files that were explicitly owned by the previous
release and are absent from the new release. It never infers ownership from the
contents of the install directory.

Preserved player-owned data includes:

- `settings.json`, `launcher.txt`, and `controls.txt`
- saves and extracted cartridge data
- real replay recordings and virtual clip descriptors
- custom maps and creator source files
- user audio, screenshots, exports, and other media

The first manifest-aware update from an older build performs only conservative
cleanup of historical Project Prime/Fruity Prime/MphRead executable/runtime names.

## Startup maintenance

Once per installed version, Project Prime performs a cheap maintenance pass. The
same actions are available under **Settings > Maintenance**.

The sweep:

- removes abandoned updater staging
- bounds `ProjectPrime/map-cache` to 2 GiB and evicts entries older than 60 days
- removes old `*.part` replay writes
- removes orphan materialized virtual replay cache files
- removes incomplete replay export directories older than seven days
- removes zero-byte/stale map thumbnails and preview worker markers
- removes an old thumbnail batch log

No real replay is deleted by this maintenance pass. Replay retention remains owned
by the separate Replay Studio storage policy.

## Settings migrations

`MenuSettings.SettingsSchemaVersion` makes persisted settings explicit. Every
load validates graphics and frame-pacing values and writes a normalized file if an
older schema needs migration.

The Maintenance page also offers **Reset performance settings**, which restores:

- 100% render scale
- Display/VSync frame pacing
- lighting and fog on
- bilinear/trilinear filtering off
- 1x anisotropy
- cel shading off
- FPS counter off
- native HUD smoothing on

## Diagnostics and persistent logging

Normal releases keep a bounded in-memory diagnostic ring and enable persistent
debug logging by default so support reports retain the complete session leading
up to a failure. Players can disable persistent logging in Settings when they
prefer to avoid steady-state disk writes; crash reports still include the recent
in-memory diagnostics.

Launcher preferences are schema-versioned too. The first load of a pre-schema
`launcher.txt` adds the schema marker while preserving any explicit
`debug_logs` choice. If the key is absent, the current default applies.

## Render performance check

Use a fixed real-render workload:

```bash
ProjectPrime -perfcheck "MP3 PROVING GROUND" -players 8 -seconds 20 -hz 60 -output perf-60.json
ProjectPrime -perfcheck "MP3 PROVING GROUND" -players 8 -seconds 20 -hz 120 -output perf-120.json
ProjectPrime -perfcheck "MP3 PROVING GROUND" -players 8 -seconds 20 -hz 144 -output perf-144.json
```

The workload runs at 1920x1080 and clean-install rendering defaults while the
simulation remains fixed at 60 Hz. Presentation cadence is independent: 60 Hz
draws once per step, 120 Hz twice per step, and 144 Hz uses a deterministic
12-draws-per-5-steps cadence (2, 2, 3, 2, 3 pictures) so the high-refresh path is
measured without changing gameplay.

The JSON records average, p50/p95/p99/p99.9 draw work, derived 1% and 0.1% low FPS,
simulation p99, managed bytes allocated per draw, GC counts, workload identity,
and build version.

For both standard runs:

```bash
bash tools/run-perf-suite.sh ./ProjectPrime perf-results
```

Compare two matching results:

```bash
python3 tools/compare-perf.py baseline.json current.json
```

The comparator fails on material regressions: 10% average, 12% p95, 15% p99 and
simulation p99, 20% p99.9, 25% allocations, or equivalent low-FPS drops.
Both reports must include the six workload fields and all eight compared metrics;
metrics must be finite nonnegative JSON numbers. Missing or invalid fields and
workload mismatches return exit code 2, including with `--no-fail`. Optional GC
counts may be omitted, but present values must be nonnegative integers. An
increase from a zero baseline in a lower-is-better metric is a regression.
Run the content-free comparator checks with
`python3 -m unittest discover -s tools -p test_compare_perf.py -v`.

For renderer-level measurements, `-renderbenchmark` also works with explicit
backend selection. On legacy desktop OpenGL it now reports a `legacyUniforms`
sample containing requested, submitted and skipped uniform writes plus the skip
percentage. The compatibility wrapper suppresses only exact duplicate scalar,
vector and single-matrix values within the same linked program. Large mutable
array uploads, including bone/matrix stacks and lookup tables, are measured but
always submitted because hashing/comparing them can cost more than the driver
call being avoided.

Example:

```bash
ProjectPrime -renderbenchmark "MP3 PROVING GROUND" -renderer opengl -samples 1200 -output render-gl.json
```

Use the counters to decide whether the cache is paying for itself on the target
driver. A zero or tiny skip rate is evidence to remove or narrow an optimization,
not a reason to grow the cache.

A public CI runner cannot execute the real client benchmark from this repository
because Project Prime intentionally ships no cartridge data. The benchmark and
comparator are therefore release/local gates run against a legally prepared test
installation rather than a synthetic no-assets substitute.
