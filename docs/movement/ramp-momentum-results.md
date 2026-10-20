# Ramp momentum and slope slides

Date: 2026-10-20. Classic remains the default in practice and multiplayer.
Quake III profile v4 and Traversal profile v5 use live protocol 76. Historical
profile classes and recorded version boundaries remain unchanged.

## Behavior

On proven continuing walkable support, Quake III and Traversal now solve ground
acceleration using surface speed and redirect the result along the floor. This
prevents repeated uphill clipping from draining earned momentum. The contact
adapter preserves only the velocity entering each individual qualified contact;
it cannot restore speed removed by a wall or ceiling. Native jumps, blast
separation, first airborne landing, steep/surf surfaces, damaging/slippery floors,
carriers, Hunter abilities and alternate forms keep their existing paths.

Traversal slides retain their 72-tick (1.2-second) flat duration, friction and
single-use entry rules. A bounded downhill tangent force is applied once per
fixed 60 Hz tick, giving gradual uphill braking and modest downhill gain. Slide
entry on a ramp uses proven surface speed. Held Ctrl cannot renew an expired or
interrupted slide.

Tests also reproduced a separate raised-floor problem: the shortened crouch
candidate-gather sphere could round a resting floor just outside its radius.
The crouched broad-phase gather now includes a 1/4096-unit margin. Actual body
bounds, narrow-phase contact tolerance and ledge support rules are unchanged.
This fixes alternating floor contact that could cancel a slide after one tick.

Protocol 76 advertises the new rules. Replay discovery, metadata, checkpoint and
passive playback acceptance include 76 and reject unknown future protocols.
Historical Quake III and Traversal profile attribution, custom archive refusal
and custom ownership/knowledge/pickup boundaries are retained. Packet widths and
registry reservations are unchanged.

## Verification

All compilation and runtime verification ran on Windows over SSH. No Mac build,
simulation, rendering or performance workload was used. Actual-input fixtures
cover all seven Hunters, both affected modes, ramp entrances, uphill/downhill,
cross-slope travel, neutral friction, wall stops, crest departures, jumps and
first landings, with Classic and Momentum controls. Only labeled initial lane
placements are fixture-owned; support, velocity and slide timers come from native
simulation and actual W/Ctrl/Jump input.

- Ground rules: 335 assertions, including warmed zero-allocation checks.
- Asset-backed ground scenes: 413,037 assertions passed.
- All 21 focused movement, authority, stance, native golden, metadata,
  architecture, HUD and replay gates passed on the frozen integration candidate.
- Both real UDP cohorts used an authority, two independent owners and native AI.
  Quake III recorded 2,368 frames; its passive replay passed 78,691 checks.
  Traversal recorded 2,399 frames; replay passed 115,830 checks, including 7,018
  stance comparisons and 402 crouched frames.
- Six genuine historical recordings (protocols 61, 62, 68, 69, 71 and 74) passed
  with unchanged input hashes. Protocol-58 archive checks also passed.
- Latest custom weapon contracts passed 22,724 checks, actual online pickup
  passed 22 checks, and native pickup protocol/claim/network/replay gates passed.
- The normal Release build had zero errors and one pre-existing CS0649 warning
  for `CustomReadinessProbe.DiscardPickupAcks`. No unrelated warning fix is included.

Normal supported ramp walking holds approximately 7.1997 world units/second of
surface speed. Horizontal speed correctly decreases as more travel points
upward. Across all seven Hunters, the raised-deck reproduction retains 21
contiguous supported slide ticks at exactly Y=8.5 before the actual deck edge.
The earned downhill slide lasts 72 ticks and increases from 6.7938 to 7.8845
units/second; the uphill control brakes gradually from 7.1215 to 2.8343. A
separate fall-earned overspeed case brakes rather than receiving an artificial
minimum-speed boost. Real crest departure cancels the slide without held-key
renewal and follows the native airborne/landing path.

The Quake III recording was produced by candidate 05. Candidate 06 changed only
three nettest files to correct current-version expectations/report labels and
replayed the unchanged archive against the identical production binary. This
producer/replayer distinction is retained in the sanitized evidence.

Final integration uses main `2ed2da6862ee63761e4cae84f7320bd0933fc63f`.
The twelve newer upstream import/HD-acceptance files do not change the 732
Player/Movement/Network/nettest files compared with full qualification. A fresh
normal Windows Release build, eight targeted gates and playback of both retained
current multiplayer recordings all passed on this final integration. The ground
scene result is byte-identical to full qualification. The final source readback
matched all 3,534 runtime/test files; owned QA runtime process count returned to
zero.

## Retained failures and limits

Early fixtures pressed Ctrl before the intended slope or requested a downhill
speed gain while already above the slope force/friction equilibrium. These were
corrected with independent earned-speed and overspeed scenarios. A crest fixture
also initially required the center to pass Z=8 although actual native support
ended at Z=7.954095; the corrected assertion observes the actual departure.
The raised-floor contact failure was a production bug and required the gather
margin described above. All failed logs and original recording bytes remain
local. No movement acceptance bound was weakened to conceal a failure.

These checks establish CPU movement, authority and replay contracts. They do not
establish whole-frame performance, physical keyboard/controller feel or visual
animation quality. Dedicated slide animation, improved crouch footwork and
optional HUD/audio feedback follow as separate PRs. Hosted CI is observed on the
PR; merge does not wait for CI or review, as requested.

See the [sanitized selected evidence](evidence/ramp-momentum-selection.json) for
exact source/binary/recording hashes and final integration results. Extracted
assets, screenshots, endpoints and original recordings are not published.
