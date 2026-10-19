# Competition evidence identity gate (CPP-00)

This validates manifests around existing measurements. It never launches a game,
builds a client, imports assets, renders, or declares a performance/device PASS.
`COMPARABLE` means the supplied identities and local file digests agree; reviewers
must still inspect build provenance, workload execution and the raw measurements.
Manifests are operator-authored evidence records, not independently authenticated
device attestations. Do not generate missing measurements from assumed values.

```sh
python3 tools/competition-evidence/evidence.py lock --repo . --output /private/campaign/source-lock.json
python3 tools/competition-evidence/evidence.py check /private/campaign/baseline.json
python3 tools/competition-evidence/evidence.py compare /private/campaign/baseline.json /private/campaign/candidate.json
python3 -m unittest discover -s tools/competition-evidence -p 'test_*.py' -v
```

`lock` reads real Git HEAD and tracked/untracked cleanliness. It writes once with
exclusive creation, preserves dirty observations, and returns 2 for dirty source.
It observes the source at invocation time; it does not prove an existing binary
was built from that source. Record a source lock before building in a clean pinned
checkout, preserve the actual build command/log, and verify source remains clean
afterward. Keep generated evidence outside that checkout. A dirty source lock can
be retained for development but cannot pass this canonical comparison gate.

Reuse these existing owners:

- `tools/native-render-check`: normal OpenGL frame loop, observed completed-frame
  p50/p95/p99, GPU identity, allocations, GC counts and resident memory. It supports
  60/120/144/240 Hz and seven native hunters through `-hunter`. Use separate runs for
  profiling, screenshots, `-windowcycle`, and resource stress. Those modes cannot
  substitute for unprofiled frame timing. `campaign_identity.py` already provides
  asset hashing, isolated `paths.txt`, and host observations; this gate reuses its
  SHA-256 helper.
- `tools/opengl-modernization/short_checks.py`: existing paired/ABBA runs, binary
  hashes/MVIDs, immutable capture names, separate visual/profile/timing arms.
- `ProjectPrime -perfcheck`, `tools/run-perf-suite.sh`, `tools/compare-perf.py`: draw
  work excluding Present/Swap. These reports have a different timing scope and
  cannot be relabeled completed-frame evidence. The suite currently schedules
  60/120/144; use its underlying command for 240.
- `tools/fidelity-oracle`: existing normalized simulation hashes and verification;
  `tools/nettest` and `tools/net-evidence-benchmark`: existing real-process network
  and replay evidence. Offline native bots do not establish dedicated online
  behavior. The current adapter rejects `dedicated-online`; integration with a
  real-process frame capture remains outstanding.

## Manifest contract

Use schema version 1. All identities below are required, and each measurement must
explicitly say `measured` with its observed value, or `unavailable` with null and a
reason. Frame p50/p95/p99, total allocated bytes, summed GC collection count and
resident bytes must be measured for this adapter. CPU/GPU timing, GC pause, direct
native bytes and input latency may remain unavailable. Resident memory is not
direct native allocation ownership; a zero GC collection count is not zero pause.

| Object | Required fields |
| --- | --- |
| `source` | `repository`, full 40-digit `sha`, `dirty: false` |
| `build` | `sourceSha` matching source, `sourceDirty: false`, actual `command`, `configuration`, `toolchain`, nonempty normalized `flags` object |
| `device` | stable non-secret `machineId`, `kind: physical`, exact `os`, `cpu`, per-context `gpu`, `driver` (macOS build where the driver is OS bundled) |
| `workload` | `fixtureVersion: cpp00-v2`, `mode: offline`, `room: MP3 PROVING GROUND`, `mapSha256`, `replaySha256`, `simulationSha256`, `simulationHz: 60` |
| `workload` continued | `players` 2/4/8, one native `hunter`, `detail` low/high, actual `width`, `height`, `renderScale` as the native report's resolutionScale, `presentationHz` 60/120/144/240 |
| `workload` continued | `seed` (assigned rng1), `rngSeeds: {rng1,rng2}`, `measurementStartRng`/`measurementEndRng` observed uint32 pairs, exact `roster: [{slot,hunter,bot}]`, `inputExecution: frozen-device-snapshots-with-bot-ai`, `replayExecuted: false`, `warmupSimulationSteps`, `measurementSimulationSteps`, `phase: entry`, `harness: native-render-check-v2`, `timingScope: completed-frame-includes-swap`, `profiling: false`, `readback: false` |
| `workload.runtimeSettings` | actual `shadows`, `textureQuality`, `lighting`, `resolutionScale`, `vsync`, `effectiveCap`; record any additional relevant settings too |
| `measurements` | `frameP50Ms`, `frameP95Ms`, `frameP99Ms`, `allocatedBytes`, `gcCollections`, `residentBytes`, `cpuMs`, `gpuMs`, `gcPauseMs`, `directNativeBytes`, `inputLatencyMs` |
| `artifacts` | array of unique `{role, path, sha256}` records; paths resolve against the manifest directory, absolute private paths are also supported |

Measurement shapes:

```json
{
  "gpuMs": {"status": "unavailable", "value": null, "reason": "Actual driver probe reported no supported GPU timer"}
}
```

That snippet is a schema example, not an observed GPU result. Never copy its reason
without actually probing the target driver.

Required artifact roles: `client-binary`, `harness-binary`, `raw-result`, `raw-log`,
`source-lock`, `build-log`, `asset-manifest`, `replay-fixture`, `simulation-report`,
`simulation-verify-log`. Binary hashes are the actual loaded client and harness,
not a later build occupying the same path. Preserve dependent libraries and the
complete invocation/environment flag recipe in the private run bundle as needed.
Do not include credentials or player settings. Verify all environment overrides,
not just a selected experimental flag.

`asset-manifest` is the existing `campaign_identity.asset_identity(paths_file)`
result; its `sha256` is the canonical complete asset fixture fingerprint recorded
as `mapSha256` here. This deliberately also rejects differences elsewhere in the
asset set. Keep its root paths private. `replay-fixture` is a preserved external reference replay
file (not executed by this runner); its file digest must match `replaySha256`. `simulation-report` is an actual
Fidelity Oracle report with `canonicalHash` equal to `simulationSha256`, backed by
its independent verify log. A stored baseline alone does not demonstrate the
current build reproduced it. This gate verifies artifact integrity/declared hash
agreement; the existing oracle owns canonical simulation verification.

The native adapter requires the new raw `observedWorkload` receipt. Older reports cannot establish execution by copying a new manifest around them. The receipt records the actual slot/hunter/bot roster at measurement start and end, RNG seed assignment readback and observed start/end states, offline authority at both boundaries, exact measurement ticks, and loaded-module MVID plus binary SHA bindings. The native helper reads binary files before timing/allocation baselines, verifies their PE MVID against loaded modules, and records final observations after the last interval. It excludes its own final-observation allocation overhead from the existing allocation result. This binds observed files/modules; it does not prove the source/build recipe.

The adapter checks those observations against manifest roster/seeds/mode/phase/input labels and preserved binary artifacts, alongside raw completion, GPU, room, output cap/framebuffer, settings, timing samples, percentiles, allocation/GC totals, resident memory and zero dropped steps/stalls. Native `-hunter` controls slot zero; other ordinary bot slots use their existing rotating hunter assignment. A manifest cannot relabel all slots as the requested hunter. Entry is the only currently admitted timing phase. Reentry lifecycle/capture runs and profiled/readback/transition-GC runs remain separate evidence.

This runner does not execute a replay/input file: `replayExecuted` must be false, even when a replay artifact and independent Oracle report are attached. Those artifacts remain external reference integrity records; they do not establish that the timed bots consumed the replay or reproduced its simulation. Real replay-driven timing, dedicated-online timing, and Fruity completed-frame adapters remain unimplemented. `IDENTITIES_COMPLETE`/`COMPARABLE` remain evidence integrity scopes and do not close the complete canonical competition fixture. Mismatched timing scopes cannot pass by renaming fields.

## Canonical matrix and remaining collection

One cell is a fixed native hunter (Samus/Kanden/Trace/Sylux/Noxus/Spire/Weavel),
2/4/8 players, MP3 PROVING GROUND, offline/dedicated-online, low/high detail,
entry/reentry, and 60/120/144/240 Hz output at unchanged 60 Hz simulation. Freeze
the resolution, seed, warmup, duration, input replay, complete graphics flags and
normalized detail definitions before capturing. This describes 672 cells; it is
not a claim that 672 runs exist. Repeat compatible A/B arms in alternating order.
Room-reentry/resource stress and screenshot fixtures are evidence of those
operations, not substitutes for timing runs.

Comparisons require identical device/OS/driver, map/asset/replay/simulation hashes,
workload/settings, measurement scope and build configuration/normalized flags.
Source repository/SHA and binary hashes may differ as the intentional A/B variable.
Toolchains must be recorded and reviewed; their names need not match across forks.
A fork whose trace, map, settings or capture semantics cannot be normalized stays
`NOT_COMPARABLE`. Missing values on both sides never establish comparability.

No current exact-head Project/Fruity benchmark or physical Android result was
collected by this tooling change. Prior result files retain their original scope;
they fail this new gate until the missing provenance is independently supplied.
