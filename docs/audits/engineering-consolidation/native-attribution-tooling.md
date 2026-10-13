# Native-memory attribution tooling

Date: October 12, 2026. Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e`.
Branch: `codex/consolidation-native-attribution`.

This slice implements Phase 1A's controlled diagnostic workloads. It does not
resolve the historical RSS growth or change runtime ownership/GC policy.
The historical [completion report](../../performance/opengl-completion-memory.md)
remains evidence for its captured binary and fixture.

## Existing implementation reused

| Requirement | Existing code | Extension |
| --- | --- | --- |
| Exact map reload/failure witnesses | `ResourceStress.cs` reference/injected comparisons, fixed steps/RNG, GL ownership inspection | Preserve them in combined/map-GL arms |
| Studio renderer/cache reload | `RunStudioReload`, `MapViewportCache`, `Scene.CreateEditorRenderer` | Standalone Studio arm; per-cycle memory and program release |
| Disable map audio without a runtime flag | `ISceneServices.AllowsPresentationSideEffects`, `SfxInstanceBase`, `MusicPlayer.Shutdown` | Diagnostic forwarding wrapper only during AddRoom; shutdown before measurement |
| Audio initialization/destruction | `Sfx.Load/Update/ShutDown`, `ShutdownCompletion` | Windowless two-load audio arm; optional serialized close |
| Process memory and final drain | Existing RSS/GC sample, retained task list, diagnostic full GC/GL completion | Worker/handle/LOH telemetry, repeated quiescent samples, final detailed tasks |
| Immutable evidence and trends | Existing result JSON and binary MVID | Embedded manifests, sequential runner with DLL/log/result hashes, separate slopes/blocks |

The map-GL wrapper forwards all scene policies except presentation side effects
only while AddRoom runs. In the pinned source the only two such guarded
statements in `SceneSetup.LoadGame` are SFX load and room-music start. Services
are restored before initialization and stepping. The existing MusicPlayer static
constructor still probes a native device once; it is closed before isolated
measurement and explicitly recorded. No zero-startup-audio claim is made.
The new stress fixture omits the historical harness's unused initial map load
for all arms; run a new combined reference with the same immutable DLLs.
Non-stress renderer/performance startup remains unchanged.

## Reproduction

Build normally with the installed .NET 10 SDK:

```sh
/Users/jarrett/.dotnet/dotnet build tools/native-render-check -c Release
python3 -m unittest discover -s tools/native-render-check -p 'test_*.py' -v
```

This machine's extracted data is located at
`/Users/jarrett/Library/Application Support/Project Prime` (a space between
Project and Prime), with its operator-owned paths.txt. Do not commit game data.

Run the [documented sequential campaign](../../../tools/native-render-check/README.md)
with `--cycles 60 --frames 120 --quiescent-samples 30`, then repeat in reverse
order in a fresh output directory. Keep all `PROJECT_PRIME_GL_*` flags identical
across the map arms, including forced legacy policies if used. Use 120 cycles
for confirmation. A two-cycle pilot can validate execution, but cannot establish
long-run boundedness. Five warmup cycles are excluded from slopes by default.
Audio-only covers SFX/feedback initialization and fixed audio updates, excludes
room music/playback, and records no GL context. Studio-only covers the renderer
and cache fixture, excludes full editor UI and map simulation.

## Validation

| Check | Status | Evidence / limitation |
| --- | --- | --- |
| Harness Release compile against pinned baseline client | PASS | SDK 10.0.401; 0 warnings, 0 errors; `BuildProjectReferences=false`, `UseSharedCompilation=false`, `-m:1 -nodeReuse:false`; client source unchanged |
| Analyzer regression cases | PASS | Six tests: known slopes, warmup, separate cleanup points, quiescent time, null/nonfinite counters, insufficient samples |
| Sequential runner / evidence preservation | PASS | Three tests: dry-run invariant commands, timeout preservation/stop, existing-directory rejection |
| Real four-arm dry run | PASS | Manifest, runtime info and all commands generated without executing native workloads |
| Whitespace check | PASS | `git diff --check` |
| Physical map/GL/Studio/audio two-cycle pilots | PASS | macOS 27 arm64, same immutable DLLs, FORCE_LEGACY=0; exact paired map color/depth and 3 quiescent samples per arm |
| Long-run native boundedness | NOT_RUN / UNVERIFIED | Requires actual 60+/120-cycle arms and native allocation/VM-category attribution |
| Replay-only, full Studio UI, Windows, Android | NOT_RUN | Outside this diagnostic fixture |

The first full client compilation was canceled while the shared compiler was
busy with other independent branches. A subsequent harness-only retry lost an
MSBuild child node (`MSB4166`). The independent single-node harness-only retry
passed; these infrastructure interruptions are not presented as successful
full-client builds. No production files changed in this slice.

## Risk, limits and rollback

Reflection stays in the diagnostic, matching existing harness conventions.
Changes to the scene service or audio owner shapes may require updating it;
audio telemetry recognizes both the original SFX fields and the candidate
resource-owner shape. Ownership counters describe existing objects, not a
global GL/OpenAL driver census. Unsupported process counters are null. RSS,
virtual/private bytes and GC committed/live bytes are distinct observations;
no subtraction is labeled native subsystem memory. Native allocation stacks,
VM categories and GPU driver residency remain unavailable. OLS/R-squared and
quiescent slopes are descriptive; the analyzer always marks boundedness
UNVERIFIED and never labels RSS growth a proven leak. Readbacks, forced GC and
lifecycle waits provide no FPS evidence.

Protocol, replay, gameplay, settings and production GC policy are unchanged.
Rollback is removal/reversion of this diagnostic-only slice. The next gate is
physical matched-arm attribution; only an identified ownership/concurrency
failure justifies a runtime fix.

## Preserved pilot findings

The first two-cycle combined pilot stopped after ~5 seconds at the new strict
`GL.IsProgram` assertion. OpenGL permits a currently bound program marked for
deletion to remain a valid name. The repaired assertion requires zero owner
program fields and DeleteStatus for any still-recognized program, counting
pending deletion separately without changing the binding. The failed JSON/log
and runner manifest remain in the local `artifacts/native-attribution/pilot-2cycle/`
directory and are not overwritten by the retry. The retry also restores the
existing ForceEveryone fixture hook before the stress constructor's early
return. No production ownership or GL binding policy is changed.

## Corrected physical pilots

All four two-cycle arms passed on macOS 27 arm64 using source
`d89def52b9c378c2f67000d0119e706b3f4969df` with a clean working tree. Only
`PROJECT_PRIME_GL_FORCE_LEGACY=0` was explicitly set; other production policies
used their defaults. Each map arm used eight players, 1920x1080, 120 exact
simulation steps per load, enabled High shadows and the default failure
schedule. Two cycles exercise the promotion and VAO stages; the longer campaign
is needed to exercise all five stages. Quiescence used three samples at 100 ms,
which validates sampling but is insufficient for plateau claims.

| Arm | Wall seconds | Completed workload | Result |
| --- | ---: | --- | --- |
| combined | 10.05 | Four map loads, two Studio sessions / six cache rebuilds | PASS |
| map-gl | 8.63 | Four map loads, zero Studio sessions, SfxInstanceBase at every live sample | PASS |
| studio | 1.76 | Two Studio sessions / six cache rebuilds, zero map loads | PASS |
| audio | 1.75 | Four native SFX loads / shutdowns, zero GL contexts | PASS |

All four map color/depth hashes matched across combined and map-GL arms. Every
map unload had zero program owner fields and one recognized program marked for
deletion while still current; this is pending driver deletion, not fully
released native residency. All four native shutdown tasks completed in both
combined and audio arms, with no fault/cancellation/backlog after drain. The
map-GL arm had four observations of one shared completed stub task, correctly
distinguished from four native close operations.

Immutable hashes:

- Harness SHA-256: `4d91d9976bc1374ef6700fa1e174f48a4171b8710201167dfb2cde011f9c9a61`.
- Pinned baseline client SHA-256: `22c7eb0fb0e59c7e40fa656c4bda1c20a58c42b5628e3fdd5f47fb79fb6ff0b2`.

Raw results/logs, analyses and runner manifest are preserved locally under
`artifacts/native-attribution/pilot-2cycle-retry/`; the original failed trial is
under `artifacts/native-attribution/pilot-2cycle/`. Bulky logs and game captures
are intentionally not committed. Root-task coordination prevented simultaneous
native workloads. The 60+/120-cycle hardware gate remains open.
