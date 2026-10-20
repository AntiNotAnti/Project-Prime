# Original credits timing and animation-duration admission

This SH-08 slice adds an explicit 30 Hz pure timeline over the original admitted
credits/results document. No runtime route activates it yet. The current ending
trigger, movie lifetime, committed-save receipt, native results page and audio
session retain their existing ownership.

Behavior follows `MenuEngine.cs`, `MenuWidgets.cs` and `FrontendSession.cs` at
the [frozen MIT reference](https://github.com/BountyHunterKanden/SecondHunt/tree/41452f2d053fcfa679e33c00a73d120a53f59e61/src/MphRecomp.Core/Frontend).
Existing attribution remains in LICENSE. No upstream menu/render engine is
imported and no commercial assets or game text are included.

## Implemented

`CampaignCreditsAnimationDuration.Read` accepts at most 4 MiB and 1–64 slots.
It validates all five count-sized pointer tables, every advertised group header,
bounded monotonic node/UV LUT spans, derived node records and material/UV/texture
record ranges. Unsupported nonzero unused-channel pointers are refused. Each
channel is limited to 18,000 frames and each group to 4,096 records. The returned
immutable metadata owns the complete input SHA256 and first-slot channel frame
counts. Widget duration is `max(1, node[0], material[0], texcoord[0], texture[0])`;
later slots are checked but do not lengthen the selected slot's animation.

This is duration admission only. LUT values/indices, interpolation modes, model
node bindings and vertex instructions require a separate bounded renderer-side
admission. A future renderer must check the exact animation hashes rather than
load unrelated bytes under these duration identities.

`CampaignCreditsFrameTable.Bind` copies a bounded inventory using exact ordinal
normalized source paths, requires every referenced animation and retains only
referenced immutable snapshots. A genuinely absent optional animation has a
one-frame still duration; missing required data is unavailable. Caller dictionary
comparers/mutations cannot retarget the captured table. Both frontend input
identities bind the table to its admitted document.

`CampaignCreditsTimeline` begins at page 6. Its creating thread owns every step,
snapshot, explicit item intent and retirement. It performs no IO, wall-clock
sampling, Scene/model loading, GL/audio calls, executable callbacks or saves.
Sampling snapshots does not advance it; the future host must supply menu ticks
independently from the 60 Hz simulation and display refresh.

- Enter creates Hidden items, preserves original fixed-point delays and starts
  initially due items in source order. Newly due items start after the item loop
  at frame zero. Synchronous arrival actions can start a later item before its
  same-tick turn. Fractional delays/durations/timers use widened integer ceiling.
- Exact transition visuals run for their admitted duration. Missing transitions
  arrive immediately; missing steady visuals remain absent. No invented final
  entrance-frame hold or generic visual fallback is added.
- Source-order arrival actions retain the captured arrival state. Page 12's
  authored self-hide actions therefore begin immediately and survive the outer
  arrival call. Action order remains item mutation, calls, then page change.
- The first settlement tick establishes timer zero. The clock then continues
  even if an item later begins another transition. Only Enter resets it. A page
  request retains the old page until its exits settle. Exceeding the original
  120-tick exit grace period produces explicit failure for host fallback rather
  than declaring successful playback by jumping past unfinished animation.
- After an ending, callback 11 does not skip. Callback 12 requests good page 49
  or bad page 50 through normal exit transitions. Explicit results item intents
  can invoke authored kind-1 actions; global keys are not mapped here. Original
  OK is touch-only in the pinned reference. Any future accessibility mapping is
  an explicit host policy. Callback 206 emits one terminal return intent and
  never saves. Pending/stale/hidden item intents are refused.

Each external step has a 4,096-operation/64-level recursion guard. Cycles close
with a failure snapshot; they are not silently dropped. New pages publish fully
initialized item arrays atomically, so a guard failure cannot expose null slots.
Nested terminal actions stop all later callbacks. Frames use long counters;
overflow becomes explicit terminal failure. Retirement clears any pending return
and makes subsequent steps/intents inert.

## Qualification

Pending frozen qualification. Generated numeric fixtures and the opt-in owned
metadata probe will verify both branches and exact source-derived milestones.
No presentation or physical-device acceptance is implied by those checks.

## Unproven, disabled and rollback

The timeline is unregistered. Original credits/results drawing, bounded native
widget/animation evaluation, text/glyph placement, existing-window integration,
audio phase changes, context-loss recovery and physical input acceptance remain
implementation work. No original-playback completion claim is made.

Ending timing/statistics need separate genuine-new-save initialization and
pre-teardown capture work. Missing old records must remain unknown. None of
these pure owners captures a mutable StorySave or acknowledges a newer receipt.

Rollback removes the unregistered duration/table/timeline owners. Current
campaign endings and their accepted save/audio lifetimes remain unchanged.
