# Map Studio next-pass implementation

## Authoring

Face, edge, and vertex selections now share `MapSubSelection`. Click replaces, Shift adds, Ctrl toggles, and box selection selects multiple elements. Double-click selects a face island or a regular quad edge loop. Ctrl+A selects all, Alt+A clears, L selects linked, and Ctrl+plus/minus grows/shrinks.

Use 1–4 for Object/Face/Edge/Vertex; G/R/S starts a transform, X/Y/Z constrains an axis, Shift+axis constrains a plane, numeric input sets the amount, Enter commits, and Escape cancels. S, Z, 0 flattens vertically. Sub-element previews use detached vertex arrays; the document receives one compact history command on commit. Median, active, cursor, world pivots and local/world orientations apply to sub-elements.

The modeling bar, context menu and numeric inspector expose E extrude, I inset, B bevel, M merge, X delete and F fill, plus split/dissolve/collapse/slide, targeted merge, connect/rip, flatten, duplicate/separate/join, triangulate, winding repair and cleanup. Collapse supports center, A, B and cursor. Merge first/last follows vertex selection order. Face-index UV overrides are remapped when faces are removed or triangulated.

`MapMeshTopology` supplies transient half-edge adjacency, islands, boundaries, loops and non-manifold diagnostics. `MapMeshValidator` reports errors, warnings and cleanup suggestions; failed modeling operations leave the document unchanged. General authored-brush CSG accepts box, wedge, prism and convex brush inputs, including rotation, and emits editable meshes with source materials, UVs and physical properties.

### Deliberate geometry limits

- Edge loops require regular quad topology. Edge bevel currently requires manifold endpoints incident to three faces and supports 1–8 segments. Ambiguous topology is rejected.
- Region inset uses a center-relative ratio on a planar connected region; it is not a constant-width offset algorithm.
- Non-manifold, open-collision and winding diagnostics can be warnings rather than blockers. Self-intersection detection is local to face polygons, not a full triangle-intersection solver.
- Arbitrary imported triangle-mesh booleans remain outside this pass. CSG clipping can introduce coplanar subdivisions; inspect Problems after complex cuts.

## Model sources and project folders

Static glTF 2.0 and GLB import supports scenes, node transforms, triangle primitives, UV0, base colors, PNG/JPEG textures, external and embedded buffers, and data URIs. Animated/skinned/morph/PBR details are not rendered; unsupported features produce warnings and required extensions are rejected. Source paths and memory budgets are validated.

Source manifests fingerprint the main file plus buffers, materials and textures. A 500 ms debounced watcher reports changes without modifying the map. File → Source changes offers explicit review/reimport/reload/ignore. Reimport previews report object/vertex/face/material counts, added/changed/removed objects and preserved edits.

Export project folder writes a new portable directory containing `map.json` and referenced models, textures, collision and audio files. External model graphs must remain inside their source directory. Serialization uses stable IDs/order and remaps material indices; it retains values necessary for correct default handling. Runtime packages continue stripping private source paths.

## Community creator setup

Normal creator identity is Hunter License-backed. Map Studio asks
`HunterLicenseClient` for a short-lived `ppm1` Community ticket and sends only
that ticket to the map service. The player's Supabase access/refresh tokens remain
inside the launcher. The `community-map-ticket` Edge Function mints and verifies
the ticket; the map service caches the verified `hunter:<uuid>` creator identity
only until ticket expiry.

The filesystem `creators.json` registry remains supported for operator-managed
service accounts and migrations. The root-only `PROJECT_PRIME_MAP_UPLOAD_TOKEN`
remains the `service-owner` moderator credential and must not be handed to normal
creators.

Owners and explicitly authorized collaborators may publish subsequent versions.
Existing version package bytes cannot be replaced. Publish a new version instead.
Published, unlisted and private draft visibility can change independently of archive
identity. Draft access requires owner/collaborator authentication; unlisted exact
links remain accessible.

The Community UI includes My Maps, My favorites, favorite ordering,
favorite/unfavorite, report reasons/details and visibility controls. Reports never
automatically delist content. Moderators use `GET reports` and
`POST reports/{reportId}`; statuses are Open, Reviewed, Resolved and Dismissed.

## Acceptance and reproducibility

Run with .NET 10:

```sh
dotnet run --project tools/map-editor-check
dotnet run --project tools/map-editor-check -- --next-pass-only
dotnet run --project tools/map-editor-check -- --model-import-only
dotnet run --project tools/map-editor-check -- --collision-only
dotnet run --project tools/map-editor-check -- --community-only
dotnet run --project tools/map-editor-check -- --runtime-only
dotnet run --project tools/map-multiplayer-check -- /tmp/prime-map-acceptance
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -mapviewportcheck /tmp/prime-map-viewport
```

The multiplayer output folder must be empty. The runner creates separate Community, server and client processes with isolated package/runtime directories and saves their logs. It covers two-map preparation/rotation, readiness, preparation disconnect, server/service restarts, exact hashes, truncated archives/HTTP, mid-stream cancellation, wrong-version substitution, concurrent downloads and a real replay header requesting historical v1 after v2 is available. It uses the existing asset-free server authority fixture and runtime-model decode; full gameplay/rendered replay still requires the game's extracted assets.

CI runs editor/import/collision/community and real-GL viewport checks on Linux, Windows and macOS. Linux uses Xvfb. Android runs the portable package/download/build/register/decode gate in a debug-only activity on an emulator; this does not open the editor UI. `tools/map-multiplayer-check/android-check.sh` drives that gate. The diagnostic activity and loopback cleartext permission are Debug-only.

Local verification is performed in a managed validation worktree to avoid concurrent unrelated gameplay edits in the shared checkout. Cross-platform CI and Android emulator results require those runners; adding the gates does not establish that they have passed.

### Local results

- Final shared-workspace desktop build: succeeded, 0 errors (21 warnings).
- Full editor suite: 376 checks passed; final focused next-pass rerun: 33 checks passed.
- Community ownership/favorites/reports suite: 20 checks passed.
- Portable package/download/runtime check: passed.
- macOS real OpenGL viewport: 48 checks passed.
- Separate-process two-client/two-map acceptance, restarts, HTTP fault injection and historical replay package retrieval: passed. Logs: `/tmp/prime-map-multiplayer-acceptance-4`.
- Windows/Linux CI and Android emulator: configured, not executed locally. No Android SDK/emulator was available at the local SDK paths.
