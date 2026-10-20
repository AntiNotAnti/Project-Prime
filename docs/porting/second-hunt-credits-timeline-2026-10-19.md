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

Frozen source `83531ea59a02593adf614cf2b786e5f827ef5057` includes main's
concurrent CW-05c change. Protocol remains 68; this slice changes no wire, replay,
asset or save format. Two independent 5,949-file git archives remained unchanged
through all 31 serial commands. Windows SSH authentication still failed; tests
used the user-authorized Mac fallback with SDK10.0.401 and Android SDK36/JDK21.

| Evidence | Result |
| --- | --- |
| Desktop / dedicated Release | Pass, zero warnings/errors |
| Android Release/AOT | Pass; 119 assemblies per ABI; 25 existing compiler/documentation warnings |
| Signed APK | v2/v3 verification pass; arm64-v8a and x86_64 include ProjectPrime AOT |
| Generated credits source / duration / timeline checks | 239 / 87 / 89 pass |
| Warmed tiny invalid duration input | Refused, 296 bytes allocated |
| Owned source / owned timeline probes | Pass; 11,044 timeline assertions |
| Importer, body/ball source and character contracts | Pass; original RVZ unchanged |
| Map editor / custom weapon contracts | 502 / 14,204 checks pass |
| Historical replay, map detail and all seven campaign/movie/save suites | Pass |

The metadata-only owned probe covers the pinned AMHE1 English source, 39 widget
bindings and 18 animation files totaling 20,032 bytes. All 20 input hashes and
the exact running assembly remained unchanged. No model payloads, source text,
pixels or commercial bytes were exported. Isolated test contexts were removed.
Both ending branches match every independently simulated source milestone:

| Page | Enter tick | First settled tick | Exit request tick |
| --- | ---: | ---: | ---: |
| 6 | 0 | 261 | 401 |
| 7 | 421 | 612 | 752 |
| 8 | 772 | 963 | 1103 |
| 9 | 1123 | 1314 | 1454 |
| 10 | 1474 | 1665 | 1835 |
| 11 | 1855 | 2046 | 2186 |
| 12 | 2206 | 2500 | 2560 |
| 49 good / 50 bad | 2610 | 2740 | none |

These are derived from admitted source values, never baked into runtime. Both
branches also verify exact authored OK item identity, one terminal acknowledgment,
stale/pending input refusal and retirement discarding an unconsumed return.
The nested-terminal and atomic-page-publication regressions reproduced against
the first DLL before their fixes. The latter reproduced an unreadable null-slot
failure snapshot; the final code retains a complete prior graph on budget failure.

Local ignored counts/hash/log receipts are in
`artifacts/sh-integration/credits-timeline-83531ea59/{results,source-manifest,source-integrity-after-tests}.json`.

| Frozen output | SHA256 |
| --- | --- |
| Desktop DLL | `ab9eeab8164fd7c1bf1d0d709856f35bfce9aa3ec389a54c77b076702cda3b84` |
| Dedicated DLL | `4be4e74d7ae679da93890f5c3515605278b0c966e8a0ab341bda4e35a88b639a` |
| Signed APK | `6a95156468b48211c0338640080df54d3c253e3dd2fa29b66d8dfc4fd422fb81` |

Other languages/revisions, original ROM timing parity, native/GPU rendering and
physical Android/controller/Windows runtime remain unqualified. Pure timeline
agreement is not original credits presentation acceptance.

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
