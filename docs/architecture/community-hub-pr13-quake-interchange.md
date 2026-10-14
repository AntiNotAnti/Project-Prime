# Community Hub PR-13: Editable Quake .map Interchange

**Status:** Draft implementation. This is a strict subset, not full NetRadiant
brushDef3/patchDef3/full texture parity. PR-12 #549 is the stacked predecessor; PR-10
and PR-11 remain upstream dependencies. Do not switch the default Map Studio
source format until the real editor/gameplay gates pass.

## Supported authoring path

1. Import a classic three-plane-point Quake 3 .map with one worldspawn,
   worldspawn closed solid brushes, info_player_deathmatch starts and approved
   canonical pickup classnames. Optional project-prime.json is read as an
   explicit pinned MapId, unit scale and source fingerprint.
2. Six truly axial planes forming one rectangular volume become a native
   editable MapBox. Closed finite convex hulls of 4–32 planes, including
   angled ramps, wedge cover and beveled solids, become editable
   MapConvexBrush vertices/faces, never a linked BSP render-only mesh.
3. Classic Quake brush-face shader names and texture shift/rotation/scale
   now have an opt-in per-face source record on native MapBox and MapConvexBrush.
   Quake's six axial projection bases convert source coordinates into native
   texel UVs at geometry-compile time, so brush edits refresh the projection.
   Source face material indices participate in deterministic material remapping.
   Nonzero Quake surface/content flags are still rejected; proprietary game
   textures and executable shader programs are not imported.
4. Import Q3 patchDef2 quadratic control grids as native MapBezierPatch
   geometry. The source master contains an odd 3-17 by 3-17 column-major
   [x,y,z,s,t] grid, with 1-4 tessellation steps per biquadratic cell.
   The normal render and collision paths reuse the same bounded triangles.
   PRIME_PATCH_SOLID and PRIME_PATCH_TESSELLATION comments preserve the
   collision and detail choices on .map export/reimport. Without explicit
   collision metadata, the imported patch is tentatively solid with a warning
   requiring shader/collision review before publication.
5. Import uses the same handedness-preserving (x,z,-y) coordinates, and the
   same +90 degree spawn heading conversion as existing Q3Gameplay.
   Large arenas select the lowest supported fixed-point ScaleFactor that
   covers imported geometry, spawns and pickups. Native geometry compiles
   are checked for convex/collision and UV budget compliance.
6. Export writes original authoring-readable classic Quake .map planes,
   permanent PRIME_ID comments, stable prime_id entity keys and a
   project-prime.json sidecar. Both source and sidecar are written only after
   source validity and sidecar MapId conflicts have been checked.
7. A supported Prime pickup is exported using Q3Gameplay's shared canonical
   classname mapping plus explicit prime_spawn_interval and prime_has_base.
   The source importer preserves those native gameplay values exactly.
8. Save the imported project as JSON for normal Prime build, package,
   playtest and Community publication. The package builder now lowers new
   patch controls and Quake-specific face projections to the existing
   explicit-UV MapMesh schema only for distributed .ppmap packages, retaining
   the editable JSON/.map master outside the archive. Historical .ppmap bytes,
   existing maps without these new types, SHA identities, Ready, and v2 APIs
   remain unchanged.

## Unsupported data is a hard failure

- Quake brushDef, brushDef3, patchDef3, patchDef2 grids outside the bounded
  subset, unknown patch header metadata, custom shader stages and explicit
  subdivisions not expressible by our native patch grid
- Nonzero Quake surface/content flags, displacements, external material
  shader programs or shader-derived behavior; only classic numeric UV
  shifts/rotation/scales and per-face shader names are supported.
  Imported shader names are authoring identities, not evidence that licensed
  textures or equivalent runtime shading have been installed
- Nonconvex/disconnected volume, open, duplicate or inverted planes,
  degenerate faces, malformed or oversized lines/archives
- Brush-model/scripted/linked gameplay entities, team CTF spawns,
  unknown spawn/pickup metadata or noncanonical Quake pickup aliases
- Native imported maps/rooms, source-attached custom collision, platform
  movers, navigation links, jump pads, triggers, advanced textures and
  mesh geometry not expressible by the restricted native .map roundtrip

These constraints preserve authoring and simulation fidelity rather than
introducing guessed collision, texture alignment, target links or gameplay.
For unsupported constructs, the separate PR-12 q3map2 -> BSP -> Q3ImportService
route remains available as a *read-only import fallback*; it is not a
substitute for editable geometry/source parity.

## Identity, security and rollback

- .map input <=32 MiB, sidecar <=64 KiB, lines <=4,096 characters,
  200,000 lines total, 4–32 source planes/brush, 256 max convex corners.
- Bezier patches: odd dimensions 3–17, up to 289 control points, maximum
  2,048 tessellated triangles per patch, 32 patches and 8,192 triangles in
  one project. NaN/Inf values, out-of-range UVs, malformed nesting and
  duplicate GUIDs are rejected before publishing.
- All scene-space floats must be finite. Invalid sidecar unit scales,
  duplicate GUIDs across brushes/spawns/pickups, duplicate entity keys,
  changed source during reading, malformed geometry and untrusted tool
  outputs fail loudly. Never execute creator-provided programs.
- The source sidecar SHA mismatch is presented as a nonfatal warning because
  authors routinely edit .map source in NetRadiant. MapId remains pinned and
  exported object GUIDs remain stable where explicitly authored.
- Existing JSON projects, old .ppmap archives, revisions and replay
  retrieval are left unchanged. Roll back by removing the isolated
  QuakeMapInterchange/QuakeConvexHull/QuakePatchDef2, opt-in MapBezierPatch
  geometry and the package-compatibility lowering path. Historical archives
  and existing JSON map projects need no migration.

## Acceptance and evidence

Focused suite: dotnet run --project tools/community-format-check -c Release.
It covers axis boxes, convex wedge hull topology and a second round trip;
stable GUID/MapId/spawns/pickups; source fingerprint and hostile metadata
controls; inverted planes, bounded patchDef2 construction/roundtrip,
source UV control points and non-solid/solid collision parity, portable mesh
package conversion and patchDef3/advanced-geometry refusal; and a large
geometry fixed-point scale case. The suite is in build.yml on this
branch, but a passing hosted job must be confirmed at the exact head SHA.

Before marking PR-13 complete, still require:
- Actual Windows/Linux/macOS .NET builds and focused checks on a clean head
- Real NetRadiant-opened/saved Quake .map fixtures, including angled geometry
- Full normal Prime JSON build -> validated .ppmap -> local game smoke,
  correct collision, textures, spawns and item respawns
- 1v1, 8-player FFA, exact-map join/host rotation, historic replay checks
- Direct brush primitive, patchDef3, unsupported larger patches and full Quake
  shader/texture projection parity need separate lossless implementation
  or an explicit feature report before NetRadiant is the default source editor
- Editor package/license/asset provenance and supported-platform checks

## Texture reference and scope

Classic Quake numeric face projection follows the original Q3Map
`TextureAxisFromPlane` / `QuakeTextureVecs` six-base-axis mapping (Q3 Z-up
world-to-texture mapping, then Q3-to-Prime coordinate conversion). See the
upstream Quake 3 `q3map/map.c` implementation; this is not a guess based
on editor screenshots. The extra authoring metadata is optional/null for
legacy map projects, preserving existing geometry and serializer behavior.

Project Prime still needs permission-cleared source textures and separate
in-game visual QA to establish actual material parity. Identically named
`MapMaterial` slots alone do not guarantee that a Quake shader is available
or identical in the Prime renderer. New material metadata is not a multiplayer
protocol revision, but mixed-client visual compatibility should be versioned
and explicitly checked before publishing these maps to older builds.

## Package/readiness compatibility

Newly authored MapBezierPatch control grids and opt-in classic Quake face
metadata are converted at the .ppmap serialization boundary into the existing
MapMesh + explicit texel UV + face material representation. This transformation
is verified for identical face positions/materials/UVs and preserves shared
mesh vertex topology. It does not modify the Studio authoring source or
historical package bytes, and is a no-op on old maps.

The source Quake .map s/t values are stored exactly. Native rendered texel UVs
currently use the Project Prime material's configured TexScale; this is not
proof of source texture dimensions or shader stage equivalence. Full visual
matching still needs licensed texture input and in-game screenshots. The
source shader's surface contents are not automatically inferred for
patchDef2; the importer warns about implicit collision and authors must
validate/declare a solid or render-only patch before publishing.

The standalone acceptance suite must verify all newly introduced behavior
at the exact branch SHA. A headless conversion and clean .NET build cannot
replace a real NetRadiant open/save plus offline/1v1/FFA/game-device check.
