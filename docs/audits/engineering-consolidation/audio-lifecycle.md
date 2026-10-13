# SFX resource ownership correction

Baseline: `91b1c0c6c44e8c90effd250729709f791a84765e` (`origin/main`).
Branch: `codex/consolidation-audio-lifetime`.
Host: macOS 27.0 arm64, .NET SDK 10.0.401, runtime 10.0.12.

## Attributable findings

- `SfxInstance.Load` generated 128 pool buffers but recorded only every second ID
  in its 64-entry cache. It also acquired a stream buffer and 129 sources.
- Desktop context destruction eventually reclaims those objects. Android's
  `AlcEs.DestroyContext` is a no-op; `CloseDevice` calls `SfxMixer.Close`, which
  only marks voices stopped. Scene cleanup never deleted its IDs. The static
  mixer dictionaries therefore gained 129 buffers and 129 voices per scene,
  retaining filled float PCM arrays for the process lifetime.
- Android close ran asynchronously on the shared mixer. A delayed old close
  could mark the newly loaded scene's voices stopped.
- After native acquisition failures, `Sfx.Load` replaced the instance with a
  silent stub without closing successfully acquired devices/contexts. Its
  previous teardown assumed fully populated managed instance arrays.
- Desktop create/destroy overlap was documented in the historical OpenAL stack
  sample. This is evidence of native lock contention, not proof of RSS growth.

## Correction

`SfxResourceOwner` records all acquisitions, validates handles/context activation,
checks AL allocation errors, and releases partial initialization. The pool now
allocates the 64 buffer IDs it actually uses, plus one stream buffer. Shutdown
stops managed sample ownership, deletes individual sources/buffers while current,
detaches the context, and closes native ownership exactly once. Cleanup attempts
continue after individual failures and expose a faulted completion task.

Desktop destruction stays on a worker. New scene audio acquisition drains the
previous close for up to two seconds during loading, falling back to silent SFX
if it remains pending. No device opens or empty cleanup queues behind that stalled
close. Native lifetime calls share one gate. This
bounds normal foreground ownership to one device/context; no waits or new tasks
are added to playback. Android completes its bookkeeping close synchronously,
so an old worker cannot stop a later scene's voices. Headless/thumbnail loads
continue to use the silent backend without reading assets or opening audio.

## Reproducible checks

```sh
~/.dotnet/dotnet run --project tools/audio-check -c Release
~/.dotnet/dotnet build src/MphRead.Android/MphRead.Android.csproj -c Release
```

The original audio-check passed its pool recycling, 10,000 failed allocations,
buffer saturation, routing, gains and settings-default checks before editing.
The extended check passed **750 lifecycle assertions**, including:

- 120 cycles using the actual linked Android mixer, filling every owned buffer;
  after each release, zero dictionary buffers/voices remain.
- 120 desktop worker-close cycles; peak one live fake device/context, zero after
  final drain.
- 120 asset/device-independent headless loads and repeated teardown.
- Eight injected acquisition failures: thrown/null device/context,
  thrown/failed activation, partial buffer batch, partial source batch.
- A deletion failure that still attempts context/device cleanup, reports failure,
  a paused destruction witness that prevents concurrent acquisition, readiness
  timeout without blocking fallback cleanup, context rebind after a probe,
  contained probe-close failures, and preservation of a failed load's cleanup
  task across silent fallback teardown.

These are synthetic resource-ownership results. They do not exercise Android
hardware, miniaudio playback, music decoding, real driver allocation failure,
frame timing, or process RSS. A baseline Android mixer-close characterization
also confirms that closing alone retains its buffers and sources.

The full native reload/transition comparison is owned by the separate attribution
slice. Windows/Linux/Android audible/device-loss checks are NOT_RUN here. No claim
is made that this correction explains or resolves historical macOS RSS growth.
A native close that hangs triggers silent SFX after the two-second readiness
wait; process-final cleanup retains its existing bounded wait. This readiness
deadline does not bound every third-party native call. Transition timing still
needs hardware evidence before claiming acceptance.

## Remaining separate audio concerns

The music engine constructor can abandon its engine if playback-device creation
throws. Music loading replaces a static task reference, uses shared mutable
stream/provider/player fields, and cleanup requires a player to exist. Cancellation
or failure before player creation therefore lacks explicit partial ownership.
Those concerns require a separate music lifecycle regression and change; this
slice does not change music/replay/gameplay timing or classify them as the cause
of observed native memory growth.

Rollback: revert this slice; there is no settings, wire, asset, replay or save
migration. Reverting restores the Android ownership defect.

## Validation status

| Check | Status | Evidence / limitation |
|---|---|---|
| Baseline audio-check | PASS | Original ownership-pool/routing checks, before edits |
| Final desktop Release and audio-check | PASS | 750 lifecycle assertions; 30 pre-existing desktop warnings; zero errors |
| Final Android Release AOT/APK | PASS | 117 AOT assemblies; final incremental packaging 21 existing/toolchain warnings; zero errors; earlier full build 71 warnings |
| Final Android shared-source compile | PASS | Release Compile target; 51 existing/toolchain warnings; zero errors |
| Concurrent final-build retries | BLOCKED then retried | Two MSB4166 child-node exits; logs retained outside Git; non-shared single-node build succeeded |
| Real native audio/reload comparison | NOT_RUN here | Attribution slice owns real-device and scene comparisons |
| Windows/Linux/Android audible or device-loss checks | NOT_RUN | No hardware result claimed |

Local task artifacts (not committed): `/tmp/consolidation-audio-check-final5.log`,
`/tmp/consolidation-audio-check-final5-build.log`,
`/tmp/consolidation-audio-android-build.log`, and final Android logs with the
`/tmp/consolidation-audio-android-final` prefix. Intermediate failed trials use
`/tmp/consolidation-audio-check-final2.log` and `final3.log`.

The existing Linux renderer-policy and Windows/macOS controller-policy CI jobs
also run `tools/audio-check`. The linked Android mixer and fake native backend
require no cartridge data, native playback device or Android hardware. CI status
for the PR remains pending until GitHub executes those jobs.
