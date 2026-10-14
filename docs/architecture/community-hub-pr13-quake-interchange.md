# Community Hub PR-13: Editable Quake .map Interchange

**Status:** Draft implementation. This is a strict subset, not full NetRadiant
brushDef/patch/texture parity. PR-12 #549 is the stacked predecessor; PR-10
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
3. Import uses the same handedness-preserving (x,z,-y) coordinates, and the
   same +90 degree spawn heading conversion as existing Q3Gameplay.
   Large arenas select the lowest supported fixed-point ScaleFactor that
   covers imported geometry, spawns and pickups. Native geometry compiles
   are checked for convex/collision and UV budget compliance.
4. Export writes original authoring-readable classic Quake .map planes,
   permanent PRIME_ID comments, stable prime_id entity keys and a
   project-prime.json sidecar. Both source and sidecar are written only after
   source validity and sidecar MapId conflicts have been checked.
5. A supported Prime pickup is exported using Q3Gameplay's shared canonical
   classname mapping plus explicit prime_spawn_interval and prime_has_base.
   The source importer preserves those native gameplay values exactly.
6. Save the imported project as JSON for normal Prime build, package,
   playtest and Community publication. Nothing in this PR changes the
   self-contained .ppmap gameplay output, historic SHA-256 identity,
   multiplayer Ready barrier or the existing v2 Community APIs.

## Unsupported data is a hard failure

- Quake brushDef, brushDef3, patchDef2/3 Bezier patches or detail shader stages
- Classic face UV shifts, texture rotation/scale, nonzero surface flags,
  mixed per-face shaders, displacement and shader-provided material effects
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
- All scene-space floats must be finite. Invalid sidecar unit scales,
  duplicate GUIDs across brushes/spawns/pickups, duplicate entity keys,
  changed source during reading, malformed geometry and untrusted tool
  outputs fail loudly. Never execute creator-provided programs.
- The source sidecar SHA mismatch is presented as a nonfatal warning because
  authors routinely edit .map source in NetRadiant. MapId remains pinned and
  exported object GUIDs remain stable where explicitly authored.
- Existing JSON projects, old .ppmap archives, revisions and replay
  retrieval are left unchanged. Roll back by removing the isolated
  QuakeMapInterchange/QuakeConvexHull, Map Studio menu additions and check tool.

## Acceptance and evidence

Focused suite: dotnet run --project tools/community-format-check -c Release.
It covers axis boxes, convex wedge hull topology and a second round trip;
stable GUID/MapId/spawns/pickups; source fingerprint and hostile metadata
controls; inverted face and patch/UV/advanced geometry refusal; and a
large geometry fixed-point scale case. The suite is in build.yml on this
branch, but a passing hosted job must be confirmed at the exact head SHA.

Before marking PR-13 complete, still require:
- Actual Windows/Linux/macOS .NET builds and focused checks on a clean head
- Real NetRadiant-opened/saved Quake .map fixtures, including angled geometry
- Full normal Prime JSON build -> validated .ppmap -> local game smoke,
  correct collision, textures, spawns and item respawns
- 1v1, 8-player FFA, exact-map join/host rotation, historic replay checks
- Explicit support plan for mixed per-face materials/UVs and Bezier patches,
  or separate versioned editor-source support with validated round-trip
  before enabling NetRadiant as the default source editor
- Editor package/license/asset provenance and supported-platform checks
