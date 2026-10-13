# Music engine ownership after failed device initialization

## Summary

- Goal: dispose an acquired Music engine if playback-device initialization fails before startup succeeds.
- Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e`; branch `codex/consolidation-music-failure-cleanup`.
- Changed ownership boundary: MusicPlayer's static constructor delegates playback-device acquisition to `MusicStartup.InitializeDevice`. A successful call preserves the exact engine/device and existing shutdown ownership. Failure disposes the engine before the existing silent fallback clears its fields.
- Disposal errors are logged separately; the original startup exception reaches the existing diagnostics/fallback unchanged.
- Phase 1 slice following baseline #489 and native-attribution #493. SFX lifecycle cleanup is an independent family; no SFX files are modified here.
- CI runs the device-free startup fixture in the existing renderer state/lifecycle policy job. Its workflow anchor is shared with the SFX check addition and must retain both checks when integrating the independent PRs.

## Source evidence

`MusicPlayer` constructs/assigns `_audioEngine`, then calls `InitializePlaybackDevice`. Before this fix, failure entered the catch block and set `_audioEngine` and `_playbackDevice` to null without disposing the successfully constructed engine. SoundFlow 1.4.1's installed package XML describes `AudioEngine.Dispose` as disposing the engine and its associated devices; the same disposal already runs in MusicPlayer's successful process-shutdown path.

SoundFlow also has a finalizer. The finding is missing deterministic failure cleanup, not proof that the engine can never be reclaimed, and not attribution of the previous process RSS growth.

The internal helper is directly used by the production static constructor. The new device-free tool links that exact helper without touching MusicPlayer, SoundFlow, an actual audio device, a renderer, or game assets. A test engine counts disposal and can throw from disposal. The device callback injects failures at the point corresponding to production playback-device initialization.

## Before / after evidence

Environment: macOS 27.0 / Darwin arm64; .NET SDK 10.0.401 at `/Users/jarrett/.dotnet/dotnet`; Release configuration.

For the before-fix run, the new helper seam simply called its device factory, exactly preserving the original no-cleanup behavior. The same tool then reported **2 PASS / 2 FAIL**, exit 1: all 120 failed startup engines lacked a disposal call, and failed disposal was never attempted. This is an explicit ownership count in a synthetic backend, not a native/process memory measurement.

Candidate: **4 PASS / 0 FAIL**, exit 0. All 120 device-failure cases call engine disposal exactly once and preserve the original exception object. A separate disposal-failure fixture confirms that cleanup is attempted without replacing the startup failure. Successful initialization returns the same device and leaves engine disposal to ordinary shutdown.

## Validation

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/music-startup-check/music-startup-check.csproj -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
git diff --check
```

| Check | Status | Evidence / limitation |
|---|---|---|
| Before-fix startup fault injection | FAIL, expected | 2 PASS / 2 FAIL; 120/120 fake engines not explicitly disposed |
| Candidate startup fault injection | PASS | 4 PASS / 0 FAIL; 0/120 fake engines left undisposed |
| Desktop Release compilation | PASS | 30 warnings in unchanged sources / 0 errors; SDK 10.0.401 |
| Diff whitespace | PASS | `git diff --check` |
| Native MiniAudio startup failure on physical audio hardware | NOT_RUN | Root native-attribution campaign was not disturbed; no device initialized by this task |
| Windows/Linux/Android runtime startup | NOT_RUN | Shared helper uses IDisposable and Func; hardware/runtime behavior remains a platform gate |
| Native/process RSS, playback/sequence/track mute parity | NOT_RUN | No memory/performance/playback attribution claimed |

## Risk and compatibility

The disposal call is on the existing one-time startup failure path. Nothing moves into an audio callback, playback loop, render hot path, or loader worker. Successful startup, device format/sample rate, shutdown delegates, fallback behavior, playback timing, sequence handling, mutes, and public APIs remain unchanged. There is one delegate allocation at startup, outside playback.

If third-party engine disposal itself fails, the attempted cleanup is logged and the original startup failure still yields silent fallback. This cannot guarantee resource reclamation despite a failing native destructor; no retry or additional engine/device is created.

Rollback: revert this commit. No data-format or settings migration is involved.

## Remaining loader ownership issue

`MusicPlayer.Load` publishes `_stream` before track mute changes and provider/player construction. Cancellation after stream/provider construction can return without a player, and `Remove` currently only disposes when `_player` exists. This is a real source-backed partial-ownership gap, but changing Remove to unconditionally dispose the stream/provider is unsafe in this slice: `Sfx.ShutDown` calls Remove from the scene owner while the loader worker can still be constructing or using those same published fields. Loader `finally` also clears shared Loading/StopLoading flags, so overlapping requests require an explicit ownership/cancellation contract rather than a disposal-only patch.

A future focused loader slice should construct privately, fence stale requests, define publication/removal ownership, and prove cancellation/concurrent shutdown without disposing a stream still in use. This PR deliberately leaves those behavior-sensitive concurrency semantics unchanged and does not declare Phase 1 complete.
