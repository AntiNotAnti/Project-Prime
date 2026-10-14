# Conditional foreground release and room publication (LIFE-03 candidate)

This candidate scopes compatibility bindings to value leases and fences specific
room-worker publication boundaries. It preserves scene service/registry/state/RNG
ownership, default GC calls, native resource ownership and existing activation
policy. It is not full LIFE-03 acceptance or a managed leak fix.

## Binding and eligible release

[`ForegroundSceneBinding`](../../../src/MphRead/ForegroundSceneBinding.cs) binds
at the existing live Scene constructor and explicit BeginReplayLab activation.
Private replica construction never binds, and an accidental Bind(replica) is
rejected. The lease records local generation and owning thread only. It stores
no prior Scene/state/player references and has no restore chain.

A completed save alone does not unbind. The shell invokes TryRelease at the end
of the supported quiescent exit coordinator, after all existing reset and UI
return consumers. This matters particularly for persistent lobby return:
NetSession.ResetMatchState resolves NetPlayerBridge.Current via
GameState.Current.Owner. That owner remains bound through the reset. EndMatch
also completes ShowFrontScreen/training return before attempting release.

Release requires all of:

- The explicit post-match commit is Released and its local scene generation is
  still the current foreground generation.
- The owning thread and all three facade identities still match that Scene.
- Successful DoCleanup and visual release boundaries have completed.
- No active room transition or pending tracked room worker exists; the commit
  was not captured at the unsupported legacy active-transition timing.
- No known unfinished streaming decode/movie decoder owner was observed at
  cleanup, and NetAiClient does not still own the scene.
- The owner is not a side scene, private replica or replay lab.

If any condition fails, the existing bounded facade owner remains. There is no
forced GC, wait for a worker, callback abandonment, production bypass switch or
resource finalizer added. Standalone, Android, preview, replay-lab and shutdown
release remain outside this migration; EndScene itself still never unbinds.

An eligible release creates fresh bootstrap registry/state/RNG objects and copies
only the configuration already carried by SceneGameState.ForScene, nickname
string values, MaxPlayers and RNG seeds. It does not carry actors, progress,
results, camera sequences or a Scene.Owner backreference. An old completion
cannot clear a newer world, including when a preview/replay activation has
changed the foreground since capture.

The source ownership map gains a known root category: PlayerAiData._globalObjs
contains PlayerEntity and NodeData references. HasSceneReferences observes that
ownership. After the actual handoff/cleanup/identity/thread gates pass, ReleaseScene
clears only entries whose Player.OwningScene is this ended owner, including those
entries' node references/counters. It does not clear newer actors, the visibility
matrix, global counters or the entire AI table. It is not an AI ownership rewrite.
This source graph and bounded cleanup are not a runtime GC-root dump or evidence
of an unbounded leak. NetAiClient's IsBoundTo query only observes its existing owner.

## Proven room-worker publication gap

[`RoomEntity.ProcessTransition`](../../../src/MphRead/Entities/RoomEntity.cs)
previously wrote TransitionState.End after its last cancellation check, with no
atomic relationship to CancelTransition. LoadedEntities publication also checked
cancellation after insertion. Cancellation in either gap could publish after
teardown had signalled the worker.

The existing asynchronous task is now tracked for release eligibility. A small
publication gate is shared by CancelTransition and named publication sites:
node-data/AI-global assignment, entity insertion/enqueue, connector NodeRef and
terminal TransitionState.End. Each verifies the captured local generation,
non-exiting owner and both the caller token and room CTS. A cancelled or exited
owner cannot bypass the guard through CancellationToken.None. Entry also rejects
already invalid owners before metadata/setup. The valid synchronous completion
value remains TransitionState.End. The existing Task.Run cancellation scheduling,
LoadedEntities polling and normal load pipeline remain in place.

The local generation guard identifies the target Scene; it is not a new network
identity or a blanket rule that two independently live scenes cannot exist.
Cancellation/publication guards do not restore a previous foreground world.
RoomPrewarm's existing generation-fenced publication is unchanged.

Owned room publication is separate from the global AI compatibility adapter.
TryResetLegacyAiGlobals verifies all three exact current facades and local
foreground generation under the same gate used by Bind/release. Thus an old
still-live worker cannot reset a newer foreground world's AI globals, including
a foreground switch racing the reset check. Valid current-owner AddRoom and room
transitions still perform the existing global initialization. Private replicas
do not gain permission to reset foreground globals.

## Explicit incomplete scope

SetUpRoom, InitHunterSpawns and personality/asset preparation are not converted
into immutable staged data by this patch. They can mutate the old scene during
work that began before cancellation. Their complete cancellation/rollback and
cache/actor isolation need a separate source design and native tests. The
LIFE-02 active-transition exit retains original AfterMatch capture timing, and
LIFE-03 never releases that commit's facades even if its phase later changes.
No early snapshot is certified as a final save while such a worker is active.

Replay lab rollback, preview construction policy, arbitrary queued UI/audio/
export callbacks, AI-global lifetime redesign, shader/native ownership and
network-session instance refactoring remain outside this slice. A cleared queue
is not treated as proof that its previous callbacks completed.

## Device-free tests and remaining gates

`dotnet run --project tools/nettest -c Release -- --foreground-binding` exercises
real scene construction/cleanup, post-match lease and facade release. It verifies
reference-free leases; retained binding until remaining coordinator consumers;
configuration/RNG parity; eligible weak collection while lease/snapshots remain;
old-generation and wrong-thread rejection; running/visual-pending owners;
old/new AI global sentinels, current-owner reset, scoped ended-bot entry release;
private replica isolation; active-transition ineligibility. A controlled worker
waits in the old final-check/publication gap, receives CancelTransition, and is
rejected by the real terminal helper. Reflection invokes the real cancelled
ProcessTransition with CancellationToken.None to prove it returns before setup.
Compiled IL checks preserve release after reset/UI return.

These tests create no native window, GL/audio device, game-asset load or socket.
They do not exercise the full worker asset pipeline or rendered transition.
The Release nettest build and focused checks passed locally: 24 foreground
binding checks, 26 real save/exit-order checks, 16 lifetime checks and the existing
scene-setup ownership suite (zero failures). The weak collection fixture leaves
an actual bot player and node reference in the production AI destination table
through save/cleanup and relies on eligible TryRelease to remove that root.
The only nettest build warning is the pre-existing duplicate System.Text import
in PlayerNameChatTests. Raw results are retained in the external life-evidence
directory; they are device-free evidence, not native flow acceptance.
The dedicated scene-ownership workflow runs the lifetime, save and binding
checks under the pinned SDK on PRs and main pushes; new hosted exact-head results
remain required.

Before any broad flow acceptance: rendered offline/rematch and persistent lobby
returns must preserve results, settings/save bytes, RNG/gameplay/replay hashes,
GL color/depth and native lifecycle; 10/100/500 transitions, active Adventure
load/exit, initialization and teardown failures, Studio/lab return, audio/export/
custom texture callbacks need covered runs. Obtain actual retained root paths
only after the final eligible owner release. Native flow gates and power
interruption remain NOT_RUN in this device-free candidate.
