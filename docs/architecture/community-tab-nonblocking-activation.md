# Community Hub | Nonblocking tab activation

Base: `AntiNotAnti/Project-Prime` main `55c5ca4ab3dbbfce080aa60f76041526d1ad72f7`.

## Symptom and source analysis

The Community workspace constructor previously synchronously accessed `CommunityTransferSession.Shared`. This runs `CommunityTransferManager` construction, filesystem directory creation, OS-wide `index-v1.lock` acquisition, full jobs JSON validation and crash recovery. The lock retries for up to ten seconds. The `OnActivated` path then synchronously called `StartPending()` (which takes the same index lock). All of this happened inside `PrimeWorkspaceHost.Show` on Avalonia's UI thread, before the tab was painted.

The Downloads tab also called `ListJobs` from `RenderList`, and the map preview handler decoded a PNG synchronously inside the UI continuation. Service address resolution can read `map-community.txt`, and parsing legacy catalog results can be nontrivial. These are avoidable sources of hitching.

## Fix

- Construct/start the durable shared queue asynchronously on a worker. The UI can show Discover and a loading placeholder without waiting. Normal auto-retry of interrupted jobs, per-job ownership leases and the frozen upload journal remain intact; paused/cancelled jobs do not auto-resume.
- Downloads is backed by asynchronously retrieved immutable job snapshots, with one in-flight read and a coalesced refresh flag. No `ListJobs` in the Avalonia render path; an active pop-out Studio holding the index lock cannot freeze tab navigation.
- Move client setup and catalog processing to a worker, while keeping the final controls/results on Avalonia's UI thread. Capture search/sort/page/filter values before dispatch.
- Resolve preview URLs and decode bounded map PNGs on worker threads, disposing any cancelled stale bitmap without displaying it.
- Pause/Resume/Cancel/Forget controls perform index transactions off-UI.
- Existing offline Installed tab, map installation authority, exact MapId/content/package hashes, v2/v3 service contracts and historical replay behavior are unchanged.

## Tests and acceptance

`PrimeUiChecks` includes an intentionally stalled transfer factory and verifies OnActivated returns within a bounded budget before releasing that background operation; fixture mode remains disconnected from the user's real transfer directory.

Run:
```bash
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet run --project tools/community-transfer-check -c Release
dotnet run --project tools/map-community-check -c Release
dotnet run --project tools/community-service-check -c Release
dotnet run --project tools/map-multiplayer-check -c Release
```

The normal launcher UI regression suite, Game/Studio cross-process handoff and actual Windows/Linux/Android rapid tab re-entry should be checked, including a large jobs index, slow server, malformed PNG, queued transfer and held cross-process `index-v1.lock`. Measure before/after p95 UI frame time during Community entry. No actual hardware frametime improvement is claimed from code inspection alone.
