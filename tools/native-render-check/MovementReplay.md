# Full client movement replay

`-movement-report <mapcompile-report.json> -idle -players 1 -hunter Samus`
adds a diagnostic-only biped replay. Add `-movement-alt` and choose one of the
seven playable hunters to exercise a natural keyboard-triggered morph followed
by alternate-form movement. Use a certified room such as GENERIC_FIDELITY,
`-seconds 16`, and no camera/capture/resource/window-cycle options. The first
BodyObstruction route must still exist in the current source graph.
`-movement-reverse` checks the reverse edge. `-movement-wall` inserts a genuine
blocking-wall control; acceptance for that control means traversal was blocked.

The tool loads the real OpenGL client and initializes a live animated hunter.
It swaps only the room collision list for packed authored collision, honoring
source overrides. One initial placement and velocity reset establish the start.
All subsequent poses, speeds, contact flags, morphs and tail movement come from
production simulation. In-memory WASD/F bindings make keyboard input reproducible;
preferences are never saved. Rolling forms counter-steer with pulse-modulated
keys to approach slowly. Walking forms release input when native friction predicts
a stop near the target. This driver never moves a body or overwrites velocity
after the initial placement. The replay ends as soon as its evidence is complete.

Each simulation frame records position, speed, health, controls, contact/form
flags, Kanden's actual five tail nodes and Spire's actual two rock positions.
Positive acceptance requires 60 consecutive grounded destination frames with
the correct stable form and actual movement input. An alternate-form run must
also observe the natural morph transition and at least 60 stable alt frames.
Blocking controls require sustained input and no destination arrival. Replay
failure makes the process fail even if rendering itself completed. Body/collision
and diagnostic-assembly identities are retained for evidence provenance.

These are bounded real-controller checks of the selected route. They do not
certify a complete map, its visuals, general special abilities, full tail/rock
clearance or all transport/entity behavior. Dust2 285->331 and its reverse have
slightly non-planar patches and a real height discontinuity. Do not waive
shared-edge/planarity checks to call this a continuous crease. Mandatory map certification remains active. Current results are retained with the versioned test evidence; this document does not freeze a historical failure count.

The replay owns a synthetic input stream independently of OS window focus. On
focus loss it supplies two neutral frames to release the ordinary hardware-input
barrier, then resumes controlled keys. `-movement-synthetic-focus-loss` explicitly
invokes the production focus-loss gate during an ongoing replay; this control
requires at least 30 applied movement-input frames afterward. It is a simulated
input-gate control, not an OS focus-ownership acceptance test. Frame evidence
records OS window focus separately from synthetic input focus.

`-movement-package-room` verifies the loaded package identity, source physics and complete collision arrays against the source fixture, then leaves the room collision unchanged. It cannot be combined with an injected wall control. Use this for full-input acceptance on a packaged source-stair fixture.


Packaged transport controls: -movement-source-launch requires a SourceLaunchProbe fixture and verifies an actual source-pad flight, airborne directional input with a measured horizontal velocity change, and the usual 60 standing arrival frames with strict body clearance. It briefly requests lateral steering during flight. -movement-source-teleport requires a SourceTeleportProbe fixture and verifies authored exit impulse/facing and protected exit frames before the same settling checks. Both wait for natural morph landing before approach, retain installed collision, and compare source trigger metadata. Neither rewrites player pose or velocity after initial placement.

`-movement-certified-jump -movement-jump-trial -movement-package-room
-movement-require-clearance` selects a generated biped jump from a passing
compiler report. Use `-movement-jump-route <Route>` when the report contains
multiple jumps. The harness freshly compiles the package and compares the saved
endpoints and all seven hunter body-path certificates, then verifies exact
directed packed anchors with an Aerial destination in both the compiler output
and loaded room. Saved certificate feet are evidence only; ordinary keyboard
input drives the actual native jump and strict clearance/arrival assertions.
This mode requires biped form. Alternate-form pose or traversal checks remain
separate, because generated biped jumps explicitly require unmorphing.
Changing a saved endpoint, hunter proof or path must reject before input starts.

`-movement-package-jump-probe -movement-jump-trial -movement-package-room
-movement-require-clearance` is a separate manual-region input probe. Its input
contains `nativeJumpProbe: { start, end, description }` and a package `project`
path. A fresh passing compilation and the current production all-seven-hunter
ballistic verifier must accept the exact coordinates, including supported
all-form endpoints and enclosing unmorph clearance. It verifies unchanged
installed collision before ordinary keyboard jumping. This reports manual
region access only; it makes no packed bot-route or navigation-anchor claim.

Opt-in contact diagnosis uses `-movement-contact-diagnosis` and optionally
`-movement-input-until <60..300>` to release ordinary movement keys at a bounded
frame. Selected frames record exact native body spheres and read-only virtual
continuation of static contact queries. It never writes the live actor pose or
velocity, changes collision settings, or relaxes existing acceptance limits.
The virtual normal-push sequence models the earlier sequential solver for
diagnosis; it does not model the production coupled-contact cone correction.

`-movement-layer-barrier -movement-nonwalkable-link -movement-package-room
-movement-require-clearance` is a separate finite-floor layer control. A fresh
passing package compile and the production finite-floor probe must separate the
exact endpoints. The actor must make directed progress under ordinary input,
remain on its original side of that exact convex collision part, and stand for
60 consecutive frames over its footprint near the destination X/Z. The full
3D destination must remain at least 0.4 units away, with no arrival, body overlap,
contact overflow or loss of health. This covers an upper-deck approach whose
X/Z reaches a goal below the deck; the ordinary horizontal-wall control retains
its existing distance and sustained-input requirements.
