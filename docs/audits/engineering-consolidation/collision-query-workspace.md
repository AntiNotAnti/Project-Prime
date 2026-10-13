# Scoped collision query workspace (Phase 4A)

## Summary

- Goal: isolate collision candidate and face-deduplication scratch for synchronous nested queries and simultaneous independent scenes, preserving traversal, flags and floating-point collision results.
- Prerequisite: [Phase 0 baseline PR #489](https://github.com/AntiNotAnti/Project-Prime/pull/489). This family requires Phase 0 only; broad scene interfaces and network ownership changes remain separate.
- Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e`, rechecked against fetched `origin/main` on October 12, 2026.
- Initial candidate source: `b83491e334a40ec548a44800e8518e8591cdc4a6`; large-query allocation fix: `c605f755c30ade462d331d3ba13d87d38db0c8db`, branch `codex/consolidation-collision-workspace`. The [validation manifest](collision-query-validation.json) records source-file and tested assembly SHA-256 hashes. Assemblies were built from these files before committing them; commit metadata alone does not identify the tested binary.
- Changed boundary: `Formats/CollisionDetection.cs` delegates scratch ownership to `CollisionQueryWorkspace`; all ten explicit borrower sites in seven files now use synchronous disposal. Geometry math and result structures remain unchanged. The existing map-editor CI job runs the asset-free fixture on Linux, Windows and macOS.

## Evidence

### Source inventory and original lifetime contract

The baseline holds one static active list, one inactive candidate queue, one temporary traversal stack, and one `HashSet<CollisionFace>`. Every candidate getter clears the previous active list and returns that same list. Candidate objects are recycled and mutated by subsequent queries. An outer list, candidate object, or in-progress enumerator therefore cannot survive a nested getter. Every line, sphere, penetration and radius query also clears the same face set; reentering through the supported `IReadOnlyList` consumer overload can cause duplicate contacts.

The source inventory finds 95 calls in 46 files: 64 line checks, 10 explicit candidate getters, 10 sphere sweeps, one robust sweep, three penetration checks, and seven radius checks. Reproduce it with:

```sh
rg -n 'CollisionDetection\.(CheckBetweenPoints|CheckSphereBetweenPoints|CheckSphereBetweenPointsRobust|CheckSpherePenetration|CheckInRadius|GetCandidatesFor)' src/MphRead
```

| Main callers | Ownership and behavior retained |
|---|---|
| PlayerCollision (two getters), PlayerCamera (three getters) | Shared candidates remain valid through repeated body/camera checks, including nested overlap recovery. Camera's later query has a separate lease. |
| Trocra and GoreaMeteor (one getter each) | Enemy LOS candidates remain synchronous and bounded by their existing scope. |
| AimAssistWorld and the two Enhanced Hunters files (one getter each) | Existing line-query flags and entity inclusion remain intact; these changes do not retire or activate any feature. |
| BeamProjectileEntity, BombEntity, HalfturretEntity, PlayerBombReplication, OctolithFlagEntity | Convenience queries own candidate collection and deduplication for the duration of the call. |
| EnemyInstanceEntity and 15 enemy modules; PlayerAi, PlayerAiDodge, PlayerAiMapControl, PlayerAiPositioning, PlayerAiTactical, PlayerQ3Traversal, PlayerScan | Existing LOS, floor, radius, sweep, and recovery operations retain their geometry and control flow. |
| ItemInstanceEntity, PlayerDraw, Renderer particle collision; CharacterAcceptanceCheck | Visibility/presentation queries use the same isolated convenience path as gameplay. |
| KillcamController, ReplayCamera | Private replay camera queries cannot overwrite gameplay-query leases. |
| NetHistoricalTrace, NetDynamicGeometryHistory, NetSelfSplash, NetConfirmedImpacts, NetBombVisibilityProbe, HitRig, AdvancedMatchRulesCheck | Historical/authority queries remain synchronous; no network codec, rewind timing, or authority rule changes. |

`Scene.OnLoad()` calls `CollisionDetection.Init()` at `Renderer.cs:621`. Runtime scene creation calls `OnLoad()` at `Renderer.cs:8728/8920`, and the headless server calls it at `ServerSim.cs:258`. The old initializer unconditionally enqueues another 2,048 candidate objects on every call. Three extra calls in the fixture increase the unused queue from 2,048 to 8,192. This is source-backed managed ownership evidence; it does **not** attribute the separately reported process RSS growth to collision scratch.

### Replacement ownership

Each call borrows a workspace from a thread-local free pool. Active workspaces are removed from that pool, so nested calls and multiple scenes on one thread get distinct storage. Independent query lanes have independent free pools. Every face-checking call borrows its own deduplication set even when candidates come from an outer lease.

Disposal clears room/entity references, returns ordinary candidate objects, and clears the face set. Exceptions return scratch too. Reuse retains at most eight workspaces per thread, with at most 8,192 candidate objects and 16,384 face-set capacity per retained workspace. Exceptional queries still enumerate every candidate; oversized scratch is discarded rather than cached. `Init()` remains a compatibility no-op, so scene startup cannot accumulate another unused candidate bank.

This supports concurrent reads of **independent scenes**. It does not authorize concurrent mutation of one scene. Candidates and enumerators must not escape disposal or move to another thread. All repository borrower sites are synchronous; no candidate list escapes into delayed transport or replay storage.

Room candidates are still reversed across the complete room pass; entity candidates follow in reverse entity/slot/grid order. Grid traversal, DDA comparisons, face indices, room-only dedup insertion, strict `<` nearest-hit comparison, and every floating-point expression remain unchanged. Existing quirks remain characterized, including limits-with-null-point omitting entities and the convenience sphere sweep omitting entities.

### Before/after fixture

The asset-free fixture constructs two independent translated scenes, two room instances with distinguishable tied flags, and two entity colliders. It records candidate ordering plus every collision-result float as raw bits for ordinary/robust sweeps, lines, radius, penetration, filtering, equal-distance entity ties, and translated entity contacts.

The [618-row golden baseline](collision-query-baseline.txt), generated against the original assembly, matches the candidate byte-for-byte: SHA-256 `68461616B61605635F0AA9532FA7716F2E399CF9A9D0B2190B917A5E0F7D8435`.

| Scoped check | Baseline | Candidate |
|---|---:|---:|
| Nested candidate/object/enumerator lifetime | FAIL | PASS |
| Nested face-set reentry | 2 contacts (expected 1) | 1 contact |
| Two scenes simultaneously holding candidates | 1,000 failures / 2,000 checks | 0 / 2,000 |
| Unprotected parallel convenience queries | NOT_RUN: deterministic ownership failure already reproduced | 0 failures / 32,000 queries |
| Warm convenience allocations | 0 bytes / 40,000 queries | 0 bytes / 40,000 queries |
| Warm explicit borrow + robust consumer | 0 bytes / 10,000 borrows + 10,000 checks | 0 bytes / 10,000 borrows + 10,000 checks |
| Three repeated initializer calls | +6,144 unused objects | 0 allocated bytes |
| 24 active nested leases | Shared mutable storage | 24 distinct workspaces; eight retained |
| Warm large room query | 0 bytes / 100 queries, 4,225 candidates each | 0 bytes / 100 queries, all 4,225 candidates retained for reuse |
| Exact retained-capacity boundary | 0 bytes / 100 queries, 8,192 candidates each | 0 bytes / 100 queries, all 8,192 candidates returned |
| First above-budget query | No hard retained-storage bound | All 8,193 candidates returned; exceptional workspace discarded and references cleared |
| Disposal/reference clearing and exception return | No explicit return contract | PASS |

The deterministic simultaneous-lease fixture serializes acquisition/consumption while both threads hold their leases, isolating lifetime corruption from uncontrolled collection races. A separate candidate-only stress fixture runs genuine concurrent reads without that serialization. The baseline failures are test reproducers, not claims of a previously observed production race.

### Follow-up: large-query allocation regression

Root integration review identified a real regression in the initial 2,048-candidate retained limit. A valid 4,225-cell room query needs arrays with 8,192 capacity. Discarding its workspace on every return made **100 warmed queries allocate 43,387,200 bytes (433,872 bytes/query)**, while the original implementation allocated **zero**. The earlier single oversized-query test proved result completeness and bounded disposal, but did not prove allocation neutrality for repeated large queries.

The correction retains up to **8,192 candidate objects and capacity per workspace**, the next geometric capacity that supports the tested 4,225-candidate workload. Eight retained workspaces therefore bound each lane to **65,536 candidate objects**. The inactive queue's retained array capacity is now checked explicitly too. The fixed implementation allocates **zero bytes across 100 warmed queries** for both the 4,225-candidate fixture and the exact 8,192 boundary. These checks are part of the normal asset-free CI fixture; timing batches now run only with the explicit `--benchmark-only` option.

An 8,193-candidate fixture returns every candidate, drops its above-budget workspace (pool eight → seven), and clears captured scene references. At the 8,192 boundary, fixture telemetry records eight retained workspaces and 8,252 candidate objects total; aggregate item/temporary/inactive array capacities are each 8,640. Those observed totals include the other seven small cached workspaces and remain below the explicit lane ceilings. Cached candidates in every pool entry are checked for cleared room/entity references.

`MapBudgetValidator.MaxGridCells` permits 2,000,000 cells, so neither 2,048 nor 8,192 is a maximum valid map/query size. This is a bounded reuse policy. Zero allocation after warmup is verified for the tested queries **within the reuse capacities and nesting budgets**, not for every valid input. Larger valid queries remain complete and may allocate on repeated calls. No universal allocation-neutral or full Phase 4 performance acceptance claim is made.

### Timing and resource limits

Environment: Apple M4 Pro, arm64, macOS 27.0 / 26A428, .NET SDK 10.0.401 / runtime 10.0.12, Release, headless. No native window, GPU context, VSync/cap, graphics preset, profiler, or physical input device participates.

[Historical exploratory ABBA results for the initial 2,048-cap implementation](collision-query-abba.json) retain ten processes in `ABBAABBAAB` order, five per arm, with nine batches of 40,000 queries after 40,000 warmup queries. Tiered compilation is disabled for this timing comparison. Median process batch times are **129.640 → 134.496 ms (+3.75%)**. Other consolidation branches were compiling/testing concurrently, and batch variance is large; these timings do not establish a game CPU/frame-tail improvement or acceptance. The rejected initial default-tiering trial is preserved in the JSON. There is no FPS claim. These timings predate the large-query fix and do not measure its CPU cost. The established allocation result is **zero allocation after warmup for the tested small, 4,225-candidate and 8,192-candidate queries**, with bounded retained scratch. Above-budget queries may allocate.

## Validation

| Check | Status | Evidence / limitation |
|---|---|---|
| Desktop Release | PASS | Zero errors; 30 existing warnings. No compiler warning introduced by this family. |
| Asset-free query golden/lifetime/pool checks | PASS | 618 exact rows, nested dedup/enumeration, simultaneous scenes, parallel stress, exception return, 4,225/8,192 warm allocation checks, complete 8,193-candidate discard boundary and reference clearing. |
| Custom collision | PASS | Existing `map-editor-check --collision-only`: 74 assertions. |
| Native biped collision | PASS | Existing asset-backed headless `--biped-edge-collision-scene`: 51 assertions and no simulation failures. |
| Seven-hunter native movement tour | PASS within existing fixture | 603 frames/hunter; no hard rejection. Existing fixture explicitly excludes pads, moving platforms, teleports, external knockback, grapple, Spire wall climb, and physical HUD/camera input parity. |
| Form reconciliation | PASS | 699 assertions, including 150/300 ms form-delay scenarios. |
| Authority/owner/full-state/wire architecture | PASS | Existing `--architecture` v20 byte fixtures and owner-position rules. |
| Replay form history | PASS | 43 assertions across eight slots, transactional malformed-input rejection, restored delegates. |
| Replay format | PASS | 3,034 v2/v3/v4 format/order/CRC/recovery/malformed-input assertions. |
| Asset-backed private replay scenes/checkpoints/seeks | PASS | 1,801 exact gameplay/presentation hashes; 11,186 animation restores; seven detached world restores; 1,500 continuation frames; 793 projectile frames; 62 file/frozen-clip seek comparisons. No native window. |
| Dynamic collision history | PASS | Door/force-field/platform history, fractional sampling, catch-up, exception restoration and zero rewind allocations. |
| Eight-peer impaired UDP | PASS (local synthetic impairment) | Configured 0–400 ms latency, 40 ms jitter, 5% loss, 3% reordering, 1% duplication and 400 ms client pump stall; 128 exact-once controls. This is transport coverage, not an end-to-end collision match under severe delay. |
| Cross-platform CI step | IMPLEMENTED; remote result pending | Added to the existing three-platform map-editor job. |
| Physical renderer visibility/gameplay parity, Windows/Linux/Android devices, Android candidate build, live impaired-network gameplay | NOT_RUN | Draft integration/hardware gates; headless fixtures are not physical acceptance. |
| Actual frame p99/FPS | NOT_RUN | No new native window while root lifecycle benchmarks run; exploratory CPU timing cannot substitute. |

Reproduction commands (run from repository root with SDK 10.0.401):

```sh
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet run --project tools/collision-query-check -c Release -- --compare docs/audits/engineering-consolidation/collision-query-baseline.txt
# Focused repeated large-query allocations and retained-capacity boundary checks:
dotnet run --project tools/collision-query-check -c Release -- --large-allocation-only
dotnet run --project tools/map-editor-check -c Release -- --collision-only
dotnet run --project tools/formtest -c Release
dotnet run --project tools/nettest -c Release -- --architecture
dotnet run --project tools/nettest -c Release -- --replay-form-history
dotnet run --project tools/nettest -c Release -- --dynamic-geometry
dotnet run --project tools/nettest -c Release -- --transport-stress
dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -replayformatcheck
# ASSETS is the prepared installation containing paths.txt and extracted files.
dotnet run --project tools/nettest -c Release -- --biped-edge-collision-scene "$ASSETS"
dotnet run --project tools/nettest -c Release -- --movement-tour "$ASSETS"
dotnet run --project tools/collision-query-check -c Release -- --replay-integration "$ASSETS"
# Run against baseline/candidate builds in ABBAABBAAB order for exploratory timings.
DOTNET_TieredCompilation=0 dotnet run --project tools/collision-query-check -c Release --no-build -- --benchmark-only
```

To regenerate the original golden snapshot, copy the fixture project into an isolated checkout of baseline SHA `91b1c0c6`, build it against that checkout, and run `--baseline --snapshot <path>`. Baseline mode reports the original lifetime failures without asserting that they pass. This validation also built the identical fixture against a preserved original `ProjectPrime.dll`; the assembly hashes identify both arms.

## Risk and compatibility

- Protocol remains 53; packet identities/layouts, fixed 60 Hz gameplay, replay schemas, settings, geometry math and player rules are unchanged.
- The candidate getter return type now exposes synchronous disposal. All ten source borrowers are migrated; normal collision convenience entry points are unchanged.
- Inputs above retained candidate/face capacities and nesting deeper than the eight-entry pool can allocate on repeated calls while preserving all collision candidates. The limits govern reuse, not supported map sizes or truncation. The zero-allocation guarantee is confined to the tested within-budget cases.
- Enhanced Hunters scope edits may overlap its independent retirement PR; when integrating retirement, retain disposal on surviving callers and rerun the golden/replay/movement gates.
- Rollback: revert this family as one unit, including getter callers. Reverting only workspace ownership would restore cross-query corruption or leave incompatible scopes.

## Next slice

- Phase 4B responsibility/scene services work remains separate and must establish its own replay/movement/physics evidence. This change provides a small collision ownership boundary without introducing broad scene interfaces or changing simulation scheduling.
- Merge requires Phase 0 review, remote CI, and any integration/hardware checks the maintainer selects from the remaining matrix. Full engineering consolidation acceptance is not claimed.
