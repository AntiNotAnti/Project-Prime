# Portal preparation and compile-identical geometry

Implemented on Windows, 2026-10-21. R10/R11/R12 source repairs and focused
qualification are complete; real-map fast-camera, replay/Studio and other-device
qualification remain **UNVERIFIED/NOT_RUN**. No total-frame FPS claim is made.

## Implementation

- Release builds check the 32-frustum pool, 14-point clip scratch, ten-plane
  frustum and room-part capacity. Audio traversal has a 32-level/1,024-visit
  work bound. Overflow discards partial visibility through the existing null-head
  draw-all contract, including entity visibility; audible overflow enables all
  supported parts. The diagnostic includes map/part/portal context and emits
  once per room setup. Unsupported entity part indices cannot index fixed arrays.
- Scratch belongs to each RoomEntity/view, including the two former per-clip
  temporary arrays. A clip result is fully consumed before recursion. Rendering
  still refreshes from the current picture's camera; the view-projection product
  is computed once per refresh. The already-existing `_portalSides` adjacency
  preserves `_portals` order, full NodeRef matching, active portals, connectors and
  forcefields, and is maintained at setup and connector creation. Audio remains
  tied to fixed simulation ticks.
- GenerateLists has a model/call/current-context-local reuse map keyed by source
  index, effective texture dimensions, initial texgen, room/matrix layout and
  inherited-attribute isolation. Desktop inherited color/normal remain draw-time
  attributes. GLES compilation stays per-mesh: skipping any compiled source can change
  mutable inherited normal state baked into a later source. No global
  cross-context/source cache was added. The map publishes only after successful
  compilation; failed compilation closes/deletes its list. Existing model leases
  and deduplicated deletion provide the shared owner, including retained GPU
  objects, so LegacyGeometryBatch/DesktopRetainedGeometry require no change.
- `PROJECT_PRIME_GL_GEOMETRY_REUSE=0` restores separate list compilation for
  controlled native A/B runs. R15 submission changes have **NO_ROLLOUT** here;
  existing combined-geometry telemetry/paths remain available.

## Commands actually run

All commands used native Windows PowerShell and the installed Windows SDK:

```powershell
$env:DOTNET_ROOT='C:\Users\Jarrett\AppData\Local\ProjectPrime\dotnet-10.0.401'
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet build tools/renderer-preparation-check -c Release
dotnet tools/renderer-preparation-check/bin/Release/net10.0/renderer-preparation-check.dll --native
# Tool-only iteration while other agents built the application:
dotnet build tools/renderer-preparation-check -c Release --no-restore -p:BuildProjectReferences=false
dotnet tools/renderer-preparation-check/bin/Release/net10.0/renderer-preparation-check.dll --native
```

The first application/tool build passed with zero errors and one existing
CS0649 warning. The first native attempt failed in fixture construction because
the test used an invalid numeric TextureFormat; `preparation-native-first-attempt.txt` preserves
that failure. The fixture was corrected to the named DirectRgb enum and rerun.
Durable receipts are `build-preparation.txt`,
`preparation-native-first-attempt.txt` and `preparation-native-final.txt`;
tool-only build iteration logs also remain locally as `build-preparation-tool*.log`.

## Observed evidence

`preparation-native-final.txt`: **PASS**, 14,793 checks; NVIDIA GeForce RTX 5080,
OpenGL 4.6.0 NVIDIA 617.42, application MVID
`b6489b3e-4bbc-4763-a085-8b030f892efe`.

The fixture independently clips 2,000 polygons and compares each ordered output
point exactly. It also invokes the production RoomEntity traversal with dense
cyclic graphs, malformed audio self-loops, connector identities, inactive and
opaque forcefield portals, clipping expansion, too many planes and 33 parts.
It verifies the draw-all fallback rather than treating overflow as successful
partial traversal.

For 100,000 warmed five-plane square clips, the retained audited allocation/loop
negative control allocated **38,400,000 bytes**; the production candidate
allocated **zero bytes** on that thread. Recorded timings were 49.512 ms and
36.239 ms respectively. These are one ordered microbenchmark on a shared host,
not an isolated frame/preparation throughput acceptance or total-process claim.

Three synthetic native reload cycles invoke actual GenerateLists/DoDlist and
retained uploads. Five mesh uses produce **four compiled objects instead of five**
and **768 instead of 960 resident VBO/IBO bytes** in this fixture. Compatible
geometry shares an ID; different dimensions, texgen or source identity splits.
An independently compiled side-model picture and the per-mesh baseline match
the candidate's nonempty pixels exactly. Warm calls preserve IDs; first/last
scene leases, repeated release, injected compiler failure, retry and unload
restore the retained baseline with no GL errors. Load timings are retained in
the log but do not establish a general load-time win.

The final A/B opt-out is a subsequent source addition; the root integrated build
and final rerun should record their own MVID. Windows client/server integration
builds and map fixtures belong to the root acceptance ledger. macOS, Linux and
Android tests are **NOT_RUN** at the user's Windows-only direction. In particular,
GLES preserves its existing per-mesh behavior; geometry sharing there has
**NO_ROLLOUT** until its compile-state inheritance is repaired and qualified.

The subsequent integrated build and native rerun passed in
`build-shadow-receivers-final.txt` / `shadow-receivers-native-final.txt`: 14,822
combined preparation/receiver checks, MVID
`220155ea-3f7c-4165-8228-82715dbdf68e`. The geometry A/B opt-out and current desktop
source are included. CPU allocation and native fixture geometry-byte invariants
remain unchanged; the extra receiver tests are described in `shadow-receivers.md`.

Next native qualification: compare the current immutable build with and without
the geometry-reuse opt-out on the same portal-heavy map/camera at 120/144/240 Hz,
then replay/Studio/multiview transitions; retain frame tails, visibility images,
compiled/resident counters and source/binary identity. Do not interpret the
synthetic byte reduction as a measured production-map FPS improvement.
