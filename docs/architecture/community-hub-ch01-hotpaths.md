# Community Hub CH-01 | Transfer UI and allocation hot paths

Base: Project Prime `main` at `ea017489bfc1299af1c85dbb3f1aa185d32a32f8`.

## Changes
- Transfer journal endpoint validation now calls the shared pure URI policy. Reloads and 500 ms cross-process observer passes no longer allocate/dispose a pair of `HttpClient` handlers for every job.
- The Studio creator transfer tab replaces previous rows on rebuild and updates the existing row on progress, retaining the selected receipt and its contextual actions.
- Hosted-map local archive staging uses a 64 KiB asynchronous, cancellation-aware, destination-volume atomic copy rather than `File.ReadAllBytesAsync` and a package-sized `AtomicFile.Write` array. A staged package is rechecked against its exact network identity before it can be used.
- The existing synchronous atomic output helper is preserved; no multiplayer protocol, immutable revision or replay format changes.

## Regression checks
- `tools/community-transfer-check`: URI policy accepts loopback / rejects unsafe addresses, atomic async copy preserves source SHA-256, and cancellation never publishes an incomplete archive.
- Existing multiplayer and integration checks must also run because hosted-map preparation is invoked by lobby/rotation paths.

Suggested commands:
```bash
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/map-multiplayer-check -c Release
dotnet run --project tools/map-install-io-benchmark -c Release -- --mib 32 --repeat 3
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

## Evidence and limitations
The removal of an O(package-size) managed array and an O(job-count) per-poll handler creation is apparent from the edited code. These are allocation-path changes, **not measured throughput/FPS claims**. Runtime benchmarks, 1v1/FFA, Android and replay acceptance remain required. Do not interpret this PR as CH-02 through CH-07 being complete.
