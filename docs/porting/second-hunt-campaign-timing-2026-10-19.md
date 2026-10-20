# Optional campaign time records

This SH-08 slice persists optional fixed-step campaign observations and captures
their scalar values in the existing ending receipt. It does not replace the
movie chain, save serializer, committed-save owner or ending return path.

## Implemented

`StorySave.SaveStats.CampaignTime` is a nullable versioned extension. Neither
the StorySave constructor nor the extension's properties initialize missing
history. A default/legacy/deserialized save remains unknown; absent extensions
remain omitted when serialized. An unsupported version stays untouched and is
not displayed as a known duration. Partial fields remain partial.

Only the explicit `StorySave.CreateNewCampaign` factory initializes version 1
with all three counters at zero. `StartNewSave` calls it for a deliberate new
game; `ReadSave` calls it only if no save file exists. Existing JSON containing
literal `null` retains the ordinary unknown fallback. Reading, copying or
resuming an existing save does not opt its missing history into tracking.

The existing `RunSimulationFrame` elapsed-time branch advances these counters
once per eligible 60 Hz step, never during drawing or the 30 Hz credits timeline.
Its existing ProcessFrame/InProgress/no-dialog-pause/no-menu-pause policy is
retained. Additional gates require single player, loaded original story room
27–92, and no headless/replica/demo/network session. Camera sequences, movies,
fades and death animation follow the existing elapsed-time policy when otherwise
eligible. This is an explicit Project Prime observation policy; no original ROM
timer parity is claimed.

Total play ticks advance in every admitted room; current loaded room 91/92
advances only its matching Gorea phase counter. Pending transition destinations
do not select the phase. Unknown counters remain unknown, negative counters are
never exposed, and overflow becomes unknown rather than wrapping or saturating
to a fabricated elapsed time. The combined Gorea value requires both phases
known and an overflow-safe sum before conversion to whole seconds.

The accepted checkpoint owner already excludes Stats from progression CopyTo.
Death/checkpoint retry therefore preserves the live cumulative counters while
restoring progression; room transitions reuse the same StorySave. An explicit
Continue reloads the last committed record from disk. This slice adds no second
checkpoint or save authority.

`CampaignEndingSummary` captures nullable tick scalars before world teardown.
Its existing positional constructor/deconstruction stays intact; new body init
properties extend immutable receipt equality. The existing post-match context
captures exact save bytes and the receipt once; failed-save retry still writes
the original payload/destination and publishes only after successful commit.
`SaveWhen.Never` remains a valid uncommitted receipt without a new write.

Native and shared result views show total time as hours/minutes and good-ending
Gorea time as minutes/seconds. The original Gorea format intentionally drops
whole hours. Unknown values say `NOT AVAILABLE`; bad-ending Gorea says
`INCOMPLETE`. Formatting is invariant and truncates fractional seconds. Existing
deaths and defeated-Hunter facts retain their current owners; EnemyKills remains
a separate Project Prime fact.

## Qualification

Frozen source `e7e865c4a968a50edd2c1d29bded1e6911084bde` uses protocol 68.
Three independent 5,953-file archives (shipping client, dedicated server, native
UI test build) remained unchanged. There were 37 successful serial qualification
commands and one retained/resolved runner error: the first native UI invocation
supplied an authoring parent without the bootstrap file. It refused before
opening a window. Both corrected invocations passed on the same unchanged
frozen source; no production repair or test omission was needed.

Windows SSH authentication remained unavailable; the user-authorized Mac
fallback used SDK10.0.401, Apple M4 Pro/OpenGL2.1 Metal and Android SDK36/JDK21.

| Evidence | Result |
| --- | --- |
| Shipping desktop / dedicated Release | Pass, zero warnings/errors |
| Android Release/AOT | Pass, 119 assemblies per ABI, 25 existing compiler/documentation warnings |
| Signed APK | v2/v3 verification pass; arm64-v8a and x86_64 include ProjectPrime AOT |
| Counter/save/receipt contracts | 61 checks pass, including real isolated IO, unknown history, checkpoint and captured failed-write retry |
| Owned native Renderer clock | 2,465 checks pass against the shipping DLL |
| Native synthetic result views | 58 checks each at exact 1280x720 and 1024x768 framebuffers |
| Credits source/duration/timeline contracts | 239 / 87 / 89 pass; both owned ending timeline branches pass |
| Importer, character/body-ball, editor/custom-weapon, replay and all existing campaign/movie/save suites | Pass |

The actual owned room 45 test retains a fresh factory record through real player
spawn/warmup, then observes exactly 120 saved ticks from 120 simulation calls at
each virtual 60/120/144 Hz picture cadence (120/240/289 actual draws). There were
no dropped steps/stalls. Twenty draw-only pictures add zero. Native keyboard
menu pause, applied dialog pause, Disconnected and a temporary multiplayer Mode
predicate add zero; restored eligibility adds one. Predicate controls are not
full multiplayer/session tests. No achieved display-Hz, real movie playback,
replica, replay, network, custom-room or full story claim is made.

The entire owned extraction inventory remained unchanged: 3,946 files,
845,781,038 bytes, aggregate path/length/content SHA256
`8ec13bbe523e91215bb10bf6a4aaa838098b04e781f54f44587d5fda31a2f871`.
The clock test created no save directory or media capture and removed its context.
The previously observed native Apple texture-unit warning remains in the log;
this clock/draw ownership test is not a world visual-fidelity qualification.

The native UI fixture uses synthetic immutable receipts and a silent audio
sink. It verifies known `2h 03m`/`2m 05s`, unavailable history, bad-ending
`INCOMPLETE`, later source mutation, exact once-only return, document/modal and
retirement fences. Seven totals lines occupy 154 native pixels and fit inside
the scroll area without overlapping attribution; the return action stays
visible. Synthetic framebuffer captures were inspected locally.

A separate UI test core includes the existing replay-failure fixture define;
it never replaces the shipping DLL used for the owned-room clock. The native
bridge was freshly built from current C++ fingerprint
`f92fe38c599e1ab3322c0522945b28d4da0173dfc7171b6062f107f52a39d9a1`
with pinned RmlUi6.3 and FreeType2.13.3. Its SHA256 is
`e3749602ad59e7932dbc199f457ed09dbe048e98d64ac1aa5c92a3457170b59e`;
cache/output copies remained byte-identical. The stale prebuilt bridge in the
original checkout was not used or changed.

| Frozen output | SHA256 |
| --- | --- |
| Shipping desktop DLL | `99f85036bf5b0a94106a9a38f2949a56df1470a0208acac8a6923aca6ab8ef65` |
| Dedicated DLL | `d775c35b87d6e150daff2491cad5f519d4f4e3de04691158394c448f3ac74d28` |
| Native UI test core | `6b43075bdd60b89d9a775c7ec2d448f8d126d891ea4701fa38cbeed7ec3d4814` |
| Native UI test tool | `76ff19966f8f0b24dfadab546a00a0fb320ac8733c3a901085eec10472ccb714` |
| Signed APK | `f23d2847ea0f64201f5e4baf1a505c0f6ddec8e47495eb240cdbe6dd5074a392` |

Local ignored counts/hash/log/synthetic-image receipts are in
`artifacts/sh-integration/campaign-timing-e7e865c4a`. No protected payload or
owned-media image is committed or retained by the timing test. Physical Android,
Windows/Linux runtime, full story/movie timing and original ROM parity remain
unqualified.

## Remaining and rollback

Original credits/results rendering, source glyphs/backgrounds, source result
placeholder binding and physical-device/story acceptance remain unfinished.
No setting or UI route activates an incomplete original frontend. Legacy saves
cannot recover missing past timing; they intentionally remain unknown.

Rollback removes the counter hook and nullable extension/display. Existing
progress, movies, kill/death counters and save/receipt ownership remain intact;
new timing JSON is additive data that older serializers may ignore.
