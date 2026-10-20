# CW-10c — bounded copied static surfaces

This slice adds the leaf `CorpseStaticMeshBuilder` and tests it against actual
filtered stock room collision. It does not connect physics to the game, qualify
dynamic barriers or enable a physical presentation option. The engine/server
still have no dependency on the experimental physics assembly.

The single-use builder accepts copied world-space polygon vertices and the stored
plane `dot(normal, point)=distance`. A caller supplies a finite capture center and
half extent of1–16 units. The whole face bounding box determines intersection;
large surfaces crossing the region cannot disappear merely because their vertices
or centroid lie outside it. Every visited face counts against8192; polygons are
bounded to32 vertices and output to128 triangles. The builder never emits a
partial prefix when a nearby surface is invalid or the budget is exhausted.

Eligible polygons must be planar within.002 world units, convex, simple,
nondegenerate, consistently ordered and finite. Fan triangles retain the actual
vertices; no projection, concavity repair or omission makes a bad face acceptable.
Each fan must also have positive nondegenerate signed area projected onto the
stored normal; a within-tolerance warped collinear vertex cannot create a thin
vertical wall or a reverse-facing fan.
Output winding is right-handed counterclockwise toward the stored normal. The
existing BEPU adapter then converts to its clockwise convention exactly once.
IgnorePlayers excludes a surface; IgnoreBeams alone does not. Empty, failed or
previously sealed builders cannot publish another world. Successful construction
copies into an immutable world. Capture operations belong to one thread.

The native diagnostic loads27 actual stock rooms, after room-layer filtering,
and visits unique RuntimeData faces rather than repeated spatial-grid references.
Both supported MPH source formats use their runtime points directly, with only
CollisionInstance.Translation added; the stored plane distance receives the
corresponding dot(normal, translation). Grid MinPosition and a second4096 scale
are not object transforms. Wide source indices/spans are checked before copying.
Full geometry digests, references, Active/Translation, gameplay RNG and scene time
are compared before/after. This diagnostic does not advance the game or call its
collision handlers.

Only12 of81 tested map/extent candidates were accepted. Most rejected faces
disagree with their stored plane; rejection is a required fallback, not a failed
gameplay map. These are local geometry candidates, not whole-map or corpse
qualification. A future runtime owner must also exclude unsupported nearby
doors/forcefields/moving surfaces, fence scene/connector/timeline changes and
retire before leaving the validated region. This builder has no engine references
and cannot provide those guarantees on its own.

The backend's falling guard now uses birth height minus12, rather than absolute
Y=-12. Valid negative-height rooms must not immediately lose a body. Translated
floor contact and unsupported falling cases exercise the same actual solver.
See [evidence](evidence/CW-10c.md) for measured results and explicit limits.
