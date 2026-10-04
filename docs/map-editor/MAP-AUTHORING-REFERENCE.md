# Project Prime / Prime Hunters Online: map editor and AI map-authoring reference

Reference date: September 30, 2026. Based on the repository at commit `1f1a4963` and the current source files listed at the end. This is a practical specification for an agent creating or editing maps, not a claim that every map made from it has been playtested. Source code and actual compiler diagnostics take precedence if the repository changes.

## 1. Start here: the map-authoring contract

A map is an editable JSON recipe plus its referenced assets. Forge / Map Studio edits that recipe. The compiler converts it into the game's normal room geometry, collision, entities, animation and navigation files. A `.ppmap` is the portable package handed to players; generated runtime `.bin` files are not the editable source.

For a new agent-authored map:

1. Create a project directory with one map JSON and project-relative assets.
2. Use `formatVersion: 2`, a new persistent UUID in `mapId`, and a unique runtime `name`.
3. Build the initial layout with `geometry` boxes, wedges and prisms. Use meshes only where their extra control is needed.
4. Add materials, safe spawns, pickups and any jump pads. Explicitly declare supported modes.
5. Validate, inspect navigation, build, then playtest. Fix errors and review warnings before packaging.
6. Deliver the source folder, a `.ppmap` when build prerequisites are available, and a short validation/playtest report.

Do not invent schema fields for doors, capture points, scripted triggers or teleporters. The native entity lists exposed by this format are `spawns`, `items`, and `jumpPads`. A `navigationLinks` entry is a bot-routing hint, not a physical game mechanic. Preserved native-room entities are a separate route to existing objective mechanics.

The recommended starting point is direct JSON generation or a built-in editor template. Do not hand-author the runtime binary format or ZIP manifest hashes. Let the compiler and package builder do that work.

## 2. Files, identity and lifecycle

| Artifact | Purpose |
| --- | --- |
| Project `.json` | Editable map definition. Readable, case-insensitive property names; serializer writes camelCase. Comments and trailing commas are accepted on load, but strict JSON is best for generators. |
| Project asset folders | Custom textures, audio, imported models and source-level dependencies. Keep the project portable. |
| `.ppmap` | ZIP package with validated identity, recipe and required runtime assets. V2 contains `manifest.json` and `project.json`. |
| Generated runtime files | Model, animation, collision, entity and node output, created locally through the compiler. |
| Build cache | Fingerprinted local runtime output; not an authoring deliverable. |
| Recovery files | Autosaved editor state, separate from the saved source. |

`mapId` identifies the map across versions. Keep it when revising that same map; generate a new UUID for an independent map. Keep object IDs when modifying existing objects and generate new ones when creating or duplicating objects. Material references in geometry are **zero-based list indices**, not material UUIDs.

`name` is the runtime key. Use uppercase by convention. It must start with an ASCII letter or digit, contain only ASCII letters, digits, spaces, underscores or hyphens, be 1–40 characters, and have no leading/trailing whitespace. Device names such as `CON`, `NUL`, `COM1` and `LPT1` are rejected. Use a new name rather than a built-in room key. `inGameName` is the human-readable title.

V1 recipes remain readable; a missing `formatVersion` means 1. Discovery does not silently upgrade files. Editor upgrade assigns v2 identity, and Save persists it. Do not regenerate identity on every agent run.

Save writes JSON atomically. Save As from a package materializes referenced assets. **Export project folder** produces a portable editable folder and includes model-source dependencies; this is the preferred handoff when the next author needs reimportable sources. Runtime packages strip private model-source provenance and distribute normalized meshes and baked textures.

## 3. Authoring routes

| Route | Best use | Important behavior |
| --- | --- | --- |
| Native primitives / meshes | New arenas and precise agent-generated layouts | Fully editable. Boxes, wedges, prisms, convex brushes and general meshes. |
| OBJ / glTF / GLB model import | Externally modeled architecture or props | Produces normalized editable geometry with material/texture assets and reimport provenance. |
| Q3 BSP / PK3 import | Converting an existing Quake 3 map | Imported BSP architecture remains read-only; authored geometry and entities can be added over it. |
| Native-room remix | Reworking a built-in cartridge room | Stores a room reference and policies; compilation reads the user's extracted game assets. |

Q3 `import` and `nativeRoom` are mutually exclusive. Model-imported meshes can coexist with native geometry. For a Q3 hybrid, keep authored material indices local to `materials`; the compiler offsets them after imported materials. Do not manually add that runtime offset.

### Templates

File → New currently offers Blank Arena, Simple Box Arena, Basic FFA, 1v1 Duel, Team Symmetric, Vertical Arena, Jump Pad Playground and Large Outdoor, plus source workflows Import Review Workspace and Native Remix Starter.

Templates are starting layouts, not acceptance certificates. Validate generated content before use. In particular, the Large Outdoor template's recommendation mentions 6–12 and creates 12 spawn locations, while current capability validation limits simultaneous players to **1–8**. Spawn count does not equal maximum player count. The current Team Symmetric implementation also places some starts within its base-box footprint; verify and relocate obstructed starts rather than assuming a template is ready to publish.

## 4. Coordinates, scale and geometry conventions

- World coordinates are `[x, y, z]`, with **Y up**.
- One world unit equals 4096 in the engine's fixed-point representation.
- Spawn yaw uses degrees: 0 faces +Z, 90 faces +X, 180 faces −Z, 270 faces −X.
- Primitive local coordinates are centered near the origin, with a unit box spanning −0.5 to +0.5 on each axis.
- A box's `transform.scale` therefore gives its full width, height and depth; `transform.position` is its center.
- Transformation order is local componentwise scale, quaternion rotation, then translation.
- `rotation` is `[x, y, z, w]`, a normalized quaternion, **not Euler angles**. Identity is `[0,0,0,1]`.
- Every object scale component must be positive. Negative scale is not a supported mirroring shortcut. Zero object scale is invalid; vertex flattening is a different operation.

For a Y rotation by angle θ, use `[0, sin(θ/2), 0, cos(θ/2)]`, computing trigonometry in radians. A 90-degree Y rotation is approximately `[0,0.70710678,0,0.70710678]`.

A floor centered at `[0,-0.5,0]` with scale `[32,1,32]` has its walking surface at Y=0. Place an initial spawn at Y=0.1 above that floor, then verify it in gameplay. A platform centered at Y=4 with height 1 has its top at Y=4.5.

A wedge rises from its low end at local −Z to its high end at local +Z; rotate around Y to orient the ramp. A prism runs along local Y, with its polygon in XZ and a nominal radius of 0.5 before scaling.

### Runtime range and practical dimensions

`scaleFactor` is a power-of-two packing scale, not an instruction to resize the authored world. With `s = scaleFactor`, rendered coordinates must satisfy `−8 × 2^s ≤ coordinate < 8 × 2^s`. Default 4 gives `[−128,128)`. Increasing it allows larger extents but reduces vertex precision; the approximate world increment is `2^s / 4096`. Validate transformed vertices, not just object centers.

Keep geometry near the origin and use the smallest packing scale that fits. There are additional fixed-point position limits, collision-grid budgets and UV limits; increasing `scaleFactor` does not bypass those.

Historical movement calculations in the importer estimate a normal Samus jump at about 2.39 units high and 7.7 units across. Treat these as blockout estimates, not guaranteed route limits for every hunter, movement configuration or takeoff. Give routes margin and test the intended hunters and forms. The validator's spawn-headroom probe uses 1.8 units; clearance must still be checked along whole routes.

## 5. Top-level JSON reference

Defaults below are source-class defaults unless noted. Omitted collections normally default to empty lists; do not write them as `null`.

| Field | Type / default | Meaning |
| --- | --- | --- |
| `formatVersion` | integer, 1 | Use 2 for new maps. |
| `mapId` | UUID | Required nonempty identity for v2. |
| `name` | string, `CUSTOM` | Runtime key. |
| `inGameName` | optional string | Display title; maximum 256 characters. |
| `author`, `version`, `description` | optional strings | Maximum 256 / 64 / 8192 characters respectively. |
| `textureSource` | string, `MP3 PROVING GROUND` | Built-in room used for borrowed materials. |
| `scaleFactor` | integer, 4 | Allowed 0–16, subject to final coordinate constraints. |
| `killHeight` | number, −40 | Kill plane. Starts must be above it. |
| `farClip` | positive number, 350 | Rendering distance. |
| `fogEnabled` | boolean, true | Fog toggle. |
| `fogColor` | RGB triple, `[8,10,16]` | Channels are **0–31**, not 0–255. |
| `fogSlope`, `fogOffset` | integers, 5 / 65180 | Allowed 0–10 / 0–65535. |
| `light1Color`, `light2Color` | RGB triples | Defaults `[31,28,24]` and `[10,11,16]`; channels 0–31. |
| `light1Vector`, `light2Vector` | XYZ triples | Defaults `[0.3,-1,0.2]` and `[-0.3,1,-0.2]`; finite and nonzero. |
| `battleTimeLimit` | unsigned integer, 12600 | Default represents 7 minutes at 30 ticks/second. |
| `pointLimit` | signed short, 7 | Room scoring default. Match settings can govern actual play. |
| `preview` | `{position, target}` | Camera framing for the map image. Both values are XYZ triples. |
| `materials` | material array | Authored material palette. |
| `geometry`, `brushes` | arrays | Modern editable geometry and legacy axis-aligned boxes. |
| `spawns`, `items`, `jumpPads` | arrays | Native authored gameplay entities. |
| `capabilities` | object | Supported mode names and player range. |
| `navigationLinks` | array | Explicit traversal links for bot navigation. |
| `assets`, `audio` | array / object | Declared portable assets and music settings. |
| `modelSources` | array | Importer-managed model provenance and reimport mappings. |
| `import`, `nativeRoom`, `collision` | optional objects | External source policies and collision override. |
| `partitioning` | optional object | Runtime spatial partition controls. |
| `hardpointOrder` | integer array | Existing objective entity IDs in rotation order. Does not create objectives. |

Unknown JSON fields are not a supported extension mechanism. A loader accepting a file does not prove that an invented field affects gameplay.

## 6. Geometry schema

### Common modern geometry fields

All entries in `geometry` use a `kind` discriminator. Put it first in generated JSON for serializer compatibility.

| Field | Default | Meaning |
| --- | --- | --- |
| `kind` | required | `box`, `wedge`, `prism`, `convex`, or `mesh`. |
| `id`, `label` | generated UUID / `Geometry` | Stable identity and descriptive editor name. Explicitly generate IDs in agent output. |
| `transform` | position zero, identity rotation, scale one | World placement. |
| `material` | 0 | Index into authored `materials`. |
| `solid` | true | Adds collision. |
| `damaging` | false | Hazardous collision flag; test contact behavior in game. |
| `terrain` | `Metal` | Collision surface classification. |
| `shade` | 1 | Brightness, allowed 0–1; compiler also applies normal-dependent shading. |
| `uv` | scale `[1,1]`, offset `[0,0]`, rotation 0 | Default face UV projection transform. |
| `faceUv` | empty dictionary | Overrides keyed by face index, e.g. `"2"`. |
| `hidden`, `locked` | false | Editor visibility and editing state. |
| `layer` | `Architecture` | Editor organization. |

**Hidden geometry still compiles.** `hidden`, `locked` and `layer` are editor state, not gameplay activation conditions. Remove geometry to exclude it; use `solid:false` for visible decoration without collision. For invisible collision, use a solid mesh with `collisionOnly:true`.

Useful terrain names include `Metal`, `OrangeHolo`, `GreenHolo`, `BlueHolo`, `Ice`, `Snow`, `Sand`, `Rock`, `Lava`, `Acid`, and `Gorea`. Choose hazard behavior explicitly; do not assume a texture or terrain name alone creates the desired damage behavior.

### Shape-specific data

- **box:** no additional fields. Dimensions come from transform scale.
- **wedge:** no additional fields. Unit ramp with rise toward local +Z.
- **prism:** `sides`, integer 3–32, default 6.
- **convex:** `vertices` as local XYZ triples and `faces` as zero-based vertex-index arrays. Requires 4–256 vertices and 4–256 faces, each with 3–32 indices. Must be closed, planar-faced, convex and have nonzero volume; each undirected edge must occur exactly twice.
- **mesh:** `vertices` and `faces`; supports open or concave objects. Requires 3–65535 vertices and 1–65535 faces, each with 3–32 indices. Use planar, non-self-intersecting faces; triangles are the simplest interchange representation. Mesh winding is authoritative, so orient outward surfaces correctly.

Meshes also support:

| Field | Meaning |
| --- | --- |
| `faceMaterials` | Per-face material indices; omitted entries fall back to the object's material. |
| `faceTexcoords` | Per-face arrays containing one `[u,v]` per face corner, in **texels**. Null face entries use projected UVs. |
| `collisionOnly` | Omit this mesh from the runtime visual model. Keep `solid:true` to retain collision. |
| `slipperiness` | Integer 0–3. |
| `reflectBeams`, `ignorePlayers`, `ignoreBeams`, `ignoreScan` | Collision behavior flags, default false. |

Closed primitives and convex brushes can have their winding corrected outward by the compiler. General meshes cannot rely on that correction. A single plane is not a dependable two-sided wall. For collision architecture, use thickness or intentionally authored closed meshes. Open/non-manifold mesh warnings are not proof of safe collision.

### Legacy `brushes`

A legacy brush uses `id`, `label`, `min`, `max`, `material`, `shade`, `solid`, `damaging`, and optional `terrain`. Coordinates are world-space corners, not center/size. Write `min < max` on every axis and avoid zero thickness. New maps should generally use modern `geometry`, which supports transforms and editor modeling workflows.

## 7. Materials, UVs and assets

The complete local material inventory is included in [Section 19](#19-complete-locally-available-map-texture-catalog), with a matching [machine-readable catalog](texture-catalog.json).

A material is `{id, name, sourceMaterial, texScale, texture?, albedo?, normal?, specularRoughness?, emissive?, alpha?, twoSided?, animation?}`. `sourceMaterial` defaults to 0; `texScale` defaults to 16 texels per world unit. `texture` is the native FPTX fallback. The four HD fields are optional PNG/JPEG channels that travel with the project/package and are resolved at the client-selected 1K/2K/4K/8K quality tier. `alpha` optionally overrides the native 0–31 opacity value, while `twoSided:true` disables face culling for that material.

### Borrowed game textures

Set `textureSource` to a valid built-in room key and `sourceMaterial` to the desired material slot in that room. Texture and palette are copied together. Use:

```sh
ProjectPrime -mapmaterials "MP3 PROVING GROUND"
```

Do not assume indices from one source room apply to another. Borrowed textures require the user's extracted game files at runtime build time and are not copied into distributable source packages as cartridge assets.

### Custom textures

Use the editor's texture import path to keep two layers: a **single-texture `.tex` fallback** plus the original portable HD image. Declare both in `assets`; reference the fallback with `material.texture` and the HD image with `material.albedo` (plus optional `normal`, `specularRoughness`, and `emissive`):

```json
{
  "assets": [
    {"path":"textures/wall.tex","kind":"texture","name":"Wall fallback"},
    {"path":"textures/wall.png","kind":"texture","name":"Wall HD"}
  ],
  "materials": [{
    "id":"469dc077-a65b-40c0-8045-a7a9b6eb8c4b",
    "name":"Wall",
    "sourceMaterial":0,
    "texScale":16,
    "texture":"textures/wall.tex",
    "albedo":"textures/wall.png"
  }]
}
```

This is a fragment, not a complete map. Every referenced file must exist and be declared as a texture asset. Assigning PNG/JPEG directly to `material.texture` is still invalid: that field is the native baked fallback. HD channels accept PNG/JPEG/TGA up to 8192×8192. Q3 imports additionally maintain `import.modernTextures`, mapping source shader indices to portable PNG/JPEG/TGA art while `import.textures` remains the FPTX fallback.

`assets` entries have `path`, `kind`, optional `name` and optional `sourcePath`. Runtime asset paths must be safe project-relative paths, unique without case collisions. `sourcePath` is authoring provenance, not a substitute for a missing runtime asset. Use the editor/exporter to materialize files.

Supported asset kinds/extensions are texture (`.png`, `.jpg`, `.jpeg`, `.tga`, `.tex`), audio (`.wav`, `.ogg`, `.mp3`) and preview (`.png`, at most 4096×4096). Do not reference files outside the portable project using `../` or absolute runtime paths.

### Animated materials

`animation` is optional. Project Prime compiles supported effects into the native MPH animation formats, so playback uses the same model animation clock on legacy GL and modern Vulkan/DX12/Metal paths.

| Field | Default | Meaning |
| --- | --- | --- |
| `uvScroll` | `[0,0]` | U/V texture-tile velocity per second. Each component must be finite and within −4..4. |
| `uvRotationDegreesPerSecond` | 0 | Clockwise texture rotation, limited to −1440..1440 degrees/second. |
| `uvScale` | `[1,1]` | Base native texture-coordinate scale, positive and at most 8. |
| `uvScalePulse` | `[0,0]` | Per-axis sinusoidal amplitude around `uvScale`; one pulse occurs per material loop. |
| `flipbookFrames` | empty | Up to 64 additional declared single-texture `.tex` assets. The material's normal texture is frame zero. |
| `flipbookHoldFrames` | 3 | Number of native 30 Hz frames each flipbook image remains visible. |
| `loopFrames` | 3000 | Material cycle length in native 30 Hz frames; allowed 30–6000. |
| `phaseFrames` | 0 | Starting offset within the material loop; must be 0 through `loopFrames - 1`. |

Scrolling must land on whole texture tiles at the loop boundary. Rotation must land on a whole turn. A flipbook's complete image cycle, `(1 + flipbookFrames.count) * flipbookHoldFrames`, must divide evenly into `loopFrames`. Scale pulse is inherently periodic, but its minimum must remain above 0.01 and its maximum at or below 8. Different materials may use different loop lengths when their least-common native group cycle remains at or below 6000 frames.

Flipbook frames are native baked texture assets, not HD PNG/JPEG channel swaps. Import or bake each frame as a single-texture `.tex`, declare it in `assets` with `kind:"texture"`, then add its project-relative path to `flipbookFrames`. Pure flipbook animation does not alter the material's original texgen mode. UV animation and flipbooks may be combined.

Example:

```json
{
  "id": "469dc077-a65b-40c0-8045-a7a9b6eb8c4b",
  "name": "WaterPortal",
  "sourceMaterial": 0,
  "texture": "textures/portal_0.tex",
  "alpha": 24,
  "twoSided": true,
  "animation": {
    "uvScroll": [0.03, -0.06],
    "uvRotationDegreesPerSecond": 180,
    "uvScale": [1, 1],
    "uvScalePulse": [0.08, 0.08],
    "flipbookFrames": ["textures/portal_1.tex", "textures/portal_2.tex"],
    "flipbookHoldFrames": 5,
    "loopFrames": 3000,
    "phaseFrames": 0
  }
}
```

Map Studio exposes these controls in the material inspector and provides moving previews. The compiler enforces separate native lookup-table budgets for scale, rotation, translation and flipbooks. A default/no-op `animation` object emits no runtime animation and preserves the legacy static 24-byte animation payload.

HD emissive images continue to use the existing `emissive` channel. Animated emissive **intensity** is not part of this native material-animation contract because it requires a renderer/shader uniform rather than an MPH material-animation field.

### UV behavior

Projected UVs use material `texScale` multiplied by the face/object UV scale, then rotation in degrees and offset in texels. Explicit mesh `faceTexcoords` takes precedence over projected UV settings. Typical normalized 0–1 UVs from external models must be converted by the importer to the representation expected here; do not paste them blindly into texel coordinates.

The geometry compiler rejects UV coordinates whose absolute value reaches 2048. Large surfaces at high texel density can fail even when their vertices fit. Lower `texScale`, adjust UVs, or split the surface. Maintain `faceUv`, `faceMaterials`, and `faceTexcoords` correspondence when reordering, deleting or triangulating faces.

The editor provides face painting, connected painting, material sampling, material usage selection/isolation/replacement, unused-material cleanup, and UV transform/reset/fit/copy/paste/checker tools. A continuous paint stroke is one undo operation. Inspect texture direction and density in the viewport and gameplay.

## 8. Gameplay entities and supported modes

Every authored entity has `id`, optional `label`, and `position:[x,y,z]`. Assign unique IDs across objects, entities and materials.

### Spawns

`team` defaults to −1 (neutral); valid values are −1 through 3. `yaw` defaults to 0. Use teams 0 and 1 for ordinary two-team maps.

At least one authored spawn is required unless Q3 imported spawns are enabled. Multiple well-spaced spawn choices are recommended even for duels. Starts within two units of one another produce warnings. Starts inside solids or at/below the kill plane are invalid. Check headroom, floor support, nearby hazards and immediate sightlines to other starts. The validator is not a complete player-volume or route-clearance solver.

### Pickups

An item has `type`, `hasBase` (default true), and `spawnInterval` (default 300). The interval is in 30 Hz simulation ticks: 300 corresponds to 10 seconds. Zero produces a warning.

Supported multiplayer item names are:

`HealthSmall`, `HealthMedium`, `HealthBig`, `UASmall`, `UABig`, `MissileSmall`, `MissileBig`, `DoubleDamage`, `Cloak`, `Deathalt`, `VoltDriver`, `Battlehammer`, `Imperialist`, `Judicator`, `Magmaul`, `ShockCoil`, `OmegaCannon`, `AffinityWeapon`.

Use exact names. A multiplayer map cannot freely instantiate every single-player item enum. Put pickups above supported surfaces and make powerful pickups contestable. Fairness and resource timing are design judgments that need playtesting.

### Jump pads

Provide **exactly one** trajectory form:

- `target:[x,y,z]`: preferred for agent authoring; compiler solves launch direction/speed.
- `vector:[x,y,z]` plus positive `speed`: nonzero direction and explicit launch speed in engine world-units per simulation tick.

Other fields: `size` defaults to `[1.6,1.8,1.6]`, `modelId` to 0, `cooldownTime` to 20 and `controlLockTime` to 30. The size is the trigger dimensions; it must be positive. The pad compiler supplies the runtime trigger setup for players and bots.

Position the trigger where a walking or falling player can enter it. Target a reachable landing above the platform surface, with overhead clearance along the full arc. Review the trajectory overlay and test biped/alternate-form approaches. A mathematically solvable arc may still cross a wall or kill plane.

### Capabilities and objectives

```json
"capabilities": {
  "supportedModes": ["Battle", "Survival"],
  "minPlayers": 1,
  "maxPlayers": 8
}
```

For ordinary authored maps, the current validator permits Battle, BattleTeams, Survival, SurvivalTeams, PrimeHunter, OneInTheChamber, GunGame, KillConfirmed and KillConfirmedTeams without preserved objective entities. InstaGib is accepted as a Battle alias. Team modes require starts for both team 0 and team 1.

Other modes require objective entities supplied through a native-room source with preservation enabled. Merely listing a mode does not create its flags, nodes or other objectives, and satisfying that policy check does not establish that the preserved room has the right layout for every objective mode. Inspect the source room and playtest each advertised mode.

`hardpointOrder` contains unique existing runtime objective IDs, each 0–32767, at most 512 entries. These integer IDs are not authoring UUIDs.

## 9. Navigation and bot support

Enable Navigation in the editor or run `-mapinspect` to build and inspect navigation. Look for disconnected regions, inaccessible pickups and routes that only appear connected visually.

An explicit link contains `id`, `kind`, `from`, `to`, `bidirectional` (default false), plus optional `fromNodeKind` / `toNodeKind` semantic hints. In project JSON, `kind` is currently a **numeric enum**:

| Value | Name |
| --- | --- |
| 0 | Jump |
| 1 | Drop |
| 2 | JumpPad |
| 3 | Teleporter |
| 4 | Platform |
| 5 | Manual |

Node hints use `Auto`, `Navigation`, `Special`, `Aerial`, `Vantage`, `AltForm`, or `Hazard`. `Auto` keeps geometry-derived semantics: damaging/lava/acid floors become Hazard nodes, elevated ledges can become Vantage nodes, jump destinations infer Aerial, and teleporter/platform endpoints infer Special. Explicit hints are useful for morph-only passages and authored sniper positions. Hazard classification from collision cannot be downgraded by a link hint.

Example: `{"id":"726a3aeb-27f0-49a8-9034-846b63d3dcdd","kind":0,"from":[0,0.1,0],"to":[4,1.1,0],"bidirectional":false,"fromNodeKind":"Auto","toNodeKind":"Aerial"}`.

Links connect nearby walkable graph nodes. They do not create walkable surfaces, movement code, jump pads, teleporters or moving platforms. Build the physical route first. Use directionality honestly: generated and authored drops are now kept one-way unless the reverse movement is separately valid. The routing table also prefers safe paths over Hazard nodes when both exist. The source validator allows at most 4096 links.

## 10. How to use Forge / Map Studio

Open Forge from the launcher, or launch:

```sh
ProjectPrime -mapstudio
ProjectPrime -mapstudio -studioproject /absolute/path/to/map.json
```

The desktop editor has File, Edit, View, Build and Online menus, a searchable hierarchy, a central viewport, an inspector and a Problems panel. Resize panels with their dividers. Four views provides perspective, top, front and side panes; click a pane to activate it. Selection/history are shared. Pop out saves the current project and opens a separate editor process; the original editor pauses and reloads the saved project when the child exits.

### Core controls

| Action | Input |
| --- | --- |
| Select / add selection | Click / Shift-click |
| Select multiple in empty space | Drag a selection box |
| Orbit / pan / zoom | Right mouse drag / middle mouse drag / wheel |
| Object move / rotate / scale | G / R / T (S is also routed to Scale) |
| Frame selection | F in object mode |
| Object / face / edge / vertex mode | 1 / 2 / 3 / 4 |
| Copy / paste / duplicate | Ctrl or Cmd + C / V / D |
| Undo / redo | Ctrl or Cmd + Z / Shift+Z; Ctrl or Cmd + Y also redoes |
| Save | Ctrl or Cmd + S |
| Play | Ctrl or Cmd + Enter |
| Command palette | Ctrl or Cmd + Shift+P |
| Hide / isolate / reveal all geometry | H / Shift+H / Alt+H |
| Material paint / measure | P / M in object context |
| Cancel current interaction | Escape |

Focus and editing context matter. For example F fills in mesh sub-element mode, and M merges there. Object-mode S selects Scale; do not rely on an old generic WASD description without checking the current key routing. Use mouse camera controls when driving the UI programmatically.

Position, angle and scale snapping defaults are 0.25 units, 15 degrees and 0.25. Local/world axes and explicit axis/plane restrictions are available. Placement options include cursor, camera target and surface. Prefer numeric inspector values for precise layout work.

### Mesh modeling

Convert/select an editable mesh before using element tools. In face/edge/vertex mode, Ctrl toggles selection, Shift adds, double-click selects a face island or suitable quad edge loop, Ctrl/Cmd+A selects all, Alt+A clears, L selects linked, and Ctrl/Cmd+plus/minus grows/shrinks.

G/R/S starts a sub-element transform; X/Y/Z constrains an axis, Shift+axis constrains the complementary plane, numeric entry sets the amount, Enter commits, and Escape cancels. `S`, `Z`, `0` flattens selected mesh elements vertically; it does not set the object's transform scale to zero.

Tools include extrude (E), inset (I), bevel (B), merge (M), delete (X/Delete), fill (F), split/dissolve/collapse/slide, connect/rip, flatten, duplicate/separate/join, triangulate, winding repair and cleanup. Pivots include median, active, cursor and world. Brush CSG accepts authored boxes/wedges/prisms/convex brushes and produces editable meshes. It is not a general Boolean engine for arbitrary imported triangle meshes.

Current limits: regular quad topology is needed for edge loops; edge bevel expects suitable manifold endpoints and supports 1–8 segments; inset is a center-relative ratio rather than constant-width offset; mesh diagnostics do not perform exhaustive global self-intersection detection. Inspect Problems after modeling and complex CSG.

### Saves and background jobs

Edits have undo/redo history bounded to 500 commands and approximately 256 MiB. Drags commit as one operation. Autosave occurs after about three seconds of inactivity under the map directory's `.autosave`; it does not overwrite source. Recovery can be restored, discarded or inspected. Explicitly save a finished map rather than relying on recovery.

Source changes are detected with debouncing; reimport/reload remains an explicit review action. Do not edit an open project's JSON externally and assume the unsaved editor document will merge it. Coordinate a save/reload or reopen before continuing.

Build/validation/package jobs receive detached snapshots. The bounded shared build queue and content cache avoid redundant compilation. Viewport camera motion and selection do not themselves rebuild collision/navigation. A successful viewport display is not evidence that the latest unsaved map was built into gameplay.

## 11. Import workflows

### OBJ and static glTF/GLB

Use File → Import 3D Model. Inspect scale, up axis, winding, UV orientation, collision policy and projected dimensions before accepting. OBJ import handles groups, negative indices, concave faces, face-corner UVs, MTL diffuse colors and PNG/JPEG images. Static glTF 2.0 / GLB handles scene/node transforms, triangle primitives, UV0, base colors, image/buffer dependencies and embedded data.

Animated, skinned, morph and full PBR behavior is not reproduced; unsupported features produce warnings, and required unsupported extensions are rejected. Supply static level geometry. Collision choices include visual geometry, simplified coplanar geometry, bounds and companion OBJ. Bounds collision is appropriate for some props, not automatically for a room with doorways.

Model Sources / Reimport manages source relocation, previews, generated-object selection and detach. Compatible object transforms, face painting, UV overrides and material mappings can survive reimport; topology changes can invalidate correspondence. Review the reimport diff. Let the importer manage `modelSources` hashes, dependency lists and baseline mappings.

### Quake 3 BSP/PK3

Use File → Import Q3. Preflight lists maps in the archive, dimensions, faces/patches/brushes, starts, pickups and texture coverage. The selected archive, explicit dependencies and sibling PK3s are searched in priority order. Conversion stages work before committing the finished project. Missing art is diagnosed and can be shown as checker textures; historical documentation saying missing-image geometry is always dropped is outdated.

Key `import` settings:

| Field | Default / behavior |
| --- | --- |
| `source`, `mapName` | BSP/PK3 path and level within the archive. |
| `unitsPerUnit` | 28 Q3 units per world unit. Larger values make the imported world smaller. |
| `textures` | Baked texture-pack path. |
| `texScale` | 24. |
| `defaultMaterial`, `shaderMaterials` | Fallback index and shader-name/prefix mappings. |
| `keepSky` | false; visual sky never supplies collision. |
| `keepClip` | true; imported player-clip brushes remain. |
| `keepSpawns`, `keepItems` | true; source entities supplement authored ones. |
| `patchLevel` | 3; render tessellation, allowed 1–8. |
| `collisionPatchLevel` | −1 auto; 0 off; 1–8 explicit detail. |
| `autoHealCollision` | true. |
| `collisionHealTolerance` | 0.0625; allowed 0.0005–0.25. |
| `collisionHealExclusions` | Regions with `center`, `radius` (0.25–64), optional `note`. |
| `materialReplacements` | Source-slot → authored-slot or source-slot remapping. |

Coordinates convert from Q3 `(x,y,z)` to world `(x,z,−y)` divided by `unitsPerUnit`. The importer handles winding conversion. Do not convert authored spawns a second time.

For curated pickups, export existing positions using `-mapitems`, put them into `items`, and set `keepItems:false`; otherwise originals remain alongside edits. Similarly use `keepSpawns:false` when replacing source starts. Race-map clip brushes and starts often make poor deathmatch routes, so review `keepClip` and starts deliberately. Q3 trigger or moving-brush behavior is not automatically a supported native authored mechanic.

Auto-heal aligns imported collision with final precision and reports repairs. Inspect Collision, Collision heat and Collision repairs overlays, excluded regions, floor coverage and short-walk failures. An automatic patch-collision reduction warning requires inspection of curved walkable surfaces. KeepSky, FarClip and packing scale solve different problems.

### Native-room remix

`nativeRoom` contains `room`, `materialReplacements`, `useNativeArchitecture`, `preserveEntities`, `editableSpawns`, `editableItems`, `useNativeCollision` and `multiplayerLayerOnly`. The booleans default true. Clone built-in creates the project under a new identity/name and keeps the base-game room untouched.

Preserving native entities can retain objective mechanics. Turning off preservation changes that contract. The source cartridge assets remain a local dependency and are not bundled as redistributable content.

### Whole-map collision override

`collision:{"source":"collision/room.obj","zUp":false}` replaces generated collision rather than adding to it. The default is Y-up. Start with exported collision and edit intentionally; accidentally supplying a partial override can remove the floor for the rest of the map. Use model-import collision proxies for local prop collision rather than confusing them with this global override.

## 12. Environment, music and partitioning

Use fog and lighting for readability, with separate visual language for routes, teams, hazards and major pickups. Disable fog during initial blockout if it obscures debugging. Place `killHeight` below intended lower routes but where falling players reliably die.

Custom music is `audio:{"music":"audio/theme.ogg","volume":0.8,"loop":true}` with a corresponding declared audio asset. Alternatively use a valid engine `gameMusic` enum name. Do not set custom `music` and `gameMusic` simultaneously. Audio volume is 0–1. Existing game music is referenced locally rather than copied into the package.

Default runtime partitioning is automatic with portal culling disabled. Optional `partitioning` fields are `enabled:true`, `cellSize:64`, `faceThreshold:8192`, `maxVerticesPerDisplayList:60000`, `portalCulling:false`, and `portalVerticalMargin:6`.

Valid ranges: cell size 8–512, face threshold 256–1,000,000, display-list vertex cap 1024–65000, portal margin 0–64. Requested portal culling can fall back when the generated graph is disconnected or exceeds runtime part capacity. Read compiler diagnostics instead of assuming the request was applied. Leave defaults until profiling or render problems justify changing them.

## 13. Validation and build commands

Use the packaged `ProjectPrime` executable, or build the desktop project with the .NET 10 SDK and run its resulting assembly. From the repository root:

```sh
dotnet build src/MphRead
# Then use the produced ProjectPrime executable, or:
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -mapvalidate /absolute/path/to/map.json
```

The commands below use `ProjectPrime` as shorthand for that executable/assembly invocation:

```sh
# File paths avoid catalog ambiguity during iteration.
ProjectPrime -mapvalidate /absolute/path/to/map.json
ProjectPrime -mapinspect /absolute/path/to/map.json
ProjectPrime -mapbuild /absolute/path/to/map.json -out /absolute/path/to/build-map

# Packaging selects a runtime NAME from the chosen map catalog, not a JSON path.
ProjectPrime -mapbundle "AGENT ARENA" -mapdir /absolute/path/to/project-library -out /absolute/path/to/agent-arena.ppmap

# Registered-room diagnostic and discovery tools:
ProjectPrime -mapmaterials "MP3 PROVING GROUND"
ProjectPrime -mapitems "ROOM NAME"
ProjectPrime -mapcheck "ROOM NAME"
```

`-mapvalidate` analyzes the source and compiled geometry/budgets. `-mapinspect` also builds navigation. They emit structured validation diagnostics and return failure on errors. `-mapbuild` writes runtime outputs; `-out` directs them into archive/entities/nodes beneath the chosen output directory. Borrowed-material builds require game assets; source validation cannot prove that an unavailable cartridge material slot exists. Validation/inspection have an early CLI path that can work without launcher setup for appropriate sources.

`-mapbundle` reads the catalog and selects the runtime name. Use a clean source-library location when source/package duplicates make selection ambiguous. Packages and editable sources have identity-aware catalog precedence; do not assume the file you most recently edited is the one selected by name. Legacy `-mapgen` builds discovered maps, but the explicit single-project commands are easier to audit.

`-mapcheck` intentionally examines raw collision and is not the publishing validation gate. Build success also does not prove spawn fairness, human traversal, objective behavior or multiplayer readiness.

In the editor, use Build → Validate, Build runtime, Export .ppmap, Playtest, and Run map audit. The map audit uses a child gameplay/bot/render process and needs its runtime prerequisites. Test both normal starts and Play from here: a camera-position start can hide broken real spawn placement.

### Current budgets and common failures

- At most 10,000 authored geometry-plus-legacy-brush objects in source validation.
- At most 32,767 authored spawn/item/pad entries at the source gate; compiled entity budget errors at reaching its 32,767 limit, so stay below it.
- Meshes and convexes have the per-object limits listed above.
- Collision grid cells have a 2,000,000 budget. Spatially vast sparse layouts can be expensive even with few faces.
- **Extended 32-bit collision indexing is implemented.** Old documentation describing 65,535 collision references as an absolute current ceiling is outdated. Larger counts produce an informational extended-indexing diagnostic and still have memory/grid constraints.
- Render partitions, meshes, command bytes, materials and textures have separate reported budgets. Inspect the report rather than using triangle count as the only cost metric.
- Reported bounded budgets warn at 70%, add a stronger warning at 90%, and fail at 100%.
- Package limits: 512 MiB compressed, 1 GiB expanded, 256 MiB per entry, 8 MiB project JSON, 2048 entries. These are limits, not size targets.

| Diagnostic | Typical next action |
| --- | --- |
| `FP-MAP-001` | Fix material indices, baked textures, UV settings or UV overflow. |
| `FP-MAP-002` | Move starts out of geometry; inspect clearance. |
| `FP-MAP-003` | Reduce the named budget/invalid partition setting; inspect actual reported counts. |
| `FP-MAP-004` | Reduce extents or choose a suitable packing scale. |
| `FP-MAP-005/006` | Resolve source-level or texture dependencies. |
| `FP-MAP-007` | Review navigation links/connectivity. |
| `FP-MAP-008/009/010` | Fix schema version, runtime name or identity. |
| `FP-MAP-011/012` | Fix positions, environment ranges, colors or lighting. |
| `FP-MAP-013`, `FP-MESH-001` | Repair transforms, topology, winding, degeneracy or solid geometry. |
| `FP-MAP-014/015/016` | Review spawns, supported pickups or jump pads. |
| `FP-MAP-017/018` | Review import settings or warning details; 018 covers several budget/repair warnings. |
| `FP-MAP-021/022` | Fix assets/audio or capabilities/objective policy. |
| `FP-MAP-024` | Informational extended collision indexing notice; not itself a failure. |

## 14. A complete starter project

The adjacent [agent-arena.example.json](agent-arena.example.json) is a strict-JSON blockout intended for up to two players with an enclosed floor, four corner starts, central cover and two pickups. It uses borrowed material slot 2 from `MP3 PROVING GROUND`, following the native template convention. Its runtime build therefore requires extracted game assets. It is a starting example, not a balanced or visually finished map.

Copy it into a new project directory as `map.json`, replace `mapId` and all object/material/entity IDs for a new independent map, set the runtime/display names, and validate before extending it. Do not copy the example's identity into multiple published maps. Read the verification note at the end for the checks actually performed on this reference.

## 15. Recommended agent workflow and acceptance checklist

### Plan and block out

Write down intended modes, player count, source route, visual theme, map bounds, route structure and available asset dependencies. Keep this design intent beside the deliverable. For initial native maps, favor a small set of clear routes, two or more ways to contest important areas, and sufficient cover to prevent every spawn seeing every other spawn. These are design recommendations, not compiler requirements.

Build the floor, boundaries, ramps and platforms first. Place real spawn locations early. Keep descriptive labels such as `West ramp`, `Upper health platform` and `Team A safe spawn 2`; organize layers by architecture, gameplay, decoration and collision. Validate after each structural pass rather than waiting until detailing is complete.

### Verify physically and visually

- Walk every intended route in both directions where applicable, in the intended forms/hunters.
- Check doorways, ramps, ledges, stairs, platform lips and headroom.
- Try each spawn, each pad approach and each landing; check ceilings along arcs.
- Confirm floors and walls collide from the intended side and decorative pieces do not obstruct movement.
- Test fall recovery and kill-plane behavior.
- Check bot reachability, navigation components and access to pickups.
- Review normals/culling, texture orientation/density, lighting and map-preview framing.
- Test each advertised mode and team assignment, including any preserved objectives.

### Package and report

Save the source, validate/inspect/build, then package the same revision. Open or install the produced package to catch missing dependencies. When multiplayer distribution is in scope, test the exact package with another client; a local editor build alone does not exercise download/install readiness.

Deliver: the editable project folder, referenced assets with source/license notes where applicable, portable package if produced, map name/ID/version, mode/player assumptions, commands and results, unresolved warnings, and a clear distinction between automated checks and actual gameplay tests. Do not claim “playtested” because validation passed.

Community publication and hosting are distinct actions from making a local package. The current game can synchronize exact immutable package identities through lobby downloads and readiness checks. Publication uses Hunter License-backed creator authentication; published version bytes cannot simply be overwritten, so a revision should use a new version. Follow the user's requested distribution scope.

## 16. Copyable instructions for a map-creation agent

> Create or modify a Project Prime map using this reference and the current repository source. Deliver an editable JSON project plus all referenced assets, and a `.ppmap` if build prerequisites are available. Use v2 identity, preserve existing IDs for revisions, and assign fresh IDs to new objects. Use Y-up world coordinates, normalized XYZW quaternion transforms, positive scales and valid authored material indices. Do not invent gameplay entities, enum strings or asset files. Prefer native primitives for the first blockout. Explicitly declare supported modes and a player range within current limits. Place supported spawns/pickups/pads with clearance and reachable routes. Hidden objects still compile; navigation links do not create physical mechanics. Validate and inspect the exact JSON by path, fix errors, review warnings, build, and test real spawns, routes and pad arcs. Package using the built-in builder rather than hand-writing manifests. Report the files produced, assumptions, dependencies, actual checks and remaining limitations. If required assets or a running game are unavailable, preserve the source and state exactly which checks could not be completed.

## 17. Source-of-truth index and maintenance notes

Paths below are repository-relative so this document remains useful when shared with an agent in another checkout.

| Source | What to inspect |
| --- | --- |
| `src/MphRead/Mods/MapGen/MapDefinition.cs` | Top-level recipe, imports, native remixes, materials, legacy brushes, entities and defaults. |
| `src/MphRead/Mods/MapGen/Geometry/MapGeometry.cs` | Polymorphic modern geometry and mesh fields. |
| `src/MphRead/Mods/MapGen/Geometry/GeometryCompiler.cs` | Primitive construction, transforms, winding, UVs and collision-only behavior. |
| `src/MphRead/Mods/MapGen/MapBuilder.cs` | Entity generation, supported pickups and pad solver. |
| `src/MphRead/Mods/MapGen/Validation/MapValidator.cs` | Source checks and diagnostics. |
| `src/MphRead/Mods/MapGen/Validation/MapBudgetValidator.cs` | Compiled resource budgets and extended collision policy. |
| `src/MphRead/Mods/MapGen/Project/MapAssets.cs` | Assets, audio, capabilities and objective-mode policy. |
| `src/MphRead/Mods/MapGen/Project/MapNavigationLink.cs` | Link enum and serialization fields. |
| `src/MphRead/Mods/MapGen/Project/MapModelSource.cs` | Reimport provenance. |
| `src/MphRead/Mods/MapGen/Project/MapProjectFolder.cs` | Portable saves/exports and serialization. |
| `src/MphRead/Mods/MapGen/Packaging/` | Package format, verification and installation. |
| `src/MphRead/Mods/MapGen/Build/` | Snapshot, scheduler, dependency, fingerprint and output ownership. |
| `src/MphRead/Mods/MapEditor/MapTemplates.cs` | Current starter layouts; validate before adopting. |
| `src/MphRead/Mods/MapEditor/MapMesh*.cs` | Modeling, topology and diagnostics. |
| `src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs` | Current menus, inspector actions and shell shortcuts. |
| `src/MphRead/Mods/Launcher/Gui/MapViewport*.cs` | Viewport and context-sensitive input. |
| `src/MphRead/Mods/ModEntry.cs` | Actual CLI routing and setup requirements. |
| `tools/map-editor-check/` | Automated editor/compiler/package checks. |
| `docs/architecture/map-studio-next-pass.md` | Modeling, static model import and source-folder workflow details. |

Older `maps/README.md`, `.claude/mapgen/MAP-PIPELINE.md` and `.claude/mapgen/MAP-STUDIO.md` contain valuable background but also superseded statements. In particular, do not repeat their old claims that vertex/edge modeling and CSG are absent, runtime partitioning is absent, multiplayer package download is unimplemented, or legacy 16-bit collision indexing is the only supported format. Reconcile any future changes against code and validation output.

## 18. Verification performed for this reference

The desktop project built successfully with the locally installed .NET 10 SDK (0 errors, 70 warnings). The adjacent example passed `-mapvalidate` with `isValid:true` and no project diagnostics. `-mapinspect` also returned `isValid:true`, with 26 navigation nodes, 92 edges, and one warning: **navigation contains 5 disconnected regions**. That warning remains for review; the reference blockout is not claimed to have fully connected bot navigation. Inspect wall/cover-top regions and the intended playable floor in the Navigation overlay before adopting it as a finished layout.

The example produced 36 visual faces, 36 collision faces, 6 gameplay entities and 243 collision grid cells. These are measured example counts, not recommended universal budgets. Existing local-library warnings about other maps were separate from the example's structured validation result. No interactive gameplay, runtime asset packing, package installation or multiplayer playtest was performed for this documentation task.

## 19. Complete locally available map-texture catalog

Inventoried September 30, 2026 using `-mapmaterials` against every distinct built-in room key declared in `Metadata/Rooms.cs`: **114 loadable rooms, 2,587 material bindings, 2,512 textured bindings, and 75 untextured bindings**. There are also **21 baked Dust2 texture entries** and **25 texture-source images** in the included Dust2 PK3 (plus one level preview). These counts are bindings and entries, **not globally unique images**: rooms can reuse images and palettes.

This catalog covers room surfaces directly usable through the map material system and the repository’s bundled map art. Character skins, cosmetic decals, launcher graphics and arbitrary files elsewhere on the computer are not map-surface palettes. Missing local room assets are listed explicitly below. Texture image bytes are not embedded in this Markdown; the catalog provides exact references to the available assets. The same inventory is supplied as [texture-catalog.json](texture-catalog.json) for programmatic agent lookup.

### How an agent must use this catalog

1. Choose a room heading and use its exact key as the map’s top-level `textureSource`.
2. Copy the desired **Material** column into a material’s `sourceMaterial`. Do not use the texture ID or palette ID in that field.
3. Add that material to the authored `materials` list and set geometry `material` to its zero-based position in that list.
4. A project has **one global borrowed `textureSource`**. These room tables are alternative palettes; adding a per-material room field does not enable mixing borrowed rooms. Custom baked assets can coexist with the selected borrowed palette.
5. Use editor thumbnails to choose appearance. Original names such as `lambert13` are not semantic labels; this inventory does not guess that they depict metal, stone or another surface.
6. Treat source render-mode flags as meaningful: inspect translucent/decal choices before using them as ordinary wall surfaces. Untextured entries are included for completeness and do not supply an image.

Example using a verified Proving Ground entry:

```json
"textureSource": "MP3 PROVING GROUND",
"materials": [
  { "name": "Chosen surface", "sourceMaterial": 2, "texScale": 16 }
]
```

This is a JSON fragment. Add a fresh material UUID in a complete project. The texture and palette are taken as a matched pair by the compiler. Room texture IDs are local to their source model; they are not universal asset identifiers.

### Available room palettes — index

| Room key | Material entries | Model texture entries |
| --- | ---: | ---: |
| `UNIT1_CX` | 5 | 5 |
| `UNIT1_CZ` | 5 | 5 |
| `UNIT1_MORPH_CX` | 4 | 4 |
| `UNIT1_MORPH_CZ` | 4 | 4 |
| `UNIT2_CX` | 7 | 7 |
| `UNIT2_CZ` | 7 | 7 |
| `UNIT3_CX` | 6 | 6 |
| `UNIT3_CZ` | 6 | 6 |
| `UNIT4_CX` | 5 | 5 |
| `UNIT4_CZ` | 5 | 5 |
| `CYLINDER_C1` | 8 | 6 |
| `BIGEYE_C1` | 6 | 6 |
| `UNIT1_RM1_CX` | 5 | 5 |
| `GOREA_C1` | 3 | 3 |
| `UNIT3_MORPH_CZ` | 3 | 3 |
| `UNIT1_LAND` | 19 | 17 |
| `UNIT1_C0` | 33 | 32 |
| `UNIT1_RM1` | 33 | 32 |
| `UNIT1_C4` | 15 | 13 |
| `UNIT1_RM6` | 15 | 15 |
| `CRYSTALROOM` | 20 | 17 |
| `UNIT1_RM4` | 23 | 22 |
| `UNIT1_TP1` | 25 | 18 |
| `UNIT1_B1` | 15 | 12 |
| `UNIT1_C1` | 14 | 13 |
| `UNIT1_C2` | 21 | 21 |
| `UNIT1_C5` | 23 | 22 |
| `UNIT1_RM2` | 28 | 26 |
| `UNIT1_RM3` | 31 | 29 |
| `UNIT1_RM5` | 29 | 26 |
| `UNIT1_C3` | 17 | 16 |
| `UNIT1_TP2` | 25 | 18 |
| `UNIT1_B2` | 11 | 9 |
| `UNIT2_LAND` | 30 | 28 |
| `UNIT2_C0` | 34 | 32 |
| `UNIT2_C1` | 34 | 33 |
| `UNIT2_RM1` | 37 | 33 |
| `UNIT2_C2` | 24 | 22 |
| `UNIT2_RM2` | 37 | 33 |
| `UNIT2_C3` | 31 | 29 |
| `UNIT2_RM3` | 37 | 33 |
| `UNIT2_C4` | 36 | 35 |
| `UNIT2_TP1` | 25 | 18 |
| `UNIT2_B1` | 11 | 9 |
| `UNIT2_C6` | 27 | 24 |
| `UNIT2_C7` | 29 | 26 |
| `UNIT2_RM4` | 25 | 23 |
| `UNIT2_RM5` | 32 | 32 |
| `UNIT2_RM6` | 32 | 32 |
| `UNIT2_RM7` | 32 | 32 |
| `UNIT2_RM8` | 22 | 21 |
| `UNIT2_TP2` | 25 | 18 |
| `UNIT2_B2` | 15 | 12 |
| `UNIT3_LAND` | 24 | 23 |
| `UNIT3_C0` | 23 | 22 |
| `UNIT3_C2` | 36 | 29 |
| `UNIT3_RM1` | 30 | 29 |
| `UNIT3_RM4` | 29 | 27 |
| `UNIT3_TP1` | 25 | 18 |
| `UNIT3_B1` | 11 | 9 |
| `UNIT3_C1` | 24 | 22 |
| `UNIT3_RM2` | 31 | 29 |
| `UNIT3_RM3` | 41 | 39 |
| `UNIT3_TP2` | 25 | 18 |
| `UNIT3_B2` | 15 | 12 |
| `UNIT4_LAND` | 22 | 21 |
| `UNIT4_RM1` | 34 | 30 |
| `UNIT4_RM3` | 42 | 40 |
| `UNIT4_C0` | 27 | 26 |
| `UNIT4_TP1` | 25 | 18 |
| `UNIT4_B1` | 15 | 12 |
| `UNIT4_C1` | 19 | 18 |
| `UNIT4_RM2` | 28 | 27 |
| `UNIT4_RM4` | 28 | 26 |
| `UNIT4_RM5` | 33 | 31 |
| `UNIT4_TP2` | 25 | 18 |
| `UNIT4_B2` | 11 | 9 |
| `Gorea_Land` | 25 | 19 |
| `Gorea_Peek` | 13 | 13 |
| `Gorea_b1` | 6 | 5 |
| `Gorea_b2` | 13 | 13 |
| `MP1 SANCTORUS` | 37 | 33 |
| `MP2 HARVESTER` | 20 | 15 |
| `MP3 PROVING GROUND` | 23 | 22 |
| `MP4 HIGHGROUND - EXPANDED` | 30 | 29 |
| `MP4 HIGHGROUND` | 33 | 32 |
| `MP5 FUEL SLUICE` | 29 | 27 |
| `MP6 HEADSHOT` | 27 | 26 |
| `MP7 PROCESSOR CORE` | 29 | 26 |
| `MP8 FIRE CONTROL` | 35 | 34 |
| `MP9 CRYOCHASM` | 34 | 30 |
| `MP10 OVERLOAD` | 32 | 32 |
| `MP11 BREAKTHROUGH` | 28 | 26 |
| `MP12 SIC TRANSIT` | 42 | 40 |
| `MP13 ACCELERATOR` | 31 | 29 |
| `MP14 OUTER REACH` | 23 | 21 |
| `CTF1 FAULT LINE - EXPANDED` | 32 | 30 |
| `CTF1_FAULT LINE` | 33 | 31 |
| `AD1 TRANSFER LOCK BT` | 28 | 26 |
| `AD1 TRANSFER LOCK DM` | 25 | 23 |
| `AD2 MAGMA VENTS` | 33 | 31 |
| `AD2 ALINOS PERCH` | 28 | 26 |
| `UNIT1 ALINOS LANDFALL` | 19 | 17 |
| `UNIT2 LANDING BAY` | 30 | 28 |
| `UNIT 3 VESPER STARPORT` | 24 | 23 |
| `UNIT 4 ARCTERRA BASE` | 22 | 21 |
| `Gorea Prison` | 13 | 13 |
| `E3 FIRST HUNT` | 41 | 39 |
| `biodefense chamber 06` | 2 | 2 |
| `biodefense chamber 05` | 51 | 51 |
| `biodefense chamber 03` | 11 | 11 |
| `biodefense chamber 08` | 8 | 7 |
| `biodefense chamber 04` | 1 | 1 |
| `biodefense chamber 07` | 2 | 2 |

### All room material bindings

Columns show the material index to author, the original material name, model-local texture/palette IDs, image dimensions, and source format/render mode. `Normal` is implied when no special mode is printed. Multiple material rows may refer to the same image; retain their indices because bindings can differ.

#### `UNIT1_CX`

5 materials; 5 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A2:lambert10` | 4 | 4 | 128x128 | Palette4Bit |
| 1 | `A2:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 2 | `lambert2` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `lambert3` | 0 | 1 | 64x64 | Palette4Bit |
| 4 | `lambert4` | 1 | 0 | 32x64 | PaletteA5I3 Decal |

#### `UNIT1_CZ`

5 materials; 5 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `unit1_CX:A2:lambert10` | 4 | 4 | 128x128 | Palette4Bit |
| 1 | `unit1_CX:A2:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 2 | `unit1_CX:lambert2` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `unit1_CX:lambert3` | 0 | 1 | 64x64 | Palette4Bit |
| 4 | `unit1_CX:lambert4` | 1 | 0 | 32x64 | PaletteA5I3 Decal |

#### `UNIT1_MORPH_CX`

4 materials; 4 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `floor_longstripe` | 3 | 3 | 128x128 | Palette4Bit |
| 1 | `light_45` | 1 | 2 | 32x128 | Palette4Bit |
| 2 | `light_45alpha` | 2 | 1 | 32x128 | PaletteA5I3 Decal |
| 3 | `trim_grating` | 0 | 0 | 64x128 | Palette4Bit |

#### `UNIT1_MORPH_CZ`

4 materials; 4 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `floor_longstripe` | 3 | 3 | 128x128 | Palette4Bit |
| 1 | `light_45` | 1 | 2 | 32x128 | Palette4Bit |
| 2 | `light_45alpha` | 2 | 1 | 32x128 | PaletteA5I3 Decal |
| 3 | `trim_grating` | 0 | 0 | 64x128 | Palette4Bit |

#### `UNIT2_CX`

7 materials; 7 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A:lambert10` | 4 | 4 | 128x128 | Palette4Bit |
| 1 | `lambert10` | 5 | 5 | 128x128 | Palette4Bit |
| 2 | `lambert4` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `lambert6` | 1 | 2 | 32x64 | Palette4Bit |
| 4 | `lambert7` | 2 | 1 | 32x64 | PaletteA5I3 Decal |
| 5 | `lambert8` | 3 | 3 | 64x128 | Palette4Bit |
| 6 | `lambert9` | 6 | 6 | 64x128 | Palette4Bit |

#### `UNIT2_CZ`

7 materials; 7 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A:lambert10` | 4 | 4 | 128x128 | Palette4Bit |
| 1 | `lambert10` | 5 | 5 | 128x128 | Palette4Bit |
| 2 | `lambert4` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `lambert6` | 1 | 2 | 32x64 | Palette4Bit |
| 4 | `lambert7` | 2 | 1 | 32x64 | PaletteA5I3 Decal |
| 5 | `lambert8` | 3 | 3 | 64x128 | Palette4Bit |
| 6 | `lambert9` | 6 | 6 | 64x128 | Palette4Bit |

#### `UNIT3_CX`

6 materials; 6 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A4:floor_blue_trim` | 2 | 1 | 128x128 | Palette4Bit |
| 1 | `flipped` | 1 | 1 | 128x128 | Palette4Bit |
| 2 | `lambert3` | 3 | 2 | 128x128 | Palette4Bit |
| 3 | `lambert4` | 4 | 3 | 16x32 | Palette4Bit |
| 4 | `lambert6` | 0 | 0 | 64x128 | Palette4Bit |
| 5 | `lambert7` | 5 | 4 | 32x128 | PaletteA5I3 Decal |

#### `UNIT3_CZ`

6 materials; 6 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A4:floor_blue_trim` | 2 | 1 | 128x128 | Palette4Bit |
| 1 | `flipped` | 1 | 1 | 128x128 | Palette4Bit |
| 2 | `lambert3` | 3 | 2 | 128x128 | Palette4Bit |
| 3 | `lambert4` | 4 | 3 | 16x32 | Palette4Bit |
| 4 | `lambert6` | 0 | 0 | 64x128 | Palette4Bit |
| 5 | `lambert7` | 5 | 4 | 32x128 | PaletteA5I3 Decal |

#### `UNIT4_CX`

5 materials; 5 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A16:lambert10` | 0 | 0 | 128x128 | Palette4Bit |
| 1 | `A16:lambert12` | 1 | 1 | 32x64 | Palette4Bit |
| 2 | `A6:A7:save01C` | 4 | 4 | 64x64 | Palette4Bit |
| 3 | `lambert4` | 2 | 2 | 128x128 | Palette4Bit |
| 4 | `pasted__lambert_walls` | 3 | 3 | 128x128 | Palette4Bit |

#### `UNIT4_CZ`

5 materials; 5 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A16:lambert10` | 0 | 0 | 128x128 | Palette4Bit |
| 1 | `A16:lambert12` | 1 | 1 | 32x64 | Palette4Bit |
| 2 | `A6:A7:save01C` | 4 | 4 | 64x64 | Palette4Bit |
| 3 | `lambert4` | 2 | 2 | 128x128 | Palette4Bit |
| 4 | `pasted__lambert_walls` | 3 | 3 | 128x128 | Palette4Bit |

#### `CYLINDER_C1`

8 materials; 6 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `Cylinder_C1:Bumpy` | 1 | 1 | 128x128 | Palette4Bit |
| 1 | `Cylinder_C1:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `Cylinder_C1:flatcolor` | -1 | -1 | no texture | Untextured |
| 3 | `Cylinder_C1:lambert25` | 4 | 4 | 64x128 | Palette4Bit |
| 4 | `Cylinder_C1:lambert26` | 2 | 2 | 64x128 | Palette4Bit |
| 5 | `Cylinder_C1:lambert31` | 5 | 5 | 64x64 | Palette4Bit |
| 6 | `Cylinder_C1:shield` | 0 | 0 | 8x8 | PaletteA5I3 Translucent |
| 7 | `lambert1` | -1 | -1 | no texture | Untextured |

#### `BIGEYE_C1`

6 materials; 6 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BigEyeConnect` | 2 | 2 | 32x64 | Palette4Bit |
| 1 | `BigEyeConnect3` | 1 | 1 | 128x128 | Palette4Bit |
| 2 | `BigEyeConnect4` | 5 | 5 | 32x32 | Palette4Bit |
| 3 | `spore_update:Cylinder_C1:lambert25` | 3 | 3 | 64x128 | Palette4Bit |
| 4 | `spore_update:Cylinder_C1:lambert26` | 0 | 0 | 64x128 | Palette4Bit |
| 5 | `spore_update:Cylinder_C1:lambert31` | 4 | 4 | 64x64 | Palette4Bit |

#### `UNIT1_RM1_CX`

5 materials; 5 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `floor_00` | 0 | 0 | 128x128 | Palette4Bit |
| 1 | `lambert1` | 4 | 4 | 128x128 | Palette4Bit |
| 2 | `track_01` | 2 | 2 | 128x64 | Palette4Bit |
| 3 | `trim_00` | 1 | 1 | 128x64 | Palette4Bit |
| 4 | `wall_01` | 3 | 3 | 128x128 | Palette4Bit |

#### `GOREA_C1`

3 materials; 3 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `girdFloor` | 0 | 0 | 128x128 | Palette2Bit |
| 1 | `greentop` | 1 | 1 | 128x128 | Palette4Bit Translucent |
| 2 | `lambert2` | 2 | 2 | 128x128 | Palette4Bit |

#### `UNIT3_MORPH_CZ`

3 materials; 3 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `Unit3_morph_CZ:dk_black` | 0 | 0 | 64x128 | Palette4Bit |
| 1 | `Unit3_morph_CZ:dk_black_light` | 2 | 2 | 32x128 | PaletteA5I3 Decal |
| 2 | `Unit3_morph_CZ:lambert41` | 1 | 1 | 128x128 | Palette4Bit |

#### `UNIT1_LAND`

19 materials; 17 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A12:lambert14` | 8 | 8 | 128x128 | Palette4Bit |
| 1 | `A12:lambert28` | 9 | 9 | 64x64 | PaletteA5I3 Decal |
| 2 | `Block` | 5 | 5 | 128x128 | Palette4Bit |
| 3 | `Crack` | 13 | 13 | 64x64 | Palette4Bit |
| 4 | `Lava1` | 11 | 11 | 128x128 | Palette4Bit Decal |
| 5 | `Lava2` | 11 | 11 | 128x128 | Palette4Bit Decal |
| 6 | `Wall` | 15 | 15 | 128x128 | Palette4Bit |
| 7 | `lambert1` | -1 | -1 | no texture | Untextured |
| 8 | `lambert10` | 0 | 0 | 64x64 | Palette4Bit |
| 9 | `lambert11` | 4 | 4 | 128x128 | Palette4Bit |
| 10 | `lambert12` | 3 | 3 | 32x64 | Palette4Bit |
| 11 | `lambert15` | 2 | 2 | 128x128 | Palette4Bit |
| 12 | `lambert17` | 14 | 14 | 64x128 | Palette4Bit |
| 13 | `lambert19` | 7 | 7 | 128x128 | Palette4Bit |
| 14 | `lambert20` | 10 | 10 | 128x128 | Palette4Bit |
| 15 | `lambert5` | 16 | 16 | 128x128 | Palette4Bit |
| 16 | `lambert7` | 1 | 1 | 128x128 | Palette4Bit |
| 17 | `lambert8` | 6 | 6 | 64x64 | Palette4Bit |
| 18 | `trim01` | 12 | 12 | 128x64 | Palette4Bit |

#### `UNIT1_C0`

33 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A10:Lava1` | 2 | 2 | 128x128 | Palette4Bit |
| 1 | `A11:lambert9` | 26 | 26 | 64x64 | PaletteA5I3 Translucent |
| 2 | `A12:lambert14` | 15 | 15 | 128x128 | Palette4Bit |
| 3 | `A12:lambert28` | 16 | 16 | 64x64 | PaletteA5I3 Decal |
| 4 | `A5:lambert11` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `A7:pipeAC2` | 22 | 22 | 32x64 | Palette4Bit |
| 6 | `A7:save01C` | 23 | 24 | 128x64 | Palette4Bit |
| 7 | `Wall` | 27 | 29 | 128x128 | Palette4Bit |
| 8 | `lambert1` | -1 | -1 | no texture | Untextured |
| 9 | `lambert11` | 1 | 1 | 128x128 | Palette4Bit |
| 10 | `lambert12` | 18 | 18 | 128x128 | Palette8Bit |
| 11 | `lambert13` | 0 | 0 | 128x64 | Palette4Bit |
| 12 | `lambert14` | 29 | 28 | 128x128 | Palette4Bit |
| 13 | `lambert16` | 11 | 12 | 128x128 | Palette8Bit |
| 14 | `lambert17` | 30 | 30 | 128x128 | Palette4Bit |
| 15 | `lambert18` | 19 | 19 | 128x128 | Palette4Bit |
| 16 | `lambert19` | 7 | 8 | 128x64 | Palette4Bit |
| 17 | `lambert20` | 8 | 7 | 128x64 | PaletteA5I3 Decal |
| 18 | `lambert21` | 17 | 17 | 128x64 | Palette4Bit |
| 19 | `lambert22` | 4 | 4 | 64x64 | Palette4Bit |
| 20 | `lambert23` | 3 | 3 | 128x128 | Palette4Bit |
| 21 | `lambert24` | 31 | 31 | 128x128 | Palette4Bit |
| 22 | `lambert25` | 24 | 23 | 128x64 | Palette4Bit |
| 23 | `lambert26` | 20 | 21 | 64x64 | Palette4Bit |
| 24 | `lambert27` | 25 | 25 | 64x32 | PaletteA5I3 Decal |
| 25 | `lambert28` | 21 | 20 | 64x64 | PaletteA3I5 Decal |
| 26 | `lambert29` | 9 | 9 | 128x128 | Palette4Bit |
| 27 | `lambert30` | 13 | 13 | 128x128 | Palette4Bit Translucent |
| 28 | `lambert5` | 5 | 6 | 64x64 | Palette4Bit |
| 29 | `lambert6` | 6 | 5 | 32x64 | PaletteA5I3 Decal |
| 30 | `lambert7` | 12 | 11 | 128x128 | Palette4Bit |
| 31 | `lambert8` | 10 | 10 | 64x64 | Palette4Bit |
| 32 | `lambert9` | 28 | 27 | 64x64 | Palette4Bit |

#### `UNIT1_RM1`

33 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A5:lambert14` | 13 | 13 | 128x128 | Palette4Bit |
| 1 | `A5:lambert28` | 14 | 14 | 64x64 | PaletteA5I3 Decal |
| 2 | `Lava` | 1 | 1 | 64x128 | Palette4Bit Decal |
| 3 | `cb_bridgeRockC` | 7 | 7 | 128x128 | Palette4Bit |
| 4 | `enbox02C` | 8 | 8 | 64x64 | Palette4Bit |
| 5 | `floor_00` | 9 | 9 | 128x128 | Palette4Bit |
| 6 | `glyph1` | 10 | 10 | 128x128 | Palette4Bit |
| 7 | `h_floorC` | 0 | 0 | 128x128 | Palette4Bit |
| 8 | `lambert1` | 30 | 30 | 128x128 | Palette4Bit |
| 9 | `lambert18` | 4 | 5 | 32x64 | Palette4Bit |
| 10 | `lambert20` | 5 | 4 | 16x64 | PaletteA5I3 Decal |
| 11 | `lambert21` | 16 | 16 | 128x64 | Palette4Bit |
| 12 | `lambert22` | 18 | 18 | 64x128 | Palette4Bit |
| 13 | `lambert23` | 22 | 22 | 128x32 | Palette4Bit |
| 14 | `lambert25` | 3 | 3 | 64x128 | Palette4Bit |
| 15 | `lambert26` | 2 | 2 | 64x128 | Palette4Bit |
| 16 | `new_sandC` | 17 | 17 | 128x128 | Palette4Bit |
| 17 | `panels2:lambert29` | 11 | 11 | 128x32 | PaletteA5I3 Translucent |
| 18 | `panels2:lambert31` | 12 | 12 | 32x32 | PaletteA5I3 Translucent |
| 19 | `pipeAC2` | 19 | 19 | 32x64 | Palette4Bit |
| 20 | `pipeAS` | 6 | 6 | 32x64 | Palette4Bit |
| 21 | `pmag1` | 21 | 21 | 64x64 | Palette4Bit Decal |
| 22 | `pmag2` | 21 | 21 | 64x64 | Palette4Bit Unknown3 |
| 23 | `sand_to_grass` | 23 | 23 | 128x64 | Palette4Bit |
| 24 | `save01C` | 24 | 24 | 64x64 | Palette4Bit |
| 25 | `shield2_opaq` | 20 | 20 | 64x64 | Palette4Bit |
| 26 | `track_01` | 25 | 25 | 64x64 | Palette4Bit |
| 27 | `trim_00` | 15 | 15 | 128x32 | Palette4Bit |
| 28 | `trim_01` | 26 | 26 | 128x64 | Palette4Bit |
| 29 | `wall0c` | 27 | 27 | 64x64 | Palette4Bit |
| 30 | `wall_00` | 28 | 28 | 128x128 | Palette4Bit |
| 31 | `wall_01` | 29 | 29 | 128x128 | Palette4Bit |
| 32 | `wallpiece3` | 31 | 31 | 128x128 | Palette4Bit |

#### `UNIT1_C4`

15 materials; 13 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A13:A7:save01C` | 12 | 12 | 128x64 | Palette4Bit |
| 1 | `A13:lambert19` | 6 | 7 | 128x64 | Palette4Bit |
| 2 | `A13:lambert20` | 7 | 6 | 128x64 | PaletteA5I3 Decal |
| 3 | `A13:lambert21` | 9 | 9 | 128x64 | Palette4Bit |
| 4 | `A13:lambert22` | 3 | 3 | 64x64 | Palette4Bit |
| 5 | `LAVA` | 1 | 0 | 128x128 | PaletteA5I3 Translucent |
| 6 | `LAVABACK` | 2 | 1 | 128x128 | PaletteA5I3 Translucent |
| 7 | `LAVA_C_slower` | 0 | 2 | 128x128 | Palette4Bit |
| 8 | `blocks_lambert` | 4 | 4 | 128x128 | Palette4Bit |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert18` | 11 | 11 | 128x128 | Palette4Bit |
| 11 | `lambert33` | 10 | 10 | 128x128 | Palette8Bit |
| 12 | `lambert35` | 5 | 5 | 128x128 | Palette4Bit |
| 13 | `lambertLAVA_C` | 0 | 2 | 128x128 | Palette4Bit |
| 14 | `littleRocks` | 8 | 8 | 256x256 | PaletteA3I5 Translucent |

#### `UNIT1_RM6`

15 materials; 15 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `cb_bridgeRockC` | 3 | 3 | 128x128 | Palette4Bit |
| 1 | `enbox02C` | 4 | 4 | 64x64 | Palette4Bit |
| 2 | `floor_00` | 5 | 5 | 128x128 | Palette4Bit |
| 3 | `glyph1` | 6 | 6 | 128x128 | Palette4Bit |
| 4 | `lambert1` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `lambert18` | 0 | 1 | 32x64 | Palette4Bit |
| 6 | `lambert20` | 1 | 0 | 16x64 | PaletteA5I3 Decal |
| 7 | `pipeAC2` | 8 | 8 | 32x64 | Palette4Bit |
| 8 | `pipeAS` | 2 | 2 | 32x64 | Palette4Bit |
| 9 | `save01C` | 9 | 9 | 64x64 | Palette4Bit |
| 10 | `track_01` | 10 | 10 | 64x64 | Palette4Bit |
| 11 | `trim_00` | 7 | 7 | 128x32 | Palette4Bit |
| 12 | `trim_01` | 11 | 11 | 128x64 | Palette4Bit |
| 13 | `wall0c` | 12 | 12 | 64x64 | Palette4Bit |
| 14 | `wall_01` | 13 | 13 | 128x128 | Palette4Bit |

#### `CRYSTALROOM`

20 materials; 17 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `file19Material` | 16 | 16 | 16x16 | Palette4Bit |
| 1 | `file4Material` | 0 | 0 | 128x128 | Palette8Bit |
| 2 | `file5Material` | 8 | 8 | 128x128 | Palette4Bit |
| 3 | `floor_reg` | 1 | 1 | 128x128 | Palette8Bit |
| 4 | `geo_mag:A1:shield1` | 7 | 7 | 16x16 | PaletteA5I3 Decal |
| 5 | `geo_mag:A1:shield2` | 6 | 6 | 128x128 | Palette4Bit |
| 6 | `lambert1` | -1 | -1 | no texture | Untextured |
| 7 | `lambert11` | 4 | 5 | 16x16 | Palette4Bit |
| 8 | `lambert12` | 16 | 16 | 16x16 | Palette4Bit |
| 9 | `lambert13` | 5 | 4 | 64x16 | Palette4Bit |
| 10 | `lambert24` | 7 | 7 | 16x16 | PaletteA5I3 Translucent |
| 11 | `pilar` | 2 | 3 | 128x128 | Palette8Bit |
| 12 | `pilar_edge` | 3 | 2 | 128x128 | Palette4Bit |
| 13 | `reg_walls` | 15 | 15 | 128x128 | Palette4Bit |
| 14 | `wall_02` | 14 | 14 | 128x128 | Palette8Bit |
| 15 | `wall_Trim_01` | 11 | 11 | 128x128 | Palette8Bit |
| 16 | `wall_Trim_02` | 12 | 12 | 128x128 | Palette8Bit |
| 17 | `wall_Trim_03` | 13 | 13 | 128x128 | Palette8Bit |
| 18 | `wall_mp4` | 9 | 9 | 128x128 | Palette4Bit |
| 19 | `wall_spiral` | 10 | 10 | 128x128 | Palette4Bit |

#### `UNIT1_RM4`

23 materials; 22 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert1` | -1 | -1 | no texture | Untextured |
| 1 | `lambert12` | 12 | 12 | 128x64 | Palette4Bit |
| 2 | `lambert13` | 6 | 6 | 128x128 | Palette4Bit |
| 3 | `lambert14` | 7 | 7 | 128x128 | Palette4Bit |
| 4 | `lambert15` | 16 | 16 | 128x128 | Palette4Bit |
| 5 | `lambert16` | 10 | 10 | 128x64 | Palette4Bit |
| 6 | `lambert17` | 9 | 9 | 64x128 | Palette4Bit |
| 7 | `lambert18` | 0 | 1 | 64x64 | Palette4Bit |
| 8 | `lambert19` | 18 | 18 | 128x128 | Palette4Bit |
| 9 | `lambert20` | 19 | 19 | 128x128 | Palette4Bit |
| 10 | `lambert21` | 20 | 20 | 128x128 | Palette4Bit |
| 11 | `lambert22` | 3 | 3 | 64x128 | Palette4Bit |
| 12 | `lambert24` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `lambert25` | 5 | 5 | 128x128 | Palette4Bit Translucent |
| 14 | `lambert26` | 13 | 13 | 64x64 | Palette4Bit |
| 15 | `lambert28` | 8 | 8 | 64x64 | PaletteA5I3 Decal |
| 16 | `lambert29` | 14 | 15 | 256x64 | Palette4Bit |
| 17 | `lambert30` | 15 | 14 | 256x64 | PaletteA5I3 Decal |
| 18 | `lambert31` | 1 | 0 | 64x64 | PaletteA5I3 Decal |
| 19 | `lambert32` | 21 | 21 | 128x32 | Palette4Bit |
| 20 | `lambert4` | 17 | 17 | 128x128 | Palette4Bit |
| 21 | `lambert6` | 11 | 11 | 128x64 | Palette4Bit |
| 22 | `lambert7` | 2 | 2 | 128x128 | Palette4Bit |

#### `UNIT1_TP1`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT1_B1`

15 materials; 12 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BigEyeNest_BigEyeNestBase` | 0 | 0 | 64x64 | Palette4Bit |
| 1 | `BigEyeNest_BigEyeNestCore` | 1 | 1 | 32x32 | Palette4Bit |
| 2 | `BigEyeNest_BigEyeNestGlo0` | 2 | 2 | 8x32 | PaletteA5I3 Unknown3 |
| 3 | `BigEyeNest_BigEyeNestGlo1` | 2 | 2 | 8x32 | PaletteA5I3 Translucent |
| 4 | `BigEyeNest_BigEyeNestGlo2` | 2 | 2 | 8x32 | PaletteA5I3 Unknown4 |
| 5 | `BigEyeNest_BigEyeNestHole` | 3 | 3 | 32x32 | Palette4Bit |
| 6 | `BigEyeNest_BigEyeNestOpening` | 4 | 4 | 32x32 | Palette4Bit |
| 7 | `BigEyeNest_BigEyeNestVines` | 5 | 5 | 32x16 | Palette4Bit |
| 8 | `Marker` | -1 | -1 | no texture | Untextured |
| 9 | `lambert12` | 10 | 10 | 128x256 | PaletteA5I3 Translucent |
| 10 | `lambert13` | 9 | 9 | 32x64 | Palette4Bit |
| 11 | `lambert18` | 7 | 7 | 8x16 | PaletteA5I3 Translucent |
| 12 | `lambert19` | 6 | 6 | 128x128 | Palette8Bit |
| 13 | `lambert6` | 11 | 11 | 8x16 | PaletteA5I3 Translucent |
| 14 | `wall1` | 8 | 8 | 256x256 | Palette4Bit |

#### `UNIT1_C1`

14 materials; 13 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A12:lambert14` | 5 | 5 | 128x128 | Palette4Bit |
| 1 | `A12:lambert28` | 6 | 6 | 64x64 | PaletteA5I3 Decal |
| 2 | `A6:A7:save01C` | 10 | 10 | 128x64 | Palette4Bit |
| 3 | `A6:lambert8` | 2 | 2 | 64x64 | Palette4Bit |
| 4 | `lambert1` | -1 | -1 | no texture | Untextured |
| 5 | `lambert10` | 7 | 7 | 128x128 | Palette4Bit |
| 6 | `lambert11` | 1 | 1 | 128x128 | Palette4Bit |
| 7 | `lambert12` | 12 | 12 | 128x128 | Palette4Bit |
| 8 | `lambert13` | 11 | 11 | 128x64 | Palette4Bit |
| 9 | `lambert14` | 4 | 4 | 128x128 | Palette4Bit |
| 10 | `lambert5` | 3 | 3 | 128x128 | Palette4Bit |
| 11 | `lambert6` | 8 | 8 | 128x128 | Palette4Bit |
| 12 | `lambert7` | 0 | 0 | 128x128 | Palette4Bit |
| 13 | `lambert8` | 9 | 9 | 128x128 | Palette4Bit |

#### `UNIT1_C2`

21 materials; 21 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A13:Lava1` | 13 | 13 | 128x128 | Palette4Bit |
| 1 | `A15:lambert14` | 7 | 7 | 128x128 | Palette4Bit |
| 2 | `A15:lambert28` | 8 | 8 | 64x64 | PaletteA5I3 Decal |
| 3 | `A6:A7:save01C` | 15 | 15 | 128x64 | Palette4Bit |
| 4 | `A6:lambert19` | 3 | 4 | 128x64 | Palette4Bit |
| 5 | `A6:lambert20` | 4 | 3 | 128x64 | PaletteA5I3 Decal |
| 6 | `A6:lambert21` | 9 | 9 | 128x64 | Palette4Bit |
| 7 | `A6:lambert22` | 2 | 2 | 64x64 | Palette4Bit |
| 8 | `Lava1` | 14 | 14 | 128x128 | Palette4Bit Decal |
| 9 | `lambert10` | 19 | 19 | 128x128 | Palette8Bit |
| 10 | `lambert11` | 6 | 6 | 128x128 | Palette4Bit |
| 11 | `lambert12` | 1 | 1 | 128x128 | Palette4Bit |
| 12 | `lambert14` | 20 | 20 | 128x128 | Palette4Bit |
| 13 | `lambert15` | 16 | 16 | 128x64 | Palette4Bit |
| 14 | `lambert16` | 17 | 17 | 64x64 | Palette4Bit |
| 15 | `lambert17` | 5 | 5 | 128x128 | Palette4Bit |
| 16 | `lambert18` | 12 | 12 | 128x128 | Palette4Bit |
| 17 | `lambert19` | 11 | 11 | 128x128 | PaletteA5I3 |
| 18 | `lambert5` | 18 | 18 | 128x128 | Palette4Bit |
| 19 | `lambert6` | 0 | 0 | 128x64 | Palette4Bit |
| 20 | `lambert7` | 10 | 10 | 128x128 | Palette4Bit |

#### `UNIT1_C5`

23 materials; 22 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `base_metal_sml` | 19 | 19 | 64x64 | Palette4Bit |
| 1 | `big_pipe` | 6 | 5 | 128x128 | Palette4Bit |
| 2 | `floor_longstripe` | 20 | 20 | 128x128 | Palette4Bit |
| 3 | `floor_pattern` | 13 | 13 | 128x128 | Palette4Bit |
| 4 | `lambert1` | -1 | -1 | no texture | Untextured |
| 5 | `lambert37` | 11 | 11 | 128x128 | PaletteA5I3 Translucent |
| 6 | `lambert41` | 0 | 0 | 128x128 | Palette4Bit |
| 7 | `lambert42` | 21 | 10 | 128x64 | Palette4Bit |
| 8 | `lava2` | 18 | 18 | 128x128 | Palette4Bit |
| 9 | `lava_decal` | 4 | 3 | 128x128 | Palette4Bit Decal |
| 10 | `lava_trans` | 3 | 3 | 128x128 | Palette4Bit Translucent |
| 11 | `light_2bits` | 15 | 15 | 64x32 | Palette4Bit |
| 12 | `light_2bits_alpha` | 16 | 16 | 128x64 | PaletteA5I3 Decal |
| 13 | `light_45` | 9 | 9 | 32x128 | Palette4Bit |
| 14 | `light_45alpha` | 10 | 8 | 32x128 | PaletteA5I3 Decal |
| 15 | `metal` | 5 | 4 | 128x128 | Palette4Bit |
| 16 | `rock_lavastreams` | 1 | 1 | 128x128 | Palette4Bit |
| 17 | `rockwall` | 2 | 2 | 128x128 | Palette4Bit |
| 18 | `trim_grating` | 7 | 6 | 64x128 | Palette4Bit |
| 19 | `wall_clamps` | 14 | 14 | 64x64 | PaletteA5I3 |
| 20 | `wall_dk_trim1` | 8 | 7 | 64x32 | Palette4Bit |
| 21 | `wall_orangestreak` | 12 | 12 | 256x256 | Palette4Bit |
| 22 | `wall_teethed` | 17 | 17 | 128x128 | Palette4Bit |

#### `UNIT1_RM2`

28 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `FLOOR_ancient_type` | 9 | 9 | 128x128 | Palette4Bit |
| 1 | `FLOOR_concrete` | 6 | 6 | 64x64 | Palette4Bit |
| 2 | `FLOOR_ground_2` | 13 | 13 | 128x128 | Palette4Bit |
| 3 | `FLOOR_rockstrips` | 19 | 19 | 128x128 | Palette4Bit |
| 4 | `IMP:shield2_opaq` | 15 | 15 | 64x64 | Palette4Bit |
| 5 | `TRIM_bolted` | 21 | 21 | 64x64 | Palette4Bit |
| 6 | `TRIM_grateing` | 2 | 2 | 32x64 | Palette4Bit |
| 7 | `TRIM_lighting` | 7 | 8 | 128x32 | Palette4Bit |
| 8 | `TRIM_lighting_alpha` | 8 | 7 | 128x32 | PaletteA5I3 Decal |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert36` | 3 | 3 | 128x128 | Palette4Bit |
| 11 | `lambert37` | 4 | 4 | 128x128 | Palette4Bit |
| 12 | `lambert38` | 17 | 17 | 64x64 | Palette4Bit |
| 13 | `metal` | 1 | 1 | 128x128 | Palette4Bit |
| 14 | `metal_dark_dirty` | 5 | 5 | 32x64 | Palette4Bit |
| 15 | `new_sky1:lambert14` | 10 | 10 | 128x128 | Palette4Bit |
| 16 | `new_sky1:lambert28` | 11 | 11 | 64x64 | PaletteA5I3 Decal |
| 17 | `panel_rivets` | 14 | 14 | 64x128 | Palette4Bit |
| 18 | `pmag1` | 16 | 16 | 64x64 | Palette4Bit Decal |
| 19 | `pmag2` | 16 | 16 | 64x64 | Palette4Bit Unknown3 |
| 20 | `rock_reg` | 18 | 18 | 64x128 | Palette4Bit |
| 21 | `sand` | 20 | 20 | 128x128 | Palette4Bit |
| 22 | `trim_bridge` | 24 | 24 | 64x32 | Palette4Bit |
| 23 | `trim_door` | 22 | 23 | 64x32 | Palette4Bit |
| 24 | `trim_door2` | 23 | 22 | 64x64 | Palette4Bit |
| 25 | `trim_rock` | 0 | 0 | 128x64 | Palette4Bit |
| 26 | `wall_main` | 12 | 12 | 128x128 | Palette4Bit |
| 27 | `wall_zrich` | 25 | 25 | 64x128 | Palette4Bit |

#### `UNIT1_RM3`

31 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `FLOOR_ancient_type` | 10 | 10 | 128x128 | Palette4Bit |
| 1 | `FLOOR_concrete` | 6 | 6 | 64x64 | Palette4Bit |
| 2 | `FLOOR_ground_2` | 14 | 14 | 128x128 | Palette4Bit |
| 3 | `FLOOR_rockstrips` | 21 | 21 | 128x128 | Palette4Bit |
| 4 | `IMP:shield2_opaq` | 17 | 17 | 64x64 | Palette4Bit |
| 5 | `TRIM_bolted` | 24 | 24 | 64x64 | Palette4Bit |
| 6 | `TRIM_grateing` | 3 | 3 | 32x64 | Palette4Bit |
| 7 | `TRIM_lighting` | 7 | 8 | 128x32 | Palette4Bit |
| 8 | `TRIM_lighting_alpha` | 8 | 7 | 128x32 | PaletteA5I3 Decal |
| 9 | `ad2_1:lambert7` | 15 | 15 | 128x128 | Palette4Bit |
| 10 | `lambert1` | -1 | -1 | no texture | Untextured |
| 11 | `lambert34` | 0 | 0 | 128x128 | Palette4Bit Translucent |
| 12 | `lambert36` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `lambert38` | 19 | 19 | 64x64 | Palette4Bit |
| 14 | `metal` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `metal_dark_dirty` | 5 | 5 | 32x64 | Palette4Bit |
| 16 | `new_sky1:lambert14` | 11 | 11 | 128x128 | Palette4Bit |
| 17 | `new_sky1:lambert28` | 12 | 12 | 64x64 | PaletteA5I3 Decal |
| 18 | `panel_rivets` | 16 | 16 | 64x128 | Palette4Bit |
| 19 | `pmag1` | 18 | 18 | 64x64 | Palette4Bit Decal |
| 20 | `pmag2` | 18 | 18 | 64x64 | Palette4Bit Unknown3 |
| 21 | `rock_reg` | 20 | 20 | 64x128 | Palette4Bit |
| 22 | `rock_rock_ash` | 2 | 2 | 64x128 | Palette4Bit |
| 23 | `rust` | 22 | 22 | 16x16 | Palette4Bit |
| 24 | `sand` | 23 | 23 | 128x128 | Palette4Bit |
| 25 | `trim_bridge` | 27 | 27 | 64x32 | Palette4Bit |
| 26 | `trim_door` | 25 | 26 | 64x32 | Palette4Bit |
| 27 | `trim_door2` | 26 | 25 | 64x64 | Palette4Bit |
| 28 | `tunnel_alpha` | 9 | 9 | 16x16 | PaletteA5I3 Translucent |
| 29 | `wall_main` | 13 | 13 | 128x128 | Palette4Bit |
| 30 | `wall_zrich` | 28 | 28 | 64x128 | Palette4Bit |

#### `UNIT1_RM5`

29 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `base_metal_sml` | 24 | 24 | 64x64 | Palette4Bit |
| 1 | `big_pipe` | 6 | 6 | 128x128 | Palette4Bit |
| 2 | `floor_longstripe` | 25 | 25 | 128x128 | Palette4Bit |
| 3 | `floor_pattern` | 14 | 14 | 128x128 | Palette4Bit |
| 4 | `floor_tile` | 9 | 9 | 128x128 | Palette4Bit |
| 5 | `lambert1` | -1 | -1 | no texture | Untextured |
| 6 | `lambert37` | 15 | 15 | 128x128 | Palette4Bit Translucent |
| 7 | `lambert46` | 22 | 22 | 64x64 | Palette4Bit |
| 8 | `lava2` | 4 | 4 | 128x128 | Palette4Bit Translucent |
| 9 | `lava_decal` | 4 | 4 | 128x128 | Palette4Bit Decal |
| 10 | `lava_under` | 19 | 19 | 128x128 | Palette4Bit |
| 11 | `light_2bits` | 16 | 16 | 64x32 | Palette4Bit |
| 12 | `light_2bits_alpha` | 17 | 17 | 128x64 | PaletteA5I3 Decal |
| 13 | `light_45` | 10 | 11 | 32x128 | Palette4Bit |
| 14 | `light_45alpha` | 11 | 10 | 32x128 | PaletteA5I3 Decal |
| 15 | `metal` | 5 | 5 | 128x128 | Palette4Bit |
| 16 | `pmag` | 20 | 20 | 64x64 | Palette4Bit |
| 17 | `pmag_a` | 21 | 21 | 64x64 | Palette4Bit Unknown3 |
| 18 | `pmag_b` | 21 | 21 | 64x64 | Palette4Bit Decal |
| 19 | `rock_dk_lava` | 2 | 2 | 128x128 | Palette4Bit |
| 20 | `rock_lavastreams` | 1 | 1 | 128x128 | Palette4Bit |
| 21 | `rockwall` | 3 | 3 | 128x128 | Palette4Bit |
| 22 | `trim_grating` | 7 | 7 | 64x128 | Palette4Bit |
| 23 | `wall_clamps` | 13 | 13 | 64x128 | Palette4Bit |
| 24 | `wall_dk_trim` | 0 | 0 | 64x64 | Palette4Bit |
| 25 | `wall_dk_trim1` | 8 | 8 | 64x32 | Palette4Bit |
| 26 | `wall_orangeband` | 23 | 23 | 128x128 | Palette4Bit |
| 27 | `wall_orangestreak` | 12 | 12 | 128x128 | Palette4Bit |
| 28 | `wall_teethed` | 18 | 18 | 128x128 | Palette4Bit |

#### `UNIT1_C3`

17 materials; 16 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A11:lambert14` | 6 | 6 | 128x128 | Palette4Bit |
| 1 | `A11:lambert28` | 7 | 7 | 64x64 | PaletteA5I3 Decal |
| 2 | `A7:A7:save01C` | 11 | 11 | 128x64 | Palette4Bit |
| 3 | `A7:lambert8` | 4 | 4 | 64x64 | Palette4Bit |
| 4 | `lambert1` | -1 | -1 | no texture | Untextured |
| 5 | `lambert10` | 10 | 10 | 128x128 | Palette4Bit |
| 6 | `lambert12` | 1 | 2 | 64x64 | Palette4Bit |
| 7 | `lambert13` | 2 | 1 | 32x64 | PaletteA5I3 Decal |
| 8 | `lambert15` | 5 | 5 | 128x128 | Palette4Bit |
| 9 | `lambert16` | 13 | 13 | 128x128 | Palette4Bit |
| 10 | `lambert17` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `lambert18` | 3 | 3 | 128x128 | Palette4Bit |
| 12 | `lambert19` | 14 | 14 | 128x128 | Palette8Bit |
| 13 | `lambert5` | 12 | 12 | 64x64 | Palette4Bit |
| 14 | `lambert6` | 8 | 8 | 128x128 | Palette4Bit |
| 15 | `lambert7` | 15 | 15 | 128x128 | Palette4Bit |
| 16 | `lambert9` | 0 | 0 | 128x128 | Palette4Bit |

#### `UNIT1_TP2`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT1_B2`

11 materials; 9 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BrickMat` | 2 | 2 | 128x128 | Palette4Bit |
| 1 | `Bumpy` | 5 | 5 | 128x128 | Palette4Bit |
| 2 | `broken1` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `bumpydoor` | 7 | 7 | 32x64 | Palette4Bit |
| 4 | `coil1` | 8 | 8 | 128x64 | Palette4Bit |
| 5 | `collision1` | -1 | -1 | no texture | Untextured |
| 6 | `danish` | 6 | 6 | 128x128 | Palette4Bit |
| 7 | `floor` | 4 | 4 | 128x128 | Palette4Bit |
| 8 | `lambert24` | -1 | -1 | no texture | Untextured |
| 9 | `shield` | 0 | 0 | 8x8 | PaletteA5I3 Translucent |
| 10 | `wall` | 1 | 1 | 128x128 | Palette4Bit |

#### `UNIT2_LAND`

30 materials; 28 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `blue_light` | 1 | 2 | 64x64 | Palette4Bit |
| 1 | `blue_ligth_alpha` | 2 | 1 | 32x64 | PaletteA5I3 Decal |
| 2 | `comp_wall_01` | 22 | 22 | 64x64 | Palette4Bit |
| 3 | `comp_wall_02` | 16 | 16 | 128x32 | Palette4Bit |
| 4 | `comp_wall_chips` | 24 | 24 | 64x128 | Palette4Bit |
| 5 | `floor_base_metroid` | 21 | 21 | 128x128 | Palette4Bit |
| 6 | `floor_tile_greymain` | 14 | 14 | 128x128 | Palette4Bit |
| 7 | `floor_tile_nubs` | 8 | 8 | 128x128 | Palette4Bit |
| 8 | `floor_tile_orangenubs` | 18 | 18 | 128x64 | Palette4Bit |
| 9 | `green_wall_scan` | 4 | 4 | 64x64 | PaletteA5I3 Translucent |
| 10 | `hall_mesh` | 5 | 6 | 128x128 | Palette4Bit |
| 11 | `lambert1` | -1 | -1 | no texture | Untextured |
| 12 | `lambert164` | 6 | 5 | 128x128 | Palette4Bit |
| 13 | `lambert165` | 3 | 3 | 128x128 | Palette4Bit |
| 14 | `lambert166` | 26 | 26 | 64x16 | PaletteA5I3 Translucent |
| 15 | `lambert167` | 27 | 27 | 128x64 | Palette4Bit |
| 16 | `lambert47` | 23 | 23 | 128x128 | Palette4Bit |
| 17 | `lambert6` | 20 | 20 | 128x128 | Palette4Bit |
| 18 | `lambert9` | 25 | 25 | 256x256 | Palette4Bit |
| 19 | `light_grey1` | 10 | 11 | 64x128 | Palette4Bit |
| 20 | `light_grey_alpha` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 21 | `light_orange_trim` | 0 | 0 | 128x128 | Palette4Bit |
| 22 | `light_square_grey` | 12 | 13 | 64x64 | Palette4Bit |
| 23 | `light_square_grey_alpha` | 13 | 12 | 64x64 | PaletteA5I3 Decal |
| 24 | `met_grating` | 7 | 7 | 64x64 | PaletteA5I3 Translucent |
| 25 | `notch_three_steel` | 15 | 15 | 128x16 | Palette4Bit |
| 26 | `orange_stripe` | 19 | 19 | 128x128 | Palette4Bit |
| 27 | `pilar_guts` | 17 | 17 | 128x16 | Palette4Bit |
| 28 | `space_rotate_lambert3` | 25 | 25 | 256x256 | Palette4Bit |
| 29 | `trim_grey_notches` | 9 | 9 | 128x32 | Palette4Bit |

#### `UNIT2_C0`

34 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `blue_light` | 0 | 1 | 64x64 | Palette4Bit |
| 1 | `blue_ligth_alpha` | 1 | 0 | 32x64 | PaletteA5I3 Decal |
| 2 | `comp_wall_01` | 26 | 26 | 64x64 | Palette4Bit |
| 3 | `comp_wall_02` | 20 | 20 | 128x32 | Palette4Bit |
| 4 | `comp_wall_chips` | 28 | 28 | 64x128 | Palette4Bit |
| 5 | `floor_base_metroid` | 25 | 25 | 128x128 | Palette4Bit |
| 6 | `floor_tile_greymain` | 18 | 18 | 128x128 | Palette8Bit |
| 7 | `floor_tile_nubs` | 9 | 9 | 128x128 | Palette4Bit |
| 8 | `floor_tile_orangenubs` | 22 | 22 | 128x64 | Palette4Bit |
| 9 | `green_wall_scan` | 3 | 3 | 64x64 | PaletteA5I3 Translucent |
| 10 | `hall_mesh` | 4 | 5 | 128x128 | Palette4Bit |
| 11 | `lambert1` | -1 | -1 | no texture | Untextured |
| 12 | `lambert161` | 2 | 2 | 128x128 | Palette4Bit |
| 13 | `lambert162` | 5 | 4 | 128x128 | Palette4Bit |
| 14 | `lambert163` | 31 | 31 | 128x64 | Palette4Bit |
| 15 | `lambert164` | 30 | 30 | 64x16 | PaletteA5I3 Translucent |
| 16 | `lambert3` | 29 | 29 | 256x256 | Palette4Bit |
| 17 | `lambert47` | 27 | 27 | 128x128 | Palette4Bit |
| 18 | `lambert6` | 24 | 24 | 128x128 | Palette4Bit |
| 19 | `lambert9` | 29 | 29 | 256x256 | Palette4Bit |
| 20 | `light_grey1` | 11 | 12 | 64x128 | Palette4Bit |
| 21 | `light_grey_alpha` | 12 | 11 | 64x128 | PaletteA5I3 Decal |
| 22 | `light_orange_trim` | 6 | 7 | 32x64 | Palette4Bit |
| 23 | `light_orange_trim_alpha` | 7 | 6 | 32x64 | PaletteA5I3 Decal |
| 24 | `light_square_grey` | 13 | 14 | 64x64 | Palette4Bit |
| 25 | `light_square_grey_alpha` | 14 | 13 | 64x64 | PaletteA5I3 Decal |
| 26 | `met_grating` | 8 | 8 | 64x64 | PaletteA5I3 Translucent |
| 27 | `notch_three_steel` | 19 | 19 | 128x16 | Palette4Bit |
| 28 | `orange_comp_readout` | 15 | 15 | 128x32 | PaletteA5I3 Translucent |
| 29 | `orange_scan` | 16 | 16 | 32x32 | PaletteA5I3 Translucent |
| 30 | `orange_stripe` | 23 | 23 | 128x128 | Palette4Bit |
| 31 | `pilar_guts` | 21 | 21 | 128x16 | Palette4Bit |
| 32 | `piping` | 17 | 17 | 64x128 | Palette4Bit |
| 33 | `trim_grey_notches` | 10 | 10 | 128x32 | Palette4Bit |

#### `UNIT2_C1`

34 materials; 33 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A2:spawnPadLight` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |
| 1 | `A3:lambert140` | 2 | 2 | 128x128 | Palette4Bit |
| 2 | `A3:lambert39` | 27 | 27 | 128x64 | Palette4Bit |
| 3 | `A3:ul_beam2` | 29 | 29 | 128x128 | Palette4Bit |
| 4 | `A3:ul_light1` | 32 | 32 | 32x64 | Palette4Bit Translucent |
| 5 | `A3:ul_pipe1` | 31 | 31 | 16x128 | Palette4Bit |
| 6 | `A4:hall_mesh` | 4 | 4 | 128x128 | Palette4Bit |
| 7 | `Floor` | 7 | 8 | 128x128 | Palette4Bit |
| 8 | `Vent` | 20 | 20 | 128x64 | Palette4Bit |
| 9 | `Wall` | 21 | 21 | 128x128 | Palette4Bit |
| 10 | `lambert1` | -1 | -1 | no texture | Untextured |
| 11 | `lambert10` | 22 | 22 | 128x128 | Palette4Bit |
| 12 | `lambert11` | 23 | 23 | 128x128 | Palette4Bit |
| 13 | `lambert12` | 15 | 15 | 64x128 | Palette4Bit |
| 14 | `lambert13` | 5 | 6 | 32x64 | Palette4Bit |
| 15 | `lambert14` | 6 | 5 | 32x64 | PaletteA5I3 |
| 16 | `lambert16` | 24 | 24 | 128x128 | Palette4Bit |
| 17 | `lambert17` | 19 | 19 | 128x16 | Palette4Bit |
| 18 | `lambert18` | 26 | 26 | 128x128 | Palette4Bit |
| 19 | `lambert19` | 3 | 3 | 64x64 | PaletteA5I3 Translucent |
| 20 | `lambert20` | 13 | 13 | 64x64 | PaletteA5I3 Translucent |
| 21 | `lambert22` | 28 | 28 | 128x128 | Palette4Bit |
| 22 | `lambert23` | 12 | 12 | 64x64 | Palette4Bit |
| 23 | `lambert25` | 25 | 25 | 128x128 | Palette4Bit |
| 24 | `lambert26` | 14 | 14 | 32x32 | PaletteA5I3 Translucent |
| 25 | `lambert27` | 1 | 1 | 128x128 | Palette4Bit |
| 26 | `lambert28` | 8 | 7 | 128x128 | Palette4Bit |
| 27 | `lambert29` | 0 | 0 | 128x128 | Palette4Bit |
| 28 | `lambert30` | 17 | 16 | 64x64 | PaletteA5I3 Translucent |
| 29 | `lambert5` | 9 | 9 | 32x128 | Palette4Bit |
| 30 | `lambert6` | 10 | 11 | 64x128 | Palette4Bit |
| 31 | `lambert7` | 18 | 18 | 128x128 | Palette8Bit |
| 32 | `lambert8` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 33 | `lambert9` | 30 | 30 | 64x64 | PaletteA5I3 Translucent |

#### `UNIT2_RM1`

37 materials; 33 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP:lambert35` | 1 | 1 | 8x64 | PaletteA5I3 Translucent |
| 1 | `lambert1` | -1 | -1 | no texture | Untextured |
| 2 | `lambert12` | 26 | 26 | 128x128 | Palette4Bit |
| 3 | `lambert13` | 27 | 27 | 64x64 | Palette4Bit |
| 4 | `lambert15` | 8 | 8 | 16x128 | Palette4Bit |
| 5 | `lambert161` | 32 | 32 | 32x32 | Palette4Bit |
| 6 | `lambert163` | 7 | 7 | 64x32 | Palette4Bit |
| 7 | `lambert164` | 0 | 0 | 64x128 | Palette4Bit |
| 8 | `lambert165` | 22 | 22 | 64x32 | Palette4Bit |
| 9 | `lambert166` | 28 | 28 | 128x128 | Palette4Bit |
| 10 | `lambert169` | 17 | 16 | 64x64 | PaletteA5I3 Translucent |
| 11 | `lambert17` | 6 | 6 | 128x128 | Palette4Bit |
| 12 | `lambert179` | 30 | 30 | 64x64 | Palette4Bit |
| 13 | `lambert18` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 14 | `lambert20` | 24 | 24 | 128x128 | Palette4Bit |
| 15 | `lambert21` | 20 | 20 | 128x32 | Palette4Bit |
| 16 | `lambert22` | 29 | 29 | 32x128 | Palette4Bit |
| 17 | `lambert23` | 9 | 10 | 64x128 | Palette4Bit |
| 18 | `lambert25` | 10 | 9 | 64x128 | PaletteA5I3 Decal |
| 19 | `lambert27` | 21 | 21 | 128x16 | Palette4Bit |
| 20 | `lambert3` | 19 | 19 | 64x64 | Palette4Bit |
| 21 | `lambert30` | 15 | 15 | 64x128 | Palette4Bit |
| 22 | `lambert32` | 3 | 4 | 32x32 | Palette4Bit |
| 23 | `lambert33` | 4 | 3 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert34` | 23 | 23 | 128x64 | Palette4Bit |
| 25 | `lambert39` | 2 | 2 | 64x128 | Palette4Bit |
| 26 | `lambert40` | 11 | 12 | 32x32 | Palette4Bit |
| 27 | `lambert41` | 12 | 11 | 32x32 | PaletteA5I3 Decal |
| 28 | `lambert42` | 13 | 13 | 64x64 | Palette4Bit |
| 29 | `lambert44` | 14 | 14 | 64x64 | PaletteA5I3 Translucent |
| 30 | `lambert47` | 28 | 28 | 128x128 | Palette4Bit |
| 31 | `lambert6` | 25 | 25 | 128x128 | Palette4Bit |
| 32 | `lambert9` | 18 | 18 | 32x64 | Palette4Bit |
| 33 | `pmag1` | 31 | 31 | 64x64 | Palette4Bit Decal |
| 34 | `pmag3` | 31 | 31 | 64x64 | Palette4Bit Unknown3 |
| 35 | `spawnPadLight` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |
| 36 | `spawnPadLight1` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |

#### `UNIT2_C2`

24 materials; 22 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP1:lambert17` | 4 | 4 | 128x128 | Palette4Bit |
| 1 | `IMP1:lambert19` | 10 | 10 | 128x16 | Palette4Bit |
| 2 | `IMP1:lambert20` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `IMP1:lambert21` | 11 | 11 | 128x32 | Palette4Bit |
| 4 | `IMP1:lambert22` | 17 | 17 | 64x128 | Palette4Bit |
| 5 | `IMP1:lambert23` | 6 | 7 | 64x128 | Palette4Bit |
| 6 | `IMP1:lambert25` | 7 | 6 | 64x128 | PaletteA5I3 Decal |
| 7 | `IMP1:lambert27` | 12 | 12 | 128x16 | Palette4Bit |
| 8 | `IMP1:lambert3` | 9 | 9 | 128x128 | Palette8Bit |
| 9 | `IMP1:lambert30` | 8 | 8 | 64x128 | Palette4Bit |
| 10 | `IMP1:lambert38` | 5 | 5 | 128x32 | Palette4Bit |
| 11 | `IMP1:lambert39` | 3 | 3 | 128x128 | Palette4Bit |
| 12 | `IMP1:lambert47` | 15 | 15 | 128x128 | Palette4Bit |
| 13 | `IMP1:lambert6` | 14 | 14 | 128x128 | Palette4Bit |
| 14 | `IMP4:lambert_shiled01Glow` | 18 | 18 | 8x64 | PaletteA5I3 Translucent |
| 15 | `IMP5:lambert3` | 19 | 19 | 256x256 | Palette4Bit |
| 16 | `IMP5:lambert9` | 19 | 19 | 256x256 | Palette4Bit |
| 17 | `lambert1` | -1 | -1 | no texture | Untextured |
| 18 | `lambert2` | 16 | 16 | 128x128 | Palette4Bit |
| 19 | `lambert4` | 13 | 13 | 128x128 | Palette4Bit |
| 20 | `lambert5` | 1 | 1 | 128x128 | Palette4Bit |
| 21 | `lambert7` | 2 | 2 | 64x64 | PaletteA5I3 Translucent |
| 22 | `lambert8` | 21 | 21 | 128x64 | Palette4Bit |
| 23 | `lambert9` | 20 | 20 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT2_RM2`

37 materials; 33 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP:lambert35` | 1 | 1 | 8x64 | PaletteA5I3 Translucent |
| 1 | `lambert1` | -1 | -1 | no texture | Untextured |
| 2 | `lambert12` | 26 | 26 | 128x128 | Palette4Bit |
| 3 | `lambert13` | 27 | 27 | 64x64 | Palette4Bit |
| 4 | `lambert15` | 8 | 8 | 16x128 | Palette4Bit |
| 5 | `lambert161` | 32 | 32 | 32x32 | Palette4Bit |
| 6 | `lambert163` | 7 | 7 | 64x32 | Palette4Bit |
| 7 | `lambert164` | 0 | 0 | 64x128 | Palette4Bit |
| 8 | `lambert165` | 22 | 22 | 64x32 | Palette4Bit |
| 9 | `lambert166` | 28 | 28 | 128x128 | Palette4Bit |
| 10 | `lambert169` | 17 | 16 | 64x64 | PaletteA5I3 Translucent |
| 11 | `lambert17` | 6 | 6 | 128x128 | Palette4Bit |
| 12 | `lambert179` | 30 | 30 | 64x64 | Palette4Bit |
| 13 | `lambert18` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 14 | `lambert20` | 24 | 24 | 128x128 | Palette4Bit |
| 15 | `lambert21` | 20 | 20 | 128x32 | Palette4Bit |
| 16 | `lambert22` | 29 | 29 | 32x128 | Palette4Bit |
| 17 | `lambert23` | 9 | 10 | 64x128 | Palette4Bit |
| 18 | `lambert25` | 10 | 9 | 64x128 | PaletteA5I3 Decal |
| 19 | `lambert27` | 21 | 21 | 128x16 | Palette4Bit |
| 20 | `lambert3` | 19 | 19 | 64x64 | Palette4Bit |
| 21 | `lambert30` | 15 | 15 | 64x128 | Palette4Bit |
| 22 | `lambert32` | 3 | 4 | 32x32 | Palette4Bit |
| 23 | `lambert33` | 4 | 3 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert34` | 23 | 23 | 128x64 | Palette4Bit |
| 25 | `lambert39` | 2 | 2 | 64x128 | Palette4Bit |
| 26 | `lambert40` | 11 | 12 | 32x32 | Palette4Bit |
| 27 | `lambert41` | 12 | 11 | 32x32 | PaletteA5I3 Decal |
| 28 | `lambert42` | 13 | 13 | 64x64 | Palette4Bit |
| 29 | `lambert44` | 14 | 14 | 64x64 | PaletteA5I3 Translucent |
| 30 | `lambert47` | 28 | 28 | 128x128 | Palette4Bit |
| 31 | `lambert6` | 25 | 25 | 128x128 | Palette4Bit |
| 32 | `lambert9` | 18 | 18 | 32x64 | Palette4Bit |
| 33 | `pmag1` | 31 | 31 | 64x64 | Palette4Bit Decal |
| 34 | `pmag3` | 31 | 31 | 64x64 | Palette4Bit Unknown3 |
| 35 | `spawnPadLight` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |
| 36 | `spawnPadLight1` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |

#### `UNIT2_C3`

31 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP1:lambert18` | 8 | 8 | 64x64 | PaletteA5I3 Translucent |
| 1 | `IMP1:lambert19` | 14 | 14 | 128x16 | Palette4Bit |
| 2 | `IMP1:lambert20` | 18 | 18 | 128x128 | Palette4Bit |
| 3 | `IMP1:lambert21` | 15 | 15 | 128x32 | Palette4Bit |
| 4 | `IMP1:lambert22` | 24 | 23 | 64x128 | Palette4Bit |
| 5 | `IMP1:lambert23` | 10 | 11 | 64x128 | Palette4Bit |
| 6 | `IMP1:lambert25` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 7 | `IMP1:lambert27` | 16 | 16 | 128x16 | Palette4Bit |
| 8 | `IMP1:lambert3` | 13 | 13 | 128x128 | Palette8Bit |
| 9 | `IMP1:lambert30` | 12 | 12 | 64x128 | Palette4Bit |
| 10 | `IMP1:lambert38` | 9 | 9 | 128x32 | Palette4Bit |
| 11 | `IMP1:lambert39` | 7 | 7 | 128x128 | Palette4Bit |
| 12 | `IMP1:lambert47` | 21 | 21 | 128x128 | Palette4Bit |
| 13 | `IMP1:lambert6` | 19 | 19 | 128x128 | Palette4Bit |
| 14 | `IMP3:lambert9` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 15 | `IMP4:IMP:lambert35` | 6 | 6 | 64x64 | PaletteA5I3 Translucent |
| 16 | `IMP5:lambert13` | 20 | 20 | 64x64 | Palette4Bit |
| 17 | `IMP7:IMP1:lambert22` | 23 | 24 | 64x128 | Palette4Bit |
| 18 | `IMP7:IMP4:lambert_shiled01Glow` | 25 | 25 | 8x64 | PaletteA5I3 Translucent |
| 19 | `IMP7:lambert8` | 28 | 28 | 128x64 | Palette4Bit |
| 20 | `IMP7:lambert9` | 27 | 27 | 64x16 | PaletteA5I3 Translucent |
| 21 | `IMP9:lambert3` | 26 | 26 | 256x256 | Palette4Bit |
| 22 | `IMP9:lambert9` | 26 | 26 | 256x256 | Palette4Bit |
| 23 | `lambert1` | -1 | -1 | no texture | Untextured |
| 24 | `lambert2` | 2 | 3 | 64x64 | Palette4Bit |
| 25 | `lambert3` | 3 | 2 | 32x64 | PaletteA5I3 Decal |
| 26 | `lambert5` | 22 | 22 | 128x128 | Palette4Bit |
| 27 | `lambert6` | 0 | 0 | 128x128 | Palette4Bit |
| 28 | `lambert7` | 17 | 17 | 128x64 | Palette4Bit |
| 29 | `lambert8` | 4 | 4 | 128x128 | Palette4Bit |
| 30 | `lambert9` | 1 | 1 | 128x128 | Palette4Bit |

#### `UNIT2_RM3`

37 materials; 33 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP:lambert35` | 1 | 2 | 8x64 | PaletteA5I3 Translucent |
| 1 | `lambert1` | -1 | -1 | no texture | Untextured |
| 2 | `lambert12` | 27 | 27 | 128x128 | Palette4Bit |
| 3 | `lambert13` | 28 | 28 | 64x64 | Palette4Bit |
| 4 | `lambert15` | 8 | 9 | 16x128 | Palette4Bit |
| 5 | `lambert161` | 32 | 32 | 32x32 | Palette4Bit |
| 6 | `lambert163` | 7 | 8 | 64x32 | Palette4Bit |
| 7 | `lambert164` | 0 | 0 | 64x128 | Palette4Bit |
| 8 | `lambert165` | 23 | 23 | 64x32 | Palette4Bit |
| 9 | `lambert166` | 29 | 29 | 128x128 | Palette4Bit |
| 10 | `lambert167` | 16 | 15 | 64x128 | Palette4Bit |
| 11 | `lambert168` | 9 | 1 | 32x128 | PaletteA5I3 Decal |
| 12 | `lambert169` | 18 | 17 | 64x64 | PaletteA5I3 Translucent |
| 13 | `lambert17` | 6 | 7 | 128x128 | Palette4Bit |
| 14 | `lambert18` | 5 | 6 | 64x64 | PaletteA5I3 Translucent |
| 15 | `lambert20` | 25 | 25 | 128x128 | Palette4Bit |
| 16 | `lambert21` | 21 | 21 | 128x32 | Palette4Bit |
| 17 | `lambert22` | 30 | 30 | 32x128 | Palette4Bit |
| 18 | `lambert23` | 10 | 11 | 64x128 | Palette4Bit |
| 19 | `lambert25` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 20 | `lambert27` | 22 | 22 | 128x16 | Palette4Bit |
| 21 | `lambert3` | 20 | 20 | 64x64 | Palette4Bit |
| 22 | `lambert30` | 15 | 16 | 64x128 | Palette4Bit |
| 23 | `lambert32` | 3 | 5 | 32x32 | Palette4Bit |
| 24 | `lambert33` | 4 | 4 | 32x32 | PaletteA5I3 Decal |
| 25 | `lambert34` | 24 | 24 | 128x64 | Palette4Bit |
| 26 | `lambert39` | 2 | 3 | 64x128 | Palette4Bit |
| 27 | `lambert41` | 12 | 12 | 32x32 | PaletteA5I3 Decal |
| 28 | `lambert42` | 13 | 13 | 64x64 | Palette4Bit |
| 29 | `lambert44` | 14 | 14 | 64x64 | PaletteA5I3 Translucent |
| 30 | `lambert47` | 29 | 29 | 128x128 | Palette4Bit |
| 31 | `lambert6` | 26 | 26 | 128x128 | Palette4Bit |
| 32 | `lambert9` | 19 | 19 | 32x64 | Palette4Bit |
| 33 | `pmag1` | 31 | 31 | 64x64 | Palette4Bit Decal |
| 34 | `pmag3` | 31 | 31 | 64x64 | Palette4Bit Unknown3 |
| 35 | `spawnPadLight` | 17 | 18 | 64x64 | PaletteA5I3 Translucent |
| 36 | `spawnPadLight1` | 17 | 18 | 64x64 | PaletteA5I3 Translucent |

#### `UNIT2_C4`

36 materials; 35 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:lambert132` | 2 | 2 | 128x128 | Palette4Bit |
| 1 | `A2:A3:lambert39` | 29 | 29 | 128x64 | Palette4Bit |
| 2 | `A2:A3:ul_light1` | 33 | 33 | 32x64 | Palette4Bit Translucent |
| 3 | `A3:A2:spawnPadLight` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |
| 4 | `A:lambert12` | 23 | 23 | 128x128 | Palette4Bit |
| 5 | `A:lambert23` | 10 | 11 | 64x128 | Palette4Bit |
| 6 | `A:lambert27` | 19 | 19 | 128x16 | Palette4Bit |
| 7 | `A:lambert32` | 6 | 7 | 32x64 | Palette4Bit |
| 8 | `A:lambert33` | 7 | 6 | 32x64 | PaletteA5I3 Decal |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert10` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 11 | `lambert11` | 5 | 5 | 128x128 | Palette4Bit |
| 12 | `lambert12` | 12 | 13 | 64x64 | Palette4Bit |
| 13 | `lambert13` | 3 | 3 | 128x128 | Palette4Bit |
| 14 | `lambert14` | 4 | 4 | 64x64 | PaletteA5I3 Translucent |
| 15 | `lambert15` | 32 | 32 | 64x64 | PaletteA5I3 Translucent |
| 16 | `lambert16` | 13 | 12 | 64x64 | PaletteA5I3 Decal |
| 17 | `lambert17` | 28 | 28 | 128x128 | Palette4Bit |
| 18 | `lambert18` | 21 | 21 | 128x128 | Palette4Bit |
| 19 | `lambert19` | 9 | 9 | 128x128 | Palette4Bit |
| 20 | `lambert2` | 31 | 31 | 64x64 | Palette4Bit |
| 21 | `lambert20` | 0 | 0 | 128x128 | Palette4Bit |
| 22 | `lambert21` | 8 | 8 | 128x128 | Palette4Bit Translucent |
| 23 | `lambert23` | 27 | 27 | 32x64 | Palette4Bit |
| 24 | `lambert25` | 14 | 14 | 64x64 | PaletteA5I3 Translucent |
| 25 | `lambert26` | 1 | 1 | 128x128 | Palette4Bit |
| 26 | `lambert27` | 20 | 20 | 128x64 | Palette4Bit |
| 27 | `lambert28` | 24 | 24 | 128x128 | Palette4Bit |
| 28 | `lambert29` | 18 | 18 | 128x128 | Palette8Bit |
| 29 | `lambert3` | 34 | 34 | 128x128 | Palette4Bit |
| 30 | `lambert30` | 25 | 26 | 64x128 | Palette4Bit |
| 31 | `lambert31` | 17 | 16 | 64x64 | PaletteA5I3 Translucent |
| 32 | `lambert5` | 22 | 22 | 128x128 | Palette4Bit |
| 33 | `lambert7` | 15 | 15 | 64x128 | Palette4Bit |
| 34 | `lambert8` | 26 | 25 | 64x128 | Palette4Bit |
| 35 | `lambert9` | 30 | 30 | 128x128 | Palette4Bit |

#### `UNIT2_TP1`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT2_B1`

11 materials; 9 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BrickMat` | 2 | 2 | 128x128 | Palette4Bit |
| 1 | `Bumpy` | 5 | 5 | 128x128 | Palette4Bit |
| 2 | `broken1` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `bumpydoor` | 7 | 7 | 32x64 | Palette4Bit |
| 4 | `coil1` | 8 | 8 | 128x64 | Palette4Bit |
| 5 | `collision1` | -1 | -1 | no texture | Untextured |
| 6 | `danish` | 6 | 6 | 128x128 | Palette4Bit |
| 7 | `floor` | 4 | 4 | 128x128 | Palette4Bit |
| 8 | `lambert24` | -1 | -1 | no texture | Untextured |
| 9 | `shield` | 0 | 0 | 8x8 | PaletteA5I3 Translucent |
| 10 | `wall` | 1 | 1 | 128x128 | Palette4Bit |

#### `UNIT2_C6`

27 materials; 24 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `big_blue` | 0 | 1 | 64x64 | Palette4Bit |
| 1 | `big_blue_alpha` | 1 | 0 | 32x64 | PaletteA5I3 Decal |
| 2 | `green_scan` | 4 | 4 | 64x64 | PaletteA5I3 Translucent |
| 3 | `green_scan_broke` | 4 | 4 | 64x64 | PaletteA5I3 Translucent |
| 4 | `guts` | 2 | 2 | 32x128 | Palette4Bit |
| 5 | `guts_streak` | 3 | 3 | 32x128 | Palette4Bit |
| 6 | `lambert1` | -1 | -1 | no texture | Untextured |
| 7 | `lambert12` | 21 | 21 | 128x128 | Palette4Bit |
| 8 | `lambert17` | 9 | 9 | 128x128 | Palette4Bit |
| 9 | `lambert18` | 8 | 8 | 128x128 | Palette4Bit |
| 10 | `lambert19` | 17 | 17 | 128x16 | Palette4Bit |
| 11 | `lambert20` | 19 | 19 | 128x128 | Palette4Bit |
| 12 | `lambert23` | 11 | 12 | 64x128 | Palette4Bit |
| 13 | `lambert25` | 12 | 11 | 64x128 | PaletteA5I3 Decal |
| 14 | `lambert27` | 18 | 18 | 128x16 | Palette4Bit |
| 15 | `lambert3` | 16 | 16 | 128x128 | Palette8Bit |
| 16 | `lambert32` | 6 | 7 | 32x64 | Palette4Bit |
| 17 | `lambert33` | 7 | 6 | 32x64 | PaletteA5I3 Decal |
| 18 | `lambert38` | 10 | 10 | 128x32 | Palette4Bit |
| 19 | `lambert39` | 5 | 5 | 128x128 | Palette4Bit |
| 20 | `lambert40` | 13 | 14 | 64x64 | Palette4Bit |
| 21 | `lambert47` | 22 | 22 | 128x128 | Palette4Bit |
| 22 | `lambert6` | 20 | 20 | 128x128 | Palette4Bit |
| 23 | `lambert9` | 15 | 15 | 64x64 | Palette4Bit |
| 24 | `space_rotate_lambert3` | 23 | 23 | 256x256 | Palette4Bit |
| 25 | `space_rotate_lambert9` | 23 | 23 | 256x256 | Palette4Bit |
| 26 | `white_box` | 14 | 13 | 64x64 | PaletteA5I3 Decal |

#### `UNIT2_C7`

29 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `elec_space_block_swatches:big_blue` | 0 | 1 | 64x64 | Palette4Bit |
| 1 | `elec_space_block_swatches:big_blue_alpha` | 1 | 0 | 32x64 | PaletteA5I3 Decal |
| 2 | `elec_space_block_swatches:lambert12` | 22 | 22 | 128x128 | Palette4Bit |
| 3 | `elec_space_block_swatches:lambert17` | 8 | 8 | 128x128 | Palette4Bit |
| 4 | `elec_space_block_swatches:lambert19` | 16 | 16 | 128x16 | Palette4Bit |
| 5 | `elec_space_block_swatches:lambert20` | 20 | 20 | 128x128 | Palette4Bit |
| 6 | `elec_space_block_swatches:lambert21` | 17 | 17 | 128x32 | Palette4Bit |
| 7 | `elec_space_block_swatches:lambert22` | 24 | 24 | 64x128 | Palette4Bit |
| 8 | `elec_space_block_swatches:lambert23` | 10 | 11 | 64x128 | Palette4Bit |
| 9 | `elec_space_block_swatches:lambert25` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `elec_space_block_swatches:lambert27` | 18 | 18 | 128x16 | Palette4Bit |
| 11 | `elec_space_block_swatches:lambert3` | 15 | 15 | 128x128 | Palette8Bit |
| 12 | `elec_space_block_swatches:lambert30` | 14 | 14 | 64x128 | Palette4Bit |
| 13 | `elec_space_block_swatches:lambert32` | 5 | 6 | 32x64 | Palette4Bit |
| 14 | `elec_space_block_swatches:lambert33` | 6 | 5 | 32x64 | PaletteA5I3 Decal |
| 15 | `elec_space_block_swatches:lambert34` | 19 | 19 | 128x64 | Palette4Bit |
| 16 | `elec_space_block_swatches:lambert38` | 9 | 9 | 128x32 | Palette4Bit |
| 17 | `elec_space_block_swatches:lambert39` | 4 | 4 | 128x128 | Palette4Bit |
| 18 | `elec_space_block_swatches:lambert40` | 12 | 13 | 64x64 | Palette4Bit |
| 19 | `elec_space_block_swatches:lambert41` | 13 | 12 | 64x64 | PaletteA5I3 Decal |
| 20 | `elec_space_block_swatches:lambert47` | 23 | 23 | 128x128 | Palette4Bit |
| 21 | `file6Material` | 21 | 21 | 128x128 | Palette4Bit |
| 22 | `green_broken` | 3 | 3 | 64x64 | PaletteA5I3 |
| 23 | `green_scan` | 3 | 3 | 64x64 | PaletteA5I3 Translucent |
| 24 | `guts_stripe` | 2 | 2 | 32x128 | Palette4Bit |
| 25 | `lambert1` | -1 | -1 | no texture | Untextured |
| 26 | `pasted__lambert28` | 7 | 7 | 64x64 | PaletteA5I3 Translucent |
| 27 | `space_rotate:lambert3` | 25 | 25 | 256x256 | Palette4Bit |
| 28 | `space_rotate:lambert9` | 25 | 25 | 256x256 | Palette4Bit |

#### `UNIT2_RM4`

25 materials; 23 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert41` | 11 | 11 | 64x128 | Palette4Bit |
| 1 | `lambert46` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 2 | `lambert_animGlass` | 3 | 3 | 8x64 | PaletteA5I3 Translucent |
| 3 | `lambert_ballTube` | 4 | 4 | 32x64 | Palette4Bit |
| 4 | `lambert_blueBase` | 0 | 1 | 32x64 | Palette4Bit |
| 5 | `lambert_blueLight` | 2 | 2 | 8x64 | PaletteA5I3 Translucent |
| 6 | `lambert_blueLightglow` | 1 | 0 | 16x64 | PaletteA5I3 Decal |
| 7 | `lambert_bridgeLightBASE` | 8 | 9 | 64x128 | Palette4Bit |
| 8 | `lambert_bridgeWalk` | 12 | 12 | 32x64 | Palette4Bit |
| 9 | `lambert_floorBLOCKS` | 6 | 6 | 64x128 | Palette4Bit |
| 10 | `lambert_metalTrim` | 14 | 14 | 128x16 | Palette4Bit |
| 11 | `lambert_normalPortals` | 21 | 21 | 128x128 | Palette4Bit Translucent |
| 12 | `lambert_orangeLightsStill` | 9 | 8 | 64x128 | PaletteA5I3 Decal |
| 13 | `lambert_pMag` | 19 | 19 | 64x64 | Palette4Bit |
| 14 | `lambert_pmagdoorTrim` | 10 | 10 | 64x64 | Palette4Bit |
| 15 | `lambert_stars02` | 22 | 22 | 256x256 | Palette2Bit |
| 16 | `lambert_stars03` | 22 | 22 | 256x256 | Palette2Bit |
| 17 | `lambert_wall2INDENTS` | 18 | 18 | 128x128 | Palette4Bit |
| 18 | `lambert_wall3HEX` | 17 | 17 | 128x128 | Palette4Bit |
| 19 | `lambert_wallBlackblox` | 16 | 16 | 128x128 | Palette4Bit |
| 20 | `lambert_wallInset` | 7 | 7 | 64x32 | Palette4Bit |
| 21 | `lambert_walls` | 15 | 15 | 128x128 | Palette4Bit |
| 22 | `lambert_whiteBlock` | 13 | 13 | 64x64 | Palette4Bit |
| 23 | `pmag1` | 20 | 20 | 64x64 | Palette4Bit Decal |
| 24 | `pmag3` | 20 | 20 | 64x64 | Palette4Bit Unknown3 |

#### `UNIT2_RM5`

32 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP5:lambert9` | 7 | 8 | 64x8 | PaletteA5I3 Translucent |
| 1 | `lambert33` | 31 | 31 | 8x16 | PaletteA5I3 Translucent |
| 2 | `lambert34` | 29 | 29 | 128x64 | Palette4Bit |
| 3 | `lambert36` | 1 | 1 | 128x128 | Palette4Bit |
| 4 | `lambert37` | 10 | 9 | 16x32 | PaletteA5I3 |
| 5 | `lambert38` | 8 | 7 | 64x8 | PaletteA5I3 |
| 6 | `lambert4` | 20 | 20 | 128x32 | PaletteA5I3 |
| 7 | `lambert_ELECTRIC` | 6 | 6 | 64x64 | PaletteA5I3 Decal |
| 8 | `lambert_TUBEGREEN` | 5 | 5 | 64x64 | Palette4Bit |
| 9 | `lambert_Yshape` | 27 | 27 | 64x128 | Palette4Bit |
| 10 | `lambert_alimbicFloorplate` | 15 | 15 | 128x128 | Palette4Bit |
| 11 | `lambert_alimibcWall` | 0 | 0 | 64x128 | Palette4Bit |
| 12 | `lambert_ballTube` | 11 | 12 | 128x128 | Palette4Bit |
| 13 | `lambert_blackBoxes` | 26 | 26 | 128x128 | Palette4Bit |
| 14 | `lambert_blackStripe` | 12 | 11 | 128x64 | Palette4Bit |
| 15 | `lambert_blahGrey` | 4 | 4 | 128x128 | Palette4Bit |
| 16 | `lambert_blueBase` | 2 | 3 | 32x64 | Palette4Bit |
| 17 | `lambert_blueLight` | 3 | 2 | 16x64 | PaletteA5I3 Decal |
| 18 | `lambert_bridgeWalk` | 21 | 21 | 32x64 | Palette4Bit |
| 19 | `lambert_floorgrill` | 14 | 14 | 128x128 | Palette4Bit Translucent |
| 20 | `lambert_lightBase` | 17 | 18 | 64x128 | Palette4Bit |
| 21 | `lambert_lightsORANGE` | 18 | 17 | 64x128 | PaletteA5I3 Decal |
| 22 | `lambert_trim` | 23 | 23 | 128x16 | Palette4Bit |
| 23 | `lambert_tubelights` | 13 | 13 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert_vent` | 16 | 16 | 64x32 | Palette4Bit |
| 25 | `lambert_vent2` | 24 | 24 | 128x64 | Palette4Bit |
| 26 | `lambert_wallInset` | 28 | 28 | 32x128 | Palette4Bit |
| 27 | `lambert_walls` | 25 | 25 | 128x128 | Palette4Bit |
| 28 | `lambert_whiteBlocks` | 22 | 22 | 64x128 | Palette4Bit |
| 29 | `lambert_whiteLIGHTS` | 19 | 19 | 64x64 | PaletteA5I3 Decal |
| 30 | `pasted__lambert7` | 9 | 10 | 16x32 | PaletteA5I3 Translucent |
| 31 | `pasted__lambert_stars02` | 30 | 30 | 128x128 | Palette4Bit |

#### `UNIT2_RM6`

32 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP5:lambert9` | 7 | 8 | 64x8 | PaletteA5I3 Translucent |
| 1 | `lambert33` | 31 | 31 | 8x16 | PaletteA5I3 Translucent |
| 2 | `lambert34` | 29 | 29 | 128x64 | Palette4Bit |
| 3 | `lambert36` | 1 | 1 | 128x128 | Palette4Bit |
| 4 | `lambert37` | 10 | 9 | 16x32 | PaletteA5I3 |
| 5 | `lambert38` | 8 | 7 | 64x8 | PaletteA5I3 |
| 6 | `lambert4` | 20 | 20 | 128x32 | PaletteA5I3 |
| 7 | `lambert_ELECTRIC` | 6 | 6 | 64x64 | PaletteA5I3 Decal |
| 8 | `lambert_TUBEGREEN` | 5 | 5 | 64x64 | Palette4Bit |
| 9 | `lambert_Yshape` | 27 | 27 | 64x128 | Palette4Bit |
| 10 | `lambert_alimbicFloorplate` | 15 | 15 | 128x128 | Palette4Bit |
| 11 | `lambert_alimibcWall` | 0 | 0 | 64x128 | Palette4Bit |
| 12 | `lambert_ballTube` | 11 | 12 | 128x128 | Palette4Bit |
| 13 | `lambert_blackBoxes` | 26 | 26 | 128x128 | Palette4Bit |
| 14 | `lambert_blackStripe` | 12 | 11 | 128x64 | Palette4Bit |
| 15 | `lambert_blahGrey` | 4 | 4 | 128x128 | Palette4Bit |
| 16 | `lambert_blueBase` | 2 | 3 | 32x64 | Palette4Bit |
| 17 | `lambert_blueLight` | 3 | 2 | 16x64 | PaletteA5I3 Decal |
| 18 | `lambert_bridgeWalk` | 21 | 21 | 32x64 | Palette4Bit |
| 19 | `lambert_floorgrill` | 14 | 14 | 128x128 | Palette4Bit Translucent |
| 20 | `lambert_lightBase` | 17 | 18 | 64x128 | Palette4Bit |
| 21 | `lambert_lightsORANGE` | 18 | 17 | 64x128 | PaletteA5I3 Decal |
| 22 | `lambert_trim` | 23 | 23 | 128x16 | Palette4Bit |
| 23 | `lambert_tubelights` | 13 | 13 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert_vent` | 16 | 16 | 64x32 | Palette4Bit |
| 25 | `lambert_vent2` | 24 | 24 | 128x64 | Palette4Bit |
| 26 | `lambert_wallInset` | 28 | 28 | 32x128 | Palette4Bit |
| 27 | `lambert_walls` | 25 | 25 | 128x128 | Palette4Bit |
| 28 | `lambert_whiteBlocks` | 22 | 22 | 64x128 | Palette4Bit |
| 29 | `lambert_whiteLIGHTS` | 19 | 19 | 64x64 | PaletteA5I3 Decal |
| 30 | `pasted__lambert7` | 9 | 10 | 16x32 | PaletteA5I3 Translucent |
| 31 | `pasted__lambert_stars02` | 30 | 30 | 128x128 | Palette4Bit |

#### `UNIT2_RM7`

32 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP5:lambert9` | 7 | 8 | 64x8 | PaletteA5I3 Translucent |
| 1 | `lambert33` | 31 | 31 | 8x16 | PaletteA5I3 Translucent |
| 2 | `lambert34` | 29 | 29 | 128x64 | Palette4Bit |
| 3 | `lambert36` | 1 | 1 | 128x128 | Palette4Bit |
| 4 | `lambert37` | 10 | 9 | 16x32 | PaletteA5I3 |
| 5 | `lambert38` | 8 | 7 | 64x8 | PaletteA5I3 |
| 6 | `lambert4` | 20 | 20 | 128x32 | PaletteA5I3 |
| 7 | `lambert_ELECTRIC` | 6 | 6 | 64x64 | PaletteA5I3 Decal |
| 8 | `lambert_TUBEGREEN` | 5 | 5 | 64x64 | Palette4Bit |
| 9 | `lambert_Yshape` | 27 | 27 | 64x128 | Palette4Bit |
| 10 | `lambert_alimbicFloorplate` | 15 | 15 | 128x128 | Palette4Bit |
| 11 | `lambert_alimibcWall` | 0 | 0 | 64x128 | Palette4Bit |
| 12 | `lambert_ballTube` | 11 | 12 | 128x128 | Palette4Bit |
| 13 | `lambert_blackBoxes` | 26 | 26 | 128x128 | Palette4Bit |
| 14 | `lambert_blackStripe` | 12 | 11 | 128x64 | Palette4Bit |
| 15 | `lambert_blahGrey` | 4 | 4 | 128x128 | Palette4Bit |
| 16 | `lambert_blueBase` | 2 | 3 | 32x64 | Palette4Bit |
| 17 | `lambert_blueLight` | 3 | 2 | 16x64 | PaletteA5I3 Decal |
| 18 | `lambert_bridgeWalk` | 21 | 21 | 32x64 | Palette4Bit |
| 19 | `lambert_floorgrill` | 14 | 14 | 128x128 | Palette4Bit Translucent |
| 20 | `lambert_lightBase` | 17 | 18 | 64x128 | Palette4Bit |
| 21 | `lambert_lightsORANGE` | 18 | 17 | 64x128 | PaletteA5I3 Decal |
| 22 | `lambert_trim` | 23 | 23 | 128x16 | Palette4Bit |
| 23 | `lambert_tubelights` | 13 | 13 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert_vent` | 16 | 16 | 64x32 | Palette4Bit |
| 25 | `lambert_vent2` | 24 | 24 | 128x64 | Palette4Bit |
| 26 | `lambert_wallInset` | 28 | 28 | 32x128 | Palette4Bit |
| 27 | `lambert_walls` | 25 | 25 | 128x128 | Palette4Bit |
| 28 | `lambert_whiteBlocks` | 22 | 22 | 64x128 | Palette4Bit |
| 29 | `lambert_whiteLIGHTS` | 19 | 19 | 64x64 | PaletteA5I3 Decal |
| 30 | `pasted__lambert7` | 9 | 10 | 16x32 | PaletteA5I3 Translucent |
| 31 | `pasted__lambert_stars02` | 30 | 30 | 128x128 | Palette4Bit |

#### `UNIT2_RM8`

22 materials; 21 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert41` | 10 | 10 | 64x128 | Palette4Bit |
| 1 | `lambert_ballTube` | 4 | 4 | 32x64 | Palette4Bit |
| 2 | `lambert_bigLight` | 3 | 3 | 8x64 | PaletteA5I3 |
| 3 | `lambert_blueBase` | 0 | 1 | 32x64 | Palette4Bit |
| 4 | `lambert_blueLight` | 2 | 2 | 8x64 | PaletteA5I3 Translucent |
| 5 | `lambert_blueLightglow` | 1 | 0 | 16x64 | PaletteA5I3 Decal |
| 6 | `lambert_bridgeLightBASE` | 7 | 8 | 64x128 | Palette4Bit |
| 7 | `lambert_bridgeWalk` | 11 | 11 | 32x64 | Palette4Bit |
| 8 | `lambert_floorBLOCKS` | 5 | 5 | 64x128 | Palette4Bit |
| 9 | `lambert_metalTrim` | 13 | 13 | 128x16 | Palette4Bit |
| 10 | `lambert_orangeLightsStill` | 8 | 7 | 64x128 | PaletteA5I3 Decal |
| 11 | `lambert_pmagdoorTrim` | 9 | 9 | 64x64 | Palette4Bit |
| 12 | `lambert_shiled01Glow` | 19 | 19 | 8x64 | PaletteA5I3 Translucent |
| 13 | `lambert_stars02` | 20 | 20 | 256x256 | Palette2Bit |
| 14 | `lambert_stars03` | 20 | 20 | 256x256 | Palette2Bit |
| 15 | `lambert_vent` | 14 | 14 | 128x64 | Palette4Bit |
| 16 | `lambert_wall2INDENTS` | 18 | 18 | 128x128 | Palette4Bit |
| 17 | `lambert_wall3HEX` | 17 | 17 | 128x128 | Palette4Bit |
| 18 | `lambert_wallBlackblox` | 16 | 16 | 128x128 | Palette4Bit |
| 19 | `lambert_wallInset` | 6 | 6 | 64x32 | Palette4Bit |
| 20 | `lambert_walls` | 15 | 15 | 128x128 | Palette4Bit |
| 21 | `lambert_whiteBlock` | 12 | 12 | 64x64 | Palette4Bit |

#### `UNIT2_TP2`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT2_B2`

15 materials; 12 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BigEyeNest_BigEyeNestBase` | 0 | 0 | 64x64 | Palette4Bit |
| 1 | `BigEyeNest_BigEyeNestCore` | 1 | 1 | 32x32 | Palette4Bit |
| 2 | `BigEyeNest_BigEyeNestGlo0` | 2 | 2 | 8x32 | PaletteA5I3 Unknown3 |
| 3 | `BigEyeNest_BigEyeNestGlo1` | 2 | 2 | 8x32 | PaletteA5I3 Translucent |
| 4 | `BigEyeNest_BigEyeNestGlo2` | 2 | 2 | 8x32 | PaletteA5I3 Unknown4 |
| 5 | `BigEyeNest_BigEyeNestHole` | 3 | 3 | 32x32 | Palette4Bit |
| 6 | `BigEyeNest_BigEyeNestOpening` | 4 | 4 | 32x32 | Palette4Bit |
| 7 | `BigEyeNest_BigEyeNestVines` | 5 | 5 | 32x16 | Palette4Bit |
| 8 | `Marker` | -1 | -1 | no texture | Untextured |
| 9 | `lambert12` | 10 | 10 | 128x256 | PaletteA5I3 Translucent |
| 10 | `lambert13` | 9 | 9 | 32x64 | Palette4Bit |
| 11 | `lambert18` | 7 | 7 | 8x16 | PaletteA5I3 Translucent |
| 12 | `lambert19` | 6 | 6 | 128x128 | Palette8Bit |
| 13 | `lambert6` | 11 | 11 | 8x16 | PaletteA5I3 Translucent |
| 14 | `wall1` | 8 | 8 | 256x256 | Palette4Bit |

#### `UNIT3_LAND`

24 materials; 23 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A3:lambert11` | 8 | 8 | 128x128 | Palette4Bit |
| 1 | `A4:trim_gray_skinny` | 16 | 16 | 16x128 | Palette4Bit |
| 2 | `A4:trim_light` | 13 | 13 | 32x64 | Palette4Bit Translucent |
| 3 | `I_lambert35` | 20 | 21 | 64x64 | PaletteA5I3 Translucent |
| 4 | `Ice` | 7 | 7 | 128x128 | Palette4Bit Translucent |
| 5 | `lambert1` | -1 | -1 | no texture | Untextured |
| 6 | `lambert15` | 6 | 6 | 128x32 | Palette4Bit |
| 7 | `lambert20` | 0 | 0 | 128x128 | Palette4Bit |
| 8 | `lambert21` | 9 | 9 | 128x128 | Palette4Bit |
| 9 | `lambert23` | 12 | 12 | 128x128 | Palette4Bit |
| 10 | `lambert24` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 11 | `lambert6` | 1 | 1 | 64x128 | Palette4Bit |
| 12 | `lambert60` | 21 | 20 | 64x64 | PaletteA5I3 Translucent |
| 13 | `lights:lambert_iceChunks` | 10 | 10 | 128x128 | Palette4Bit |
| 14 | `shaders:light_add` | 15 | 14 | 64x64 | PaletteA5I3 Decal |
| 15 | `shaders:light_add1` | 18 | 18 | 32x128 | PaletteA5I3 Decal |
| 16 | `space` | 19 | 19 | 128x128 | Palette2Bit |
| 17 | `xport1:A4:wall_grid_base` | 22 | 22 | 256x256 | Palette4Bit |
| 18 | `xport2:lambert7` | 14 | 15 | 64x64 | Palette4Bit |
| 19 | `xport:A4:floor_roomc` | 4 | 4 | 128x128 | Palette4Bit |
| 20 | `xport:A5:wall_squares` | 2 | 2 | 128x128 | Palette4Bit |
| 21 | `xport:lambert12` | 11 | 11 | 128x128 | Palette4Bit |
| 22 | `xport:lambert14` | 3 | 3 | 64x64 | Palette4Bit |
| 23 | `xport:lambert16` | 17 | 17 | 64x128 | Palette4Bit |

#### `UNIT3_C0`

23 materials; 22 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A4:wall_scan` | 20 | 20 | 32x32 | PaletteA5I3 Translucent |
| 1 | `A5:black_wall` | 11 | 11 | 128x128 | Palette4Bit |
| 2 | `A5:panel_02` | 13 | 14 | 64x64 | Palette4Bit |
| 3 | `A6:blue_wall` | 19 | 19 | 256x256 | Palette4Bit |
| 4 | `lambert1` | -1 | -1 | no texture | Untextured |
| 5 | `lambert11` | 3 | 3 | 128x64 | Palette4Bit |
| 6 | `lambert12` | 17 | 17 | 64x64 | PaletteA5I3 Translucent |
| 7 | `lambert13` | 2 | 2 | 128x128 | Palette4Bit |
| 8 | `lambert14` | 15 | 15 | 128x128 | Palette4Bit |
| 9 | `lambert15` | 9 | 9 | 32x128 | Palette4Bit |
| 10 | `lambert16` | 21 | 21 | 128x128 | Palette4Bit |
| 11 | `lambert17` | 0 | 0 | 128x128 | Palette4Bit |
| 12 | `lambert18` | 7 | 7 | 128x128 | Palette4Bit |
| 13 | `lambert19` | 14 | 13 | 64x64 | PaletteA5I3 Decal |
| 14 | `lambert21` | 10 | 10 | 128x128 | Palette4Bit |
| 15 | `lambert22` | 1 | 1 | 64x128 | Palette4Bit |
| 16 | `lambert23` | 18 | 18 | 32x128 | PaletteA5I3 Decal |
| 17 | `lambert24` | 5 | 4 | 64x64 | PaletteA5I3 Decal |
| 18 | `lambert3` | 8 | 8 | 128x128 | PaletteA3I5 |
| 19 | `lambert4` | 12 | 12 | 32x64 | Palette4Bit |
| 20 | `lambert5` | 4 | 5 | 64x64 | Palette4Bit |
| 21 | `lambert6` | 6 | 6 | 128x128 | Palette4Bit |
| 22 | `lambert7` | 16 | 16 | 64x128 | Palette4Bit |

#### `UNIT3_C2`

36 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A13:A7:save01C` | 19 | 18 | 128x64 | Palette4Bit |
| 1 | `A13:lambert21` | 12 | 12 | 128x64 | Palette4Bit |
| 2 | `A13:lambert22` | 0 | 0 | 64x64 | Palette4Bit |
| 3 | `A15:black_wall` | 11 | 11 | 128x128 | Palette4Bit |
| 4 | `A15:panel_02` | 14 | 15 | 64x64 | Palette4Bit |
| 5 | `A20:trim_light` | 13 | 13 | 32x64 | Palette4Bit |
| 6 | `A22:A4:floor_blue_trim` | 5 | 6 | 128x128 | Palette4Bit |
| 7 | `I_lambert35` | 21 | 21 | 64x64 | PaletteA5I3 Translucent |
| 8 | `Unit3_morph_CZ1:Unit3_morph_CZ:GratingC_1Material` | 6 | 6 | 128x128 | Palette4Bit |
| 9 | `blueGrillLight` | 3 | 4 | 128x64 | Palette4Bit |
| 10 | `lambert1` | -1 | -1 | no texture | Untextured |
| 11 | `lambert24` | 4 | 5 | 64x64 | Palette4Bit |
| 12 | `lambert26` | 16 | 3 | 128x64 | PaletteA3I5 Decal |
| 13 | `lambert27` | 24 | 23 | 8x128 | PaletteA5I3 |
| 14 | `lambert5` | 8 | 8 | 128x128 | PaletteA5I3 Translucent |
| 15 | `lambert60` | 22 | 20 | 64x64 | PaletteA5I3 Translucent |
| 16 | `lambert_pMag` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `movingTrimlightINC` | 18 | 17 | 32x128 | PaletteA5I3 Decal |
| 18 | `moving_TRIM_lighting` | 1 | 1 | 64x128 | Palette4Bit |
| 19 | `newTUBE` | 24 | 23 | 8x128 | PaletteA5I3 Translucent |
| 20 | `pTRIM_lighting` | 1 | 1 | 64x128 | Palette4Bit |
| 21 | `pTRIM_lighting_alpha` | 18 | 17 | 32x128 | PaletteA5I3 Decal |
| 22 | `pasted__icechunks` | 7 | 7 | 64x128 | PaletteA5I3 Translucent |
| 23 | `plambert12` | 17 | 16 | 64x64 | PaletteA5I3 Translucent |
| 24 | `plambert13` | 2 | 2 | 128x128 | Palette4Bit |
| 25 | `plambert16` | 27 | 26 | 128x128 | Palette4Bit |
| 26 | `plambert27` | 25 | 24 | 8x128 | PaletteA5I3 Translucent |
| 27 | `plambert4` | 13 | 13 | 32x64 | Palette4Bit |
| 28 | `plambert51` | 28 | 27 | 128x128 | Palette4Bit |
| 29 | `plambert7` | 24 | 23 | 8x128 | PaletteA5I3 Translucent |
| 30 | `plambert_iceChunks` | 10 | 10 | 128x128 | Palette4Bit |
| 31 | `pwall_grid_base` | 23 | 22 | 256x256 | Palette4Bit |
| 32 | `pwall_scan45` | 26 | 25 | 32x32 | PaletteA5I3 Translucent |
| 33 | `space` | 20 | 19 | 128x128 | Palette2Bit |
| 34 | `tealPanel_Light` | 15 | 14 | 64x64 | PaletteA5I3 Decal |
| 35 | `use_to_light_tubes` | 18 | 17 | 32x128 | PaletteA5I3 Decal |

#### `UNIT3_RM1`

30 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `I_lambert35` | 24 | 25 | 64x64 | PaletteA5I3 Translucent |
| 1 | `black_wall` | 15 | 15 | 128x128 | Palette4Bit |
| 2 | `blackwall_streak` | 16 | 16 | 128x128 | Palette4Bit |
| 3 | `blue_streak` | 17 | 17 | 32x64 | Palette4Bit |
| 4 | `blue_wall` | 26 | 26 | 256x256 | Palette4Bit |
| 5 | `cans_bot` | 7 | 8 | 64x64 | Palette4Bit |
| 6 | `cans_bot_alpha` | 8 | 7 | 64x64 | PaletteA5I3 Decal |
| 7 | `file6Material` | 13 | 13 | 128x128 | Palette4Bit |
| 8 | `floor_blue_trim` | 9 | 9 | 128x128 | Palette4Bit |
| 9 | `floor_metroid` | 14 | 14 | 128x128 | Palette4Bit |
| 10 | `glass` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 11 | `lambert1` | -1 | -1 | no texture | Untextured |
| 12 | `lambert40` | 6 | 6 | 64x128 | Palette4Bit |
| 13 | `lambert41` | 19 | 19 | 64x64 | Palette4Bit |
| 14 | `lambert60` | 25 | 24 | 64x64 | PaletteA5I3 Translucent |
| 15 | `light_big_blue` | 1 | 2 | 32x64 | Palette4Bit |
| 16 | `light_big_blue_alpha` | 2 | 1 | 16x64 | PaletteA5I3 Decal |
| 17 | `lights_blue_2s` | 3 | 3 | 64x128 | Palette4Bit |
| 18 | `lights_blue_2s_alpha` | 21 | 21 | 32x128 | PaletteA5I3 Decal |
| 19 | `metal_boxes` | 4 | 4 | 128x128 | Palette4Bit |
| 20 | `orange_streak` | 28 | 28 | 128x128 | Palette4Bit |
| 21 | `panel_02` | 18 | 18 | 64x64 | Palette4Bit |
| 22 | `skinny_gray` | 5 | 5 | 128x16 | Palette4Bit |
| 23 | `snow2` | 22 | 22 | 128x128 | Palette4Bit |
| 24 | `space` | 23 | 23 | 128x128 | Palette2Bit |
| 25 | `vent` | 10 | 10 | 128x64 | Palette4Bit |
| 26 | `wall_circutbox` | 0 | 0 | 128x128 | Palette4Bit |
| 27 | `wall_facing` | 20 | 20 | 64x128 | Palette4Bit |
| 28 | `wall_microchip` | 11 | 11 | 32x128 | Palette4Bit |
| 29 | `wall_scan` | 27 | 27 | 32x32 | PaletteA5I3 Translucent |

#### `UNIT3_RM4`

29 materials; 27 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `I_lambert35` | 21 | 22 | 64x64 | PaletteA5I3 Translucent |
| 1 | `Ice_trans_infront` | 24 | 24 | 128x128 | PaletteA5I3 Unknown3 |
| 2 | `big_blue` | 1 | 2 | 32x64 | Palette4Bit |
| 3 | `bigblue_alpha` | 2 | 1 | 16x64 | PaletteA5I3 Decal |
| 4 | `blue_wall` | 23 | 23 | 256x256 | Palette4Bit |
| 5 | `dk__black_streak` | 11 | 11 | 128x128 | Palette4Bit |
| 6 | `dk_black` | 10 | 10 | 128x128 | Palette4Bit |
| 7 | `dk_black_light` | 12 | 12 | 64x128 | Palette4Bit |
| 8 | `ice_trans_more` | 24 | 24 | 128x128 | PaletteA5I3 Translucent |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert37` | 8 | 8 | 128x64 | Palette4Bit |
| 11 | `lambert38` | 15 | 14 | 64x64 | PaletteA5I3 Decal |
| 12 | `lambert60` | 22 | 21 | 64x64 | PaletteA5I3 Translucent |
| 13 | `lambert8` | 0 | 0 | 128x128 | Palette4Bit |
| 14 | `latch_2` | 5 | 5 | 128x128 | Palette4Bit |
| 15 | `light_2` | 3 | 3 | 64x128 | Palette4Bit |
| 16 | `light_2_alpha` | 18 | 18 | 32x128 | PaletteA5I3 Decal |
| 17 | `light_streak` | 13 | 13 | 32x64 | Palette4Bit |
| 18 | `light_streak1` | 17 | 17 | 64x64 | PaletteA5I3 Translucent |
| 19 | `metroid_wall` | 9 | 9 | 128x128 | Palette4Bit |
| 20 | `panel_3light` | 14 | 15 | 64x64 | Palette4Bit |
| 21 | `panel_4split` | 4 | 4 | 128x128 | Palette4Bit |
| 22 | `snow` | 19 | 19 | 128x128 | Palette4Bit |
| 23 | `space` | 20 | 20 | 128x128 | Palette2Bit |
| 24 | `tube` | 7 | 7 | 128x128 | Palette4Bit |
| 25 | `wall_blue01` | 6 | 6 | 128x128 | Palette4Bit |
| 26 | `wall_dkpanel` | 16 | 16 | 64x128 | Palette4Bit |
| 27 | `wall_orangeline` | 26 | 26 | 128x128 | Palette4Bit |
| 28 | `wall_scan` | 25 | 25 | 32x32 | PaletteA5I3 Translucent |

#### `UNIT3_TP1`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT3_B1`

11 materials; 9 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BrickMat` | 2 | 2 | 128x128 | Palette4Bit |
| 1 | `Bumpy` | 5 | 5 | 128x128 | Palette4Bit |
| 2 | `broken1` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `bumpydoor` | 7 | 7 | 32x64 | Palette4Bit |
| 4 | `coil1` | 8 | 8 | 128x64 | Palette4Bit |
| 5 | `collision1` | -1 | -1 | no texture | Untextured |
| 6 | `danish` | 6 | 6 | 128x128 | Palette4Bit |
| 7 | `floor` | 4 | 4 | 128x128 | Palette4Bit |
| 8 | `lambert24` | -1 | -1 | no texture | Untextured |
| 9 | `shield` | 0 | 0 | 8x8 | PaletteA5I3 Translucent |
| 10 | `wall` | 1 | 1 | 128x128 | Palette4Bit |

#### `UNIT3_C1`

24 materials; 22 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A3:lambert11` | 6 | 6 | 128x128 | Palette4Bit |
| 1 | `A5:A5:black_wall` | 8 | 8 | 128x128 | Palette4Bit |
| 2 | `A5:A5:panel_02` | 12 | 13 | 64x64 | Palette4Bit |
| 3 | `A6:A4:wall_grid_base` | 20 | 20 | 256x256 | Palette4Bit |
| 4 | `I_lambert35` | 18 | 19 | 64x64 | PaletteA5I3 Translucent |
| 5 | `lambert1` | -1 | -1 | no texture | Untextured |
| 6 | `lambert11` | 13 | 12 | 64x64 | PaletteA5I3 Decal |
| 7 | `lambert14` | 3 | 3 | 64x64 | PaletteA5I3 |
| 8 | `lambert16` | 10 | 9 | 32x64 | Palette4Bit |
| 9 | `lambert3` | 11 | 11 | 32x64 | Palette4Bit |
| 10 | `lambert4` | 2 | 2 | 128x128 | Palette4Bit |
| 11 | `lambert5` | 4 | 4 | 128x128 | Palette4Bit |
| 12 | `lambert6` | 14 | 14 | 128x128 | Palette4Bit |
| 13 | `lambert60` | 19 | 18 | 64x64 | PaletteA5I3 Translucent |
| 14 | `lambert7` | 0 | 0 | 128x128 | Palette4Bit |
| 15 | `lambert8` | 1 | 1 | 64x128 | Palette4Bit |
| 16 | `lambert9` | 16 | 16 | 32x128 | PaletteA5I3 Decal |
| 17 | `lights:lambert_iceChunks` | 7 | 7 | 128x128 | Palette4Bit |
| 18 | `pasted__glass` | 15 | 15 | 64x64 | PaletteA5I3 Translucent |
| 19 | `pasted__lambert5` | 9 | 10 | 128x128 | Palette4Bit |
| 20 | `pasted__lambert_ELECTRIC1` | 3 | 3 | 64x64 | PaletteA5I3 Translucent |
| 21 | `pasted__pasted__icechunks` | 5 | 5 | 64x128 | PaletteA5I3 Translucent |
| 22 | `pasted__plambert27` | 21 | 21 | 8x128 | PaletteA5I3 Translucent |
| 23 | `space` | 17 | 17 | 128x128 | Palette2Bit |

#### `UNIT3_RM2`

31 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `I_lambert35` | 22 | 23 | 64x64 | PaletteA5I3 Translucent |
| 1 | `I_lambert60` | 23 | 22 | 64x64 | PaletteA5I3 Translucent |
| 2 | `TRIM_lighting` | 19 | 20 | 32x128 | Palette4Bit |
| 3 | `TRIM_lighting_alpha` | 20 | 19 | 32x128 | PaletteA5I3 Decal |
| 4 | `bigWhiteGlow` | 6 | 6 | 32x64 | PaletteA5I3 Translucent |
| 5 | `floor_roomc` | 4 | 4 | 128x128 | Palette4Bit |
| 6 | `glass` | 18 | 18 | 64x64 | PaletteA5I3 Translucent |
| 7 | `ice` | 5 | 5 | 128x128 | Palette8Bit |
| 8 | `lambert1` | -1 | -1 | no texture | Untextured |
| 9 | `lambert51` | 28 | 28 | 128x128 | Palette4Bit |
| 10 | `lambert59` | 7 | 7 | 8x8 | Palette8Bit |
| 11 | `lambert60` | 14 | 14 | 64x64 | Palette4Bit Translucent |
| 12 | `lambert62` | 16 | 16 | 64x64 | Palette4Bit |
| 13 | `metal` | 0 | 0 | 128x128 | Palette4Bit |
| 14 | `panel_graynotch` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `panel_trim_light` | 12 | 12 | 32x64 | Palette4Bit |
| 16 | `panel_u_blue` | 17 | 17 | 64x128 | Palette4Bit |
| 17 | `pmag1` | 15 | 15 | 64x64 | Palette4Bit Decal |
| 18 | `pmag2` | 15 | 15 | 64x64 | Palette4Bit Unknown3 |
| 19 | `rock_gradient` | 3 | 3 | 128x128 | Palette4Bit |
| 20 | `snow_flurry` | 25 | 25 | 16x64 | PaletteA5I3 Translucent |
| 21 | `space` | 21 | 21 | 128x128 | Palette2Bit |
| 22 | `trim_gray_skinny` | 13 | 13 | 16x128 | Palette4Bit |
| 23 | `trim_light` | 11 | 11 | 32x64 | Palette4Bit |
| 24 | `wall_darklip` | 10 | 10 | 64x128 | Palette4Bit |
| 25 | `wall_grid_base` | 24 | 24 | 256x256 | Palette4Bit |
| 26 | `wall_metroid` | 9 | 9 | 128x128 | Palette4Bit |
| 27 | `wall_orangetrim` | 27 | 27 | 128x128 | Palette4Bit |
| 28 | `wall_scan` | 26 | 26 | 32x32 | PaletteA5I3 Translucent |
| 29 | `wall_squares` | 2 | 2 | 128x128 | Palette4Bit |
| 30 | `wall_tube` | 8 | 8 | 64x128 | Palette4Bit |

#### `UNIT3_RM3`

41 materials; 39 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:shield2` | 16 | 16 | 64x64 | Palette4Bit |
| 1 | `A:A4:hall_mesh` | 13 | 13 | 128x128 | Palette4Bit |
| 2 | `AlimbicDoorPanel_Material` | 0 | 0 | 64x128 | Palette4Bit |
| 3 | `_Intro_Elevator_blinn72` | 24 | 25 | 128x128 | Palette4Bit |
| 4 | `__FenceColl_lambert2` | 1 | 1 | 64x64 | PaletteA5I3 Translucent |
| 5 | `blinn163` | 5 | 5 | 32x32 | Palette8Bit |
| 6 | `blinn31` | 35 | 35 | 128x128 | Palette4Bit |
| 7 | `computer_1_1SG1` | 6 | 6 | 128x128 | Palette4Bit |
| 8 | `file261Material` | 25 | 24 | 32x64 | PaletteA5I3 Decal |
| 9 | `floortile_2_1SG1` | 11 | 11 | 128x128 | Palette4Bit |
| 10 | `lambert1` | -1 | -1 | no texture | Untextured |
| 11 | `lambert135` | 19 | 19 | 128x16 | Palette4Bit |
| 12 | `lambert140` | 9 | 9 | 128x128 | Palette4Bit |
| 13 | `lambert149` | 12 | 12 | 64x128 | Palette4Bit |
| 14 | `lambert152` | 21 | 21 | 128x128 | Palette4Bit |
| 15 | `lambert159` | 7 | 7 | 64x32 | Palette4Bit |
| 16 | `lambert160` | 8 | 8 | 32x64 | PaletteA5I3 Decal |
| 17 | `lambert161` | 18 | 18 | 64x64 | Palette4Bit |
| 18 | `lambert2` | 14 | 14 | 128x128 | Palette4Bit |
| 19 | `lambert39` | 20 | 20 | 128x64 | Palette4Bit |
| 20 | `lambert4` | 2 | 3 | 64x64 | Palette4Bit |
| 21 | `lambert41` | 15 | 15 | 32x64 | Palette4Bit |
| 22 | `lambert6` | 10 | 10 | 128x128 | Palette4Bit |
| 23 | `lambert86` | 36 | 36 | 16x32 | PaletteA5I3 Translucent |
| 24 | `lambert9` | 3 | 2 | 32x64 | PaletteA5I3 Decal |
| 25 | `leftul_field4` | 30 | 30 | 64x64 | PaletteA5I3 Translucent |
| 26 | `pasted__glass` | 37 | 37 | 128x128 | PaletteA5I3 Translucent |
| 27 | `pasted__pasted__plambert5` | 38 | 37 | 128x128 | PaletteA5I3 Translucent |
| 28 | `pmag1` | 17 | 17 | 64x64 | Palette4Bit Decal |
| 29 | `pmag2` | 17 | 17 | 64x64 | Palette4Bit Unknown3 |
| 30 | `spawnPad` | 22 | 23 | 128x128 | Palette4Bit |
| 31 | `spawnPadLight` | 23 | 22 | 64x64 | PaletteA5I3 Decal |
| 32 | `ul_beam1` | 29 | 29 | 16x128 | Palette4Bit |
| 33 | `ul_beam2` | 26 | 26 | 128x128 | Palette4Bit |
| 34 | `ul_beam3` | 34 | 34 | 16x128 | Palette4Bit |
| 35 | `ul_dome1` | 32 | 32 | 128x128 | Palette4Bit |
| 36 | `ul_field1` | 31 | 31 | 32x128 | Palette4Bit |
| 37 | `ul_light1` | 33 | 33 | 32x64 | Palette4Bit Translucent |
| 38 | `ul_wall1` | 4 | 4 | 128x128 | Palette4Bit |
| 39 | `ul_wall4` | 27 | 27 | 128x128 | Palette4Bit |
| 40 | `ul_wall5` | 28 | 28 | 128x128 | Palette4Bit |

#### `UNIT3_TP2`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT3_B2`

15 materials; 12 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BigEyeNest_BigEyeNestBase` | 0 | 0 | 64x64 | Palette4Bit |
| 1 | `BigEyeNest_BigEyeNestCore` | 1 | 1 | 32x32 | Palette4Bit |
| 2 | `BigEyeNest_BigEyeNestGlo0` | 2 | 2 | 8x32 | PaletteA5I3 Unknown3 |
| 3 | `BigEyeNest_BigEyeNestGlo1` | 2 | 2 | 8x32 | PaletteA5I3 Translucent |
| 4 | `BigEyeNest_BigEyeNestGlo2` | 2 | 2 | 8x32 | PaletteA5I3 Unknown4 |
| 5 | `BigEyeNest_BigEyeNestHole` | 3 | 3 | 32x32 | Palette4Bit |
| 6 | `BigEyeNest_BigEyeNestOpening` | 4 | 4 | 32x32 | Palette4Bit |
| 7 | `BigEyeNest_BigEyeNestVines` | 5 | 5 | 32x16 | Palette4Bit |
| 8 | `Marker` | -1 | -1 | no texture | Untextured |
| 9 | `lambert12` | 10 | 10 | 128x256 | PaletteA5I3 Translucent |
| 10 | `lambert13` | 9 | 9 | 32x64 | Palette4Bit |
| 11 | `lambert18` | 7 | 7 | 8x16 | PaletteA5I3 Translucent |
| 12 | `lambert19` | 6 | 6 | 128x128 | Palette8Bit |
| 13 | `lambert6` | 11 | 11 | 8x16 | PaletteA5I3 Translucent |
| 14 | `wall1` | 8 | 8 | 256x256 | Palette4Bit |

#### `UNIT4_LAND`

22 materials; 21 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A3:lambert11` | 14 | 14 | 64x64 | Palette4Bit |
| 1 | `A4:lambert9` | 18 | 18 | 16x64 | Palette4Bit |
| 2 | `Ice` | 3 | 3 | 128x128 | PaletteA5I3 |
| 3 | `IceEdge` | 2 | 2 | 128x128 | PaletteA5I3 |
| 4 | `Sky1` | 0 | 0 | 128x128 | Palette4Bit |
| 5 | `alimbicEye1` | 11 | 11 | 32x32 | Palette4Bit |
| 6 | `blueGlowStick` | 7 | 8 | 16x32 | Palette4Bit |
| 7 | `bluemetal` | 13 | 13 | 128x128 | Palette4Bit |
| 8 | `lambert1` | -1 | -1 | no texture | Untextured |
| 9 | `lambert11` | 20 | 20 | 128x128 | Palette4Bit |
| 10 | `lambert13` | 19 | 19 | 128x64 | Palette4Bit |
| 11 | `lambert15` | 10 | 10 | 128x128 | Palette4Bit |
| 12 | `lambert16` | 5 | 4 | 128x128 | Palette4Bit |
| 13 | `lambert17` | 4 | 5 | 128x128 | Palette4Bit |
| 14 | `lambert33` | 1 | 1 | 64x64 | PaletteA5I3 Decal |
| 15 | `lambert_alimbicHead` | 12 | 12 | 64x64 | Palette4Bit |
| 16 | `metaltrim` | 15 | 15 | 32x64 | Palette4Bit |
| 17 | `ramp` | 6 | 6 | 64x64 | Palette4Bit |
| 18 | `redGlowstick` | 8 | 7 | 16x32 | Palette4Bit |
| 19 | `redGlowy` | 17 | 17 | 16x32 | Palette4Bit |
| 20 | `snow` | 16 | 16 | 128x128 | Palette4Bit |
| 21 | `walls` | 9 | 9 | 128x128 | Palette4Bit |

#### `UNIT4_RM1`

34 materials; 30 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP1:lambert_alimbicEye1` | 2 | 2 | 32x32 | Palette4Bit |
| 1 | `lambert1` | -1 | -1 | no texture | Untextured |
| 2 | `lambert10` | 8 | 8 | 64x128 | Palette4Bit |
| 3 | `lambert11` | 9 | 9 | 64x64 | Palette4Bit |
| 4 | `lambert12` | 10 | 10 | 32x64 | Palette4Bit |
| 5 | `lambert14` | 3 | 3 | 64x128 | Palette4Bit |
| 6 | `lambert15` | 24 | 24 | 128x128 | Palette4Bit |
| 7 | `lambert16` | 14 | 14 | 16x64 | PaletteA5I3 Decal |
| 8 | `lambert18` | 6 | 6 | 128x128 | Palette4Bit Translucent |
| 9 | `lambert19` | 25 | 25 | 64x64 | Palette4Bit |
| 10 | `lambert21` | 21 | 21 | 64x32 | Palette4Bit |
| 11 | `lambert23` | 23 | 23 | 128x64 | Palette4Bit |
| 12 | `lambert24` | 19 | 19 | 16x64 | Palette4Bit |
| 13 | `lambert28` | 26 | 26 | 128x128 | Palette4Bit |
| 14 | `lambert29` | 15 | 15 | 16x64 | PaletteA5I3 Decal |
| 15 | `lambert3` | 5 | 5 | 256x256 | Palette4Bit |
| 16 | `lambert30` | 11 | 11 | 128x32 | Palette4Bit |
| 17 | `lambert32` | 12 | 12 | 32x32 | Palette4Bit |
| 18 | `lambert33` | 16 | 16 | 8x32 | Palette4Bit |
| 19 | `lambert35` | 27 | 27 | 128x128 | Palette4Bit |
| 20 | `lambert36` | 14 | 14 | 16x64 | PaletteA5I3 Translucent |
| 21 | `lambert37` | 6 | 6 | 128x128 | Palette4Bit |
| 22 | `lambert39` | 0 | 0 | 64x128 | Palette4Bit |
| 23 | `lambert4` | 17 | 17 | 128x64 | Palette4Bit |
| 24 | `lambert40` | 1 | 1 | 128x64 | Palette4Bit |
| 25 | `lambert41` | 20 | 20 | 16x64 | Palette4Bit |
| 26 | `lambert42` | 28 | 28 | 64x64 | Palette4Bit |
| 27 | `lambert43` | 29 | 29 | 64x64 | Palette4Bit Decal |
| 28 | `lambert44` | 29 | 29 | 64x64 | Palette4Bit Unknown3 |
| 29 | `lambert5` | 13 | 13 | 128x128 | Palette4Bit |
| 30 | `lambert6` | 22 | 22 | 128x128 | Palette4Bit |
| 31 | `lambert7` | 4 | 4 | 64x64 | PaletteA5I3 Translucent |
| 32 | `lambert8` | 7 | 7 | 64x128 | Palette4Bit |
| 33 | `lambert9` | 18 | 18 | 16x64 | Palette4Bit |

#### `UNIT4_RM3`

42 materials; 40 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:lambert12` | 36 | 36 | 32x64 | Palette4Bit |
| 1 | `IMP1:lambert_alimbicEye1` | 2 | 2 | 32x32 | Palette4Bit |
| 2 | `IMP1:lambert_alimbicHead` | 3 | 3 | 64x64 | Palette4Bit |
| 3 | `lambert1` | -1 | -1 | no texture | Untextured |
| 4 | `lambert30` | 31 | 31 | 128x128 | Palette4Bit |
| 5 | `lambert52` | 1 | 1 | 64x64 | PaletteA5I3 Decal |
| 6 | `lambert53` | 0 | 0 | 64x64 | Palette4Bit |
| 7 | `lambert_ICE` | 10 | 10 | 64x64 | Palette4Bit |
| 8 | `lambert_ceiling` | 5 | 5 | 64x64 | Palette4Bit |
| 9 | `lambert_glowyLight` | 4 | 4 | 16x16 | PaletteA5I3 Translucent |
| 10 | `lambert_glyphTrim` | 7 | 7 | 128x32 | Palette4Bit |
| 11 | `lambert_greySnow` | 27 | 27 | 128x128 | Palette4Bit |
| 12 | `lambert_groundOutside` | 8 | 8 | 128x128 | Palette4Bit |
| 13 | `lambert_icyWalls` | 14 | 14 | 128x128 | Palette4Bit |
| 14 | `lambert_metalFloor` | 6 | 6 | 64x64 | Palette4Bit |
| 15 | `lambert_minilight2` | 17 | 16 | 16x32 | Palette4Bit |
| 16 | `lambert_newIceStones` | 11 | 13 | 128x128 | Palette4Bit |
| 17 | `lambert_newIceStonesM` | 13 | 12 | 128x128 | Palette4Bit |
| 18 | `lambert_orangeLight` | 18 | 19 | 64x64 | Palette4Bit |
| 19 | `lambert_orangeLightGlow` | 19 | 18 | 32x32 | PaletteA5I3 Decal |
| 20 | `lambert_pipes` | 21 | 21 | 32x256 | Palette4Bit |
| 21 | `lambert_rampsIcy` | 22 | 22 | 64x64 | Palette4Bit |
| 22 | `lambert_rockIcetransition` | 12 | 11 | 128x128 | Palette4Bit |
| 23 | `lambert_ruinTrim` | 25 | 25 | 128x32 | Palette4Bit |
| 24 | `lambert_sky` | 39 | 39 | 128x128 | Palette4Bit |
| 25 | `lambert_snowCol` | 28 | 28 | 128x128 | Palette4Bit |
| 26 | `lambert_tanWall` | 23 | 23 | 128x128 | Palette4Bit |
| 27 | `lambert_trim` | 34 | 34 | 64x64 | Palette4Bit |
| 28 | `lambert_trimmedWalls` | 26 | 26 | 128x128 | Palette4Bit |
| 29 | `lambert_wallBottom` | 35 | 35 | 128x128 | Palette4Bit |
| 30 | `lambert_walls` | 24 | 24 | 128x128 | Palette4Bit |
| 31 | `lightsEXP03:squareLight:blinn87` | 15 | 15 | 16x16 | Palette4Bit |
| 32 | `little:cylinderLight:lightparts_lambert17` | 16 | 17 | 16x32 | Palette4Bit |
| 33 | `p__blinn79` | 20 | 20 | 64x64 | Palette4Bit |
| 34 | `pasted__lambert18` | 9 | 9 | 64x64 | Palette4Bit |
| 35 | `pasted__lambert24` | 33 | 33 | 16x64 | Palette4Bit |
| 36 | `pasted__lambert9` | 32 | 32 | 16x64 | Palette4Bit |
| 37 | `pasted__lambert_pMag` | 37 | 37 | 64x64 | Palette4Bit |
| 38 | `pasted__lambert_snowBox01` | 29 | 30 | 128x128 | Palette4Bit |
| 39 | `pasted__lambert_snowBox02` | 30 | 29 | 128x128 | Palette4Bit |
| 40 | `pmag1` | 38 | 38 | 64x64 | Palette4Bit Decal |
| 41 | `pmag2` | 38 | 38 | 64x64 | Palette4Bit Unknown3 |

#### `UNIT4_C0`

27 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A16:lambert10` | 16 | 16 | 128x128 | Palette4Bit |
| 1 | `A16:lambert12` | 17 | 17 | 32x64 | Palette4Bit |
| 2 | `A20:IMP1:lambert_alimbicEye1` | 14 | 14 | 32x32 | Palette4Bit |
| 3 | `A20:lambert24` | 20 | 20 | 16x64 | Palette4Bit |
| 4 | `A20:lambert9` | 19 | 19 | 16x64 | Palette4Bit |
| 5 | `A6:A7:save01C` | 24 | 24 | 128x64 | Palette4Bit |
| 6 | `A6:lambert19` | 1 | 2 | 128x64 | Palette4Bit |
| 7 | `A6:lambert20` | 2 | 1 | 128x64 | PaletteA5I3 Decal |
| 8 | `A6:lambert21` | 23 | 23 | 128x64 | Palette4Bit |
| 9 | `A6:lambert22` | 0 | 0 | 64x64 | Palette4Bit |
| 10 | `GlowStickBlue` | 13 | 13 | 16x32 | Palette4Bit |
| 11 | `GlowStickRed` | 5 | 5 | 16x32 | Palette4Bit |
| 12 | `Ground` | 7 | 7 | 128x128 | Palette8Bit |
| 13 | `Ice` | 9 | 9 | 128x128 | Palette4Bit |
| 14 | `IceTrans` | 8 | 8 | 128x128 | PaletteA5I3 Translucent |
| 15 | `Sky` | 25 | 25 | 128x128 | Palette4Bit |
| 16 | `Snow` | 18 | 18 | 128x128 | Palette4Bit |
| 17 | `lambert1` | -1 | -1 | no texture | Untextured |
| 18 | `lambert27` | 12 | 12 | 64x64 | Palette4Bit |
| 19 | `lambert32` | 6 | 6 | 64x64 | PaletteA5I3 Decal |
| 20 | `lambert_ceilingTiles` | 21 | 21 | 128x64 | Palette4Bit |
| 21 | `lambert_iceCrystals` | 15 | 15 | 128x128 | Palette4Bit |
| 22 | `lambert_iceEdge` | 3 | 3 | 128x128 | PaletteA5I3 Translucent |
| 23 | `lambert_pillarDecor` | 10 | 10 | 128x64 | Palette4Bit |
| 24 | `lambert_pillars` | 11 | 11 | 128x128 | Palette4Bit |
| 25 | `lambert_snowyRock` | 4 | 4 | 128x128 | Palette4Bit |
| 26 | `lambert_walls` | 22 | 22 | 128x128 | Palette4Bit |

#### `UNIT4_TP1`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT4_B1`

15 materials; 12 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BigEyeNest_BigEyeNestBase` | 0 | 0 | 64x64 | Palette4Bit |
| 1 | `BigEyeNest_BigEyeNestCore` | 1 | 1 | 32x32 | Palette4Bit |
| 2 | `BigEyeNest_BigEyeNestGlo0` | 2 | 2 | 8x32 | PaletteA5I3 Unknown3 |
| 3 | `BigEyeNest_BigEyeNestGlo1` | 2 | 2 | 8x32 | PaletteA5I3 Translucent |
| 4 | `BigEyeNest_BigEyeNestGlo2` | 2 | 2 | 8x32 | PaletteA5I3 Unknown4 |
| 5 | `BigEyeNest_BigEyeNestHole` | 3 | 3 | 32x32 | Palette4Bit |
| 6 | `BigEyeNest_BigEyeNestOpening` | 4 | 4 | 32x32 | Palette4Bit |
| 7 | `BigEyeNest_BigEyeNestVines` | 5 | 5 | 32x16 | Palette4Bit |
| 8 | `Marker` | -1 | -1 | no texture | Untextured |
| 9 | `lambert12` | 10 | 10 | 128x256 | PaletteA5I3 Translucent |
| 10 | `lambert13` | 9 | 9 | 32x64 | Palette4Bit |
| 11 | `lambert18` | 7 | 7 | 8x16 | PaletteA5I3 Translucent |
| 12 | `lambert19` | 6 | 6 | 128x128 | Palette8Bit |
| 13 | `lambert6` | 11 | 11 | 8x16 | PaletteA5I3 Translucent |
| 14 | `wall1` | 8 | 8 | 256x256 | Palette4Bit |

#### `UNIT4_C1`

19 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP1:lambert_alimbicEye1` | 4 | 4 | 32x32 | Palette4Bit |
| 1 | `IMP4:lambert_alimbicHead` | 5 | 5 | 64x64 | Palette4Bit |
| 2 | `lambert1` | -1 | -1 | no texture | Decal |
| 3 | `lambert10` | 7 | 7 | 128x128 | Palette4Bit |
| 4 | `lambert11` | 8 | 8 | 64x64 | Palette4Bit |
| 5 | `lambert12` | 9 | 9 | 32x64 | Palette4Bit |
| 6 | `lambert24` | 13 | 13 | 16x64 | Palette4Bit |
| 7 | `lambert25` | 11 | 11 | 16x32 | Palette4Bit |
| 8 | `lambert26` | 16 | 16 | 128x64 | Palette4Bit |
| 9 | `lambert27` | 14 | 14 | 64x32 | Palette4Bit |
| 10 | `lambert28` | 17 | 17 | 128x128 | Palette4Bit |
| 11 | `lambert30` | 15 | 15 | 128x128 | Palette4Bit |
| 12 | `lambert31` | 2 | 2 | 128x128 | Palette4Bit |
| 13 | `lambert32` | 6 | 6 | 256x256 | Palette4Bit |
| 14 | `lambert33` | 10 | 10 | 16x64 | PaletteA5I3 |
| 15 | `lambert34` | 0 | 1 | 128x128 | Palette4Bit |
| 16 | `lambert35` | 1 | 0 | 128x128 | PaletteA5I3 Decal |
| 17 | `lambert9` | 12 | 12 | 16x64 | Palette4Bit |
| 18 | `lambert_pillars` | 3 | 3 | 128x128 | Palette4Bit |

#### `UNIT4_RM2`

28 materials; 27 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert10` | 22 | 22 | 64x64 | Palette4Bit |
| 1 | `lambert11` | 23 | 23 | 32x64 | Palette4Bit |
| 2 | `lambert12` | 24 | 24 | 32x64 | Palette4Bit |
| 3 | `lambert36` | 21 | 21 | 64x64 | PaletteA5I3 Decal |
| 4 | `lambert37` | 16 | 16 | 64x64 | Palette4Bit |
| 5 | `lambert_Ice` | 11 | 11 | 64x128 | Palette4Bit |
| 6 | `lambert_Sky` | 17 | 17 | 128x128 | Palette4Bit |
| 7 | `lambert_Snow` | 18 | 18 | 128x128 | Palette4Bit |
| 8 | `lambert_alimbicEye` | 0 | 0 | 32x32 | Palette4Bit |
| 9 | `lambert_bigWalls` | 5 | 5 | 128x128 | Palette4Bit |
| 10 | `lambert_blueLights` | 8 | 8 | 8x64 | PaletteA5I3 Decal |
| 11 | `lambert_bridgeRock` | 1 | 1 | 128x128 | Palette4Bit |
| 12 | `lambert_brokenRocks` | 2 | 2 | 128x128 | Palette4Bit |
| 13 | `lambert_brownTile` | 3 | 3 | 64x64 | Palette4Bit |
| 14 | `lambert_ceilingTile` | 4 | 4 | 64x64 | Palette4Bit |
| 15 | `lambert_crackedWall` | 6 | 6 | 128x128 | Palette4Bit |
| 16 | `lambert_darkMetal` | 7 | 7 | 64x64 | Palette4Bit |
| 17 | `lambert_iceRiver` | 12 | 12 | 128x64 | PaletteA5I3 Translucent |
| 18 | `lambert_icyGround` | 10 | 10 | 128x128 | Palette4Bit |
| 19 | `lambert_icyTrim` | 13 | 13 | 128x64 | Palette4Bit |
| 20 | `lambert_pMag` | 25 | 25 | 64x64 | Palette4Bit |
| 21 | `lambert_pillars` | 15 | 15 | 64x128 | Palette4Bit |
| 22 | `lambert_trimDeco` | 14 | 14 | 64x64 | Palette4Bit |
| 23 | `lambert_tunnelTrim` | 19 | 19 | 128x64 | Palette4Bit |
| 24 | `lambert_wall` | 20 | 20 | 64x128 | Palette4Bit |
| 25 | `lambert_yellowGlow` | 9 | 9 | 16x16 | PaletteA5I3 Translucent |
| 26 | `pmag1` | 26 | 26 | 64x64 | Palette4Bit Decal |
| 27 | `pmag2` | 26 | 26 | 64x64 | Palette4Bit Unknown3 |

#### `UNIT4_RM4`

28 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert1` | -1 | -1 | no texture | Untextured |
| 1 | `lambert7` | 5 | 5 | 16x64 | PaletteA5I3 Decal |
| 2 | `lambert_alimbicEye1` | 0 | 0 | 32x32 | Palette4Bit |
| 3 | `lambert_alimbicHead` | 1 | 1 | 64x64 | Palette4Bit |
| 4 | `lambert_brownMetal` | 22 | 22 | 16x64 | Palette4Bit |
| 5 | `lambert_ceilingTile` | 3 | 3 | 128x128 | Palette4Bit |
| 6 | `lambert_columnBase` | 16 | 16 | 64x64 | Palette4Bit |
| 7 | `lambert_columnWalls` | 4 | 4 | 128x128 | Palette4Bit |
| 8 | `lambert_darkMetal` | 14 | 14 | 128x128 | Palette4Bit |
| 9 | `lambert_glowGradient` | 6 | 6 | 16x16 | PaletteA5I3 |
| 10 | `lambert_greyTiles` | 2 | 2 | 128x128 | Palette4Bit |
| 11 | `lambert_groundMat` | 7 | 7 | 128x128 | Palette4Bit |
| 12 | `lambert_iceEdge` | 8 | 8 | 128x128 | PaletteA5I3 |
| 13 | `lambert_iceLake` | 9 | 9 | 128x128 | PaletteA5I3 |
| 14 | `lambert_iceOpaque` | 10 | 10 | 128x128 | Palette4Bit |
| 15 | `lambert_iceRockwalls` | 11 | 12 | 128x128 | Palette4Bit |
| 16 | `lambert_icyRamp` | 19 | 19 | 64x64 | Palette4Bit |
| 17 | `lambert_icyWalls` | 12 | 11 | 128x128 | Palette4Bit |
| 18 | `lambert_lakeBottom` | 13 | 13 | 128x128 | Palette4Bit |
| 19 | `lambert_miniLight` | 15 | 15 | 16x32 | Palette4Bit |
| 20 | `lambert_pillarBase` | 17 | 17 | 128x128 | Palette4Bit |
| 21 | `lambert_pillarTop` | 18 | 18 | 64x64 | Palette4Bit |
| 22 | `lambert_snow` | 20 | 20 | 128x128 | Palette4Bit |
| 23 | `lambert_stone` | 21 | 21 | 64x64 | Palette4Bit |
| 24 | `lambert_tunnelBlocks` | 24 | 24 | 128x64 | Palette4Bit |
| 25 | `lambert_tunnelBot` | 25 | 25 | 128x128 | Palette4Bit |
| 26 | `lambert_tunnelCeiling` | 23 | 23 | 128x128 | Palette4Bit |
| 27 | `lambert_whiteMetal` | 23 | 23 | 128x128 | Palette4Bit |

#### `UNIT4_RM5`

33 materials; 31 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert10` | 25 | 25 | 64x64 | Palette4Bit |
| 1 | `lambert11` | 26 | 26 | 32x64 | Palette4Bit |
| 2 | `lambert12` | 27 | 27 | 32x64 | Palette4Bit |
| 3 | `lambert32` | 12 | 13 | 128x128 | Palette4Bit |
| 4 | `lambert33` | 13 | 12 | 128x128 | Palette4Bit |
| 5 | `lambert35` | 30 | 30 | 32x128 | Palette4Bit |
| 6 | `lambert36` | 24 | 24 | 64x64 | PaletteA5I3 Decal |
| 7 | `lambert37` | 18 | 18 | 64x64 | Palette4Bit |
| 8 | `lambert_Ice` | 10 | 10 | 64x128 | Palette4Bit |
| 9 | `lambert_Ice1` | 10 | 10 | 64x128 | Palette4Bit |
| 10 | `lambert_Sky` | 20 | 20 | 128x128 | Palette4Bit |
| 11 | `lambert_Snow` | 21 | 21 | 128x128 | Palette4Bit |
| 12 | `lambert_bigWalls` | 5 | 5 | 128x128 | Palette4Bit |
| 13 | `lambert_bridgeRock` | 0 | 0 | 128x128 | Palette4Bit |
| 14 | `lambert_brokenRocks` | 1 | 2 | 128x128 | Palette4Bit |
| 15 | `lambert_brokenRocksIce` | 2 | 1 | 128x128 | Palette4Bit |
| 16 | `lambert_brownTile` | 3 | 3 | 64x64 | Palette4Bit |
| 17 | `lambert_ceilingTile` | 4 | 4 | 64x64 | Palette4Bit |
| 18 | `lambert_chasmRocks` | 19 | 19 | 64x64 | Palette4Bit |
| 19 | `lambert_crackedWall` | 6 | 6 | 128x128 | Palette4Bit |
| 20 | `lambert_darkMetal` | 7 | 7 | 64x64 | Palette4Bit |
| 21 | `lambert_iceRiver` | 11 | 11 | 128x64 | PaletteA5I3 Translucent |
| 22 | `lambert_iceWalls` | 14 | 14 | 128x128 | Palette4Bit |
| 23 | `lambert_icyGround` | 9 | 9 | 128x128 | Palette4Bit |
| 24 | `lambert_icyTrim` | 15 | 15 | 128x64 | Palette4Bit |
| 25 | `lambert_minilight` | 16 | 16 | 16x32 | Palette4Bit |
| 26 | `lambert_pMag` | 28 | 28 | 64x64 | Palette4Bit |
| 27 | `lambert_pillars` | 17 | 17 | 64x128 | Palette4Bit |
| 28 | `lambert_tunnelTrim` | 22 | 22 | 128x64 | Palette4Bit |
| 29 | `lambert_wall` | 23 | 23 | 64x128 | Palette4Bit |
| 30 | `lambert_yellowGlow` | 8 | 8 | 16x16 | PaletteA5I3 Translucent |
| 31 | `pmag1` | 29 | 29 | 64x64 | Palette4Bit Decal |
| 32 | `pmag2` | 29 | 29 | 64x64 | Palette4Bit Unknown3 |

#### `UNIT4_TP2`

25 materials; 18 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `TeleportRoom:Bumpy` | 5 | 5 | 256x256 | Palette2Bit |
| 1 | `TeleportRoom:bumpydoor` | 3 | 3 | 32x64 | Palette4Bit |
| 2 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert11` | 15 | 15 | 128x128 | Palette4Bit |
| 4 | `TeleportRoom:gorea_land_stuff:GoreaLand_killer:lambert12` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `TeleportRoom:gorea_land_stuff:lambert11` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `TeleportRoom:gorea_land_stuff:lambert5` | 10 | 11 | 64x128 | Palette4Bit |
| 7 | `TeleportRoom:gorea_land_stuff:lambert6` | 7 | 7 | 64x128 | Palette4Bit |
| 8 | `TeleportRoom:gorea_land_stuff:lambert9` | 8 | 8 | 128x128 | Palette4Bit |
| 9 | `TeleportRoom:lambert35` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 10 | `art_rotate` | 9 | 9 | 128x128 | Palette4Bit |
| 11 | `art_rotate1` | 14 | 14 | 128x128 | Palette4Bit |
| 12 | `art_rotate2` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `fire_pit` | 4 | 4 | 128x128 | Palette4Bit |
| 14 | `gorea_land_stuff:lambert10` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `lambert1` | -1 | -1 | no texture | Untextured |
| 16 | `lambert38` | 9 | 9 | 128x128 | Palette4Bit |
| 17 | `lambert41` | 13 | 13 | 16x32 | Palette4Bit |
| 18 | `lava_bridge:floor_00` | 6 | 6 | 128x128 | Palette4Bit |
| 19 | `mp9ice` | 12 | 12 | 128x128 | Palette4Bit |
| 20 | `mp9ice_bridge_space` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 21 | `mp9ice_trans` | 12 | 12 | 128x128 | Palette4Bit Translucent |
| 22 | `roof` | 16 | 16 | 128x128 | Palette4Bit Unknown3 |
| 23 | `roof1` | 16 | 16 | 128x128 | Palette4Bit |
| 24 | `window_glow` | 17 | 17 | 64x16 | PaletteA5I3 Translucent |

#### `UNIT4_B2`

11 materials; 9 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `BrickMat` | 2 | 2 | 128x128 | Palette4Bit |
| 1 | `Bumpy` | 5 | 5 | 128x128 | Palette4Bit |
| 2 | `broken1` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `bumpydoor` | 7 | 7 | 32x64 | Palette4Bit |
| 4 | `coil1` | 8 | 8 | 128x64 | Palette4Bit |
| 5 | `collision1` | -1 | -1 | no texture | Untextured |
| 6 | `danish` | 6 | 6 | 128x128 | Palette4Bit |
| 7 | `floor` | 4 | 4 | 128x128 | Palette4Bit |
| 8 | `lambert24` | -1 | -1 | no texture | Untextured |
| 9 | `shield` | 0 | 0 | 8x8 | PaletteA5I3 Translucent |
| 10 | `wall` | 1 | 1 | 128x128 | Palette4Bit |

#### `Gorea_Land`

25 materials; 19 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `GoreaLand_killer:Floor` | 0 | 0 | 128x128 | Palette4Bit |
| 1 | `GoreaLand_killer:GoreaSymbol` | 3 | 3 | 128x128 | Palette4Bit |
| 2 | `GoreaLand_killer:RootWall` | 15 | 15 | 128x128 | Palette4Bit |
| 3 | `GoreaLand_killer:ballglowconvert_1Material` | 18 | 18 | 32x32 | Palette4Bit |
| 4 | `GoreaLand_killer:lambert10` | 6 | 6 | 256x256 | Palette2Bit |
| 5 | `GoreaLand_killer:lambert11` | 12 | 13 | 128x128 | Palette4Bit |
| 6 | `GoreaLand_killer:lambert12` | 11 | 11 | 128x128 | Palette4Bit |
| 7 | `art_glow` | 5 | 5 | 64x16 | PaletteA5I3 Translucent |
| 8 | `gorea_black` | -1 | -1 | no texture | Untextured |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert10` | 2 | 2 | 128x128 | Palette4Bit |
| 11 | `lambert11` | 4 | 4 | 128x128 | Palette4Bit |
| 12 | `lambert12` | 14 | 14 | 64x64 | Palette4Bit |
| 13 | `lambert13` | 11 | 11 | 128x128 | Palette4Bit Translucent |
| 14 | `lambert14` | 17 | 17 | 128x32 | Palette4Bit |
| 15 | `lambert16` | 15 | 15 | 128x128 | Palette4Bit Translucent |
| 16 | `lambert3` | 16 | 16 | 64x16 | PaletteA5I3 Unknown3 |
| 17 | `lambert4` | 10 | 10 | 64x128 | Palette4Bit |
| 18 | `lambert5` | 8 | 9 | 64x128 | Palette4Bit |
| 19 | `lambert6` | 1 | 1 | 128x128 | Palette4Bit |
| 20 | `lambert8` | 13 | 12 | 128x256 | Palette4Bit Translucent |
| 21 | `lambert9` | 7 | 7 | 128x128 | Palette4Bit |
| 22 | `landingarea` | 15 | 15 | 128x128 | Palette4Bit Decal |
| 23 | `lights1` | 9 | 8 | 64x128 | PaletteA5I3 Decal |
| 24 | `sphere_noanim` | 3 | 3 | 128x128 | Palette4Bit |

#### `Gorea_Peek`

13 materials; 13 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `Gorea_b2:col:Gorea_b2:Gorea_b2:blinn36` | 8 | 8 | 64x256 | PaletteA5I3 |
| 1 | `Gorea_b2:col:Gorea_b2:Gorea_b2:root1:blinn28` | 6 | 6 | 32x32 | Palette4Bit |
| 2 | `blinn1` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `floor1` | 4 | 4 | 256x256 | Palette2Bit |
| 4 | `globe` | 11 | 11 | 128x128 | Palette4Bit |
| 5 | `glow2` | 7 | 7 | 128x128 | Palette4Bit |
| 6 | `gravl` | 1 | 1 | 128x128 | Palette4Bit |
| 7 | `lambert10` | 9 | 9 | 256x256 | Palette4Bit |
| 8 | `lambert11` | 10 | 10 | 256x256 | Palette4Bit |
| 9 | `root1` | 2 | 2 | 256x256 | Palette4Bit |
| 10 | `rootgg` | 3 | 3 | 256x256 | Palette4Bit |
| 11 | `trim` | 5 | 5 | 64x128 | Palette4Bit |
| 12 | `window` | 12 | 12 | 64x16 | PaletteA5I3 |

#### `Gorea_b1`

6 materials; 5 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert14` | 2 | 2 | 128x128 | Palette8Bit |
| 1 | `lambert16` | 0 | 1 | 8x32 | PaletteA5I3 Translucent |
| 2 | `lambert17` | 3 | 3 | 128x128 | Palette4Bit |
| 3 | `lambert5` | 1 | 0 | 512x512 | Palette4Bit |
| 4 | `lambert9` | 0 | 1 | 8x32 | PaletteA5I3 Translucent |
| 5 | `x_lambert14` | 4 | 4 | 256x256 | Palette4Bit |

#### `Gorea_b2`

13 materials; 13 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `Gorea_b2:col:Gorea_b2:Gorea_b2:blinn36` | 8 | 8 | 64x256 | PaletteA5I3 |
| 1 | `Gorea_b2:col:Gorea_b2:Gorea_b2:root1:blinn28` | 6 | 6 | 32x32 | Palette4Bit |
| 2 | `blinn1` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `floor1` | 4 | 4 | 256x256 | Palette2Bit |
| 4 | `globe` | 11 | 11 | 128x128 | Palette4Bit |
| 5 | `glow2` | 7 | 7 | 128x128 | Palette4Bit |
| 6 | `gravl` | 1 | 1 | 128x128 | Palette4Bit |
| 7 | `lambert10` | 9 | 9 | 256x256 | Palette4Bit |
| 8 | `lambert11` | 10 | 10 | 256x256 | Palette4Bit |
| 9 | `root1` | 2 | 2 | 256x256 | Palette4Bit |
| 10 | `rootgg` | 3 | 3 | 256x256 | Palette4Bit |
| 11 | `trim` | 5 | 5 | 64x128 | Palette4Bit |
| 12 | `window` | 12 | 12 | 64x16 | PaletteA5I3 |

#### `MP1 SANCTORUS`

37 materials; 33 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP:lambert35` | 1 | 1 | 8x64 | PaletteA5I3 Translucent |
| 1 | `lambert1` | -1 | -1 | no texture | Untextured |
| 2 | `lambert12` | 26 | 26 | 128x128 | Palette4Bit |
| 3 | `lambert13` | 27 | 27 | 64x64 | Palette4Bit |
| 4 | `lambert15` | 8 | 8 | 16x128 | Palette4Bit |
| 5 | `lambert161` | 32 | 32 | 32x32 | Palette4Bit |
| 6 | `lambert163` | 7 | 7 | 64x32 | Palette4Bit |
| 7 | `lambert164` | 0 | 0 | 64x128 | Palette4Bit |
| 8 | `lambert165` | 22 | 22 | 64x32 | Palette4Bit |
| 9 | `lambert166` | 28 | 28 | 128x128 | Palette4Bit |
| 10 | `lambert169` | 17 | 16 | 64x64 | PaletteA5I3 Translucent |
| 11 | `lambert17` | 6 | 6 | 128x128 | Palette4Bit |
| 12 | `lambert179` | 30 | 30 | 64x64 | Palette4Bit |
| 13 | `lambert18` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 14 | `lambert20` | 24 | 24 | 128x128 | Palette4Bit |
| 15 | `lambert21` | 20 | 20 | 128x32 | Palette4Bit |
| 16 | `lambert22` | 29 | 29 | 32x128 | Palette4Bit |
| 17 | `lambert23` | 9 | 10 | 64x128 | Palette4Bit |
| 18 | `lambert25` | 10 | 9 | 64x128 | PaletteA5I3 Decal |
| 19 | `lambert27` | 21 | 21 | 128x16 | Palette4Bit |
| 20 | `lambert3` | 19 | 19 | 64x64 | Palette4Bit |
| 21 | `lambert30` | 15 | 15 | 64x128 | Palette4Bit |
| 22 | `lambert32` | 3 | 4 | 32x32 | Palette4Bit |
| 23 | `lambert33` | 4 | 3 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert34` | 23 | 23 | 128x64 | Palette4Bit |
| 25 | `lambert39` | 2 | 2 | 64x128 | Palette4Bit |
| 26 | `lambert40` | 11 | 12 | 32x32 | Palette4Bit |
| 27 | `lambert41` | 12 | 11 | 32x32 | PaletteA5I3 Decal |
| 28 | `lambert42` | 13 | 13 | 64x64 | Palette4Bit |
| 29 | `lambert44` | 14 | 14 | 64x64 | PaletteA5I3 Translucent |
| 30 | `lambert47` | 28 | 28 | 128x128 | Palette4Bit |
| 31 | `lambert6` | 25 | 25 | 128x128 | Palette4Bit |
| 32 | `lambert9` | 18 | 18 | 32x64 | Palette4Bit |
| 33 | `pmag1` | 31 | 31 | 64x64 | Palette4Bit Decal |
| 34 | `pmag3` | 31 | 31 | 64x64 | Palette4Bit Unknown3 |
| 35 | `spawnPadLight` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |
| 36 | `spawnPadLight1` | 16 | 17 | 64x64 | PaletteA5I3 Translucent |

#### `MP2 HARVESTER`

20 materials; 15 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert26` | 1 | 1 | 64x64 | PaletteA5I3 Translucent |
| 1 | `lambert27` | 4 | 4 | 128x32 | Palette4Bit |
| 2 | `lambert28` | 5 | 5 | 64x128 | Palette4Bit |
| 3 | `lambert29` | 10 | 10 | 128x128 | Palette4Bit |
| 4 | `lambert30` | 2 | 2 | 128x128 | Palette4Bit |
| 5 | `lambert31` | 9 | 9 | 128x128 | Palette4Bit |
| 6 | `lambert32` | 11 | 11 | 128x128 | Palette4Bit |
| 7 | `lambert35` | 14 | 14 | 64x64 | PaletteA5I3 Translucent |
| 8 | `lambert36` | 0 | 0 | 256x64 | Palette4Bit |
| 9 | `lambert37` | 7 | 7 | 128x64 | PaletteA5I3 Decal |
| 10 | `lambert38` | 3 | 3 | 128x128 | Palette4Bit |
| 11 | `lambert39` | 6 | 6 | 128x64 | Palette4Bit |
| 12 | `lambert40` | 8 | 8 | 128x32 | Palette4Bit |
| 13 | `lambert41` | 13 | 13 | 64x64 | Palette4Bit |
| 14 | `lambert42` | 7 | 7 | 128x64 | PaletteA5I3 Decal |
| 15 | `lambert44` | 7 | 7 | 128x64 | PaletteA5I3 Decal |
| 16 | `lambert45` | 1 | 1 | 64x64 | PaletteA5I3 Translucent |
| 17 | `lambert47` | 13 | 13 | 64x64 | Palette4Bit |
| 18 | `pmag1` | 12 | 12 | 64x64 | Palette4Bit Decal |
| 19 | `pmag2` | 12 | 12 | 64x64 | Palette4Bit Unknown3 |

#### `MP3 PROVING GROUND`

23 materials; 22 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert1` | -1 | -1 | no texture | Untextured |
| 1 | `lambert12` | 12 | 12 | 128x64 | Palette4Bit |
| 2 | `lambert13` | 6 | 6 | 128x128 | Palette4Bit |
| 3 | `lambert14` | 7 | 7 | 128x128 | Palette4Bit |
| 4 | `lambert15` | 16 | 16 | 128x128 | Palette4Bit |
| 5 | `lambert16` | 10 | 10 | 128x64 | Palette4Bit |
| 6 | `lambert17` | 9 | 9 | 64x128 | Palette4Bit |
| 7 | `lambert18` | 0 | 1 | 64x64 | Palette4Bit |
| 8 | `lambert19` | 18 | 18 | 128x128 | Palette4Bit |
| 9 | `lambert20` | 19 | 19 | 128x128 | Palette4Bit |
| 10 | `lambert21` | 20 | 20 | 128x128 | Palette4Bit |
| 11 | `lambert22` | 3 | 3 | 64x128 | Palette4Bit |
| 12 | `lambert24` | 4 | 4 | 128x128 | Palette4Bit |
| 13 | `lambert25` | 5 | 5 | 128x128 | Palette4Bit Translucent |
| 14 | `lambert26` | 13 | 13 | 64x64 | Palette4Bit |
| 15 | `lambert28` | 8 | 8 | 64x64 | PaletteA5I3 Decal |
| 16 | `lambert29` | 14 | 15 | 256x64 | Palette4Bit |
| 17 | `lambert30` | 15 | 14 | 256x64 | PaletteA5I3 Decal |
| 18 | `lambert31` | 1 | 0 | 64x64 | PaletteA5I3 Decal |
| 19 | `lambert32` | 21 | 21 | 128x32 | Palette4Bit |
| 20 | `lambert4` | 17 | 17 | 128x128 | Palette4Bit |
| 21 | `lambert6` | 11 | 11 | 128x64 | Palette4Bit |
| 22 | `lambert7` | 2 | 2 | 128x128 | Palette4Bit |

#### `MP4 HIGHGROUND - EXPANDED`

30 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A5:lambert14` | 11 | 11 | 128x128 | Palette4Bit |
| 1 | `A5:lambert28` | 12 | 12 | 64x64 | PaletteA5I3 Decal |
| 2 | `Lava` | 1 | 1 | 64x128 | Palette4Bit Decal |
| 3 | `cb_bridgeRockC` | 5 | 5 | 128x128 | Palette4Bit |
| 4 | `enbox02C` | 6 | 6 | 64x64 | Palette4Bit |
| 5 | `floor_00` | 7 | 7 | 128x128 | Palette4Bit |
| 6 | `glyph1` | 8 | 8 | 128x128 | Palette4Bit |
| 7 | `h_floorC` | 0 | 0 | 128x128 | Palette4Bit |
| 8 | `lambert1` | 27 | 27 | 128x128 | Palette4Bit |
| 9 | `lambert18` | 2 | 3 | 32x64 | Palette4Bit |
| 10 | `lambert20` | 3 | 2 | 16x64 | PaletteA5I3 Decal |
| 11 | `lambert22` | 15 | 15 | 64x128 | Palette4Bit |
| 12 | `lambert23` | 19 | 19 | 128x32 | Palette4Bit |
| 13 | `new_sandC` | 14 | 14 | 128x128 | Palette4Bit |
| 14 | `panels2:lambert29` | 9 | 9 | 128x32 | PaletteA5I3 Translucent |
| 15 | `panels2:lambert31` | 10 | 10 | 32x32 | PaletteA5I3 Translucent |
| 16 | `pipeAC2` | 16 | 16 | 32x64 | Palette4Bit |
| 17 | `pipeAS` | 4 | 4 | 32x64 | Palette4Bit |
| 18 | `pmag1` | 18 | 18 | 64x64 | Palette4Bit Decal |
| 19 | `pmag2` | 18 | 18 | 64x64 | Palette4Bit Unknown3 |
| 20 | `sand_to_grass` | 20 | 20 | 128x64 | Palette4Bit |
| 21 | `save01C` | 21 | 21 | 64x64 | Palette4Bit |
| 22 | `shield2_opaq` | 17 | 17 | 64x64 | Palette4Bit |
| 23 | `track_01` | 22 | 22 | 64x64 | Palette4Bit |
| 24 | `trim_00` | 13 | 13 | 128x32 | Palette4Bit |
| 25 | `trim_01` | 23 | 23 | 128x64 | Palette4Bit |
| 26 | `wall0c` | 24 | 24 | 64x64 | Palette4Bit |
| 27 | `wall_00` | 25 | 25 | 128x128 | Palette4Bit |
| 28 | `wall_01` | 26 | 26 | 128x128 | Palette4Bit |
| 29 | `wallpiece3` | 28 | 28 | 128x128 | Palette4Bit |

#### `MP4 HIGHGROUND`

33 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A5:lambert14` | 13 | 13 | 128x128 | Palette4Bit |
| 1 | `A5:lambert28` | 14 | 14 | 64x64 | PaletteA5I3 Decal |
| 2 | `Lava` | 1 | 1 | 64x128 | Palette4Bit Decal |
| 3 | `cb_bridgeRockC` | 7 | 7 | 128x128 | Palette4Bit |
| 4 | `enbox02C` | 8 | 8 | 64x64 | Palette4Bit |
| 5 | `floor_00` | 9 | 9 | 128x128 | Palette4Bit |
| 6 | `glyph1` | 10 | 10 | 128x128 | Palette4Bit |
| 7 | `h_floorC` | 0 | 0 | 128x128 | Palette4Bit |
| 8 | `lambert1` | 30 | 30 | 128x128 | Palette4Bit |
| 9 | `lambert18` | 4 | 5 | 32x64 | Palette4Bit |
| 10 | `lambert20` | 5 | 4 | 16x64 | PaletteA5I3 Decal |
| 11 | `lambert21` | 16 | 16 | 128x64 | Palette4Bit |
| 12 | `lambert22` | 18 | 18 | 64x128 | Palette4Bit |
| 13 | `lambert23` | 22 | 22 | 128x32 | Palette4Bit |
| 14 | `lambert25` | 3 | 3 | 64x128 | Palette4Bit |
| 15 | `lambert26` | 2 | 2 | 64x128 | Palette4Bit |
| 16 | `new_sandC` | 17 | 17 | 128x128 | Palette4Bit |
| 17 | `panels2:lambert29` | 11 | 11 | 128x32 | PaletteA5I3 Translucent |
| 18 | `panels2:lambert31` | 12 | 12 | 32x32 | PaletteA5I3 Translucent |
| 19 | `pipeAC2` | 19 | 19 | 32x64 | Palette4Bit |
| 20 | `pipeAS` | 6 | 6 | 32x64 | Palette4Bit |
| 21 | `pmag1` | 21 | 21 | 64x64 | Palette4Bit Decal |
| 22 | `pmag2` | 21 | 21 | 64x64 | Palette4Bit Unknown3 |
| 23 | `sand_to_grass` | 23 | 23 | 128x64 | Palette4Bit |
| 24 | `save01C` | 24 | 24 | 64x64 | Palette4Bit |
| 25 | `shield2_opaq` | 20 | 20 | 64x64 | Palette4Bit |
| 26 | `track_01` | 25 | 25 | 64x64 | Palette4Bit |
| 27 | `trim_00` | 15 | 15 | 128x32 | Palette4Bit |
| 28 | `trim_01` | 26 | 26 | 128x64 | Palette4Bit |
| 29 | `wall0c` | 27 | 27 | 64x64 | Palette4Bit |
| 30 | `wall_00` | 28 | 28 | 128x128 | Palette4Bit |
| 31 | `wall_01` | 29 | 29 | 128x128 | Palette4Bit |
| 32 | `wallpiece3` | 31 | 31 | 128x128 | Palette4Bit |

#### `MP5 FUEL SLUICE`

29 materials; 27 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `I_lambert35` | 21 | 22 | 64x64 | PaletteA5I3 Translucent |
| 1 | `Ice_trans_infront` | 24 | 24 | 128x128 | PaletteA5I3 Unknown3 |
| 2 | `big_blue` | 1 | 2 | 32x64 | Palette4Bit |
| 3 | `bigblue_alpha` | 2 | 1 | 16x64 | PaletteA5I3 Decal |
| 4 | `blue_wall` | 23 | 23 | 256x256 | Palette4Bit |
| 5 | `dk__black_streak` | 11 | 11 | 128x128 | Palette4Bit |
| 6 | `dk_black` | 10 | 10 | 128x128 | Palette4Bit |
| 7 | `dk_black_light` | 12 | 12 | 64x128 | Palette4Bit |
| 8 | `ice_trans_more` | 24 | 24 | 128x128 | PaletteA5I3 Translucent |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert37` | 8 | 8 | 128x64 | Palette4Bit |
| 11 | `lambert38` | 15 | 14 | 64x64 | PaletteA5I3 Decal |
| 12 | `lambert60` | 22 | 21 | 64x64 | PaletteA5I3 Translucent |
| 13 | `lambert8` | 0 | 0 | 128x128 | Palette4Bit |
| 14 | `latch_2` | 5 | 5 | 128x128 | Palette4Bit |
| 15 | `light_2` | 3 | 3 | 64x128 | Palette4Bit |
| 16 | `light_2_alpha` | 18 | 18 | 32x128 | PaletteA5I3 Decal |
| 17 | `light_streak` | 13 | 13 | 32x64 | Palette4Bit |
| 18 | `light_streak1` | 17 | 17 | 64x64 | PaletteA5I3 Translucent |
| 19 | `metroid_wall` | 9 | 9 | 128x128 | Palette4Bit |
| 20 | `panel_3light` | 14 | 15 | 64x64 | Palette4Bit |
| 21 | `panel_4split` | 4 | 4 | 128x128 | Palette4Bit |
| 22 | `snow` | 19 | 19 | 128x128 | Palette4Bit |
| 23 | `space` | 20 | 20 | 128x128 | Palette2Bit |
| 24 | `tube` | 7 | 7 | 128x128 | Palette4Bit |
| 25 | `wall_blue01` | 6 | 6 | 128x128 | Palette4Bit |
| 26 | `wall_dkpanel` | 16 | 16 | 64x128 | Palette4Bit |
| 27 | `wall_orangeline` | 26 | 26 | 128x128 | Palette4Bit |
| 28 | `wall_scan` | 25 | 25 | 32x32 | PaletteA5I3 Translucent |

#### `MP6 HEADSHOT`

27 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `I_lambert35` | 23 | 24 | 64x64 | PaletteA5I3 Translucent |
| 1 | `bigWhiteGlow` | 7 | 7 | 32x64 | PaletteA5I3 Translucent |
| 2 | `lambert12` | 10 | 10 | 128x128 | Palette4Bit |
| 3 | `lambert13` | 13 | 13 | 128x128 | Palette4Bit |
| 4 | `lambert14` | 14 | 14 | 128x128 | Palette4Bit |
| 5 | `lambert15` | 6 | 6 | 128x128 | Palette4Bit |
| 6 | `lambert17` | 15 | 15 | 128x32 | PaletteA5I3 Decal |
| 7 | `lambert19` | 0 | 0 | 64x128 | Palette4Bit |
| 8 | `lambert22` | 11 | 11 | 128x128 | Palette4Bit |
| 9 | `lambert25` | 19 | 19 | 64x128 | Palette4Bit |
| 10 | `lambert26` | 1 | 1 | 64x128 | Palette4Bit |
| 11 | `lambert29` | 12 | 12 | 128x128 | Palette4Bit |
| 12 | `lambert3` | 22 | 22 | 128x128 | Palette2Bit |
| 13 | `lambert30` | 5 | 4 | 16x16 | Palette4Bit |
| 14 | `lambert31` | 4 | 5 | 32x32 | PaletteA5I3 Translucent |
| 15 | `lambert35` | 3 | 3 | 128x128 | Palette4Bit |
| 16 | `lambert36` | 2 | 2 | 128x16 | Palette4Bit |
| 17 | `lambert37` | 25 | 25 | 64x32 | Palette4Bit |
| 18 | `lambert39` | 18 | 18 | 64x64 | Palette4Bit |
| 19 | `lambert42` | 9 | 9 | 128x128 | Palette4Bit Translucent |
| 20 | `lambert5` | 16 | 16 | 64x64 | Palette4Bit |
| 21 | `lambert59` | 8 | 8 | 8x8 | Palette8Bit |
| 22 | `lambert60` | 24 | 23 | 64x64 | PaletteA5I3 Translucent |
| 23 | `lambert62` | 20 | 21 | 128x128 | Palette4Bit |
| 24 | `lambert66` | 21 | 20 | 64x128 | Palette4Bit |
| 25 | `pmag1` | 17 | 17 | 64x64 | Palette4Bit Decal |
| 26 | `pmag2` | 17 | 17 | 64x64 | Palette4Bit Unknown3 |

#### `MP7 PROCESSOR CORE`

29 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `base_metal_sml` | 24 | 24 | 64x64 | Palette4Bit |
| 1 | `big_pipe` | 6 | 6 | 128x128 | Palette4Bit |
| 2 | `floor_longstripe` | 25 | 25 | 128x128 | Palette4Bit |
| 3 | `floor_pattern` | 14 | 14 | 128x128 | Palette4Bit |
| 4 | `floor_tile` | 9 | 9 | 128x128 | Palette4Bit |
| 5 | `lambert1` | -1 | -1 | no texture | Untextured |
| 6 | `lambert37` | 15 | 15 | 128x128 | Palette4Bit Translucent |
| 7 | `lambert46` | 22 | 22 | 64x64 | Palette4Bit |
| 8 | `lava2` | 4 | 4 | 128x128 | Palette4Bit Translucent |
| 9 | `lava_decal` | 4 | 4 | 128x128 | Palette4Bit Decal |
| 10 | `lava_under` | 19 | 19 | 128x128 | Palette4Bit |
| 11 | `light_2bits` | 16 | 16 | 64x32 | Palette4Bit |
| 12 | `light_2bits_alpha` | 17 | 17 | 128x64 | PaletteA5I3 Decal |
| 13 | `light_45` | 10 | 11 | 32x128 | Palette4Bit |
| 14 | `light_45alpha` | 11 | 10 | 32x128 | PaletteA5I3 Decal |
| 15 | `metal` | 5 | 5 | 128x128 | Palette4Bit |
| 16 | `pmag` | 20 | 20 | 64x64 | Palette4Bit |
| 17 | `pmag_a` | 21 | 21 | 64x64 | Palette4Bit Unknown3 |
| 18 | `pmag_b` | 21 | 21 | 64x64 | Palette4Bit Decal |
| 19 | `rock_dk_lava` | 2 | 2 | 128x128 | Palette4Bit |
| 20 | `rock_lavastreams` | 1 | 1 | 128x128 | Palette4Bit |
| 21 | `rockwall` | 3 | 3 | 128x128 | Palette4Bit |
| 22 | `trim_grating` | 7 | 7 | 64x128 | Palette4Bit |
| 23 | `wall_clamps` | 13 | 13 | 64x128 | Palette4Bit |
| 24 | `wall_dk_trim` | 0 | 0 | 64x64 | Palette4Bit |
| 25 | `wall_dk_trim1` | 8 | 8 | 64x32 | Palette4Bit |
| 26 | `wall_orangeband` | 23 | 23 | 128x128 | Palette4Bit |
| 27 | `wall_orangestreak` | 12 | 12 | 128x128 | Palette4Bit |
| 28 | `wall_teethed` | 18 | 18 | 128x128 | Palette4Bit |

#### `MP8 FIRE CONTROL`

35 materials; 34 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:shield2` | 21 | 21 | 64x64 | Palette4Bit |
| 1 | `I_lambert35` | 29 | 30 | 64x64 | PaletteA5I3 Translucent |
| 2 | `black_wall` | 17 | 17 | 128x128 | Palette4Bit |
| 3 | `blackwall_streak` | 18 | 18 | 128x128 | Palette4Bit |
| 4 | `blue_streak` | 19 | 19 | 32x64 | Palette4Bit |
| 5 | `blue_wall` | 31 | 31 | 256x256 | Palette4Bit |
| 6 | `cans_bot` | 7 | 8 | 64x64 | Palette4Bit |
| 7 | `cans_bot_alpha` | 8 | 7 | 64x64 | PaletteA5I3 Decal |
| 8 | `file6Material` | 15 | 15 | 128x128 | Palette4Bit |
| 9 | `floor_blue_trim` | 9 | 9 | 128x128 | Palette4Bit |
| 10 | `floor_grill` | 11 | 11 | 64x64 | PaletteA5I3 Translucent |
| 11 | `floor_metroid` | 16 | 16 | 128x128 | Palette4Bit |
| 12 | `glass` | 14 | 14 | 128x128 | Palette4Bit Translucent |
| 13 | `lambert40` | 6 | 6 | 64x128 | Palette4Bit |
| 14 | `lambert41` | 23 | 23 | 64x64 | Palette4Bit |
| 15 | `lambert60` | 30 | 29 | 64x64 | PaletteA5I3 Translucent |
| 16 | `light_big_blue` | 1 | 2 | 32x64 | Palette4Bit |
| 17 | `light_big_blue_alpha` | 2 | 1 | 16x64 | PaletteA5I3 Decal |
| 18 | `lights_blue_2s` | 3 | 3 | 64x128 | Palette4Bit |
| 19 | `lights_blue_2s_alpha` | 26 | 26 | 32x128 | PaletteA5I3 Decal |
| 20 | `metal_boxes` | 4 | 4 | 128x128 | Palette4Bit |
| 21 | `orange_streak` | 33 | 33 | 128x128 | Palette4Bit |
| 22 | `panel_02` | 20 | 20 | 64x64 | Palette4Bit |
| 23 | `pmag1` | 22 | 22 | 64x64 | Palette4Bit Decal |
| 24 | `pmag2` | 22 | 22 | 64x64 | Palette4Bit Unknown3 |
| 25 | `shield1` | 24 | 24 | 16x16 | PaletteA5I3 Translucent |
| 26 | `skinny_gray` | 5 | 5 | 128x16 | Palette4Bit |
| 27 | `snow2` | 27 | 27 | 128x128 | Palette4Bit |
| 28 | `space` | 28 | 28 | 128x128 | Palette2Bit |
| 29 | `tube_wall` | 10 | 10 | 128x128 | Palette4Bit |
| 30 | `vent` | 12 | 12 | 128x64 | Palette4Bit |
| 31 | `wall_circutbox` | 0 | 0 | 128x128 | Palette4Bit |
| 32 | `wall_facing` | 25 | 25 | 64x128 | Palette4Bit |
| 33 | `wall_microchip` | 13 | 13 | 32x128 | Palette4Bit |
| 34 | `wall_scan` | 32 | 32 | 32x32 | PaletteA5I3 Translucent |

#### `MP9 CRYOCHASM`

34 materials; 30 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP1:lambert_alimbicEye1` | 0 | 0 | 32x32 | Palette4Bit |
| 1 | `IMP1:lambert_alimbicHead` | 1 | 1 | 64x64 | Palette4Bit |
| 2 | `lambert1` | -1 | -1 | no texture | Untextured |
| 3 | `lambert10` | 7 | 7 | 64x128 | Palette4Bit |
| 4 | `lambert11` | 8 | 8 | 64x64 | Palette4Bit |
| 5 | `lambert12` | 9 | 9 | 32x64 | Palette4Bit |
| 6 | `lambert14` | 2 | 2 | 64x128 | Palette4Bit |
| 7 | `lambert15` | 24 | 24 | 128x128 | Palette4Bit |
| 8 | `lambert16` | 13 | 13 | 16x64 | PaletteA5I3 Decal |
| 9 | `lambert18` | 5 | 5 | 128x128 | Palette4Bit Translucent |
| 10 | `lambert19` | 25 | 25 | 64x64 | Palette4Bit |
| 11 | `lambert21` | 21 | 21 | 64x32 | Palette4Bit |
| 12 | `lambert23` | 23 | 23 | 128x64 | Palette4Bit |
| 13 | `lambert24` | 19 | 19 | 16x64 | Palette4Bit |
| 14 | `lambert25` | 14 | 14 | 16x32 | PaletteA5I3 |
| 15 | `lambert28` | 26 | 26 | 128x128 | Palette4Bit |
| 16 | `lambert29` | 15 | 15 | 16x64 | PaletteA5I3 Decal |
| 17 | `lambert3` | 4 | 4 | 256x256 | Palette4Bit |
| 18 | `lambert30` | 10 | 10 | 128x32 | Palette4Bit |
| 19 | `lambert32` | 11 | 11 | 32x32 | Palette4Bit |
| 20 | `lambert33` | 16 | 16 | 8x32 | Palette4Bit |
| 21 | `lambert35` | 27 | 27 | 128x128 | Palette4Bit |
| 22 | `lambert36` | 13 | 13 | 16x64 | PaletteA5I3 Translucent |
| 23 | `lambert37` | 5 | 5 | 128x128 | Palette4Bit |
| 24 | `lambert4` | 17 | 17 | 128x64 | Palette4Bit |
| 25 | `lambert41` | 20 | 20 | 16x64 | Palette4Bit |
| 26 | `lambert42` | 28 | 28 | 64x64 | Palette4Bit |
| 27 | `lambert43` | 29 | 29 | 64x64 | Palette4Bit Decal |
| 28 | `lambert44` | 29 | 29 | 64x64 | Palette4Bit Unknown3 |
| 29 | `lambert5` | 12 | 12 | 128x128 | Palette4Bit |
| 30 | `lambert6` | 22 | 22 | 128x128 | Palette4Bit |
| 31 | `lambert7` | 3 | 3 | 64x64 | PaletteA5I3 Translucent |
| 32 | `lambert8` | 6 | 6 | 64x128 | Palette4Bit |
| 33 | `lambert9` | 18 | 18 | 16x64 | Palette4Bit |

#### `MP10 OVERLOAD`

32 materials; 32 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `IMP5:lambert9` | 7 | 8 | 64x8 | PaletteA5I3 Translucent |
| 1 | `lambert33` | 31 | 31 | 8x16 | PaletteA5I3 Translucent |
| 2 | `lambert34` | 29 | 29 | 128x64 | Palette4Bit |
| 3 | `lambert36` | 1 | 1 | 128x128 | Palette4Bit |
| 4 | `lambert37` | 10 | 9 | 16x32 | PaletteA5I3 |
| 5 | `lambert38` | 8 | 7 | 64x8 | PaletteA5I3 |
| 6 | `lambert4` | 20 | 20 | 128x32 | PaletteA5I3 |
| 7 | `lambert_ELECTRIC` | 6 | 6 | 64x64 | PaletteA5I3 Decal |
| 8 | `lambert_TUBEGREEN` | 5 | 5 | 64x64 | Palette4Bit |
| 9 | `lambert_Yshape` | 27 | 27 | 64x128 | Palette4Bit |
| 10 | `lambert_alimbicFloorplate` | 15 | 15 | 128x128 | Palette4Bit |
| 11 | `lambert_alimibcWall` | 0 | 0 | 64x128 | Palette4Bit |
| 12 | `lambert_ballTube` | 11 | 12 | 128x128 | Palette4Bit |
| 13 | `lambert_blackBoxes` | 26 | 26 | 128x128 | Palette4Bit |
| 14 | `lambert_blackStripe` | 12 | 11 | 128x64 | Palette4Bit |
| 15 | `lambert_blahGrey` | 4 | 4 | 128x128 | Palette4Bit |
| 16 | `lambert_blueBase` | 2 | 3 | 32x64 | Palette4Bit |
| 17 | `lambert_blueLight` | 3 | 2 | 16x64 | PaletteA5I3 Decal |
| 18 | `lambert_bridgeWalk` | 21 | 21 | 32x64 | Palette4Bit |
| 19 | `lambert_floorgrill` | 14 | 14 | 128x128 | Palette4Bit Translucent |
| 20 | `lambert_lightBase` | 17 | 18 | 64x128 | Palette4Bit |
| 21 | `lambert_lightsORANGE` | 18 | 17 | 64x128 | PaletteA5I3 Decal |
| 22 | `lambert_trim` | 23 | 23 | 128x16 | Palette4Bit |
| 23 | `lambert_tubelights` | 13 | 13 | 32x32 | PaletteA5I3 Decal |
| 24 | `lambert_vent` | 16 | 16 | 64x32 | Palette4Bit |
| 25 | `lambert_vent2` | 24 | 24 | 128x64 | Palette4Bit |
| 26 | `lambert_wallInset` | 28 | 28 | 32x128 | Palette4Bit |
| 27 | `lambert_walls` | 25 | 25 | 128x128 | Palette4Bit |
| 28 | `lambert_whiteBlocks` | 22 | 22 | 64x128 | Palette4Bit |
| 29 | `lambert_whiteLIGHTS` | 19 | 19 | 64x64 | PaletteA5I3 Decal |
| 30 | `pasted__lambert7` | 9 | 10 | 16x32 | PaletteA5I3 Translucent |
| 31 | `pasted__lambert_stars02` | 30 | 30 | 128x128 | Palette4Bit |

#### `MP11 BREAKTHROUGH`

28 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert1` | -1 | -1 | no texture | Untextured |
| 1 | `lambert7` | 5 | 5 | 16x64 | PaletteA5I3 Decal |
| 2 | `lambert_alimbicEye1` | 0 | 0 | 32x32 | Palette4Bit |
| 3 | `lambert_alimbicHead` | 1 | 1 | 64x64 | Palette4Bit |
| 4 | `lambert_brownMetal` | 22 | 22 | 16x64 | Palette4Bit |
| 5 | `lambert_ceilingTile` | 3 | 3 | 128x128 | Palette4Bit |
| 6 | `lambert_columnBase` | 16 | 16 | 64x64 | Palette4Bit |
| 7 | `lambert_columnWalls` | 4 | 4 | 128x128 | Palette4Bit |
| 8 | `lambert_darkMetal` | 14 | 14 | 128x128 | Palette4Bit |
| 9 | `lambert_glowGradient` | 6 | 6 | 16x16 | PaletteA5I3 |
| 10 | `lambert_greyTiles` | 2 | 2 | 128x128 | Palette4Bit |
| 11 | `lambert_groundMat` | 7 | 7 | 128x128 | Palette4Bit |
| 12 | `lambert_iceEdge` | 8 | 8 | 128x128 | PaletteA5I3 |
| 13 | `lambert_iceLake` | 9 | 9 | 128x128 | PaletteA5I3 |
| 14 | `lambert_iceOpaque` | 10 | 10 | 128x128 | Palette4Bit |
| 15 | `lambert_iceRockwalls` | 11 | 12 | 128x128 | Palette4Bit |
| 16 | `lambert_icyRamp` | 19 | 19 | 64x64 | Palette4Bit |
| 17 | `lambert_icyWalls` | 12 | 11 | 128x128 | Palette4Bit |
| 18 | `lambert_lakeBottom` | 13 | 13 | 128x128 | Palette4Bit |
| 19 | `lambert_miniLight` | 15 | 15 | 16x32 | Palette4Bit |
| 20 | `lambert_pillarBase` | 17 | 17 | 128x128 | Palette4Bit |
| 21 | `lambert_pillarTop` | 18 | 18 | 64x64 | Palette4Bit |
| 22 | `lambert_snow` | 20 | 20 | 128x128 | Palette4Bit |
| 23 | `lambert_stone` | 21 | 21 | 64x64 | Palette4Bit |
| 24 | `lambert_tunnelBlocks` | 24 | 24 | 128x64 | Palette4Bit |
| 25 | `lambert_tunnelBot` | 25 | 25 | 128x128 | Palette4Bit |
| 26 | `lambert_tunnelCeiling` | 23 | 23 | 128x128 | Palette4Bit |
| 27 | `lambert_whiteMetal` | 23 | 23 | 128x128 | Palette4Bit |

#### `MP12 SIC TRANSIT`

42 materials; 40 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:lambert12` | 36 | 36 | 32x64 | Palette4Bit |
| 1 | `IMP1:lambert_alimbicEye1` | 2 | 2 | 32x32 | Palette4Bit |
| 2 | `IMP1:lambert_alimbicHead` | 3 | 3 | 64x64 | Palette4Bit |
| 3 | `lambert1` | -1 | -1 | no texture | Untextured |
| 4 | `lambert30` | 31 | 31 | 128x128 | Palette4Bit |
| 5 | `lambert52` | 1 | 1 | 64x64 | PaletteA5I3 Decal |
| 6 | `lambert53` | 0 | 0 | 64x64 | Palette4Bit |
| 7 | `lambert_ICE` | 10 | 10 | 64x64 | Palette4Bit |
| 8 | `lambert_ceiling` | 5 | 5 | 64x64 | Palette4Bit |
| 9 | `lambert_glowyLight` | 4 | 4 | 16x16 | PaletteA5I3 Translucent |
| 10 | `lambert_glyphTrim` | 7 | 7 | 128x32 | Palette4Bit |
| 11 | `lambert_greySnow` | 27 | 27 | 128x128 | Palette4Bit |
| 12 | `lambert_groundOutside` | 8 | 8 | 128x128 | Palette4Bit |
| 13 | `lambert_icyWalls` | 14 | 14 | 128x128 | Palette4Bit |
| 14 | `lambert_metalFloor` | 6 | 6 | 64x64 | Palette4Bit |
| 15 | `lambert_minilight2` | 17 | 16 | 16x32 | Palette4Bit |
| 16 | `lambert_newIceStones` | 11 | 13 | 128x128 | Palette4Bit |
| 17 | `lambert_newIceStonesM` | 13 | 12 | 128x128 | Palette4Bit |
| 18 | `lambert_orangeLight` | 18 | 19 | 64x64 | Palette4Bit |
| 19 | `lambert_orangeLightGlow` | 19 | 18 | 32x32 | PaletteA5I3 Decal |
| 20 | `lambert_pipes` | 21 | 21 | 32x256 | Palette4Bit |
| 21 | `lambert_rampsIcy` | 22 | 22 | 64x64 | Palette4Bit |
| 22 | `lambert_rockIcetransition` | 12 | 11 | 128x128 | Palette4Bit |
| 23 | `lambert_ruinTrim` | 25 | 25 | 128x32 | Palette4Bit |
| 24 | `lambert_sky` | 39 | 39 | 128x128 | Palette4Bit |
| 25 | `lambert_snowCol` | 28 | 28 | 128x128 | Palette4Bit |
| 26 | `lambert_tanWall` | 23 | 23 | 128x128 | Palette4Bit |
| 27 | `lambert_trim` | 34 | 34 | 64x64 | Palette4Bit |
| 28 | `lambert_trimmedWalls` | 26 | 26 | 128x128 | Palette4Bit |
| 29 | `lambert_wallBottom` | 35 | 35 | 128x128 | Palette4Bit |
| 30 | `lambert_walls` | 24 | 24 | 128x128 | Palette4Bit |
| 31 | `lightsEXP03:squareLight:blinn87` | 15 | 15 | 16x16 | Palette4Bit |
| 32 | `little:cylinderLight:lightparts_lambert17` | 16 | 17 | 16x32 | Palette4Bit |
| 33 | `p__blinn79` | 20 | 20 | 64x64 | Palette4Bit |
| 34 | `pasted__lambert18` | 9 | 9 | 64x64 | Palette4Bit |
| 35 | `pasted__lambert24` | 33 | 33 | 16x64 | Palette4Bit |
| 36 | `pasted__lambert9` | 32 | 32 | 16x64 | Palette4Bit |
| 37 | `pasted__lambert_pMag` | 37 | 37 | 64x64 | Palette4Bit |
| 38 | `pasted__lambert_snowBox01` | 29 | 30 | 128x128 | Palette4Bit |
| 39 | `pasted__lambert_snowBox02` | 30 | 29 | 128x128 | Palette4Bit |
| 40 | `pmag1` | 38 | 38 | 64x64 | Palette4Bit Decal |
| 41 | `pmag2` | 38 | 38 | 64x64 | Palette4Bit Unknown3 |

#### `MP13 ACCELERATOR`

31 materials; 29 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `I_lambert35` | 22 | 23 | 64x64 | PaletteA5I3 Translucent |
| 1 | `I_lambert60` | 23 | 22 | 64x64 | PaletteA5I3 Translucent |
| 2 | `TRIM_lighting` | 19 | 20 | 32x128 | Palette4Bit |
| 3 | `TRIM_lighting_alpha` | 20 | 19 | 32x128 | PaletteA5I3 Decal |
| 4 | `bigWhiteGlow` | 6 | 6 | 32x64 | PaletteA5I3 Translucent |
| 5 | `floor_roomc` | 4 | 4 | 128x128 | Palette4Bit |
| 6 | `glass` | 18 | 18 | 64x64 | PaletteA5I3 Translucent |
| 7 | `ice` | 5 | 5 | 128x128 | Palette8Bit |
| 8 | `lambert1` | -1 | -1 | no texture | Untextured |
| 9 | `lambert51` | 28 | 28 | 128x128 | Palette4Bit |
| 10 | `lambert59` | 7 | 7 | 8x8 | Palette8Bit |
| 11 | `lambert60` | 14 | 14 | 64x64 | Palette4Bit Translucent |
| 12 | `lambert62` | 16 | 16 | 64x64 | Palette4Bit |
| 13 | `metal` | 0 | 0 | 128x128 | Palette4Bit |
| 14 | `panel_graynotch` | 1 | 1 | 128x128 | Palette4Bit |
| 15 | `panel_trim_light` | 12 | 12 | 32x64 | Palette4Bit |
| 16 | `panel_u_blue` | 17 | 17 | 64x128 | Palette4Bit |
| 17 | `pmag1` | 15 | 15 | 64x64 | Palette4Bit Decal |
| 18 | `pmag2` | 15 | 15 | 64x64 | Palette4Bit Unknown3 |
| 19 | `rock_gradient` | 3 | 3 | 128x128 | Palette4Bit |
| 20 | `snow_flurry` | 25 | 25 | 16x64 | PaletteA5I3 Translucent |
| 21 | `space` | 21 | 21 | 128x128 | Palette2Bit |
| 22 | `trim_gray_skinny` | 13 | 13 | 16x128 | Palette4Bit |
| 23 | `trim_light` | 11 | 11 | 32x64 | Palette4Bit |
| 24 | `wall_darklip` | 10 | 10 | 64x128 | Palette4Bit |
| 25 | `wall_grid_base` | 24 | 24 | 256x256 | Palette4Bit |
| 26 | `wall_metroid` | 9 | 9 | 128x128 | Palette4Bit |
| 27 | `wall_orangetrim` | 27 | 27 | 128x128 | Palette4Bit |
| 28 | `wall_scan` | 26 | 26 | 32x32 | PaletteA5I3 Translucent |
| 29 | `wall_squares` | 2 | 2 | 128x128 | Palette4Bit |
| 30 | `wall_tube` | 8 | 8 | 64x128 | Palette4Bit |

#### `MP14 OUTER REACH`

23 materials; 21 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `glow` | 19 | 19 | 16x32 | PaletteA5I3 Translucent |
| 1 | `ice_01` | 10 | 10 | 64x128 | PaletteA5I3 Translucent |
| 2 | `ice_02` | 10 | 10 | 64x128 | PaletteA5I3 Translucent |
| 3 | `lambert19` | 0 | 0 | 64x64 | Palette4Bit |
| 4 | `lambert20` | 9 | 9 | 64x128 | Palette4Bit |
| 5 | `lambert21` | 2 | 2 | 128x128 | Palette4Bit |
| 6 | `lambert22` | 8 | 8 | 128x128 | Palette4Bit |
| 7 | `lambert23` | 4 | 4 | 64x32 | Palette4Bit |
| 8 | `lambert26` | 11 | 11 | 256x256 | Palette4Bit |
| 9 | `lambert27` | 12 | 12 | 128x128 | Palette4Bit |
| 10 | `lambert28` | 5 | 6 | 32x128 | Palette4Bit |
| 11 | `lambert29` | 6 | 5 | 32x128 | PaletteA5I3 Decal |
| 12 | `lambert30` | 13 | 13 | 128x128 | PaletteA3I5 Translucent |
| 13 | `lambert32` | 15 | 15 | 128x64 | PaletteA5I3 Decal |
| 14 | `lambert33` | 14 | 14 | 64x32 | Palette4Bit |
| 15 | `lambert34` | 7 | 7 | 128x128 | Palette4Bit |
| 16 | `lambert35` | 18 | 18 | 128x128 | Palette4Bit |
| 17 | `lambert36` | 20 | 20 | 128x128 | Palette4Bit |
| 18 | `lambert37` | 16 | 16 | 128x128 | Palette4Bit |
| 19 | `lambert38` | 3 | 3 | 64x128 | Palette4Bit |
| 20 | `metal` | 1 | 1 | 128x128 | Palette4Bit |
| 21 | `new_space:lambert3` | 17 | 17 | 128x128 | Palette4Bit |
| 22 | `new_space:lambert9` | 17 | 17 | 128x128 | Palette4Bit |

#### `CTF1 FAULT LINE - EXPANDED`

32 materials; 30 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert32` | 14 | 15 | 128x128 | Palette4Bit |
| 1 | `lambert33` | 15 | 14 | 128x128 | Palette4Bit |
| 2 | `lambert36` | 27 | 27 | 64x64 | PaletteA5I3 Decal |
| 3 | `lambert37` | 21 | 21 | 64x64 | Palette4Bit |
| 4 | `lambert_Ice` | 12 | 12 | 64x128 | Palette4Bit |
| 5 | `lambert_Ice1` | 12 | 12 | 64x128 | Palette4Bit |
| 6 | `lambert_Sky` | 23 | 23 | 128x128 | Palette4Bit |
| 7 | `lambert_Snow` | 24 | 24 | 128x128 | Palette4Bit |
| 8 | `lambert_alimbicEye` | 0 | 0 | 32x32 | Palette4Bit |
| 9 | `lambert_bigWalls` | 6 | 6 | 128x128 | Palette4Bit |
| 10 | `lambert_blueLights` | 9 | 9 | 8x64 | PaletteA5I3 Decal |
| 11 | `lambert_bridgeRock` | 1 | 1 | 128x128 | Palette4Bit |
| 12 | `lambert_brokenRocks` | 2 | 3 | 128x128 | Palette4Bit |
| 13 | `lambert_brokenRocksIce` | 3 | 2 | 128x128 | Palette4Bit |
| 14 | `lambert_brownTile` | 4 | 4 | 64x64 | Palette4Bit |
| 15 | `lambert_ceilingTile` | 5 | 5 | 64x64 | Palette4Bit |
| 16 | `lambert_chasmRocks` | 22 | 22 | 64x64 | Palette4Bit |
| 17 | `lambert_crackedWall` | 7 | 7 | 128x128 | Palette4Bit |
| 18 | `lambert_darkMetal` | 8 | 8 | 64x64 | Palette4Bit |
| 19 | `lambert_iceRiver` | 13 | 13 | 128x64 | PaletteA5I3 Translucent |
| 20 | `lambert_iceWalls` | 16 | 16 | 128x128 | Palette4Bit |
| 21 | `lambert_icyGround` | 11 | 11 | 128x128 | Palette4Bit |
| 22 | `lambert_icyTrim` | 17 | 17 | 128x64 | Palette4Bit |
| 23 | `lambert_minilight` | 18 | 18 | 16x32 | Palette4Bit |
| 24 | `lambert_pMag` | 28 | 28 | 64x64 | Palette4Bit |
| 25 | `lambert_pillars` | 20 | 20 | 64x128 | Palette4Bit |
| 26 | `lambert_trimDeco` | 19 | 19 | 64x64 | Palette4Bit |
| 27 | `lambert_tunnelTrim` | 25 | 25 | 128x64 | Palette4Bit |
| 28 | `lambert_wall` | 26 | 26 | 64x128 | Palette4Bit |
| 29 | `lambert_yellowGlow` | 10 | 10 | 16x16 | PaletteA5I3 Translucent |
| 30 | `pmag1` | 29 | 29 | 64x64 | Palette4Bit Decal |
| 31 | `pmag2` | 29 | 29 | 64x64 | Palette4Bit Unknown3 |

#### `CTF1_FAULT LINE`

33 materials; 31 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert10` | 25 | 25 | 64x64 | Palette4Bit |
| 1 | `lambert11` | 26 | 26 | 32x64 | Palette4Bit |
| 2 | `lambert12` | 27 | 27 | 32x64 | Palette4Bit |
| 3 | `lambert32` | 12 | 13 | 128x128 | Palette4Bit |
| 4 | `lambert33` | 13 | 12 | 128x128 | Palette4Bit |
| 5 | `lambert35` | 30 | 30 | 32x128 | Palette4Bit |
| 6 | `lambert36` | 24 | 24 | 64x64 | PaletteA5I3 Decal |
| 7 | `lambert37` | 18 | 18 | 64x64 | Palette4Bit |
| 8 | `lambert_Ice` | 10 | 10 | 64x128 | Palette4Bit |
| 9 | `lambert_Ice1` | 10 | 10 | 64x128 | Palette4Bit |
| 10 | `lambert_Sky` | 20 | 20 | 128x128 | Palette4Bit |
| 11 | `lambert_Snow` | 21 | 21 | 128x128 | Palette4Bit |
| 12 | `lambert_bigWalls` | 5 | 5 | 128x128 | Palette4Bit |
| 13 | `lambert_bridgeRock` | 0 | 0 | 128x128 | Palette4Bit |
| 14 | `lambert_brokenRocks` | 1 | 2 | 128x128 | Palette4Bit |
| 15 | `lambert_brokenRocksIce` | 2 | 1 | 128x128 | Palette4Bit |
| 16 | `lambert_brownTile` | 3 | 3 | 64x64 | Palette4Bit |
| 17 | `lambert_ceilingTile` | 4 | 4 | 64x64 | Palette4Bit |
| 18 | `lambert_chasmRocks` | 19 | 19 | 64x64 | Palette4Bit |
| 19 | `lambert_crackedWall` | 6 | 6 | 128x128 | Palette4Bit |
| 20 | `lambert_darkMetal` | 7 | 7 | 64x64 | Palette4Bit |
| 21 | `lambert_iceRiver` | 11 | 11 | 128x64 | PaletteA5I3 Translucent |
| 22 | `lambert_iceWalls` | 14 | 14 | 128x128 | Palette4Bit |
| 23 | `lambert_icyGround` | 9 | 9 | 128x128 | Palette4Bit |
| 24 | `lambert_icyTrim` | 15 | 15 | 128x64 | Palette4Bit |
| 25 | `lambert_minilight` | 16 | 16 | 16x32 | Palette4Bit |
| 26 | `lambert_pMag` | 28 | 28 | 64x64 | Palette4Bit |
| 27 | `lambert_pillars` | 17 | 17 | 64x128 | Palette4Bit |
| 28 | `lambert_tunnelTrim` | 22 | 22 | 128x64 | Palette4Bit |
| 29 | `lambert_wall` | 23 | 23 | 64x128 | Palette4Bit |
| 30 | `lambert_yellowGlow` | 8 | 8 | 16x16 | PaletteA5I3 Translucent |
| 31 | `pmag1` | 29 | 29 | 64x64 | Palette4Bit Decal |
| 32 | `pmag2` | 29 | 29 | 64x64 | Palette4Bit Unknown3 |

#### `AD1 TRANSFER LOCK BT`

28 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert41` | 12 | 12 | 64x128 | Palette4Bit |
| 1 | `lambert46` | 6 | 6 | 64x64 | PaletteA5I3 Translucent |
| 2 | `lambert_animGlass` | 4 | 4 | 8x64 | PaletteA5I3 Translucent |
| 3 | `lambert_ballTube` | 5 | 5 | 32x64 | Palette4Bit |
| 4 | `lambert_bigLight` | 3 | 3 | 8x64 | PaletteA5I3 |
| 5 | `lambert_blueBase` | 0 | 1 | 32x64 | Palette4Bit |
| 6 | `lambert_blueLight` | 2 | 2 | 8x64 | PaletteA5I3 Translucent |
| 7 | `lambert_blueLightglow` | 1 | 0 | 16x64 | PaletteA5I3 Decal |
| 8 | `lambert_bridgeLightBASE` | 9 | 10 | 64x128 | Palette4Bit |
| 9 | `lambert_bridgeWalk` | 13 | 13 | 32x64 | Palette4Bit |
| 10 | `lambert_floorBLOCKS` | 7 | 7 | 64x128 | Palette4Bit |
| 11 | `lambert_metalTrim` | 15 | 15 | 128x16 | Palette4Bit |
| 12 | `lambert_normalPortals` | 24 | 24 | 128x128 | Palette4Bit Translucent |
| 13 | `lambert_orangeLightsStill` | 10 | 9 | 64x128 | PaletteA5I3 Decal |
| 14 | `lambert_pMag` | 21 | 21 | 64x64 | Palette4Bit |
| 15 | `lambert_pmagdoorTrim` | 11 | 11 | 64x64 | Palette4Bit |
| 16 | `lambert_shiled01Glow` | 23 | 23 | 8x64 | PaletteA5I3 Translucent |
| 17 | `lambert_stars02` | 25 | 25 | 256x256 | Palette2Bit |
| 18 | `lambert_stars03` | 25 | 25 | 256x256 | Palette2Bit |
| 19 | `lambert_vent` | 16 | 16 | 128x64 | Palette4Bit |
| 20 | `lambert_wall2INDENTS` | 20 | 20 | 128x128 | Palette4Bit |
| 21 | `lambert_wall3HEX` | 19 | 19 | 128x128 | Palette4Bit |
| 22 | `lambert_wallBlackblox` | 18 | 18 | 128x128 | Palette4Bit |
| 23 | `lambert_wallInset` | 8 | 8 | 64x32 | Palette4Bit |
| 24 | `lambert_walls` | 17 | 17 | 128x128 | Palette4Bit |
| 25 | `lambert_whiteBlock` | 14 | 14 | 64x64 | Palette4Bit |
| 26 | `pmag1` | 22 | 22 | 64x64 | Palette4Bit Decal |
| 27 | `pmag3` | 22 | 22 | 64x64 | Palette4Bit Unknown3 |

#### `AD1 TRANSFER LOCK DM`

25 materials; 23 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert41` | 11 | 11 | 64x128 | Palette4Bit |
| 1 | `lambert46` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 2 | `lambert_animGlass` | 3 | 3 | 8x64 | PaletteA5I3 Translucent |
| 3 | `lambert_ballTube` | 4 | 4 | 32x64 | Palette4Bit |
| 4 | `lambert_blueBase` | 0 | 1 | 32x64 | Palette4Bit |
| 5 | `lambert_blueLight` | 2 | 2 | 8x64 | PaletteA5I3 Translucent |
| 6 | `lambert_blueLightglow` | 1 | 0 | 16x64 | PaletteA5I3 Decal |
| 7 | `lambert_bridgeLightBASE` | 8 | 9 | 64x128 | Palette4Bit |
| 8 | `lambert_bridgeWalk` | 12 | 12 | 32x64 | Palette4Bit |
| 9 | `lambert_floorBLOCKS` | 6 | 6 | 64x128 | Palette4Bit |
| 10 | `lambert_metalTrim` | 14 | 14 | 128x16 | Palette4Bit |
| 11 | `lambert_normalPortals` | 21 | 21 | 128x128 | Palette4Bit Translucent |
| 12 | `lambert_orangeLightsStill` | 9 | 8 | 64x128 | PaletteA5I3 Decal |
| 13 | `lambert_pMag` | 19 | 19 | 64x64 | Palette4Bit |
| 14 | `lambert_pmagdoorTrim` | 10 | 10 | 64x64 | Palette4Bit |
| 15 | `lambert_stars02` | 22 | 22 | 256x256 | Palette2Bit |
| 16 | `lambert_stars03` | 22 | 22 | 256x256 | Palette2Bit |
| 17 | `lambert_wall2INDENTS` | 18 | 18 | 128x128 | Palette4Bit |
| 18 | `lambert_wall3HEX` | 17 | 17 | 128x128 | Palette4Bit |
| 19 | `lambert_wallBlackblox` | 16 | 16 | 128x128 | Palette4Bit |
| 20 | `lambert_wallInset` | 7 | 7 | 64x32 | Palette4Bit |
| 21 | `lambert_walls` | 15 | 15 | 128x128 | Palette4Bit |
| 22 | `lambert_whiteBlock` | 13 | 13 | 64x64 | Palette4Bit |
| 23 | `pmag1` | 20 | 20 | 64x64 | Palette4Bit Decal |
| 24 | `pmag3` | 20 | 20 | 64x64 | Palette4Bit Unknown3 |

#### `AD2 MAGMA VENTS`

33 materials; 31 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `FLOOR_ancient_type` | 12 | 12 | 128x128 | Palette4Bit |
| 1 | `FLOOR_concrete` | 8 | 8 | 64x64 | Palette4Bit |
| 2 | `FLOOR_ground_2` | 16 | 16 | 128x128 | Palette4Bit |
| 3 | `FLOOR_rockstrips` | 23 | 23 | 128x128 | Palette4Bit |
| 4 | `IMP:shield2_opaq` | 19 | 19 | 64x64 | Palette4Bit |
| 5 | `TRIM_bolted` | 26 | 26 | 64x64 | Palette4Bit |
| 6 | `TRIM_grateing` | 4 | 4 | 32x64 | Palette4Bit |
| 7 | `TRIM_lighting` | 9 | 10 | 128x32 | Palette4Bit |
| 8 | `TRIM_lighting_alpha` | 10 | 9 | 128x32 | PaletteA5I3 Decal |
| 9 | `ad2_1:lambert7` | 17 | 17 | 128x128 | Palette4Bit |
| 10 | `lambert1` | -1 | -1 | no texture | Untextured |
| 11 | `lambert34` | 1 | 1 | 128x128 | Palette4Bit Translucent |
| 12 | `lambert36` | 5 | 5 | 128x128 | Palette4Bit |
| 13 | `lambert37` | 6 | 6 | 128x128 | Palette4Bit |
| 14 | `lambert38` | 21 | 21 | 64x64 | Palette4Bit |
| 15 | `metal` | 2 | 2 | 128x128 | Palette4Bit |
| 16 | `metal_dark_dirty` | 7 | 7 | 32x64 | Palette4Bit |
| 17 | `new_sky1:lambert14` | 13 | 13 | 128x128 | Palette4Bit |
| 18 | `new_sky1:lambert28` | 14 | 14 | 64x64 | PaletteA5I3 Decal |
| 19 | `panel_rivets` | 18 | 18 | 64x128 | Palette4Bit |
| 20 | `pmag1` | 20 | 20 | 64x64 | Palette4Bit Decal |
| 21 | `pmag2` | 20 | 20 | 64x64 | Palette4Bit Unknown3 |
| 22 | `rock_reg` | 22 | 22 | 64x128 | Palette4Bit |
| 23 | `rock_rock_ash` | 3 | 3 | 64x128 | Palette4Bit |
| 24 | `rust` | 24 | 24 | 16x16 | Palette4Bit |
| 25 | `sand` | 25 | 25 | 128x128 | Palette4Bit |
| 26 | `trim_bridge` | 29 | 29 | 64x32 | Palette4Bit |
| 27 | `trim_door` | 27 | 28 | 64x32 | Palette4Bit |
| 28 | `trim_door2` | 28 | 27 | 64x64 | Palette4Bit |
| 29 | `trim_rock` | 0 | 0 | 128x64 | Palette4Bit |
| 30 | `tunnel_alpha` | 11 | 11 | 16x16 | PaletteA5I3 Translucent |
| 31 | `wall_main` | 15 | 15 | 128x128 | Palette4Bit |
| 32 | `wall_zrich` | 30 | 30 | 64x128 | Palette4Bit |

#### `AD2 ALINOS PERCH`

28 materials; 26 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `FLOOR_ancient_type` | 9 | 9 | 128x128 | Palette4Bit |
| 1 | `FLOOR_concrete` | 6 | 6 | 64x64 | Palette4Bit |
| 2 | `FLOOR_ground_2` | 13 | 13 | 128x128 | Palette4Bit |
| 3 | `FLOOR_rockstrips` | 19 | 19 | 128x128 | Palette4Bit |
| 4 | `IMP:shield2_opaq` | 15 | 15 | 64x64 | Palette4Bit |
| 5 | `TRIM_bolted` | 21 | 21 | 64x64 | Palette4Bit |
| 6 | `TRIM_grateing` | 2 | 2 | 32x64 | Palette4Bit |
| 7 | `TRIM_lighting` | 7 | 8 | 128x32 | Palette4Bit |
| 8 | `TRIM_lighting_alpha` | 8 | 7 | 128x32 | PaletteA5I3 Decal |
| 9 | `lambert1` | -1 | -1 | no texture | Untextured |
| 10 | `lambert36` | 3 | 3 | 128x128 | Palette4Bit |
| 11 | `lambert37` | 4 | 4 | 128x128 | Palette4Bit |
| 12 | `lambert38` | 17 | 17 | 64x64 | Palette4Bit |
| 13 | `metal` | 1 | 1 | 128x128 | Palette4Bit |
| 14 | `metal_dark_dirty` | 5 | 5 | 32x64 | Palette4Bit |
| 15 | `new_sky1:lambert14` | 10 | 10 | 128x128 | Palette4Bit |
| 16 | `new_sky1:lambert28` | 11 | 11 | 64x64 | PaletteA5I3 Decal |
| 17 | `panel_rivets` | 14 | 14 | 64x128 | Palette4Bit |
| 18 | `pmag1` | 16 | 16 | 64x64 | Palette4Bit Decal |
| 19 | `pmag2` | 16 | 16 | 64x64 | Palette4Bit Unknown3 |
| 20 | `rock_reg` | 18 | 18 | 64x128 | Palette4Bit |
| 21 | `sand` | 20 | 20 | 128x128 | Palette4Bit |
| 22 | `trim_bridge` | 24 | 24 | 64x32 | Palette4Bit |
| 23 | `trim_door` | 22 | 23 | 64x32 | Palette4Bit |
| 24 | `trim_door2` | 23 | 22 | 64x64 | Palette4Bit |
| 25 | `trim_rock` | 0 | 0 | 128x64 | Palette4Bit |
| 26 | `wall_main` | 12 | 12 | 128x128 | Palette4Bit |
| 27 | `wall_zrich` | 25 | 25 | 64x128 | Palette4Bit |

#### `UNIT1 ALINOS LANDFALL`

19 materials; 17 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A12:lambert14` | 8 | 8 | 128x128 | Palette4Bit |
| 1 | `A12:lambert28` | 9 | 9 | 64x64 | PaletteA5I3 Decal |
| 2 | `Block` | 5 | 5 | 128x128 | Palette4Bit |
| 3 | `Crack` | 13 | 13 | 64x64 | Palette4Bit |
| 4 | `Lava1` | 11 | 11 | 128x128 | Palette4Bit Decal |
| 5 | `Lava2` | 11 | 11 | 128x128 | Palette4Bit Decal |
| 6 | `Wall` | 15 | 15 | 128x128 | Palette4Bit |
| 7 | `lambert1` | -1 | -1 | no texture | Untextured |
| 8 | `lambert10` | 0 | 0 | 64x64 | Palette4Bit |
| 9 | `lambert11` | 4 | 4 | 128x128 | Palette4Bit |
| 10 | `lambert12` | 3 | 3 | 32x64 | Palette4Bit |
| 11 | `lambert15` | 2 | 2 | 128x128 | Palette4Bit |
| 12 | `lambert17` | 14 | 14 | 64x128 | Palette4Bit |
| 13 | `lambert19` | 7 | 7 | 128x128 | Palette4Bit |
| 14 | `lambert20` | 10 | 10 | 128x128 | Palette4Bit |
| 15 | `lambert5` | 16 | 16 | 128x128 | Palette4Bit |
| 16 | `lambert7` | 1 | 1 | 128x128 | Palette4Bit |
| 17 | `lambert8` | 6 | 6 | 64x64 | Palette4Bit |
| 18 | `trim01` | 12 | 12 | 128x64 | Palette4Bit |

#### `UNIT2 LANDING BAY`

30 materials; 28 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `blue_light` | 1 | 2 | 64x64 | Palette4Bit |
| 1 | `blue_ligth_alpha` | 2 | 1 | 32x64 | PaletteA5I3 Decal |
| 2 | `comp_wall_01` | 22 | 22 | 64x64 | Palette4Bit |
| 3 | `comp_wall_02` | 16 | 16 | 128x32 | Palette4Bit |
| 4 | `comp_wall_chips` | 24 | 24 | 64x128 | Palette4Bit |
| 5 | `floor_base_metroid` | 21 | 21 | 128x128 | Palette4Bit |
| 6 | `floor_tile_greymain` | 14 | 14 | 128x128 | Palette4Bit |
| 7 | `floor_tile_nubs` | 8 | 8 | 128x128 | Palette4Bit |
| 8 | `floor_tile_orangenubs` | 18 | 18 | 128x64 | Palette4Bit |
| 9 | `green_wall_scan` | 4 | 4 | 64x64 | PaletteA5I3 Translucent |
| 10 | `hall_mesh` | 5 | 6 | 128x128 | Palette4Bit |
| 11 | `lambert1` | -1 | -1 | no texture | Untextured |
| 12 | `lambert164` | 6 | 5 | 128x128 | Palette4Bit |
| 13 | `lambert165` | 3 | 3 | 128x128 | Palette4Bit |
| 14 | `lambert166` | 26 | 26 | 64x16 | PaletteA5I3 Translucent |
| 15 | `lambert167` | 27 | 27 | 128x64 | Palette4Bit |
| 16 | `lambert47` | 23 | 23 | 128x128 | Palette4Bit |
| 17 | `lambert6` | 20 | 20 | 128x128 | Palette4Bit |
| 18 | `lambert9` | 25 | 25 | 256x256 | Palette4Bit |
| 19 | `light_grey1` | 10 | 11 | 64x128 | Palette4Bit |
| 20 | `light_grey_alpha` | 11 | 10 | 64x128 | PaletteA5I3 Decal |
| 21 | `light_orange_trim` | 0 | 0 | 128x128 | Palette4Bit |
| 22 | `light_square_grey` | 12 | 13 | 64x64 | Palette4Bit |
| 23 | `light_square_grey_alpha` | 13 | 12 | 64x64 | PaletteA5I3 Decal |
| 24 | `met_grating` | 7 | 7 | 64x64 | PaletteA5I3 Translucent |
| 25 | `notch_three_steel` | 15 | 15 | 128x16 | Palette4Bit |
| 26 | `orange_stripe` | 19 | 19 | 128x128 | Palette4Bit |
| 27 | `pilar_guts` | 17 | 17 | 128x16 | Palette4Bit |
| 28 | `space_rotate_lambert3` | 25 | 25 | 256x256 | Palette4Bit |
| 29 | `trim_grey_notches` | 9 | 9 | 128x32 | Palette4Bit |

#### `UNIT 3 VESPER STARPORT`

24 materials; 23 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A3:lambert11` | 8 | 8 | 128x128 | Palette4Bit |
| 1 | `A4:trim_gray_skinny` | 16 | 16 | 16x128 | Palette4Bit |
| 2 | `A4:trim_light` | 13 | 13 | 32x64 | Palette4Bit Translucent |
| 3 | `I_lambert35` | 20 | 21 | 64x64 | PaletteA5I3 Translucent |
| 4 | `Ice` | 7 | 7 | 128x128 | Palette4Bit Translucent |
| 5 | `lambert1` | -1 | -1 | no texture | Untextured |
| 6 | `lambert15` | 6 | 6 | 128x32 | Palette4Bit |
| 7 | `lambert20` | 0 | 0 | 128x128 | Palette4Bit |
| 8 | `lambert21` | 9 | 9 | 128x128 | Palette4Bit |
| 9 | `lambert23` | 12 | 12 | 128x128 | Palette4Bit |
| 10 | `lambert24` | 5 | 5 | 64x64 | PaletteA5I3 Translucent |
| 11 | `lambert6` | 1 | 1 | 64x128 | Palette4Bit |
| 12 | `lambert60` | 21 | 20 | 64x64 | PaletteA5I3 Translucent |
| 13 | `lights:lambert_iceChunks` | 10 | 10 | 128x128 | Palette4Bit |
| 14 | `shaders:light_add` | 15 | 14 | 64x64 | PaletteA5I3 Decal |
| 15 | `shaders:light_add1` | 18 | 18 | 32x128 | PaletteA5I3 Decal |
| 16 | `space` | 19 | 19 | 128x128 | Palette2Bit |
| 17 | `xport1:A4:wall_grid_base` | 22 | 22 | 256x256 | Palette4Bit |
| 18 | `xport2:lambert7` | 14 | 15 | 64x64 | Palette4Bit |
| 19 | `xport:A4:floor_roomc` | 4 | 4 | 128x128 | Palette4Bit |
| 20 | `xport:A5:wall_squares` | 2 | 2 | 128x128 | Palette4Bit |
| 21 | `xport:lambert12` | 11 | 11 | 128x128 | Palette4Bit |
| 22 | `xport:lambert14` | 3 | 3 | 64x64 | Palette4Bit |
| 23 | `xport:lambert16` | 17 | 17 | 64x128 | Palette4Bit |

#### `UNIT 4 ARCTERRA BASE`

22 materials; 21 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A3:lambert11` | 14 | 14 | 64x64 | Palette4Bit |
| 1 | `A4:lambert9` | 18 | 18 | 16x64 | Palette4Bit |
| 2 | `Ice` | 3 | 3 | 128x128 | PaletteA5I3 |
| 3 | `IceEdge` | 2 | 2 | 128x128 | PaletteA5I3 |
| 4 | `Sky1` | 0 | 0 | 128x128 | Palette4Bit |
| 5 | `alimbicEye1` | 11 | 11 | 32x32 | Palette4Bit |
| 6 | `blueGlowStick` | 7 | 8 | 16x32 | Palette4Bit |
| 7 | `bluemetal` | 13 | 13 | 128x128 | Palette4Bit |
| 8 | `lambert1` | -1 | -1 | no texture | Untextured |
| 9 | `lambert11` | 20 | 20 | 128x128 | Palette4Bit |
| 10 | `lambert13` | 19 | 19 | 128x64 | Palette4Bit |
| 11 | `lambert15` | 10 | 10 | 128x128 | Palette4Bit |
| 12 | `lambert16` | 5 | 4 | 128x128 | Palette4Bit |
| 13 | `lambert17` | 4 | 5 | 128x128 | Palette4Bit |
| 14 | `lambert33` | 1 | 1 | 64x64 | PaletteA5I3 Decal |
| 15 | `lambert_alimbicHead` | 12 | 12 | 64x64 | Palette4Bit |
| 16 | `metaltrim` | 15 | 15 | 32x64 | Palette4Bit |
| 17 | `ramp` | 6 | 6 | 64x64 | Palette4Bit |
| 18 | `redGlowstick` | 8 | 7 | 16x32 | Palette4Bit |
| 19 | `redGlowy` | 17 | 17 | 16x32 | Palette4Bit |
| 20 | `snow` | 16 | 16 | 128x128 | Palette4Bit |
| 21 | `walls` | 9 | 9 | 128x128 | Palette4Bit |

#### `Gorea Prison`

13 materials; 13 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `Gorea_b2:col:Gorea_b2:Gorea_b2:blinn36` | 8 | 8 | 64x256 | PaletteA5I3 |
| 1 | `Gorea_b2:col:Gorea_b2:Gorea_b2:root1:blinn28` | 6 | 6 | 32x32 | Palette4Bit |
| 2 | `blinn1` | 0 | 0 | 128x128 | Palette4Bit |
| 3 | `floor1` | 4 | 4 | 256x256 | Palette2Bit |
| 4 | `globe` | 11 | 11 | 128x128 | Palette4Bit |
| 5 | `glow2` | 7 | 7 | 128x128 | Palette4Bit |
| 6 | `gravl` | 1 | 1 | 128x128 | Palette4Bit |
| 7 | `lambert10` | 9 | 9 | 256x256 | Palette4Bit |
| 8 | `lambert11` | 10 | 10 | 256x256 | Palette4Bit |
| 9 | `root1` | 2 | 2 | 256x256 | Palette4Bit |
| 10 | `rootgg` | 3 | 3 | 256x256 | Palette4Bit |
| 11 | `trim` | 5 | 5 | 64x128 | Palette4Bit |
| 12 | `window` | 12 | 12 | 64x16 | PaletteA5I3 |

#### `E3 FIRST HUNT`

41 materials; 39 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:shield2` | 16 | 16 | 64x64 | Palette4Bit |
| 1 | `A:A4:hall_mesh` | 13 | 13 | 128x128 | Palette4Bit |
| 2 | `AlimbicDoorPanel_Material` | 0 | 0 | 64x128 | Palette4Bit |
| 3 | `_Intro_Elevator_blinn72` | 24 | 25 | 128x128 | Palette4Bit |
| 4 | `__FenceColl_lambert2` | 1 | 1 | 64x64 | PaletteA5I3 Translucent |
| 5 | `blinn163` | 5 | 5 | 32x32 | Palette8Bit |
| 6 | `blinn31` | 35 | 35 | 128x128 | Palette4Bit |
| 7 | `computer_1_1SG1` | 6 | 6 | 128x128 | Palette4Bit |
| 8 | `file261Material` | 25 | 24 | 32x64 | PaletteA5I3 Decal |
| 9 | `floortile_2_1SG1` | 11 | 11 | 128x128 | Palette4Bit |
| 10 | `lambert1` | -1 | -1 | no texture | Untextured |
| 11 | `lambert135` | 19 | 19 | 128x16 | Palette4Bit |
| 12 | `lambert140` | 9 | 9 | 128x128 | Palette4Bit |
| 13 | `lambert149` | 12 | 12 | 64x128 | Palette4Bit |
| 14 | `lambert152` | 21 | 21 | 128x128 | Palette4Bit |
| 15 | `lambert159` | 7 | 7 | 64x32 | Palette4Bit |
| 16 | `lambert160` | 8 | 8 | 32x64 | PaletteA5I3 Decal |
| 17 | `lambert161` | 18 | 18 | 64x64 | Palette4Bit |
| 18 | `lambert2` | 14 | 14 | 128x128 | Palette4Bit |
| 19 | `lambert39` | 20 | 20 | 128x64 | Palette4Bit |
| 20 | `lambert4` | 2 | 3 | 64x64 | Palette4Bit |
| 21 | `lambert41` | 15 | 15 | 32x64 | Palette4Bit |
| 22 | `lambert6` | 10 | 10 | 128x128 | Palette4Bit |
| 23 | `lambert86` | 36 | 36 | 16x32 | PaletteA5I3 Translucent |
| 24 | `lambert9` | 3 | 2 | 32x64 | PaletteA5I3 Decal |
| 25 | `leftul_field4` | 30 | 30 | 64x64 | PaletteA5I3 Translucent |
| 26 | `pasted__glass` | 37 | 37 | 128x128 | PaletteA5I3 Translucent |
| 27 | `pasted__pasted__plambert5` | 38 | 37 | 128x128 | PaletteA5I3 Translucent |
| 28 | `pmag1` | 17 | 17 | 64x64 | Palette4Bit Decal |
| 29 | `pmag2` | 17 | 17 | 64x64 | Palette4Bit Unknown3 |
| 30 | `spawnPad` | 22 | 23 | 128x128 | Palette4Bit |
| 31 | `spawnPadLight` | 23 | 22 | 64x64 | PaletteA5I3 Decal |
| 32 | `ul_beam1` | 29 | 29 | 16x128 | Palette4Bit |
| 33 | `ul_beam2` | 26 | 26 | 128x128 | Palette4Bit |
| 34 | `ul_beam3` | 34 | 34 | 16x128 | Palette4Bit |
| 35 | `ul_dome1` | 32 | 32 | 128x128 | Palette4Bit |
| 36 | `ul_field1` | 31 | 31 | 32x128 | Palette4Bit |
| 37 | `ul_light1` | 33 | 33 | 32x64 | Palette4Bit Translucent |
| 38 | `ul_wall1` | 4 | 4 | 128x128 | Palette4Bit |
| 39 | `ul_wall4` | 27 | 27 | 128x128 | Palette4Bit |
| 40 | `ul_wall5` | 28 | 28 | 128x128 | Palette4Bit |

#### `biodefense chamber 06`

2 materials; 2 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `Floor` | 0 | 0 | 128x128 | Palette8Bit |
| 1 | `Wall` | 1 | 1 | 128x128 | Palette8Bit |

#### `biodefense chamber 05`

51 materials; 51 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A1:A:shield1` | 23 | 22 | 128x128 | PaletteA5I3 Decal |
| 1 | `A1:A:shield2` | 22 | 21 | 128x128 | Palette4Bit Translucent |
| 2 | `A:Floor` | 11 | 11 | 128x128 | Palette8Bit |
| 3 | `A:Wall` | 48 | 48 | 128x128 | Palette8Bit |
| 4 | `MetalPlatingC_1SG1` | 5 | 4 | 128x128 | Palette4Bit |
| 5 | `MetroidDoor_MetroidDoor_newScaleDoor_blinn43` | 19 | 18 | 64x256 | Palette4Bit |
| 6 | `_Intro_Elevator_blinn72` | 35 | 35 | 128x128 | Palette4Bit |
| 7 | `__FenceColl_lambert2` | 1 | 1 | 64x64 | PaletteA5I3 Translucent |
| 8 | `blinn163` | 32 | 31 | 64x64 | Palette4Bit |
| 9 | `blinn31` | 12 | 10 | 128x128 | Palette4Bit |
| 10 | `blinn49` | 49 | 47 | 128x128 | Palette4Bit |
| 11 | `blinn68` | 20 | 19 | 128x128 | Palette4Bit |
| 12 | `blinn70` | 18 | 17 | 128x64 | Palette4Bit |
| 13 | `blinn73SG1` | 4 | 3 | 128x128 | Palette4Bit |
| 14 | `blinn79SG1` | 0 | 0 | 128x128 | Palette4Bit |
| 15 | `computer_1_1SG1` | 6 | 5 | 128x128 | Palette4Bit |
| 16 | `file261Material` | 36 | 34 | 32x64 | PaletteA5I3 Decal |
| 17 | `floortile1v2_1Material` | 13 | 12 | 128x128 | Palette4Bit |
| 18 | `floortile_2_1SG1` | 14 | 13 | 128x128 | Palette4Bit |
| 19 | `lambert132` | 9 | 8 | 128x128 | Palette4Bit |
| 20 | `lambert133` | 17 | 16 | 32x128 | Palette4Bit |
| 21 | `lambert134` | 33 | 32 | 32x32 | Palette4Bit |
| 22 | `lambert135` | 25 | 24 | 128x16 | Palette4Bit |
| 23 | `lambert140` | 10 | 9 | 128x128 | Palette4Bit |
| 24 | `lambert149` | 16 | 15 | 64x128 | Palette4Bit |
| 25 | `lambert152` | 29 | 28 | 128x128 | Palette4Bit |
| 26 | `lambert153` | 24 | 23 | 128x128 | Palette4Bit |
| 27 | `lambert156` | 28 | 27 | 64x128 | Palette4Bit |
| 28 | `lambert159` | 7 | 6 | 64x32 | Palette4Bit |
| 29 | `lambert160` | 8 | 7 | 32x64 | PaletteA5I3 Translucent |
| 30 | `lambert2` | 34 | 33 | 128x128 | Palette4Bit |
| 31 | `lambert38` | 27 | 26 | 128x128 | Palette4Bit |
| 32 | `lambert39` | 26 | 25 | 128x64 | Palette4Bit |
| 33 | `lambert4` | 2 | -1 | 64x64 | DirectRgb |
| 34 | `lambert41` | 21 | 20 | 32x64 | Palette4Bit |
| 35 | `lambert86` | 50 | 49 | 16x32 | PaletteA5I3 Translucent |
| 36 | `lambert9` | 3 | 2 | 32x64 | PaletteA5I3 Decal |
| 37 | `leftul_field4` | 41 | 40 | 64x64 | PaletteA5I3 Translucent |
| 38 | `spawnPad` | 30 | 30 | 128x128 | Palette4Bit |
| 39 | `spawnPadLight` | 31 | 29 | 64x64 | PaletteA5I3 Decal |
| 40 | `text3_b_Intro_morphroom_blinn46` | 15 | 14 | 128x128 | Palette4Bit |
| 41 | `ul_beam1` | 40 | 39 | 16x128 | Palette4Bit |
| 42 | `ul_beam2` | 37 | 36 | 128x128 | Palette4Bit |
| 43 | `ul_beam3` | 47 | 46 | 16x128 | Palette4Bit |
| 44 | `ul_dome1` | 43 | 42 | 128x128 | Palette4Bit |
| 45 | `ul_field1` | 42 | 41 | 32x128 | Palette4Bit |
| 46 | `ul_light1` | 45 | 44 | 32x64 | Palette4Bit Translucent |
| 47 | `ul_pipe1` | 44 | 43 | 16x128 | Palette4Bit |
| 48 | `ul_wall1` | 46 | 45 | 128x128 | Palette4Bit |
| 49 | `ul_wall4` | 38 | 37 | 128x128 | Palette4Bit |
| 50 | `ul_wall5` | 39 | 38 | 128x128 | Palette4Bit |

#### `biodefense chamber 03`

11 materials; 11 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A:A1:Floor` | 1 | 0 | 128x128 | Palette8Bit |
| 1 | `A:A1:Wall` | 9 | 6 | 128x128 | Palette8Bit |
| 2 | `A:Floor` | 0 | 0 | 128x128 | Palette8Bit |
| 3 | `A:Wall` | 8 | 6 | 128x128 | Palette8Bit |
| 4 | `A:shield1` | 4 | 2 | 128x128 | PaletteA5I3 Decal |
| 5 | `A:shield2` | 3 | 1 | 128x128 | Palette4Bit Translucent |
| 6 | `lambert1` | 10 | 6 | 128x128 | Palette8Bit |
| 7 | `lambert2` | 2 | 0 | 128x128 | Palette8Bit |
| 8 | `lambert3` | 7 | 5 | 256x256 | Palette4Bit |
| 9 | `lambert4` | 5 | 3 | 128x128 | PaletteA5I3 |
| 10 | `lambert5` | 6 | 4 | 128x128 | Palette4Bit |

#### `biodefense chamber 08`

8 materials; 7 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `A:Floor` | 3 | 3 | 256x256 | Palette8Bit |
| 1 | `A:shield1` | 5 | 5 | 128x128 | PaletteA5I3 Decal |
| 2 | `A:shield2` | 4 | 4 | 128x128 | Palette4Bit Translucent |
| 3 | `lambert1` | 6 | 6 | 256x256 | Palette8Bit |
| 4 | `lambert2` | 3 | 3 | 256x256 | Palette8Bit |
| 5 | `lambert3` | 1 | 1 | 256x256 | Palette8Bit |
| 6 | `lambert5` | 0 | 0 | 128x128 | Palette4Bit |
| 7 | `lambert7` | 2 | 2 | 32x32 | PaletteA5I3 Translucent |

#### `biodefense chamber 04`

1 materials; 1 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert2` | 0 | 0 | 128x128 | Palette4Bit |

#### `biodefense chamber 07`

2 materials; 2 model texture entries.

| Material | Original name | Texture ID | Palette ID | Dimensions | Format / special mode |
| ---: | --- | ---: | ---: | --- | --- |
| 0 | `lambert1` | 1 | 1 | 128x128 | Palette4Bit |
| 1 | `lambert5` | 0 | 0 | 256x256 | Palette8Bit |

### Bundled Dust2 baked texture pack

Located inside `maps/dust2.ppmap` as `textures/map.tex`. This is an imported **multi-texture pack**, not a single native material asset. Pack indices and Q3 shader indices below are not borrowed-room `sourceMaterial` values. Use the existing Q3 import workflow for this palette, or prepare a separate single-texture `.tex` asset per desired native material with the existing texture tools. Do not assign the entire 21-entry pack to one native `material.texture`.

| Pack index | Source shader index | Texture / shader name | Dimensions | Palette colors |
| ---: | ---: | --- | --- | ---: |
| 0 | 0 | `textures/dust2/SANDTRIM` | 64×64 | 256 |
| 1 | 1 | `textures/dust2/SANDCCRETE` | 64×64 | 256 |
| 2 | 3 | `textures/dust2/-0CSSANDWALL` | 64×64 | 256 |
| 3 | 5 | `textures/dust2/SANDWLLDOOR` | 64×64 | 256 |
| 4 | 6 | `textures/dust2/SANDWLLWNDW` | 64×64 | 256 |
| 5 | 7 | `textures/skies/cloudsky` | 64×64 | 256 |
| 6 | 8 | `textures/dust2/SANDROAD` | 64×64 | 256 |
| 7 | 9 | `textures/dust2/SANDCRTLRGTP` | 64×64 | 256 |
| 8 | 10 | `textures/dust2/SANDCRTLRGSD` | 64×64 | 256 |
| 9 | 11 | `textures/dust2/-0SandRock` | 64×64 | 256 |
| 10 | 12 | `textures/dust2/-0SAND` | 64×64 | 256 |
| 11 | 13 | `textures/dust2/-2SAND` | 64×64 | 256 |
| 12 | 14 | `textures/dust2/SANDWLLDOOR2` | 64×64 | 256 |
| 13 | 15 | `textures/dust2/SANDCRTSMTP` | 64×64 | 256 |
| 14 | 16 | `textures/dust2/SANDCRTSMSD` | 64×64 | 256 |
| 15 | 18 | `textures/dust2/MLTRYCRTESD2` | 64×64 | 256 |
| 16 | 19 | `textures/dust2/MLTRYCRTETP` | 64×64 | 256 |
| 17 | 20 | `textures/dust2/AAATRIGGER` | 64×64 | 256 |
| 18 | 21 | `textures/dust2/MLTRYCRTESD` | 64×64 | 256 |
| 19 | 22 | `textures/dust2/GENERIC011` | 64×64 | 256 |
| 20 | 23 | `textures/dust2/+0~FIFTIES_LGT2` | 64×64 | 256 |

### Dust2 source images

The included `maps/dust2/df_dust2.pk3` contains the following original image paths. Preserve spelling/case when extracting or documenting a dependency. These are source images, not ready-to-reference native `.tex` assets. Sky cloud layers and unused utility images explain why this list differs from the baked shader list. The `levelshots` entry is a preview, not a map surface.

| Archive-relative image path | Role |
| --- | --- |
| `levelshots/df_dust2.jpg` | level preview |
| `textures/dust2/+0~FIFTIES_LGT2.jpg` | map texture source |
| `textures/dust2/-0Sand.JPG` | map texture source |
| `textures/dust2/-0SandRock.JPG` | map texture source |
| `textures/dust2/-0csSandWall.JPG` | map texture source |
| `textures/dust2/-2Sand.JPG` | map texture source |
| `textures/dust2/AAATRIGGER.jpg` | map texture source |
| `textures/dust2/BLACK.jpg` | map texture source |
| `textures/dust2/GENERIC011.jpg` | map texture source |
| `textures/dust2/MltryCrteSd.jpg` | map texture source |
| `textures/dust2/MltryCrteSd2.jpg` | map texture source |
| `textures/dust2/MltryCrteTp.jpg` | map texture source |
| `textures/dust2/SandCCrete.JPG` | map texture source |
| `textures/dust2/SandCrtLrgSd.jpg` | map texture source |
| `textures/dust2/SandCrtLrgTp.jpg` | map texture source |
| `textures/dust2/SandCrtSmSd.jpg` | map texture source |
| `textures/dust2/SandCrtSmTp.jpg` | map texture source |
| `textures/dust2/SandRoad.jpg` | map texture source |
| `textures/dust2/SandRoadTgtB.jpg` | map texture source |
| `textures/dust2/SandTrim.JPG` | map texture source |
| `textures/dust2/SandWllDoor.jpg` | map texture source |
| `textures/dust2/SandWllDoor2.jpg` | map texture source |
| `textures/dust2/SandWllWndw.jpg` | map texture source |
| `textures/dust2/WHITE.jpg` | map texture source |
| `textures/skies/cloudsky_1.jpg` | map texture source |
| `textures/skies/cloudsky_2.jpg` | map texture source |

### Room sources unavailable on this machine

These keys were probed but could not supply a material inventory because their required local model assets are missing. They are not selectable recommendations from this catalog. Re-run `ProjectPrime -mapmaterials "ROOM KEY"` after installing the corresponding legitimate source assets.

| Room key | Result |
| --- | --- |
| `Level TestLevel` | Required local room asset missing: testLevel_Model.bin |
| `Level AbeTest` | Required local room asset missing: testLevel_Model.bin |
| `Level MPH Morphball` | Required local room asset missing: e3Level_Model.bin |
| `Level MPH Regulator` | Required local room asset missing: blueRoom_Model.bin |
| `Level MPH Survivor` | Required local room asset missing: mp2_Model.bin |
| `Level MP1` | Required local room asset missing: mp1_Model.bin |
| `Level MP2` | Required local room asset missing: mp2_Model.bin |
| `Level MP3` | Required local room asset missing: mp3_Model.bin |
| `Level SP Morphball` | Required local room asset missing: e3Level_Model.bin |
| `Level SP Regulator` | Required local room asset missing: blueRoom_Model.bin |
| `Level SP Survivor` | Required local room asset missing: mp2_Model.bin |
| `Level FhTestLevel` | Required local room asset missing: testLevel_Model.bin |
| `Level MP5` | Required local room asset missing: mp5_Model.bin |
| `Level MP1b` | Required local room asset missing: mp1_Model.bin |
| `E3 level` | Required local room asset missing: e3Level_Model.bin |

### Catalog verification and refresh

Every available room table was parsed from a successful `-mapmaterials` result and checked against its reported material count. All 21 baked entries were parsed from the included FPTX pack with their source shader IDs, sizes and palette lengths. No cartridge image data was copied into the documentation. Appearance was not visually classified; use material previews for that choice. Availability is specific to this installation and can change with extracted assets or repository updates.

To refresh an individual room: `ProjectPrime -mapmaterials "ROOM KEY"`. Retain both the room key and material index in agent notes. Do not assume an image count is a count of distinct art across the game.
