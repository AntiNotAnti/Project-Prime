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

Frozen integrated results follow after verification. Windows authentication
was unavailable; the user-authorized Mac fallback is used. Physical touch,
controller and Android gameplay, actual upper-door/connector travel, complete
campaign acceptance and performance qualification remain open. No achieved
display-Hz, frame-pacing improvement, source-lighting fidelity, cockpit or
ending/credits completion is implied. SH-08A/B are separate work. Rollback is
turning this optional preference OFF or reverting this room-detail slice.
