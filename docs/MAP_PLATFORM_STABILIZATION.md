# Project Prime map platform stabilization

The active map pipeline preserves the existing `.fpmap` v2 format, stable map
identity, Q3 import, Prime binary packers, content-addressed cache, and runtime
overlay format.

## Ownership

`MapPlatformService.Shared` is the desktop/Android process boundary for map
catalog mutation and build scheduling. `CustomRooms` remains a compatibility
facade and owns the currently configured catalog instance; the Maps hub and
online acquisition resolve that same instance instead of constructing private
catalogs. Tools, the editor, tests, and Workers may create explicitly scoped
services where process isolation is required.

`MapCompiler.Compile` is synchronous CPU/filesystem work. UI, control-plane,
and Worker callers use `IMapBuildScheduler.BuildAsync`. The scheduler:

- snapshots mutable project DTOs before dispatch;
- runs compilation away from the caller thread;
- shares simultaneous builds for the same fingerprint, cache root, and force
  policy;
- limits total compilation concurrency to two;
- serializes builds that borrow base content until the underlying readers are
  proven safe for parallel access;
- treats caller cancellation as cancellation of that wait, while scheduler
  disposal cancels shared work.

## Dependency authority

`MapDependencyAnalyzer` is the single source of truth for base content,
imported geometry, imported texture packs, custom textures, and preview input.
Fingerprinting, compiler validation, catalog state, editor admission, package
work, acquisition, and Worker preparation flow through that analysis.

Fully custom native maps never open a base-game room. Maps borrowing cartridge
materials require a configured base-content identity and fail with a structured
dependency diagnostic when it is unavailable.

## Worker lifetime

Worker single-flight entries retain `PreparedMapContent`: exact identity,
fingerprint, cache path, and runtime definition. They do not retain
`MatchContentSnapshot` or `MapContentMount` byte payloads. The prepared metadata
cache is bounded at 64 entries, and each match constructs its own immutable
content snapshot.

## Editor history

Editor dirty state compares unique `DocumentStateId` values. `Revision` is only
a monotonic invalidation counter. Undo/redo returns to the history entry's
state identity, and editing after undo always allocates a new identity, so a
branched edit cannot collide with the saved state.

Common create/delete, transform, material, inspector, entity-team,
environment, mode, and overlay edits retain object/property deltas instead of
serializing the complete project. Continuous edits use a transaction key so
many drag updates coalesce into one command. History is bounded to 500 commands
and 256 MiB by default; pruning old entries never reuses a document state ID.

## Editor viewport

`EditorViewportLayout` is the single logical/device-pixel coordinate contract.
It drives camera aspect ratio, scene-target size, destination viewport, mouse
normalization, picking, and preview capture dimensions. The renderer composites
the scene only into the actual center viewport, leaving the surrounding editor
UI in full-window overlay space. Brush picking transforms local intersections
back to world space before comparing distances, including under non-uniform
scale and rotation.

Viewport invalidation is domain based. Geometry, grid, collision, entity,
bounds, and selection meshes have separate caches. Selection rebuilds only the
selection overlay; entity edits and overlay toggles do not re-import or rebuild
map geometry.

## Project portability

Q3 imports copy external BSP/PK3 sources into `source/` and optional texture
packs into `textures/` beside the project by default, recording relative paths.
`--external-source` is the explicit editor CLI opt-in for development workflows
that intentionally retain machine-local absolute references.

## Catalog and failure semantics

Each immutable catalog snapshot precomputes frozen indexes by exact content
identity, stable ID/version, and runtime room name. Exact lookups no longer scan
the complete map list.

Compiler failures carry `CompilationFailureKind`. Missing dependencies map to
`MissingDependency`, unsupported schema/features to `Unsupported`, source and
format-limit failures to `Invalid`, and transient I/O/internal/cancellation
conditions return to `NeedsBuild` rather than permanently poisoning the map.

Normal runtime compilation uses the content-addressed cache and content mount.
AMHE1-style file materialization is now reachable only through the explicitly
named `PrepareLegacyRuntime` compatibility path used by legacy generation
tools.

## CI

`.github/workflows/network-tests.yml` runs map-platform tests in an independent
job. Protocol or server test failure cannot skip content-free map validation;
content-backed characterization runs in the same map job when authoritative
AMHE1 content is configured.

The M1-M10 stabilization sequence is covered by the independent map job;
creator-experience features beyond M10 remain a separate P2 roadmap and do not
change the package, binary, renderer, or multiplayer compatibility contracts.
