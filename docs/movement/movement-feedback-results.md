# Movement state and slide feedback

Classic remains the default. This presentation-only slice retains protocol 76,
Quake III profile v4 and Traversal profile v5.

The optional movement HUD now separates horizontal and total velocity, and displays
AIR, SLIDING, CROUCH or GROUND using completed post-collision contact and stance.
Landing clears stale air-efficiency text. The existing movement.telemetry editor
element retains its ID, anchor and saved offsets, with matching larger bounds in
the running HUD and both editor previews. Existing position, scale, tint, opacity,
visibility, undo/redo, save and export/import controls apply to all three lines.

Traversal slides reuse the existing native terrain slide loop, or the terrain
rolling loop when no slide entry exists, with a bounded amount based on final horizontal speed and current supported
physical slide state. There is one update per movement tick; native forms and slippery-surface
behavior retain their existing path, and a larger existing terrain amount wins.
The fallback ID is tracked on the existing player source and explicitly stopped
on fade completion, terrain change, native form ownership and stance reset.
Surfaces with neither native mapping remain silent.
No new audio source, asset, dependency or handle is added. Footstep emission is
suppressed during supported Traversal slides while native sound timers and shared
RNG draws remain intact. Stopping, falling, form changes and teardown continue to
use the native source lifetime. The 18% amount-control cap is not a measured
acoustic loudness claim.

All fifteen focused Windows gates passed, including native movement golden
trajectories, lifecycle, replay ownership/director/context, Traversal scenes and
ramp rules/scenes. The movement HUD and amount helper passed 36 checks. The
asset-backed feedback fixture passed 1,276 checks for each of seven Hunters
(8,932 total), in addition to the outer stance suite. Each release, jump, form
and reset scenario earns 24 supported physical slide ticks through W then a
fresh Ctrl edge; Metal's rolling DGN reaches the original player source and
retains the loop flag. Released and unsupported tails reach zero, and each exit
stops the selected ID and clears fallback ownership. Native forms may retain
their own nonzero movement amount after the Traversal ID is retired.

The four separate HUD/editor/audio tools passed: neutral HUD layout and profile
round trips, controller editing, shared Avalonia headless UI, and device-free
audio ownership/lifetimes. Three Windows headless screenshots are preserved;
960x600 and 420x900 were inspected locally and show all three telemetry lines
inside the resized plate. These are headless editor captures, not native RmlUi
ABI or live-game screenshots.

Both fresh current-production Windows cohorts passed: Quake III and Traversal,
each using a real authority, two independent owner processes and one native AI
bot for 40 seconds. All processes exited zero, actual recordings passed general
recorded-world replay, and the fresh Traversal archive passed strict active
crouch/slide presentation checks.

All six retained actual historical recordings passed: protocols 61, 62, 68, 69,
71 and 74. Their original recording hashes stayed unchanged. Protocol 58 replay
compatibility passed as well. Final source and production binary leases stayed
unchanged; owned runtime and scheduled task counts returned to zero.

See [sanitized selected evidence](evidence/movement-feedback-selection.json) for
the source/binary/recording hashes, individual gate results, fresh cohort and
replay counts, earlier failure archive hashes, retained-body source attribution,
headless editor image hashes and cleanup receipts. Raw source, logs, recordings
and Windows image evidence remain local. No Mac builds, simulations, game runs,
performance runs or UI automation were performed.

The final candidate03 normal Windows Release production build completed in
58.47 seconds with zero errors and one unchanged CS0649 warning. Core SHA256:
`daa18283111da9d64dcf3ded8d50f4bca8dfa77fa14872b492fc35dff842358b`;
nettest SHA256:
`6f223e54b7ab05a99c58f710d726da4f6fdae28dc55f04b7c39f5b76086eb769`.
The frozen complete source manifest SHA256 is
`368380a113f9762446c4b65d570e8b98b6d0543349cbf14b575246d58223b9a7`,
against main `f1eabd493bb4a74849f661fa3b40822fce83a66d`.

The first normal-build feedback candidate passed its lifecycle, telemetry HUD and
stance rules, then failed the actual sound-request requirement for all seven
Hunters. A preserved diagnostic candidate confirmed that Metal has Slide=-1 and
Roll=32768; a positive native amount alone did not emit a sound. The final path
uses the mapped rolling DGN only where a slide entry is absent, tracks that ID on
the original player source, and explicitly stops it on silence, ID changes,
native ownership and stance reset. The fixture still requires actual loop
requests; it adds targeted source/ID stop assertions for release, jumping,
native form input and reset. Cross-terrain ID retirement is statically reviewed;
no authored cross-terrain audio-playtest claim is made.

The fresh combined qualification base also includes the accepted nonlethal
head-hit presentation from main (#899). The earlier slide/crouch native images
retain their original source and binary provenance; the new CPU/network/replay
qualification checks the combined normal production build.

Hosted CI on the preceding crouch PR subsequently passed Windows/Ubuntu
mathematics, archives and campaign checks, lifecycle, server compilation and
Windows input. Two UI fixtures failed in unchanged files: synthetic character
preview v5 readiness and campaign-ending totals viewport. Current PR CI status
is reported separately; there is no all-green hosted-CI claim.

Compatibility base includes the newer rifle cosmetic/audio ownership and Theatre
UI changes merged on main. The normal production build and fresh both-model
network cohorts qualify the combined source; earlier body-render captures remain
attributed to their own producer. The slide request fixture uses real running,
fresh Ctrl, release, jump/noncontact and native-form input for each Hunter. A
silent counting backend observes native requests and original source identity;
a naturally reached footstep opportunity verifies native timer/index transitions
and exactly one shared RNG1 draw, then restores only the isolated audio probe's
local clock/RNG before subsequent physics. It makes no device/audibility claim.

The added cue follows eligible physically simulated slides; remote/replay glide
inference remains cosmetic. This slice does not change movement rules, entitlement, packets, replay schemas or
weapon source facts. Real input feel and audible balance still benefit from human
playtesting. CI status is reported on the PR; merge proceeds without waiting for
CI or review as requested.
