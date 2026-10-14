# Community Hub PR-14 | Safe cutover preparation, cleanup and release readiness

**Repository:** `AntiNotAnti/Project-Prime`  
**Stack:** PR-10 #546 -> PR-11 #547 -> PR-12 #549 -> PR-13 #551 -> PR-14 (this draft).  
**Status:** implementation and review staging. **No production cutover or default-editor change.**  
**Test policy:** Creator/user has deliberately deferred actual NetRadiant, Q3Map2, game-device, multiplayer and replay acceptance. These are **not passed**.

## What changed

- `PrimeRoute.Community` already provides a game-level Discover / Installed /
  Favorites / Downloads workspace independent of Map Studio. PR-14 does not
  replace, redirect or reimplement it.
- The creator side now has `StudioCreatorCommunityWorkspace`, a separate
  UI component with **My Uploads**, **Transfers**, and **Publish**. It depends
  on neutral `CommunityMapOperations`, `CommunityTransferManager`, and
  the frozen `CommunityStudioPublishIntent`, not an active `MapDocument`.
  It is reachable through the existing Map Studio **Online -> Community maps**
  command only when explicitly enabled for preview.
- `CommunityStudioPublishIntent.Capture` detaches one authoring snapshot
  (MapId and runtime name fixed). On a user-selected publication, it obtains
  owner-only revision metadata, binds an optimistic parent hash, builds a
  verified self-contained `.ppmap` through `MapBuildScheduler`, then queues
  a frozen verified archive in the persistent transfer manager. Package
  building and upload are asynchronous. The transient build file is deleted.
- The game/player Community view and creator preview use one shared process
  `CommunityTransferSession` receipt index and queue. Worker callbacks
  never retain editor controls. Disposing the preview cancels only its
  foreground operation; a queued upload continues independently.
- A stale parent is **never** silently overridden. Conflict receipts require
  explicit review through the existing **Advanced Revisions** path.
- Existing Map Studio Community dashboard, creator lifecycle controls, JSON
  Map Editor, stand-alone Studio pop-out, source import, Publish & Host, and
  local/offline playtest remain accessible. The legacy dashboard is a rollback
  path, not a second actively enabled cutover route by default.

## Default-off activation controls

| Control | Default | Behavior | Revert |
|---|---|---|---|
| `PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW=1` | unset/off | Preview the independent Studio creator workspace under Online > Community maps | Unset, or set anything other than `1` |
| `PROJECT_PRIME_NETRADIANT_DEFAULT` | always **off** in PR-14 | Reserved for a future separately approved cutover; setting it currently **does not** make NetRadiant the default | Nothing to undo |
| `PROJECT_PRIME_NETRADIANT_EXE`, `PROJECT_PRIME_Q3MAP2_EXE` | optional / unset | PR-12 explicitly configured external editor/compiler only | Unset; native JSON Studio remains supported |
| `PROJECT_PRIME_TRUSTED_CDN_HOSTS` | unset/off | Existing optional trusted CDN download path with origin fallback | Unset to restore origin-only |

No new server protocol/version negotiation, v2 route removal, map archive
format requirement, user-file migration, upload identity change or replay
retention policy is introduced by these flags.

**Do not enable the creator preview by default or remove Advanced Revisions**
until its actual Windows/Linux/macOS UI, owner permissions, revisions,
transfer restart and screen navigation tests pass. Android continues to use
the player-facing Community workspace, not the desktop creator editor.

## Source format and package compatibility

- PR-13 supports a strict subset of classic Quake .map solids, convex brushes,
  per-face classic shader/UV projection, canonical neutral spawns and pickups,
  and bounded `patchDef2` Bezier control grids.
- Unsupported `patchDef3`, `brushDef3`, shader scripting and unknown game
  entities remain explicit errors, not data silently discarded.
- Editable native authoring control grids remain in JSON and .map. The
  package builder lowers them to already-supported `MapMesh` with explicit
  materials/UVs in the distributed `.ppmap`; package SHA-256 remains the exact
  immutable network/replay identity.
- Existing historical `.ppmap` archives, cached exact revisions and source
  JSON projects are never upgraded or rewritten during cutover.
- Old client + new service still uses v2/legacy map routes; new client + old
  service has the existing read fallback. These are architecture contracts,
  **not an assertion that the deferred all-version real-client matrix ran**.

## Evidence currently available

| Gate | Source | Status |
|---|---|---|
| PR-13 direct source round trip | [Focused Actions 38620034073](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38620034073) | **56 PASS** on historical exact head `1c00510f` |
| PR-13 real asset-free .ppmap scheduler, readback, rollback | Same focused run | **12 PASS**, Linux fixture; exact source/package ID verified |
| PR-13 full CI and desktop package matrix after hardening | [Actions 38621114155](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38621114155) and build 38621114022 | Reported success on PR-13 head `7a2ea3d` (check full job evidence before release) |
| PR-14 creator preview + optimistic revision upload | `tools/community-transfer-check`, new handoff fixture, normal build CI | **Pending exact-head PR-14 CI** |
| Windows/Linux/macOS creator preview actual interaction | Manual acceptance | **Deferred at user's request** |
| Actual NetRadiant + q3map2 open/save/compile | Manual editor/compiler acceptance | **Deferred** |
| In-game render/collision/audio/1v1/8-player maps | Manual device/game acceptance | **Deferred** |
| Android installation, join, device/slow-network stress | Manual device acceptance | **Deferred** |
| Historic replay open/export on released builds | Manual replay acceptance | **Deferred** |
| Staging service v2/v3, optional assets, CDN failover and production flags | Staging and operations | **Not activated / not certified** |

Do not infer device or live-load performance from a desktop/headless compile.

## Compatibility and final acceptance checklist

- [ ] Old client/old service; old client/new service; new client/old service;
      new client/new service, including public/unlisted/draft visibility
- [ ] Creator create/new revision/conflict/rollback/deletion, ownership and
      short-lived ticket refresh; no mutation of old immutable revisions
- [ ] Freeze a modified editor document; queue publication; close/restart
      Studio; source mutation/removal must not change uploaded archive bytes
- [ ] Player Game Community discovery/installed/favorites/downloads; 1v1,
      8-player FFA, host rotation across multiple Community maps, Ready and
      spectator/disconnect state
- [ ] NetRadiant .map (axial, angled, mixed UV, patch, entities) -> Project
      Prime JSON -> .ppmap -> offline render/collision/playtest; unsupported
      source must be rejected without overwriting either source file
- [ ] Real Windows/Linux/macOS editor open/save, input, game launch/return,
      DPI scaling, Escape/back/focus, Reduce Motion and crash recovery
- [ ] Android arm64, emulator/device, GLES memory, install/join and throttle
- [ ] Historical replay playback/export, immutable MapId/content/package
      SHA-256, old published map revision retrieval
- [ ] SDK/build packaging, no commercial assets, NetRadiant fork licenses,
      dependency notices and code signing before any third-party editor bundle
- [ ] Telemetry baseline vs enabled feature (metadata latency, transfer
      bytes, time to Ready, memory, disk, frame-time, error distribution),
      rollback rehearsal and signed release notes

## Rollout procedure (future, NOT performed by this PR)

1. Merge upstream PR-10 through PR-13 in dependency order after their actual
   gates. PR-14 stays staged until conflicts are resolved against the merged
   source tree.
2. Publish a staging-only candidate with creator preview **off**; compare
   v2/v3 catalog, package identities and the frozen no-regression baselines.
3. Enable creator preview only for QA/test builds via the exact opt-in flag.
   Collect error/memory/transfer evidence and log all unsupported source
   formats. Keep the full legacy dashboard reachable in the same session.
4. Once authoring/editing parity is demonstrated, separately approve a user
   default. Do not automatically enable NetRadiant or third-party CDN,
   remove JSON Studio, or modify package/readiness semantics.
5. Stage a small canary cohort with explicit rollback and incident owner;
   expand only after the full compatibility matrix and release-signoff.

## Immediate rollback

1. Unset `PROJECT_PRIME_STUDIO_COMMUNITY_HUB_PREVIEW`; relaunch the editor.
   **Online -> Community maps** returns to the existing creator dashboard.
2. Unset `PROJECT_PRIME_NETRADIANT_EXE`,
   `PROJECT_PRIME_Q3MAP2_EXE` and `PROJECT_PRIME_TRUSTED_CDN_HOSTS`
   if external tooling or CDN causes a problem. The JSON Studio and
   origin-backed v2 map flow remain available.
3. Retain all installed/cache `.ppmap` exact files, v2 API handlers,
   historical revision indexes, replay manifests and transfer receipts.
   Never purge creator data as part of a UI flag rollback.
4. For a build rollback, deploy the previous tested release/server pair
   while continuing to serve existing immutable revisions; preserve a
   tested metadata backup and reversible migration journal. No
   automatic rollback of user-generated map assets is permitted.

## Release notes (candidate; version and deployment date unassigned)

**Community Hub 3.0 Creator Preview:** New opt-in Studio Community creator
workspace for owned map listings, durable transfers and draft/unlisted/
published immutable snapshot uploads. Existing player Community and Map
Studio flows remain the default. NetRadiant direct source import now supports
validated classic brush, UV and bounded patchDef2 interchange with strict
failure diagnostics. Unsupported formats and proprietary texture assets are
not bundled. This preview is not a production release until manual platform,
gameplay, staging, replay and rollback testing is complete.
