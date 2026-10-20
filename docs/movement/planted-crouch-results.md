# Planted low walking footwork

Date: 2026-10-20. Classic remains the default. This presentation-only change
retains protocol 76, Quake III profile v4 and Traversal profile v5.

Traversal crouch walking now uses a dedicated procedural low step around each
Hunter's native neutral stance. The supporting ankle retreats by the body's
accepted horizontal travel during 62% of each step, followed by a low swing with
peak lift 6% of the calibrated leg reach. A shared native LOD0 reach calibrates
cycle distance (1.35 times reach); each LOD retains its own neutral foot width and
segment lengths. Forward, backward and side travel share the same accepted-world
travel clock, independently of aim yaw and native clip frame switches.

Small lateral pelvis shifts favor the supporting foot, with restrained vertical
settling. Accepted direction changes turn gradually and start/stop weights blend
over six completed ticks. Fresh walkable support projects the step along the
floor; no collision queries occur during draw. The knee solve adjusts only the
bend pole around a fixed hip/ankle circle to keep a feasible knee above the foot
plane without stretching either segment. Visual reach bounds retain ankle plane
height. Unknown skeletons keep the existing fallback.

The former shortened native walking clip phase is removed. A smaller completed
travel classifier retains remote glide classification and exact captured native
camera/cannon bob. Visual walking lower curves use native Idle0. The entire native upper channel,
including walking weapon posture and shot/charge/ability clips, retains its own
curves and clocks; only the legs and cosmetic torso stance are procedural. Camera bob retains its existing 30% cosmetic walking scale and blends it toward
zero through the slide envelope rather than switching it at a speed threshold.
The canonical six-argument biped path, collider, controls, source-time weapon facts,
network packets and replay checkpoints are unchanged.

Remote feet consume the completed network playout position through a diagnostics-free,
read-only sampler. Accepted snapshot velocity classifies remote low walking versus
cosmetic glide; positional packet bursts cannot masquerade as a one-tick slide.
Held samples retain the accepted walking weight while phase spends only actual
resolved travel. Passive replicas now capture traversal presentation beside their
completed camera and first-person histories, using the recording clock rather
than a viewer-dependent ACK clock. Presentation resets cold on checkpoint restore;
cosmetic helper history is not added to the checkpoint format.

Source histories reset on life/form/eligibility, missing ticks, teleports and
presentation generation changes. Extra pictures cannot spend phase, change gait,
advance native clocks or alter gameplay. Existing draw isolation restores native
nodes and matrix stacks after each picture.

Windows qualification passed all thirteen focused CPU gates, including all seven
native Hunters at both LODs, source/canonical restoration, strict native weapon
axes, ground rules/scenes, native movement goldens, lifecycle/diagnostics and the
newer replay-owner/director/context contracts. Straight planted-foot drift is at
most 0.00000095414 world units (tolerance 0.0006). Per-Hunter walking checks range
from 74,908 to 74,934, plus 1,485 dedicated-slide checks each. Pure capture/sample
allocation checks pass without warmed managed allocations.

The fresh Windows UDP cohort used one authority, two independent human-owner
processes and one native AI bot. Both owners ran 2,400 fixed ticks and all three
processes exited zero. Owner0 recorded 190 walk/38 slide visual ticks and 33
physical-slide ticks; Owner1 recorded 402/135 visual ticks and 125 physical-slide
ticks. Their remote visual walk/slide counts were 154/457 and 432/177. These are
separate cosmetic and physical observations, not exact remote slide-entitlement
or phase-parity claims.

The fresh actual recording passed 121,068 general replay checks, including 6,287
presentation checks, and 6,288 strict presentation checks requiring both active
low modes. Recorded presentation included 596 walking and 646 slide observations
across 1,397 crouched actor observations. Repeated samples/viewer changes preserve
canonical position, velocity, collision, native animation/bob clocks and captured
history. Published-world restore is refused unchanged; new unpublished worlds
restore cold, continue equally, and repeat forward/backward seeks. The retained
protocol76 archive passed 118,816 checks with 2,986 presentation assertions; its
qualifying supported walk count is zero. The actual protocol68 Traversal archive
also passed, retaining its pre-stance contract.

The normal candidate04 Windows Release production build had zero errors and one
unchanged CS0649 warning. Later candidates rebuilt only nettest with exact
candidate04 production/reference outputs fenced by SHA256. Candidate05 supplies
the thirteen focused gates; candidate07 native OpenGL supplies 28 body/hidden/repeat
groups (84 original PNGs) on the NVIDIA RTX5080 active desktop; candidate09 supplies
the fresh networking/replay evidence. Three selected native walking/slide captures
were inspected locally. Repeated RGB is exact; all owned processes/tasks return
to zero. No Mac builds, simulations, game runs or performance tests were performed.
See [sanitized evidence](evidence/planted-crouch-selection.json) for source, binary,
recording and local evidence archive hashes.

Validation history is preserved. Candidate01 exposed an overbroad stop-camera
expectation and abrupt native-bob retirement; the production envelope and fixture
were corrected. Candidate02 was preparation only. Candidate03 passed its focused
CPU/replay and native GL checks, then image inspection identified a lowered
walking weapon arm; native upper curves are now retained. A subsequent source
review found raw network-arrival displacement and absent replica capture could
misclassify or omit remote low movement; the completed cosmetic capture fix and
regressions above address those paths. Candidate04 built the final production bytes, then caught an invalid fixture
baseline at zero gait weight: Noxus and Trace's current native idle spine differs
from Idle0. Candidate05 compares the retired pose with the actual native canonical
picture while retaining the strict active-gait aim/segment tolerances and exact
whole-picture retirement check. It also gates crouch-specific replay checks on
protocol 69+, retaining protocol68's pre-stance contract. Candidate05 rebuilds only
the test tool and reuses hash-fenced candidate04 production/reference outputs.

The first current-recording presentation check over-demanded active low travel
from the prior input schedule. Recorded crouched actors were moving/coasting or
airborne, but none combined real Standing, a walking clip and eligible positive
travel (qualifying count zero). The preserved recording now checks its actual
capture/purity/restore/seek facts. Fresh dedicated live walk/slide input cycles
provide separate strict owned, remote and recorded activation requirements.
The diagnostic-only candidate06 initially failed to compile because it accessed
a private animation property; candidate07 reads the existing native cursor via
reflection. Candidate08 then exercised the documented published-world restore
refusal. Candidate09 explicitly verifies that refusal leaves state untouched and
restores only new unpublished worlds, preserving the production invariant.

Straight, fully blended travel on stable supported planes is the measured foot
planting scope. Turns, entry/exit blends, changing floor seams and remote pose
smoothing are deliberately soft transitions and are not claimed as exact world
foot locks. Fresh floor normals are available for locally simulated bodies;
replica bodies conservatively use a flat presentation normal. This slice makes no
physical input-feel or whole-frame performance claim. Optional HUD/state/audio
feedback remains for the final slice. Hosted CI observations
are reported on the PR; merge does not wait for CI or review.
