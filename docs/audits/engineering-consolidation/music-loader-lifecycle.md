# Music loader and callback ownership

Phase 1B focused follow-up to attribution #493 and failed-startup cleanup #495.
Main base: `91b1c0c6c44e8c90effd250729709f791a84765e` (origin/main rechecked October 13, 2026).
Production implementation: `6224c17a7ede5b6fdd719add3bee67f7d05324c4`. Final native fixture: `0762e18b36d4c6bcdf8b11eb9d3a2966c9ebf2fc` (subsequent changes are native-only observation/assertion refinements).

## Proven failure and changed boundary

The baseline's actual extracted MusicPlayer methods, with controlled engine/decoder doubles, reproduce three deterministic defects: A/B constructors publish shared fields out of order and leave two attached players; cancellation before player publication retains the stream/provider; removal disposes a provider while a stale mixer snapshot is reading it. The characterization output is local, not an RSS/permanent-leak claim.

MusicLoadOwner now owns one active worker, one latest pending request, one published graph, and at most one queued retiring graph. It constructs decoder/provider/component privately and publishes only the matching request version. Ten thousand requests while the first construction is blocked coalesce into two constructions with peak concurrency one. SeqId.None retires the current graph without constructing another. Each failed/cancelled private construction releases its resources, and device-start failure retires its graph. Scene Remove marks retirement and returns without waiting; only the owner worker waits for admitted provider readers.

The fixed F32/stereo/32728 Hz music component retains the existing SoundFlow SoundComponent.Process gain/pan/mix path and implements the player's direct-read, non-looping, nonseekable stream path. Music tempo still belongs to NCSF, not a SoundFlow speed/resampling control. The component has no inputs, modifiers, analyzers, or owned resampling buffers. Reader admission uses atomics, including a full retirement fence, and never waits on the audio callback. A stale base Process call that passed Enabled/IsDisposed before retirement cannot read the provider; calls already reading finish before disposal. Source review of SoundFlow 1.4.1 confirms its post-GenerateAudio path uses local rented buffers/captured gain and empty modifier/analyzer snapshots for this exact graph. Mixer.RemoveComponent replaces a snapshot; it does not drain old snapshots. This is deliberately a music-specific component, not a general SoundPlayer replacement.

Existing explicit WaitForLoad calls have a two-second readiness deadline. Timeout cancels loading and logs that the track will be skipped; normal asynchronous readiness remains unchanged. Deadline cancellation and publication/readiness are mutually exclusive transactions under the owner gate, so a timed-out graph cannot publish in the gap before an outer cancellation. Final shutdown requests retirement and allows two seconds to drain; on timeout it skips unsafe native engine disposal. An uncancellable decoder constructor or permanently blocked provider read can leave the **one bounded owner worker and its resources pending** until process exit. This is bounded work, not guaranteed drainage under permanent third-party blockage.

## Validation

Host: macOS 27 / arm64; .NET SDK 10.0.401, runtime 10.0.12; Android workload 36.1.69; normal final package validated using command-local verified Temurin 17.0.20.1+1; default OpenJDK 27 is unchanged. No native audio engine, GPU context, or retail assets are opened by the default fixture.

| Check | Result | Evidence / scope |
|---|---|---|
| Baseline extracted real-method characterization | FAIL, expected | Three ownership/race defects reproduced; original local source and output hashes below |
| Production owner/component regression suite | PASS | 707 assertions; 10,000 latest requests; 120 device-free acquire/remove cycles; partial acquisition/cleanup/attachment failures; cancellation during attachment; permanent-block readiness deadline |
| Installed SoundFlow PCM comparison | PASS | Exact float equality against real SoundPlayer and RawDataProvider: partial reads, finite end, unknown-length transient-zero/end, pan/gain, pause/stop/resume, nonseekable position |
| Actual SoundComponent.Process stale calls | PASS | Retirement before GenerateAudio and during provider Read; disposal followed by safe stale completion |
| Warm stereo callback allocation comparison | PASS | 1,000 calls per component; baseline and candidate both allocate zero managed bytes on the calling thread |
| Desktop Release | PASS | Build and fixture on macOS arm64; existing source warnings, no errors |
| Android Release/AOT package | PASS | Normal final Release/AOT with Temurin 17; 64 existing warnings, no errors; no package-size comparison; no size improvement claim |
| Native MiniAudio/NCSF playback and 120 load/remove cycles | PASS | Two actual 120-cycle macOS trials; final fixture 3,539 total assertions; each graph consumed by a native callback and advancing NCSF position; owned graph/worker drained |
| Actual NCSF PCM/16-track masks and tempo | PASS | Exact decoded PCM equality against SoundPlayer for four masks, gain/tempo and pause/stop/resume; same decoder settings and assets |
| Physical Windows/Linux/Android playback/device-loss | NOT_RUN | No hardware acceptance claimed |
| Native allocator bytes/live handles and long-run RSS improvement | NOT_RUN | Only owned graph/worker/component and engine/device disposal observations measured; no plateau or improvement claim |
| Hosted CI | PARTIAL PASS | Production SHA renderer/Music job and 25 checks passed; final fixture/report head still requires the complete hosted matrix |

## Reproduction and local raw evidence

From the worktree root, substitute `dotnet` for the installed SDK executable in hosted CI:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/music-lifecycle-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release -m:1 -p:JavaSdkDirectory=/tmp/primehunters-jdk17-validation/jdk-17.0.20.1+1/Contents/Home
# Only after obtaining the exclusive native-test slot, with user-owned extracted assets:
# Use an isolated directory containing only a copy of paths.txt, and run from it.
PROJECT_PRIME_USER_DATA=/tmp/consolidation-music-native-1a124fbd /Users/jarrett/.dotnet/dotnet "/Users/jarrett/.codex/worktrees/consolidation-music-loader/Prime Hunters Online/tools/music-lifecycle-check/bin/Release/net10.0/music-lifecycle-check.dll" --native "/Users/jarrett/Library/Application Support/Project Prime/files/AMHE1"
```

The optional native fixture requires a functioning output device and real assets. It compares decoded PCM for four masks across 16 tracks, gain/tempo/play states, runs 120 actual production load/play/pause/control/remove cycles, checks removal returns promptly and each owned component drains, keeps an independent stopped mixer sentinel present, and finally checks shutdown. That sentinel checks output lifetime only; it does not validate launcher cue, custom-map music, or Android SFX audible behavior. Per-cycle RSS/managed samples are emitted for inspection; no slope or plateau claim is automatic.

| Local evidence | SHA-256 |
|---|---|
| `/tmp/consolidation-music-characterization/LegacyMusicPlayer.cs` | `9298f3a885eaad2e573a973b44befb86ba156d46773d61c187cf8b546c48c886` |
| `/tmp/consolidation-music-characterization-result.log` | `c18928c24aac201eb28e70bc9bc1cf3a26a98da94bd7ed26d6b1c2e76c72b4fc` |
| `/tmp/consolidation-music-6224c17a-check.log` | `2fe6e7953cd1940772a0edac49375dea61cc3ba73585b8070d52472c596ddf5b` |
| `/tmp/consolidation-music-6224c17a-android-jdk17.log` | `cff2698fbc8d62a38ae8110eb7c4bf9dda46e15aedb9efdeeaa3baa40c8e8448` |
| `/tmp/consolidation-music-6224c17a-desktop.log` | `5a16f8e1cc79c4aa113af322fa6fd7c521a6370ea36141bdcd6e9cf8cea5c549` |
| `/tmp/consolidation-music-native-1a124fbd-trial1.log` | `a2c3c1f1944394ca3b675bdec6aac7e595740cf0faa79ef42a5cc4bd0f0fc4da` |
| `/tmp/consolidation-music-native-0762e18b-trial2.log` | `c90f0bcd53dd386a75467cf497a484361787a3b217178ae01466c4a30c6ce65a` |
| `/tmp/consolidation-music-0762e18b-build.log` | `21883e9ef98eeab39f30dc3a60d7f9203ccebe8c6bc8405b42d76f56ddd9c41a` |
| Installed `soundflow/1.4.1/lib/net8.0/SoundFlow.dll` | `8e5224ef27f7cd518c22533fd3fbe745eded9da56b1ed4c2f9611dbbe92bb399` |

SoundFlow source review: upstream LSXPrime/SoundFlow `16aa7783cf512cb2c89b11f0b774407104b4361a`, local `/tmp/soundflow-v1.4.1-research`. The regression actually links the installed package assembly; the source review is supporting lifetime evidence. Bulky machine logs and retail assets stay local.

## Native result and remaining attribution limits

Both native trials exited successfully and completed 120 cycles without forced GC. Trial 1 passed 3,538 assertions; trial 2 added explicit post-drain assertions and passed 3,539. Every cycle left exactly one independent stopped sentinel in the shared mixer, zero published Music graph, a disposed/idle retired component, and a completed owner task. Each played graph was consumed by MiniAudio callbacks, verified through AudioFramesRendered plus advancing NCSF Position. Final trial counters: `current=0 ownerCompleted=True engineDisposed=True deviceDisposed=True deviceRunning=False mixerDisposed=False components=0 callbacks=328`. SoundFlow device disposal does not dispose its empty managed MasterMixer shell; that bounded process-owned shell remains observable and is not presented as a native handle leak or an unsupported disposal contract.

Trial 1 per-cycle RSS ranged 222.58–570.58 MiB and ended at 391.39 MiB, with 141.54 MiB managed live estimate. Trial 2 ranged 249.53–453.17 MiB and ended at 279.05 MiB, with 140.02 MiB managed estimate. These snapshots were taken after graph retirement, before final output shutdown; they reflect decoder construction and natural GC/allocator retention. No matched original-loader native memory campaign, allocator/live-handle accounting, post-shutdown RSS plateau, FPS, or audible platform/shared-cue acceptance is claimed. The original user-owned game stayed running, and concurrent builders were permitted for this correctness campaign; timing readings are only timeout/nonblocking witnesses, not performance measurements. Native trial logs preserve map-import warnings from source metadata initialization and all samples.

Final native fixture assembly SHA-256: `977258340b3eae5058d194fff2186e072951e7f5b3c82d48ad86b285beb54ea1`. Its ProjectPrime runtime assembly SHA-256: `0ca75e9dd5df8eeb5b52d7d5c88fe20d59f6848ed8d4c0a92458f2c23213ec52`. Native fixture build passed with 30 unchanged Desktop warnings and no errors. Original settings and paths were not modified; only paths.txt was copied to isolated user data. No failed native trial occurred. The uncancellable constructor/read timeout limitation remains unchanged.

## Compatibility and integration

Music's existing faders, 16-track access, mute/volume/tempo controls and public methods remain in place. Track removal preserves the process-shared output instead of stopping the device used by Android effects and cues; custom-map scene silence uses its existing Pause. Those adjacent component/provider lifecycles are not repaired or accepted by this PR. Protocol 53, replay serialization, gameplay/60 Hz timing, GL/GLES, and platform project/package versions are untouched. This PR does not replace or duplicate #495's MusicStartup acquisition cleanup; preserve that separate helper during integration. Keep all sibling device-free CI steps when reconciling their common workflow anchor.

Rollback: revert this focused loader/fixture/workflow change; no saved-data migration. The focused macOS native loader stress gate now passes. Phase 1/P0 remains open for physical Windows/Linux/Android and adjacent shared-output behavior, the final hosted matrix, and broader native memory attribution.
