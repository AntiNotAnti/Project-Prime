# Community Hub PR-03: Durable Transfer Manager

**Branch:** `codex/community-hub-pr03-durable-transfers-20261013` (stacked on PR #535). **Scope:** persistent, bounded upload/download job state and retryable exact package bytes.

## Service ownership

- `CommunityTransferManager` lives in `Mods/MapGen/Sharing`; no UI dependencies or bearer tokens in stored JSON. Owns up to 32 persisted job receipts and two concurrent workers by default (maximum four), with cancellation and progress state transitions.
- `EnqueueUploadAsync` freezes an immutable `.ppmap` snapshot with a full strict read/hash before the original editor package can be changed or deleted. The existing `CommunityMapOperations` owns ticket refresh, v2 publication, parent conflicts and resumable authoritative server offsets.
- `EnqueueDownload` pins `CommunityMap` identity and uses `MapCommunityClient.DownloadResumableAsync` to preserve an exact-hash `.part` prefix across restarts. Downloads are promoted to a private verified archive only after strict SHA-256 and manifest/MapId/ContentHash validation.
- `StartPending` resumes interrupted `Retryable` transfers after re-authentication. `Paused` stays deliberately paused, `Cancelled` does not resume, and `NeedsReview` preserves immutable upload snapshot and server-side bytes for explicit conflict resolution.
- `VerifiedArchivePath` is only a private transfer result. **Gameplay must still call the existing map installer and register exact map readiness on its owning thread.** Merely downloading a file does not satisfy the lobby Ready barrier.
- New `tools/community-transfer-check` checks snapshot immutability, loopback publishing, exact ranged resume, duplicate download single-flight, receipt recovery and absence of secrets. Existing Community and multiplayer suites remain the network compatibility gate.

## Non-goals and separate follow-ups

- No change to net protocol, multiplayer session state, `.ppmap` encoding, v2 server routes, visibility or historical replays.
- Cross-process locking and full shared CAS eviction are PR-04. This manager is single-process and retains completed archives as a private staging cache.
- The transfer history UI in Game/Studio must not install from a worker thread or publish a partially verified download. PR-03 UI integration is separately gated with real mouse/touch/controller tests.
- No claims of remote throughput improvement from the synthetic loopback acceptance tool.

## Required gates

```bash
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --community-only
dotnet run --project tools/map-editor-check -c Release -- --runtime-only
dotnet run --project tools/map-multiplayer-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
```

Run the full Windows/Linux/macOS and Android CI matrix. Stop on first regression, report expected vs actual and preserve the exact package hashes. Rollback removes the transfer manager, additive client method, test project and UI wiring; the old `MapCommunityClient` path stays available.
