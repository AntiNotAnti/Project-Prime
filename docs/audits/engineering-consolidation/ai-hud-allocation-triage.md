# AI/HUD allocation hypothesis: leave production behavior unchanged

Base SHA `91b1c0c6c44e8c90effd250729709f791a84765e`, branch
`codex/consolidation-ai-scratch`, .NET SDK 10.0.401/runtime 10.0.12, macOS 27
arm64. No native GL window was used. Recent bot motor/perception/map-control/
hunter/adaptation PRs #481–#485 are the baseline, not work to repeat.
This follows the [Phase 0 consolidation baseline](https://github.com/AntiNotAnti/Project-Prime/pull/489).

## Source and frequency evidence

`PlayerAi.cs:10159` allocates a source-level `float[10]` for destination candidate
selection. Its caller is `FindEntityRef(Type1)` at line 9163: populated references
return immediately, and an existing Bit10 candidate visibility search reuses
Type0 rather than running another destination query. `UpdateGlobals` resolves
one registered candidate search and clears the flag. This is conditional
navigation work, not an unconditional frame allocation.

Temporary diagnostic instrumentation added one static invocation counter and
one increment to the private method in an isolated worktree. The counter was
removed after profiling; no production AI change remains. Native ServerSim ran
`MP1 SANCTORUS`, all occupied slots as bots, 1,800 fixed 60 Hz steps, native
default RNG seeds (`Rng1=0x3DE9179B`, `Rng2=0`), slot-modulo hunter/difficulty,
no player input, no GL/audio. Different bot counts have different scene workloads;
the table is query-frequency evidence rather than a performance comparison.

| Bots | Query calls | Simulation failures | Calls / simulation second |
| ---: | ---: | ---: | ---: |
| 1 | 6 | 0 | 0.20 |
| 4 | 66 | 0 | 2.20 |
| 8 | 200 | 0 | 6.67 |

The eight-bot run's total step allocation was 24,707,792 bytes across 1,800
steps. That covers the entire scene and cannot be attributed to this query.
The one/four-bot profiles ran concurrently; total step timing is therefore not
used as comparative evidence. Frequency observations are limited to this room,
duration, tuning and headless authority path.

## Actual-method allocation and generated code

`tools/ai-navigation-check` calls the production method through a bound delegate
with the same deterministic 256-node sequence, query positions, 20,000-call
warmup and five 100,000-call measurement rounds. It validates exact selected
node identities and candidate order in 27 assertions covering strict ties,
capacity, exclusions, signed zero, nonfinite/overflow distances and fallback.

On instrumented engine MVID `d58cf504-f021-4ac8-98b7-10750e4965c0`, four warmed
rounds measured **0 managed bytes** for 100,000 calls; the first measured only
88 bytes total. All rounds produced checksum 13,188,512. The first run timed
1,180.849–2,544.319 ns/query; the FullOpts/no-tier diagnostic timed
2,455.744–3,239.986 ns/query. Concurrent build load and different JIT policy
prevent a timing comparison; these values provide no optimization/FPS claim.

FullOpts ARM64 disassembly of the same source-level `new float[10]` constructs
the array object at `fp + 32`, writes its length 10, zeroes the stack storage and
contains no heap-array allocation helper. This local code and allocation evidence
confirms stack promotion on this runtime/configuration. It does not establish
the behavior of Android AOT or other runtimes/platforms.

Independent confirmation against the pristine production method (counter removed,
MVID `b6639625-0f17-47c7-9cf6-e06d09f2ed56`) passed all 27 assertions and measured
**exactly 0 managed bytes in all five 100,000-query rounds**, with the same
13,188,512 checksum. Matching Array.IndexOf warmup removed the first-round
88-byte fixture overhead. Observed timings were 1,019.789–1,148.592 ns/query,
descriptive only under concurrent build load. Pristine FullOpts disassembly
independently confirmed the array at `fp + 32` with no heap-array helper.
The final formatted-harness repeat also passed all assertions and all five
zero-byte rounds with the same checksum, but timed 1,488.971–3,864.885 ns/query.
Both timing series are retained; this variability under concurrent builds is
another reason to avoid any timing-improvement claim.
Raw logs, temporary counter patch and disassembly are preserved locally in
`/tmp/prime-ai-characterization/`; no bulky logs or game assets are committed.
See the tool README for complete reproduction instructions.

Evidence SHA-256 bindings:

| Artifact | SHA-256 |
| --- | --- |
| Final diagnostic `tools/ai-navigation-check/Program.cs` | `ee12b78315e211c2a9ba390804df89e5bca3d36c6fec22a6d7bd47d416c5df9f` |
| Pristine micro log | `31afdf1529e0ea7715dac936b50fdf88c979912caca1129dfaf6fbdc45f84b49` |
| Pristine FullOpts disassembly | `41e650b84756c2babe0bf3c53c0a7138d484bbd26eb066288893071c946f0454` |
| Final formatted-harness micro log | `3001fe5d8c69039162c0f59fdd122ce880dd3709426d4a03fd817c2f6bd04e13` |
| Native 1-bot profile log | `cd46a08affd12347e203c838fbda423c18a4806d36a41ebc3a25cf1441f483e1` |
| Native 4-bot profile log | `62a70ea2d2b4cc5016e3f5e10d0b7ac97905abf30e66167bb6a1b14843a2114f` |
| Native 8-bot profile log | `43e12ac821a06f18a8797cc667f7fc5190d537129d835b59ed6db22201f04b93` |

## HUD and decision

`PlayerHud.cs:4106` filters/sorts/materializes notifications when a combat-award
block is queued. It is event-driven. The per-frame `ReflowCombatNotifications`
at line 4171 already uses bounded scans. No evidence here shows notification
queue LINQ is material to frame time or allocation pressure; its contents,
ordering, display and lifetime stay unchanged.

**Decision:** defer both production edits. A stackalloc rewrite would remove
source syntax without reducing the measured warmed managed allocation on this
runtime, and query frequency is modest in the tested native workload. Other
platforms/AOT, maps and bot behavior may justify a later profile, but that is not
evidence for a speculative change now. The only proposed deliverable is the
reproducible diagnostic and this negative-result record.

| Check | Status | Scope |
| --- | --- | --- |
| 1/4/8-bot headless native query frequency | PASS | SANCTORUS, 1,800 steps each, zero simulation exceptions |
| Query ordering/float/exclusion/fallback fixtures | PASS | 27 actual-method assertions, instrumented source body otherwise unchanged |
| Warmed managed allocation | PASS | Four zero-byte 100k rounds; one 88-byte total round, fixed synthetic query fixture |
| FullOpts native-code attribution | PASS | Local ARM64 JIT stack array, no heap-array helper |
| Pristine-source diagnostic rerun | PASS | 27 assertions; five 100k rounds, each exactly 0 managed bytes; pristine JIT stack-array evidence |
| Hypothetical stackalloc speed/FPS improvement | NOT_RUN | No production candidate was justified or implemented |
| Other maps, physical clients, Android AOT/device | NOT_RUN | No cross-platform allocation/behavior inference |

Rollback removes the diagnostic/report only; production gameplay is unchanged.

## Additional scene and respawn frequency confirmation

The October 13 continuation ran independent, sequential headless processes on
the same main source and runtime with temporary invocation counters in both
`PlayerAiData.Func2138D28` and `PlayerEntity.GetRespawnPoint`. The counters and
temporary harness output field were restored byte-for-byte after measurement.
Each fixture advances 1,800 fixed simulation steps with the same slot-modulo
hunter/difficulty and default RNG policy used above. The instrumented engine
MVID is `976098bb-fcc4-4310-8876-51c3690a20b6`.

| Room | Bots | Navigation calls | Respawn queries | Step failures |
| --- | ---: | ---: | ---: | ---: |
| MP1 SANCTORUS | 1 | 6 | 1 | 0 |
| MP1 SANCTORUS | 4 | 66 | 9 | 0 |
| MP1 SANCTORUS | 8 | 200 | 14 | 0 |
| MP3 PROVING GROUND | 1 | 12 | 1 | 0 |
| MP3 PROVING GROUND | 4 | 119 | 7 | 0 |
| MP3 PROVING GROUND | 8 | 235 | 18 | 0 |

`GetRespawnPoint` is reached while resolving a dead/unspawned player, after
single-player transition handling. Its temporary list is limited to the first
25 spawn points and preserves their order before selecting by frame modulo.
The observed maximum was 18 queries in 30 simulation seconds, rather than an
allocation per live-player frame. This evidence does not justify replacing its
selection algorithm or introducing another reusable owner. No respawn runtime
edit is proposed. This is call-frequency evidence; concurrent host builds and
profilers make total step timing unsuitable for performance comparisons.

Additional existing checks passed against the integrated lifecycle/collision/
retirement/fixture source: BotAiChecks 85 assertions and NetAiClientTests 102
assertions. The current-source pure desktop and Android pacing tools also pass
their deterministic deadline, native-presentation, cap-change and resume
fixtures. These are simulated policy checks; physical mouse/controller/touch
latency, Android battery/thermal behavior and rendered bot snapping remain
NOT_RUN. The existing Android coarse-sleep/short-spin policy stays unchanged.

Raw command, source-patch, engine/harness and log SHA-256 receipts are preserved
under the baseline worktree's `artifacts/phase7-frequency-confirmation-20261013/`
and `artifacts/phase7-additional-validation-20261013/`. Both manifests explicitly
exclude timing comparisons and hardware acceptance. Pristine production source
remains unchanged, and no previous microbenchmark binary was overwritten.
