# Map Studio update status

The custom-package runtime, Community distribution, and OBJ/glTF/GLB authoring paths are implemented. Protocol 25 is incompatible with earlier clients and servers.

## Runtime and multiplayer

- Custom maps carry immutable MapId, ContentHash, and PackageHash through host requests, lobby state, and replay bootstrap. Installed-package checks verify the actual archive bytes.
- Lobby clients automatically fetch the exact package over HTTP, verify it, build privately, install, register, and prewarm it. Progress, failure, and retry appear in the lobby.
- Publication waits for scene and prewarm resource leases. Cancellation cannot publish a partial package. Failed installation restores the previous archive.
- Server start and load acknowledgement barriers require exact map readiness. Authority, match, generation, and sequence checks reject stale availability reports. Custom-map rotation returns through a preparation lobby.
- Local server staging copies immutable package bytes. Directory and regional hosts download missing exact packages asynchronously before spawning a child. Requests are deduplicated, bad hashes are rejected, and child package libraries and runtime outputs are isolated from active matches.
- Replay lookup retains the historical package identity and source, and can retrieve or repair that exact package before playback.

## Community

- Publish & Host, existing published-package hosting, and unlisted publication use the same immutable package identity.
- Concurrent downloads, bounded uploads/storage, authenticated publication, version-conflict rejection, persisted unlisted visibility, search/pagination, version history, and exact archive/metadata endpoints are implemented.
- The catalog displays version, author, supported modes, player limits, and installed-version status.

## Model authoring

- Bounded OBJ/MTL import resolves groups, negative indices, concave faces, face-corner UVs, diffuse colors, and PNG/JPEG textures. Unsafe asset paths are rejected.
- Import analysis and preview include scale, axis, winding, UV orientation, collision modes, statistics, and warnings.
- Source manifests support reimport, source relocation, generated-object selection, and detach. Compatible object edits, face painting, UV overrides, and material mappings survive reimport. Unchanged imports do not add history.
- Runtime packages contain normalized meshes and baked textures; private source paths are stripped.
- Material-aware viewport GPU batches retain textures during selection and reuse uploads during camera movement. Collision proxies are separate from normal visual rendering.
- Continuous face-paint strokes produce one undo command, with connected painting, sampling, and base-material restoration. Material usage selection, isolation, replacement, and unused-material deletion are available.
- UV tools include transform, reset, fit, copy/paste, connected/same-material expansion, numbered checker, and texel density. Asset controls include preview, dimensions, source reload, export, and replacement.
- Collision options include visual geometry, simplified coplanar geometry, bounds, and companion OBJ. Surface flags are preserved; diagnostics direct creators to existing collision repair tools.

## Verification

Verified on this macOS development machine:

- Map editor: 376 checks after the next-pass update (see the linked next-pass results).
- Focused model import/reimport/collision: 50 checks (included in the editor suite).
- Community version/concurrency suite: 12 checks; existing Community suite: 21 checks.
- Full real-UDP lobby suite: 5,296 assertions, including exact readiness and stale-report rejection.
- Clean-client HTTP download/build/install/prewarm/start integration, using a self-contained package and real UDP server. Remote host preparation checks cover exact HTTP fetch, retry deduplication, mismatched hashes, cache reuse/repair, cancellation, and private runtime-path consistency.
- Replay format suite: 2,712 checks, including exact custom identity/source metadata round trip.
- OpenGL viewport: 45 checks covering textured pixels, cache reuse, checker switching, picking, and layout.
- CPU viewport-cache benchmarks: 86.08 ms for 50k triangles and 161.35 ms for 100k; selection invalidation averaged 0.0001 ms. These are local microbenchmarks, not graphical frame-rate measurements.

Reproduce with the .NET 10 SDK:

```sh
dotnet run --project tools/map-editor-check
dotnet run --project tools/map-editor-check -- --community-only
dotnet run --project tools/map-editor-check -- --large-model-benchmark
dotnet run --project tools/map-community-check
dotnet run --project tools/nettest -- --lobby
dotnet run --project tools/nettest -- --custom-map-download
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -replayformatcheck
dotnet src/MphRead/bin/Debug/net10.0/ProjectPrime.dll -mapviewportcheck /tmp/prime-map-viewport-check
```

## Remaining limits

- Remote hosts use their configured Community service (`PROJECT_PRIME_MAP_COMMUNITY`, saved Community preference, or the default service). A map published to another service must also be available on that configured service. Later custom maps in a multi-map rotation still need to be installed on the host; the request carries exact identity for its initial map.
- The host archive cache is bounded to approximately 2 GiB plus in-flight downloads. Operators can remove unused files from `hosted-map-packages` under the application user-data directory. Private lobby libraries and generated files are removed when their child is reaped.
- The asset-free multiprocess acceptance runner now covers two clients, rotation, service/server restarts, download failures and historical replay package retrieval. Full interactive gameplay and rendered historical replay still require extracted game assets. Windows/Linux/Android gates are configured but have not been run locally.
- Filesystem changes produce debounced review notifications. Reimport and texture reload remain explicit actions.
- Static glTF/GLB, creator ownership, favorites, reports, and source-folder export are implemented. See [next-pass implementation](map-studio-next-pass.md) for controls, service setup, acceptance commands, and limits.
