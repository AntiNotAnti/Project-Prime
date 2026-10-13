# Music loader and callback ownership

Phase 1B focused follow-up to attribution #493 and failed-startup cleanup #495.
Main base: `91b1c0c6c44e8c90effd250729709f791a84765e` (origin/main rechecked October 13, 2026).
Runtime/test implementation: `539fa839bbd07c1308fcd31c154cd9a531bf7507`.

## Proven failure and changed boundary

The baseline's actual extracted MusicPlayer methods, with controlled engine/decoder doubles, reproduce three deterministic defects: A/B constructors publish shared fields out of order and leave two attached players; cancellation before player publication retains the stream/provider; removal disposes a provider while a stale mixer snapshot is reading it. The characterization output is local, not an RSS/permanent-leak claim.

MusicLoadOwner now owns one active worker, one latest pending request, one published graph, and at most one queued retiring graph. It constructs decoder/provider/component privately and publishes only the matching request version. Ten thousand requests while the first construction is blocked coalesce into two constructions with peak concurrency one. SeqId.None retires the current graph without constructing another. Each failed/cancelled private construction releases its resources, and device-start failure retires its graph. Scene Remove marks retirement and returns without waiting; only the owner worker waits for admitted provider readers.

The fixed F32/stereo/32728 Hz music component retains the existing SoundFlow SoundComponent.Process gain/pan/mix path and implements the player's direct-read, non-looping, nonseekable stream path. Music tempo still belongs to NCSF, not a SoundFlow speed/resampling control. The component has no inputs, modifiers, analyzers, or owned resampling buffers. Reader admission uses atomics, including a full retirement fence, and never waits on the audio callback. A stale base Process call that passed Enabled/IsDisposed before retirement cannot read the provider; calls already reading finish before disposal. Source review of SoundFlow 1.4.1 confirms its post-GenerateAudio path uses local rented buffers/captured gain and empty modifier/analyzer snapshots for this exact graph. Mixer.RemoveComponent replaces a snapshot; it does not drain old snapshots. This is deliberately a music-specific component, not a general SoundPlayer replacement.

Existing explicit WaitForLoad calls have a two-second readiness deadline. Timeout cancels loading and logs that the track will be skipped; normal asynchronous readiness remains unchanged. Final shutdown requests retirement and allows two seconds to drain; on timeout it skips unsafe native engine disposal. An uncancellable decoder constructor or permanently blocked provider read can leave the **one bounded owner worker and its resources pending** until process exit. This is bounded work, not guaranteed drainage under permanent third-party blockage.

## Validation

Host: macOS 27 / arm64; .NET SDK 10.0.401, runtime 10.0.12; Android workload 36.1.69; locally installed OpenJDK 27. No native audio engine, GPU context, or retail assets are opened by the default fixture.

| Check | Result | Evidence / scope |
|---|---|---|
| Baseline extracted real-method characterization | FAIL, expected | Three ownership/race defects reproduced; original local source and output hashes below |
| Production owner/component regression suite | PASS | 699 assertions; 10,000 latest requests; 120 device-free acquire/remove cycles; partial acquisition/cleanup/attachment failures; cancellation during attachment; permanent-block readiness deadline |
| Installed SoundFlow PCM comparison | PASS | Exact float equality against real SoundPlayer and RawDataProvider: partial reads, finite end, unknown-length transient-zero/end, pan/gain, pause/stop/resume, nonseekable position |
| Actual SoundComponent.Process stale calls | PASS | Retirement before GenerateAudio and during provider Read; disposal followed by safe stale completion |
| Warm stereo callback allocation comparison | PASS | 1,000 calls per component; baseline and candidate both allocate zero managed bytes on the calling thread |
| Desktop Release | PASS | Build and fixture on macOS arm64; existing source warnings, no errors |
| Android Release/AOT package | PASS | Normal Release build before final standalone retirement-fence hardening; 71 existing warnings, no errors; signed APK 108,865,201 bytes; no size improvement claim |
| Native MiniAudio/NCSF playback and 120 load/remove cycles | NOT_RUN | Exclusive native slot pending; opt-in command below |
| Actual NCSF PCM/16-track masks and tempo | NOT_RUN | Opt-in native fixture includes detached decoder comparison against SoundPlayer before playback |
| Physical Windows/Linux/Android playback/device-loss | NOT_RUN | No hardware acceptance claimed |
| Long-run native/RSS improvement | NOT_RUN | No memory-growth or performance improvement claimed from simulated lifetime checks |
| Hosted CI | PENDING | New device-free gate in existing renderer lifecycle policy job |

## Reproduction and local raw evidence

From the worktree root, substitute `dotnet` for the installed SDK executable in hosted CI:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/music-lifecycle-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release
# Only after obtaining the exclusive native-test slot, with user-owned extracted assets:
/Users/jarrett/.dotnet/dotnet run --no-build --project tools/music-lifecycle-check -c Release -- --native /path/to/AMHE1/filesystem
```

The optional native fixture requires a functioning output device and real assets. It compares decoded PCM for four masks across 16 tracks, gain/tempo/play states, runs 120 actual production load/play/pause/control/remove cycles, checks removal returns promptly and each owned component drains, keeps an independent stopped mixer sentinel present, and finally checks shutdown. That sentinel checks output lifetime only; it does not validate launcher cue, custom-map music, or Android SFX audible behavior. Per-cycle RSS/managed samples are emitted for inspection; no slope or plateau claim is automatic.

| Local evidence | SHA-256 |
|---|---|
| `/tmp/consolidation-music-characterization/LegacyMusicPlayer.cs` | `9298f3a885eaad2e573a973b44befb86ba156d46773d61c187cf8b546c48c886` |
| `/tmp/consolidation-music-characterization-result.log` | `c18928c24aac201eb28e70bc9bc1cf3a26a98da94bd7ed26d6b1c2e76c72b4fc` |
| `/tmp/consolidation-music-539fa839-check.log` | `42bbde3b8c573ccf3877e13763720610a3761bd47c3404d157996a8c879488fe` |
| `/tmp/consolidation-music-recovery-android-full.log` | `48a3d342b71dd0a6291fac888deb0b2b00d163dc1dd0c2c6db968dbee55fc137` |
| Installed `soundflow/1.4.1/lib/net8.0/SoundFlow.dll` | `8e5224ef27f7cd518c22533fd3fbe745eded9da56b1ed4c2f9611dbbe92bb399` |

SoundFlow source review: upstream LSXPrime/SoundFlow `16aa7783cf512cb2c89b11f0b774407104b4361a`, local `/tmp/soundflow-v1.4.1-research`. The regression actually links the installed package assembly; the source review is supporting lifetime evidence. Bulky machine logs and retail assets stay local.

## Compatibility and integration

Music's existing faders, 16-track access, mute/volume/tempo controls and public methods remain in place. Track removal preserves the process-shared output instead of stopping the device used by Android effects and cues; custom-map scene silence uses its existing Pause. Those adjacent component/provider lifecycles are not repaired or accepted by this PR. Protocol 53, replay serialization, gameplay/60 Hz timing, GL/GLES, and platform project/package versions are untouched. This PR does not replace or duplicate #495's MusicStartup acquisition cleanup; preserve that separate helper during integration. Keep all sibling device-free CI steps when reconciling their common workflow anchor.

Rollback: revert this focused loader/fixture/workflow change; no saved-data migration. Phase 1/P0 remains open until native stress, platform/shared-output behavior, and broader memory attribution gates are satisfied.
