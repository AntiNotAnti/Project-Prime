# CW-12f18 — opt-in ordinary-client package recording

This source contract describes a default-off recording path. Local qualification
and limits are recorded in the [evidence ledger](evidence/CW-12f18.md).

`PROJECT_PRIME_CUSTOM_WEAPON_RECORDING=1` permits `DemoRecorder.Start` only for a
healthy ordinary network client with confirmed package/readiness state, a current
accepted custom context and an exact bound online package. Authority/headless,
playback/replica owners, offline combat, private-character gameplay and developer
movement tuning are refused. The existing native `CanStartLiveRecording` remains
unchanged; this is a separate client admission path, not server or instant-clip
enablement. Admission also requires a complete current custom recorder state.

`ReplayLiveWorld` independently retains the already-prewarmed runtime package
from the current live Scene. It performs no store lookup or archive decoding.
The private owner starts from cached accepted native construction and the
recorder's current CWRS, with an exact custom checkpoint/descriptor. It begins
head-history coverage before stepping and consumes retained genuine accepted
records through `StepCustomInspection`. It owns its Scene/entities/RNG and does
not copy or borrow the live simulation world.

Accepted records wait in an8,192-record/4MiB queue. Private reconstruction and
capture stay on the scene thread. World13 snapshots are appended through the
explicit custom-world API when a restore point is needed or300 frames elapse.
The disk sink writes format7 `FullMatch` metadata, real accepted record clocks
and a quiet-period end frontier. Live recording preserves its nonindexed firing
policy; no captured owner HUD/audio sidecar is produced.

Starting is pending until a valid private world exists. The writer owns its
metadata; completed chunks can be retained for recovery after interruption.
Context/readiness retirement, recorder reset, private reconstruction failure or
disk failure retires the recording sink. Record retention and independently
owned package/Scene resources are released on reset/disposal. Recording failure
does not stop ordinary replica packet acceptance or mutate gameplay admission.

`Stop` and failure call `RetireCustomRecording`. On the owner thread outside an
advance, retirement releases the private world and retained package immediately,
without another simulation tick. Off-thread retirement requests are drained by
the existing `DemoRecorder.PollSaves` render-loop path even while paused. A capture
gate serializes construction and retirement; cleanup requested during an advance
waits until that step finishes. Foreground Scene cleanup also releases the world
on full exit. Actual lease/Stop behavior is covered by the evidence ledger.

Custom recording suppresses quiet end markers at relative frame0. The reader's
complete-frame baseline rule remains strict. A genuine accepted record at or
before the completed origin interrupts the custom sink explicitly; paused
frontiers admitting such records are unsupported. Native recording is unchanged.
The format7 writer worker retains the greatest Record/Checkpoint/Frame frontier
and emits one quiet255 marker only on successful completion. The native per-tick
writer behavior and strict readers remain unchanged.

Installation remains separate from acquisition and grants no weapon. The feature
is opt-in and locally qualified for actual recording, close/reopen and private
continuation. Ordinary theatre/export/killcam and server/clip gates are
not opened by this slice. Full CW-12, bots, every hunter/form and owner/audio
fidelity remain open; Android is deferred and CI is unobserved.
