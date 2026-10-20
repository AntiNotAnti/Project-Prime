# Rocket, slide and HUD reliability results

Date: 2026-10-20. Classic remains the default. Current optional Quake III and
Traversal matches use profile v3 and protocol 71; all live peers must update.
Historical v1/v2 profile implementations and their replay identities remain
unchanged. The initial qualification used main `c4426b35fdbe8b656bfdafd92f14f64423186040`,
including its protocol-70 custom weapon/pickup boundaries. Publication also
integrates newer main `f99af4a6de1e5ee2222f9533c83afc7e708e5160`, including
private character animation archive support and offline custom world pickups.
Their draw/pickup hooks remain intact alongside the completed-tick crouch capture.

## Changes and reproduction

A real-input floor-shot fixture reproduced the rocket timing failure on the
unmodified protocol-70 build. It uses one offline local human under multiplayer
Battle rules; the live multiplayer test separately includes a native AI bot. Samus jumping on tick 0 and firing
on tick 1 took no self damage and gained no rocket lift: the missile reached the
floor after the body point had moved outside its blast radius. These native
multiplayer missiles use a binary splash damage curve; this was not evidence of
accidentally applying linear damage attenuation twice. All seven Hunters' first
protected shots matched their repeat shots, so no spawn-protection change was
made.

The new opt-in self-blast policy measures distance to the accepted biped capsule
and derives physical strength from base weapon splash, independently of health
handicaps. Native radius, line of sight, immunity and lethal health cost remain
in force. The authority captures its own capsule/explosion facts and retains
at-most-once payment and receipt checks. Classic, Momentum, other-player damage,
custom weapons and excluded forms retain their existing policy.

On the same actual-input Samus jump/fire sequence, the candidate took the native
24 HP self damage and reached 4.35647 world units above the start, versus 2.46582
without a rocket boost before the change. Same-tick launches also boosted all
seven Hunters. A shot six ticks late still missed when the capsule was outside
the blast radius. These heights describe the test arena and can be limited by
ceilings/collision; they are not universal ballistic targets.

Traversal power slides now start from an ordinary run at 90% of normal ground
speed. A fresh crouch request can wait up to six ticks for a physical landing,
then is consumed once. Active slides take precedence over automatic held-Jump
hops; a fresh accepted Jump still ends the slide. The 48-tick duration, no free
speed impulse, low-friction policy, collision exclusions and held-key
non-retrigger rule remain intact.

Movement telemetry is now a normal HUD editor element, with position, scale,
opacity, tint, visibility, preview, undo/redo, reset and saved profile support.
Existing element indices and old profile fallbacks remain stable. Its default
placement stays centered across supported aspect ratios. Repeated pictures
reuse formatted telemetry text.

Crouch body and first-person eye presentation ease over six simulation ticks.
The legs retain their native lengths and planted feet, with the animated bend
plane preserved where usable. Cosmetic eye movement is clamped against low
ceilings. Camera-history loss, timing resets and discarded intervals snap safely
to accepted state. Collision, aim, muzzle/source facts and replay checkpoints
remain canonical and do not consume the cosmetic timeline. Clearance shares one
broadphase across its sphere/sweep/edge checks, and owned human input avoids a
duplicate stance update.

## Windows verification

Builds, gameplay fixtures and rendering ran on Windows x64. No Mac builds,
performance runs or app focus were used. Sanitized source/binary/archive hashes
and selected results are in [the evidence manifest](evidence/reliability-polish-selection.json).
Raw logs and rendered images remain local; extracted game assets are not
published.

The unmodified protocol-70 baseline passed 5,323 assertions over 49 actual
missile emissions and floor impacts. Candidate integration 02 passed:

- A normal Release build with zero warnings/errors and 18 focused movement,
  authority, native-baseline, protocol and replay routes.
- 5,413 actual rocket launch assertions, including all seven Hunters, first and
  repeat shots, timing offsets, crouched launches, and native/Momentum controls.
- Running and buffered-landing slides, exact buffer expiry, low-speed refusal,
  duration/non-retrigger, fresh-jump cancellation and held-hop priority; existing
  tunnel, thin-rail, replicated stance and source-time tests.
- 56 unchanged native movement golden cases; current/historical profile identity,
  packet widths, source facts, self-splash immunity, handicap, capsule range and
  duplicate receipts.
- Four HUD authoring/runtime tools, including 991 HUD assertions and 44 controller
  contracts. Warmed HUD traversal allocated zero bytes in its measured loop.
- 15,098 custom weapon/match contracts, including the protocol-70 custom replay
  refusal boundary, which remains enforced after the live version bump.
- A real 40-second UDP match with an authority, two independent scripted human
  owners and one native AI bot. The 2,400-frame protocol-71/profile-v3 recording
  passed 116,975 passive replay checks, including 7,200 canonical stance
  comparisons and 400 crouched frames. The owners produced held hops and stance
  transitions. Diagnostic displacement rows (1/3) were retained; the 0.25-unit
  diagnostic trigger is not an acceptance threshold or proof of final collision
  resolution.
- Genuine unchanged protocol-61/62 Quake III and protocol-68/69 Traversal archives
  replayed successfully under their historical identities.

Final integration 03 passed a second normal Release build, ten focused routes,
all four CPU HUD tools and all 15,098 custom contracts. Its actual-input camera
coverage includes all seven Hunters, both body LODs, history loss, timing reset,
discarded intervals, recovery and a real authored low-roof clamp probe. Repeated
renders preserve canonical collider, CameraInfo, aim, muzzle and source facts.

The corrected pinned native editor bridge passed **476 real DOM checks** across
four viewport/density combinations, including the movement element's selection,
preview dimensions, nudge, layout fields and exact undo. The same harness first
failed on the old native 14-element bound, then passed with the rebuilt bridge;
its assertions were unchanged. Explicit managed index-14 acceptance/index-15
refusal checks also passed.

Native Windows OpenGL captured **27 PNGs**: standing and completed ticks 2/4/6/8
while entering and releasing crouch, each with body-visible, body-hidden and
identical repeated-render controls. All checks passed. Visual amounts progressed
0 → 0.2593 → 0.7407 → 1, then reversed on release, with a stable presentation
generation. Local image inspection confirmed planted feet and a smooth body
transition. The diagnostic uses the normal continuous scheduler with controlled
1/60-second inputs; PNG work does not qualify wall-clock frame pacing. Owned
processes and the one-shot interactive task were retired after testing.

Exact results and provenance are recorded in the evidence manifest. Integration
02's rocket/UDP evidence applies to its exact binary; the later managed
production delta is camera-history fencing and cosmetic eye collision checks.
The final native editor bridge additionally admits the fifteenth HUD element.


A final integration-04 normal Release build on `f99af4a6de1e5ee2222f9533c83afc7e708e5160`
passed with zero warnings/errors, followed by eleven targeted routes including
the actual rocket matrix, stance/pose/camera, 56 native golden cases and replay
identity/compatibility checks. The latest custom suite passed 15,156 contracts,
including stock pickups, followed by seven Windows UTF-8 source contracts and
the actual default-client startup smoke test. Source/binary leases stayed
unchanged, and cleanup found no owned processes/tasks. All movement/native
HUD/render fixture files are
byte-identical to the earlier qualified snapshot except the additive upstream
pickup draw hook in `Renderer.cs`; the earlier native DOM/GL evidence retains
its explicit snapshot scope.

## Limits and remaining work

Warmed profile and HUD allocation checks cover their measured loops only.
No whole-frame timing target, broad GPU/frame-pacing improvement or physical
keyboard/controller feel acceptance is claimed. Machine load can affect timing;
optimization remains a separate tuning task. Current visual checks use native
Windows OpenGL with scripted input. Mac and other platform/device playtesting
remain outside this run.

Intermediate failed attempts are retained locally: the baseline fixture first
tried a read-only input setting, then supplied insufficient raw pointer travel;
both setup mistakes were corrected before the baseline reproduction. The first
Windows clone required long-path support in that owned checkout. Integration 01
caught a historical protocol-70 custom replay bootstrap refusal that was skipped
by an equality-to-live-version guard; validation now starts at the immutable
protocol-70 boundary, with legitimate old native bootstraps still accepted.
The first real native HUD run also caught its C++ action decoder retaining the
old 14-element limit. That production bound was updated and the pinned bridge
rebuilt, with the original failing run retained. The visual diagnostic originally
used debug stepping, which correctly reset the new presentation fence every
picture; it now uses the normal continuous simulation scheduler and asserts
generation stability. No acceptance threshold was weakened to make these
attempts pass.

Open PR #848 also edits third-person biped presentation in `PlayerDraw.cs`.
Its unmerged changes were reviewed separately and are not included here; a
future integration must retain both its canonical anchor isolation and this
slice's partial-crouch isolation/eased visual calls. PR #849 changes bot-match
restart UI state and has no movement implementation overlap.

Hosted CI status is reported on the pull request. Local acceptance completes
before publication; merging does not wait for hosted CI or review, as requested.
