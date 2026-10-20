# SH-08C: optional native campaign map room detail

The existing campaign pause map can now replace explored room shapes with
detail from the user's configured local Hunters collision files. The new
`Detailed campaign map` preference defaults OFF, uses the existing native
Display settings and Android settings draft, and takes effect on reopening
the map. It does not alter room simulation, collision, saves, travel, camera
ownership, campaign menus, network or replay formats.

The existing Scene submits the detail through its ordinary untextured Ngon
draws and pause-map projection. Native room selection, centers, markers,
legend, doors, controller/keyboard Move-action panning and Android's mapping
to those same actions remain the owners. Native connector shapes and exact
visited-connector checks remain in place; this slice makes no heuristic
connector replacement. Unsupported/missing/malformed room detail retains
the entire native NAV room shape.

## Ownership and bounded source handling

On pause opening, the Scene owner snapshots exact native room bindings,
visited-room bits, collision paths, node layers and the two area halves'
already-transformed NAV origins. A bounded sequential worker reads detached
wc01 collision without using the simulation's shared Collision cache. It
never reads mutable StorySave, Model/Node, Player or Scene objects. Parsing
validates every section extent, allocation count and point/face/grid index
before the existing collision reader filters the native layer.
The sum of all grid-cell reference counts is also bounded before that
reader expands them; repeated individually valid spans cannot amplify a
small file into unbounded layer-filter allocations.

Each opening owns its cancellation and immutable result. Closing or reopening
retires the old result immediately. A shared read lane bounds outstanding IO;
retired waiters cannot publish into another opening. Individual missing or
malformed rooms fall back independently. The batch is limited to66 native
rooms and1048576 output vertices; each room also has strict input/output
budgets. The draw owner rechecks the real visited-room bit even for ready
cached detail. No source geometry is requested for an unexplored room.
Selected-room door markers and location-name lookup now recheck that same
live exploration state. A retained hidden selection shows the cartridge's
unknown-location label and no room-specific doors, for either map style.
Scene cleanup and player destruction retire the same worker independently
of HUD restoration, including leaving while the native map is still open.

The shared room-height helper preserves Fan Room Alpha's2.05 and Beta's1.305
factors for detail, the existing marker and native door symbols. Local Y is
scaled before adding the NAV origin and subtracting a connector translation;
the marker's one-unit lift stays separate. Detail uses the NAV origin which
already contains its area-half offset, without adding it twice.

## Actual geometry contract

An initial strict planar/convex conversion only qualified13/66 retail rooms.
The owned-source audit found real plane errors up to19.55 units, geometric
warps up to5.91 units, concave fan shapes and two referenced zero planes.
Those are not safely addressed by increasing an epsilon or projecting source
vertices onto physics planes. Existing Scene.RenderNgon already renders
these polygons as triangle fans.

The final presentation conversion reproduces that exact fan order and
winding, with detached triangles and geometric normals for map shading. It
never reorders, projects or repairs the source vertices. It skips only
exactly zero-area triangles, which produce no native raster area. Structural,
finite-coordinate, plane-index and allocation checks remain fail closed;
physics plane normals are not treated as presentation normals.

The read-only owned AMHE1 probe qualified all66 native campaign rooms:
154816 nonzero triangles,464448 output vertices,54 exactly zero-area triangles
skipped. All15 tiny nonzero triangles and41 fan triangles opposing their
face's first triangle were retained. The largest single room used16095
output vertices. Every output SourceFace, transformed vertex triplet, winding
and geometric normal matched a direct native fan reconstruction. Every
collision-file SHA256 matched before/after. This isolated probe used zero NAV
origin for geometry qualification; it does not prove live placement or GPU
rendering. Local metadata-only receipts are under
`artifacts/sh-integration/map-detail-native-audit`.

No source media, collision payload, textures, NAV models or screenshots are
committed or shipped. Diagnostic captures are private derivatives and must
be inspected locally and deleted after verification.

## Verification and remaining gates

The source-free helper/source/load suite covers91 checks, including exact
fan and winding preservation, tiny/zero-area behavior, bounds and index
refusal, native layer filtering, malformed-room isolation, cancellation,
retirement and detached snapshots. Native settings fixtures cover default
OFF, draft isolation, Discard, Apply, persistence, reload and later-store
failure rollback.

`tools/nettest --campaign-map-gl <owned extracted AMHE1> <fresh private output
directory>` is an explicit opt-in diagnostic using actual native Scene,
pause input, cartridge NAV/collision and the existing shaders/HUD. It creates
a disposable fixture save context, records source/binary hashes, exercises
scripted keyboard pan under virtual host cadences, and captures private
OFF/ON and hidden-room frames. Its scripted visited bits are fixture state,
not evidence of a real campaign playthrough. Fan Room arithmetic with real
NAV origins is distinct from standing at a real upper door or connector.

Frozen integrated results are recorded below. Windows authentication
was unavailable; the user-authorized Mac fallback is used. Physical touch,
controller and Android gameplay, actual upper-door/connector travel, complete
campaign acceptance and performance qualification remain open. No achieved
display-Hz, frame-pacing improvement, source-lighting fidelity, cockpit or
ending/credits completion is implied. SH-08A/B are separate work. Rollback is
turning this optional preference OFF or reverting this room-detail slice.


## Frozen combined verification

Production source `6724e5a6950c87a3e8ecd49e6e7793ecc6648cfc`, tree
`ac3fdbf8b4bfab1c7db21d8aabfe7f4028462ae3`, passed desktop and dedicated
Release with0 warnings/errors and Android Release/AOT/signedAPK with25
existing XML documentation warnings and0 errors. All5852 archived tracked
files remained unchanged. This union includes the earlier Overcharged,
protocol64 and lobby slices. DLL SHA256:

- Desktop: `5eff325be56ca0f13c7d97198f7800cbe5838ceb7ed166fc6f53dc192d820cfc`.
- Dedicated: `8d0820045512375bd5d1efd160594061e5a5316a630ff4a96db82fd472013664`.
- SignedAPK: `354ac4c7e59d17e85e0d100e965bc53464d209cea11b84f38011e82d182657e1`.

Passing regressions include map-detail91, campaign presentation/navigation/
journey/dialog/ship/endings, the full native menu/settings suite, settings
archive26, game files18, touch/Classic/handoff, Android input54 and pause38,
pointer1790, controller1014, character/source-rig904, import RVZ997 and CPP09197
plus bundle/batch/motion fixtures, ownership42, browser126, closed UDP
loopback652 and four replay suites (414/299/84/51). The explicit legacy
controller diagnostic uses the documented `PROJECT_PRIME_RMLUI_GL_SMOKE=0`;
its Avalonia-startup fixture assumes that mode. The bare diagnostic's stale
startup failure is retained in predecessor evidence. Native UI separately
passes its complete suite; no runtime UI policy was reverted.

The final actual-native GL diagnostic uses test-only commit
`981cace4e959f41ace22a502fc32bf8651d35795` against the exact production DLL
above. A complete5852-file witness confirms the sole changed tracked file is
the diagnostic; every production file is byte-identical. It passed1682 checks
on Apple M4 Pro/OpenGL2.1 Metal91.7 at actual1600x1200 and1920x1080 framebuffers.
OFF submits0 detail triangles; ON10286. Clearing the selected room48 visit bit
removes exactly3205 triangles, leaving7081. Its4 door-mesh draws become0;
legitimate reopened/current visited room45 retains2 doors, and explicitly
reselecting hidden48 emits0. Root visually inspected both aspect ratios:
the room shape and door glyphs disappear and the location reads UNKNOWN
LOCATION. All12 relevant source input hashes remain unchanged, and the
isolated save context is deleted.

The original6724 diagnostic incorrectly assumed reopening retained the
hidden selection. Native reopening correctly selects the visited current
room. The test-only correction first proves that room keeps its doors, then
explicitly selects the hidden node to test its annotation gate. The failed
fixture receipt remains recorded; this correction changed no runtime bytes.

Scripted held Move input advances60 fixed ticks under virtual60/120/144 host
cadences, with matching pan offsets within0.001; the corresponding picture
counts are60/120/145. These are virtual schedules and actual functional GL
draws, not achieved refresh, pacing or performance measurements. Normal
Pause exit and actual Scene cleanup while paused both retire the detail owner,
including refusal through an older completed load reference. Fan Room tests
use real NAV origins and the shared height arithmetic; the player remains in
room45, so actual upper-door traversal is not claimed.

All16 final private screenshots were inspected and removed, retaining only
metadata/hash receipts. Local evidence is under
`artifacts/sh-integration/map-6724e5a69`, including the production archive,
source integrity, separate harness witness and final GL receipt. Earlier
failed/superseded runs remain explicitly scoped. Actual Echoes source exports
were not rerun for this UI-only slice; their earlier frozen source proof
remains separate. Per user instruction this locally verified slice merges
without waiting for review or CI, with no CI result claim.
