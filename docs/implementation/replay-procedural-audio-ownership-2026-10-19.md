# Procedural beam audio follows replay ownership

Merged beam firing and impact accents write directly to the existing SoundFlow
mixer. The previous guard checked private replay ownership only for replica
scenes. A muted live scene could therefore emit an accent while a killcam owned
audio, and cached procedural voices could continue through a handoff after the
native sound instances had stopped.

The accent path now applies the existing replay audio-owner policy to every
scene. Genuine acquire/release transitions stop both cached firing and impact
voices without disposing the cache or rebuilding the audio device. Release keeps
the existing version-token and backend-identity fences: stale/zero tokens cannot
stop a later owner or a replacement backend. Device shutdown still disposes the
cache through the existing path. Waveforms, native weapon cues, distance/gain,
gameplay, movement, packets and replay formats remain unchanged.

The small cached-voice interface exposes the same production Play/Stop/Dispose
operations. An asset-free regression inserts two fake voices into the actual
bounded cache and tests live/replica permission, replacement acquisition, tail
stopping, stale release and cache preservation. Its Scene objects are identity
fixtures only; they never simulate or install foreground/gameplay bindings. This
is a deterministic ownership/lifecycle test, not an audible-device measurement.

Validation on macOS 27 ARM64, Apple M4 Pro, SDK 10.0.401/runtime 10.0.12, Release,
against main `ad75bbcd7` (including PR-07 and merged beam impact changes):

| Check | Result |
| --- | --- |
| Production and nettest build | Zero warnings/errors |
| `--instagib-ion-fracture` | 284 assertions, including the new ownership cases |
| Actual Native replay killcam | 2,355 checks; 24 lifecycle cycles, 314 historical frames, audio handoff |
| Actual Momentum replay killcam | Same checks and cycles pass |

After CW-03 and the CI fixture repair merged, the branch was updated to main
`3f67abc12` and rebuilt with zero warnings/errors. The 284 audio-policy checks,
all 56 unchanged Native golden trajectories, 1,181 custom-weapon pure checks,
45 actual offline combat scene checks, architecture, weapon-policy and immutable
launch-damage checks pass. The optional weapon runtime remains absent by default;
no audio owner or movement mode activates it.

Killcam checks use the PR-06 actual recordings and exercise the existing real GL
path. Their death/skip contexts are diagnostic identities, not newly earned hit
results. The recordings are ignored local artifacts. No game assets, credentials
or captured audio are committed. The existing beam-effect CI workflow now watches
the owner and regression file. Publication does not wait for CI/review; no
all-green or physical-audibility claim is made.

PR-07 merged with two successful, 24 running and nine queued checks at its
publication snapshot. Its separate portable-presence and Studio-focus CI repairs
passed local checks and merged in PR #726. Windows SSH is now connected for the later
platform matrix; PR-08/09 and physical input/performance acceptance remain open.
