# Community Hub PR-02: player Community workspace

Baseline: stacked on PR-01 (which is stacked on PR-00). The current `main` shell is Avalonia; this slice does not assume the older merged standalone Studio PR #367 or the closed RmlUi candidate is the current UI.

## Delivered

- Add `PrimeRoute.Community` to keyboard/controller/Q-E navigation and an independent Community header tab, preserving existing route enum values. Shorten other visible tab labels to avoid crowding the eighth tab.
- Add `CommunityWorkspace` on the game shell (including Android), separate from `MapStudioScreen`. Uses PR-01's `CommunityMapOperations` to browse the *existing* public v2 grouped map catalog and exact-version installs.
- Four faces: Discover, Installed (local/offline), Favorites (authenticated) and Downloads (currently active transfer only; honest empty state until PR-03 durable queue).
- Details show owner, supported modes/player range, protocol requirements, exact installed-version state; controls install, favorite, report, cancel.
- Installation calls the existing `MapCommunityClient.InstallAsync`, then `Metadata.RegisterDownloadedMap` on the owning UI thread. It honors `GameFiles.Ready`, `MapRuntimeUsage` and the exact package hash. Existing lobby map picker remains unchanged.
- Workspace cancellation is scoped to activation/deactivation. A committed installation is registered even if navigation was cancelled after publication.
- GUI fixture route creation uses a synthetic project without network access; verifies tab/action navigation and content selection.

## Compatibility

No change to map encoding, custom map identity, multiplayer protocol, service endpoints, replay package retrieval, readiness barrier, installed archive naming or Android gameplay runtime. Editor creator publishing remains available from Map Studio while the independent Studio tab awaits a verified standalone Studio shell.

## Explicit deferrals

No durable download/resume/job history yet (PR-03/04). Local uninstall/repair requires safe eviction and scene lease integration (PR-04/05). Generic textures/models require v3 schema (PR-08/09). A direct "Use in Lobby" deep link will be enabled only when a verified lobby selector contract exists; current UI guides players to the existing Create Lobby rotation picker.

## Acceptance gates

- Game Community tab opens independently on desktop and Android, with offline Installed view.
- Discover/favorites/authenticated actions and exact install work with the configured Community service.
- Mouse/touch/keyboard/gamepad focus and small-window captures through existing `PrimeUiChecks`, in addition to loopback headless tests.
- No source or visual regressions to Map Studio, matchmaking, lobby map picker, and historical replay.
- Full cross-platform CI and Android runtime smoke (compile alone is not on-device acceptance).

Do not merge PR-02 ahead of the successful PR-00 and PR-01 gates. Rollback is the new route + new view + test fixture only, without data migration.
