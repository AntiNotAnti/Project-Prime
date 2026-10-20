# Low-travel transition consistency

Classic remains the default. This slice changes optional Traversal presentation,
not movement coefficients, input sampling, the fixed 60 Hz solver, collision,
jump/slide entitlement, canonical weapon origins, packets or replay schemas.
Protocol 76, Quake III v4 and Traversal v5 remain unchanged.

The previous walking/slide activation replaced the entire lower skeleton with
native Idle0 at the first positive visual weight. Feet had an envelope, but the
skeleton beneath them did not. The lower curves now cross-blend using the same
walk/slide amount. Positive native local scale, rotation and translation blend
before hierarchy composition, preserving rigid joint rotations instead of
interpolating whole world matrices. The upper channel still samples its own
native clip and clock. Exact zero/full weights use the original sampler directly;
steady walking/sliding does not evaluate two clips. No warmed pose scratch array,
source asset, dependency or per-frame history is added.

Mixed missing curves retain each endpoint's original parent-inheritance behavior
through an affine fallback. Collapsed/reflected optional transforms use a finite
affine fallback too; these fallbacks do not claim rigid native joint preservation.
The canonical six-argument biped path and normal node sampler remain unchanged.
Draw isolation restores native nodes and submitted matrix stacks after pictures.

Floor correction starts from the same native knee bend pole as stationary crouch
and turns toward floor clearance around the fixed hip/ankle circle. Both that
correction and cosmetic reach limiting fade with the visual envelope, avoiding a
second binary change at the first positive walking weight. Full walking retains
its declared floor clearance and support targets. Transitional pictures are soft
blends rather than exact world foot locks.

The low foot swing now matches support horizontal velocity at toe-off and
contact, and uses zero vertical endpoint velocity. Linear support travel, cycle
length, lift maximum and the 62 percent support interval are unchanged. Airborne
follow-through is bounded below four percent of the cycle length. This changes
foot motion only; physical displacement, acceleration, friction and jump timing
retain their original values.

Accepted remote speed enters cosmetic glide above 105 percent of crouch speed
and retires it at crouch speed. Snapshot speed jitter inside that band no longer
repeatedly restarts walking/slide envelopes. Invalid speed, gaps and a false
walking level clear inference. Local completed-distance classification and
physical slide ownership keep their prior behavior; no new replicated event or
remote slide entitlement is introduced.

The normal Windows Release build completed in 57.83 seconds with zero errors and
one unchanged CS0649 warning. The source manifest is
`c3bb335ba74452ad277db54358e57852a3a28324a3e5909a364ee44e60957467`,
based on main `017a7ea776c09ad2bc2d744d65ce145e9739b47a`. Production
Core SHA256 is `1bb9f04cf1e0b9275154cef2dd5cca2f48c2f3c41390c70263e148defa934e37`;
nettest SHA256 is `2141dae4c885f52274f3b71dc95f741fa80c4d3000267c02fd10e754d85402dd`.

The pure regressions require matching horizontal foot velocity and zero vertical
velocity at both stance/swing joins for both feet, retain exact linear support
travel, and bound airborne follow-through. A synthetic hierarchy checks matching
local curves, rigid segment lengths, shortest rotation, missing curves in both
directions, nonuniform parents, post-pitch order, untouched upper branches,
collapsed/reflected fallbacks and exact native endpoints. Repeated warmed
native/partial/full hierarchy sampling allocates no managed memory.

The actual asset-backed scene checks include all seven Hunters and both native
LODs, real crouch-walking in four directions and uphill, full-blend support-foot
planting, native segment lengths, upper animation/aim and draw restoration.
Fixed physical snapshots additionally test tiny positive walk/slide weights for
convergence toward the same exact zero-weight picture; only cosmetic timelines
are temporarily replaced and restored before another simulation tick. Physical
state, source-time facts, native animation clocks and completed capture histories
are fenced throughout those picture probes.

All fifteen focused gates passed. Pure stance/presentation checks total 22,442;
the asset-backed outer stance scene passed 89,569 assertions. Per-Hunter walking
checks range from 120,644 to 125,800, plus 1,539 dedicated-slide checks and 1,276
feedback checks per Hunter. All 35 low-walking lanes cover both native LODs and
20 phase bins for each foot. Maximum fully blended support-foot drift is
0.0000009541399 world units (unchanged tolerance 0.0006); maximum thigh/shin
errors are 0.00003159046 and 0.00003325939 (tolerance 0.0005).

Fresh 40-second Quake III and Traversal cohorts passed on the same production
build, each with a real authority, two independent owner processes and a native
AI bot. Every process exited zero. Both actual recordings passed general replay;
the Traversal recording also passed strict replay requiring active recorded low
walking and sliding. Six historical recordings (protocols 61, 62, 68, 69, 71 and
74) passed with their byte hashes unchanged; protocol 58 compatibility passed.

Fresh native OpenGL runs passed for Samus, Noxus and Trace on the same frozen
production Core, using the actual continuous 60 Hz scheduler and keyboard input.
The native render tool was built against hash-fenced production/reference
outputs. All 92 body-hidden/repeat groups passed, with zero unstable RGB pixels.
The 276 original PNGs remain private. Seven selected original walk and
slide captures were inspected locally. Repeated RGB, body-hidden controls and
source invariance are recorded separately in the selected evidence.

These images cover settled phase bins; they are not labeled as transition or
stop pictures. The all-Hunter/both-LOD fixed-snapshot tests establish zero-weight
entry/retirement continuity separately. Static images do not establish user
animation preference. Procedural crouch locomotion can still benefit from an
authored animation pass per Hunter.

See [sanitized selected evidence](evidence/consistent-low-travel-selection.json)
for source, binary, recording and archive hashes, individual gate results,
measured geometry bounds, current native-render producer and cleanup counts.
The fresh standalone Windows lease used a private exact .NET 10.0.401 SDK from
the official Microsoft distribution, verified against its published SHA512;
the machine's global installation and original extracted assets were unchanged.

All validation is attributed to its own frozen source and normal production
binary. Human input feel and animation preference remain playtest questions;
allocation or synthetic timing checks do not establish whole-frame performance.
No Mac builds, game/simulation/performance runs or UI automation were performed.
Hosted CI is observed once on the PR and merge does not wait for it or review.

Final frozen source and binary checks passed. Owned Windows runtime processes
and scheduled tasks returned to zero. No source, assets or retained evidence
were deleted.
