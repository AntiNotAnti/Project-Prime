# Explicit post-match save ownership (LIFE-02 first migration)

The shell previously tore down its scene before `MatchStart.AfterMatch` read
`GameState.Current.CommitSave`. This slice captures a quiescent owner's final normalized save
into `PostMatchCommitContext` before visual teardown, then commits it at the
original post-match boundary. It leaves renderer/native ownership and facade
bindings unchanged. It stacks on LIFE-01's local scene generation diagnostics.

## Exact data and supported boundaries

[`SceneGameState.CommitSave`](../../../src/MphRead/GameState.cs) needs only the
StorySave, slot, save path and serializer. The captured `StorySaveCommit` owns
immutable JSON, absolute destination and scalar diagnostic metadata. The capture
uses the existing normalization: Weapons masked to 0xFF, OmegaCannon in weapon
slot 2 removed, rooms 91–92 cleared; JSON indentation and numeric byte-array
converter are unchanged. Capturing normalizes the ending scene's StorySave before
visual teardown, and performs no filesystem write. Immediate CommitSave still
creates its save folder before normalization and writes synchronously.

No Scene, SceneGameState, player registry, player, StorySave array or callback is
kept in the handoff. In particular, StorySave.CopyTo is not used because it omits
Stats. All statistics and progression fields are serialized from the exact final
save. Slot zero and NeededSave.Never skip even lazy StorySave creation; launcher
Prompt continues to mean yes.

The post-match call needs no player-result/profile/progression/killcam/replay
snapshot beyond that save. `AfterMatch` still clears chat, stops active playback
and commits. Network PostMatchReport/MatchAward handling remains separate and
runs before the match/lobby transition.

| Supported path | Existing ordering retained |
| --- | --- |
| Quiescent shell leave / offline rematch / disconnect / failed launch cleanup | CloseMenu, **capture**, EndScene, NetSession.Stop, NetHostSession.Stop, AfterMatch(commit), ShowFrontScreen. |
| Quiescent shell persistent-lobby match end / rotation return | CloseMenu, **capture**, EndScene, AfterMatch(commit), ResetMatchState, PauseMenu.Reset, ResumeLobby. |
| Standalone MatchStart.Launch | Run returns after existing cleanup, **capture explicitly from renderer.Scene**, AfterMatch(commit), using-owned window disposal. |
| Existing public AfterMatch/CommitAdventureSave wrappers | Capture current owner immediately and use the same immutable commit. Android's wrapper receives this behavior without changing its existing render-thread cleanup/return order. An explicit Android scene handoff is not yet migrated. |

Full shell application shutdown still does not call AfterMatch or introduce a
new save: RunSession finally and Renderer.OnUnload keep their original cleanup
semantics. No production GC calls, shader/resource ownership, results, settings,
replay format or network protocol changed.

## Commit lease state and retry

Capture moves `Active -> Ending -> CommitPending` synchronously. Commit moves to
Committed or Failed; failure retains the immutable payload. Retry writes those
same captured bytes instead of reading a mutated/new world's state. Successful
commit is idempotent and Release drops the payload. Pending/Failed release is
rejected; there is no production abandon/reset API.

MatchStart keeps the pending lease in a static value-only owner until success.
The existing exception behavior is preserved on exit; a subsequent Begin retries
before accessing the window, constructing a new world or selecting its slot. A
still-failing write returns false and a specific LastError. Repeated preparation
of the same request reuses its immutable lease. A different request cannot
replace a still-owned pending save.

Menu's existing SaveSlot/NeededSave properties also advance a local request
version. Even a same-value assignment represents a new request. Completion clears
NeededSave only when that version still matches, so an old save cannot erase a
replacement world's save request. This is local save ownership metadata, not a
wire/replay identity or a replacement for foreground facade binding leases.
Calls remain on their existing scene/render owner thread; the context serializes
commit/release operations to prevent duplicate terminal writes.

## Device-free evidence

`dotnet run --project tools/nettest -c Release -- --post-match-save` exercises the
real production capture, normalization, serializer, writer and MatchStart pending
owner with an isolated temporary CWD/Savedata. It does not start a window, GL,
audio device, game-asset load or network session.

The test keeps an independent, source-linked frozen baseline normalization,
options and byte writer from `16b1d89bfb6ed7776b801b3e39d513e740ee5e58`
(GameState.cs CommitSave and ByteArrayConverter). Both immediate CommitSave and
handoff bytes equal that baseline across every StorySave property, including
Stats, high byte values, negative IDs and normalized special-room/weapon fields.
The fixture's saved-byte SHA-256 is
`575d615b5150732527fe454f7903ec049a92da17fe479302990225523427eae1`.

Measured Release nettest: 26 passing checks. Coverage includes capture without
write; immutable bytes after original-save mutation and slot/CWD/profile switch;
no-save gates; one terminal write; no overwrite on duplicate success; release;
real IOException from an obstructed destination; retry preservation; launch
blocked before world/window access until retry; same-value replacement request
fencing; actual Scene collection after diagnostic facade release while the
production static pending lease remains alive. Compiled IL call-order checks
verify both shell exits and the absence of newly added shutdown saves. They are
source-order evidence, not a substitute for rendered application exit tests.

## Remaining gates and honest limits

This is the first supported-path migration, not full LIFE-02 acceptance. Full
shutdown save behavior deliberately remains unchanged. Explicit replay-lab and
Studio playtest rollback, Android owner handoff and async owner fencing still
need independent migrations and real application transitions. Compatibility
facades must not be unbound on the strength of this patch alone.

The existing File.WriteAllText persistence format/write primitive remains
unchanged. A failed in-process write keeps retryable bytes; process/power loss is
not made transactional or durable by an in-memory lease. Mid-write interruption,
atomic replacement/recovery, native resource teardown failures and rendered
10/100/500 scene transitions remain validation/design follow-up. No managed leak
fix, native lifecycle parity or broad campaign PASS is claimed.

Additional source gate: Adventure exit while an asynchronous door/room transition
is active is **not a validated flow** for this first migration. RoomEntity's
ProcessTransition performs setup mutations before its first cancellation check,
then publishes LoadedEntities and terminal TransitionState from a worker;
CancelTransition only signals its CTS. The quiescent-owner byte oracle does not
prove ordering against those mutations. The draft candidate needs worker-owner
fencing and native transition/exit evidence before accepting that flow. Do not
interpret the shell call-order check as blanket Adventure-transition acceptance.

Release dedicated-server compilation passed with zero warnings/errors. Desktop
nettest compilation passed with one pre-existing duplicate-using warning; LIFE-01
lifetime and setup ownership regressions also passed after the handoff change.
Native transition exits, Android rendered flow and power-interruption tests:
**NOT_RUN**. Original File.WriteAllText persistence semantics are preserved.

The active-transition gate now preserves the original timing in code:
PrepareBeforeTeardown returns no early context while InRoomTransition is true;
the shell tears down/cancels as before, then calls the no-argument AfterMatch.
That wrapper clears chat/stops playback before capturing at CommitAdventureSave,
matching the original save point. Two device-free tests prove this fallback does
not pre-capture and observes values at the original post-cleanup boundary. This
conservative fallback preserves timing; it does not certify worker quiescence or
solve active-transition save consistency. Future facade release must remain
ineligible for that flow until independently fenced and validated.
