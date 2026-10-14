# SecondHunt campaign migration into Project Prime

Status: **CAMP-01 proposed** on `codex/secondhunt-campaign-01-save-management`, based on Project Prime `main` `00776b7c5acbb2769ff9abd1721b00db69a6eb1c` (2026-10-14).
Reference: [BountyHunterKanden/SecondHunt](https://github.com/BountyHunterKanden/SecondHunt) `master` `45c44ea5085f9f728b52c8b4a1543e97b4fe95a4`. Review changed upstream files again before each later phase.

## Decision

**Migrate SecondHunt's campaign experience, not its entire application framework.** Both forks already share the foundational MphRead story simulation. Project Prime already starts native adventure rooms, manages `StorySave`, supports three story slots, and performs post-match save handoff. Replacing `Scene`, `GameState`, input, renderer, or multiplayer with SecondHunt's `CampaignHost` would duplicate authority and risk networking/replay regressions.

SecondHunt provides a substantially more complete **campaign shell**: file-select management, gunship cockpit/star map, pause-map presentation, original menus, cutscene flow, music and movie handling, audio polish, and one-screen campaign HUD. Port these behind existing Project Prime services, one independently accepted feature at a time.

### Non-negotiable invariants

1. Keep the native `Savedata/save001.json` to `save003.json` `StorySave` format and `GameState` serializer; no automatic rewrite to SecondHunt's `CampaignSaveFile` JSON. Existing saves, including from older releases, must continue to load.
2. A new game never overwrites a slot until normal gameplay requests a save. A corrupted save may be archived; it is not silently relabeled as empty and overwritten.
3. Respect the `MatchStart` pending-save handoff and `SceneGameState` ownership. Do not mutate or swap a live campaign scene while managing files in the hub.
4. Keep the 60 Hz gameplay simulation, high-refresh presentation, renderer, dedicated authoritative matches, multiplayer protocol, replay formats and existing community/map identities unchanged unless a separately reviewed phase proves a necessary change.
5. Keep platform-specific input, video/audio device resources, save locations, and Android lifecycle in their existing platform adapters. No parallel window/GL/render stack.
6. Ship no Nintendo ROM, extracted assets, proprietary music, movies, model data, or generated imported content.
7. Preserve notices for reused MIT source; review individual third-party dependencies and their licenses before pulling them into the release.

## Where the two campaigns differ

| Subsystem | Current Project Prime | SecondHunt | Adopt |
|---|---|---|---|
| Story gameplay | Existing MphRead adventure with Prime multiplayer and scene changes | Existing MphRead adventure hosted by `CampaignHost` | **Keep Prime**; use reference behavior tests |
| Story saves | Three `StorySave` JSON slots; resume/new game; post-match save owner | Three managed `CampaignSaveFile` slots, copy/delete, richer file data | **Port file operations**, not file format |
| Ship/star map | Core story data and checkpoint launch; no SecondHunt cockpit shell | `ShipNavigation`, `CampaignShip`, planet states, gunship movies | **Port next**, with stateful UI adapter |
| Movies/endings | Base MphRead story and movie logic | Expanded original menu/movie/ending orchestration | Compare and import only missing behaviors |
| Pause map/HUD | Existing Prime in-game UI/renderer | One-screen/Trilogy-like HUD, explored-room map, scan view | Adapt into existing UI pipeline |
| Audio | Project Prime mixer/notifications/music | DS music enhancements, cockpit/campaign cues, cutscene sequencing | Keep mixer; add presentation adapters |
| Optional extra content | Community Hub, native models and `.ppmap` imports | Prime/Echoes disc import, HD suits, bonus characters/arenas | Optional later project with source-local import |
| Networking/replays | Dedicated server, Netcode 2.1, archival `.ppdemo` | Host-authoritative LAN/online, different replay and saves | **Do not migrate** |

## Ordered PR sequence

### CAMP-01: Save-file experience (this branch)

- Add raw byte-preserving copy between existing Project Prime story slots. An occupied destination is backed up under a never-reused `.bak`, `.bak001`, etc. path.
- Archive rather than permanently delete a slot. An unreadable save is shown as unreadable and cannot be silently launched over without first archiving it.
- Add Copy into / Copy selected save / Archive selected save controls to the normal Story screen only (not over an active game).
- Require a second explicit selection before replacing an occupied destination or archiving a slot. Changing the source/destination cancels confirmation.
- Add an asset-free regression suite `dotnet run --project tools/nettest/nettest.csproj -c Release -- --campaign-save-management`. Run it in the scene ownership workflow alongside `--post-match-save`.
- **Not in this PR:** importing SecondHunt's save schema, changing story serialization, world load/teardown or adding gunship UI.

Acceptance: byte-identical copy; backups never clobbered; empty/missing/bad slots handled without data loss; exact source slot remains unchanged until archive; I/O failures preserve originals and clean temp copies; multiplayer/legacy save and scene-lifecycle checks stay green. Confirm actual keyboard/controller/touch focus and two-click confirmation before merging. CI must pass on the exact PR head.

### CAMP-02: Campaign state and ship-navigation adapter (implemented on stacked branch; CI pending)

- Extract *data-only* planet/area/landing-room catalog and unlock/visit/completion calculations from SecondHunt `ShipNavigation.cs`.
- Reference existing `StorySave`, `Metadata`, `GameState.GetAreaState`, and `SceneGameState`, without a second campaign save owner.
- Classify ship-entry, normal quit, bad/good ending and return to menu from established scene state and `Menu.NeededSave`; guard pending commits.
- Add fast asset-free and asset-backed parity fixtures for all five planets, checkpoint/teleport transitions, and ending flags.

### CAMP-03: Gunship cockpit, star map, and file select flow

- Adapt SecondHunt `CampaignShip.cs` and native frontend interactions to Project Prime's active UI/scene service, rather than importing SecondHunt's Android/Windows GL shell.
- Build and verify planet-select transitions, cockpit sequence IDs, landing-site launches, locked destinations, scanning/navigation, file A/B/C, ship returns and cancel/back behavior.
- Keep original cartridge-sourced presentation assets local; do not publish them to Community Hub.

### CAMP-04: Campaign media and ending flow

- Add/reconcile `CampaignMovie` sequences, landing/takeoff/intro/escape movies, skipping, pause/resume, ending variants, credits and return-to-file select.
- Keep existing video decoder/renderer and shared audio ownership; match original triggers and save boundaries.
- Run a room/portal/movie/ship loop on desktop and Android; test teardown/cancel/recreate.

### CAMP-05: One-screen HUD, scan visor and pause-map

- Adapt native first-person presentation for dialogue, weapon ring, HUD, scanning, logbook, explored-room pause map, and touch/controller input.
- Preserve Project Prime's competitive HUD, 60 Hz simulation, replay/camera invariants, and no-touch multiplayer controls.
- Add viewport/occlusion, 16:9/ultrawide and phone aspect-ratio goldens.

### CAMP-06: Music, sound and campaign polish

- Compare DS menu/cutscene/escape/cockpit audio with SecondHunt's sequencer and quality options; port isolated missing fixes through Project Prime's existing mixer.
- Preserve notifications, multiplayer sound levels, render pacing, file ownership and device/lifecycle behavior.
- Consider extra HD imported content only as a separately accepted local importer.

### CAMP-07: End-to-end campaign acceptance and cleanup

- Fresh file -> opening -> first scan -> ship -> planet unlock -> save/continue -> late game -> both endings -> credits.
- Original cartridge reference comparisons; Windows, Linux, macOS, Android; cold/warm boot; 60/120/240 Hz present with stable simulation.
- Save byte parity and compatibility, renderer/media/scene lifetime, previous replay/protocol compatibility, memory/frametime regressions.
- Retire temporary adapters only after proving parity; do not remove legacy campaign behavior prematurely.

## Reference code

- [SecondHunt CampaignSaves.cs](https://github.com/BountyHunterKanden/SecondHunt/blob/master/src/MphRecomp.Core/Campaign/CampaignSaves.cs)
- [SecondHunt ShipNavigation.cs](https://github.com/BountyHunterKanden/SecondHunt/blob/master/src/MphRecomp.Core/Campaign/ShipNavigation.cs)
- [SecondHunt CampaignShip.cs](https://github.com/BountyHunterKanden/SecondHunt/blob/master/src/MphRecomp.App/Campaign/CampaignShip.cs)
- [SecondHunt CampaignMovie.cs](https://github.com/BountyHunterKanden/SecondHunt/blob/master/src/MphRecomp.App/Campaign/CampaignMovie.cs)
- [Project Prime AdventureSave.cs](../../src/MphRead/Mods/Launcher/Portable/AdventureSave.cs)
- [Project Prime MatchStart.cs](../../src/MphRead/Mods/Launcher/Portable/MatchStart.cs)
- [Project Prime StorySaveCommit.cs](../../src/MphRead/StorySaveCommit.cs)

## Verification and rollback

Run the CAMP-01 test, `--post-match-save`, `--scene-lifetime`, `--foreground-binding` and the normal relevant build/CI jobs. The regression command is **added but not claimed to have passed until CI actually runs on this branch**. The automation environment used for this PR has no local .NET SDK, so native builds, media playback and device acceptance remain unverified here.

CAMP-01 can be reverted without migrating `StorySave` or modifying any previous `Savedata/save###.json` format. A player's existing `.bak*` archives remain untouched by a rollback. This migration must never silently overwrite production saves or pretend a narrower device-free test establishes completed campaign acceptance.

## CAMP-02 implementation notes

The campaign planet catalog lives in `src/MphRead/Mods/Campaign/CampaignNavigation.cs` and projects the native `StorySave` area bits, checkpoint, area states and collectible counts. It carries the original cockpit camera-sequence IDs as metadata, not rendering code. `Classify` is intentionally pure: callers must supply a verified ship-entry signal, rather than guessing based on `Menu.NeededSave` policy, because the user may configure the same policy for normal quit and ship entry. The default game-save serializer and current scene ownership remain unchanged. The asset-free `--campaign-navigation` test is included in the existing scene-ownership workflow.

Next, CAMP-03 may consume this projection in the existing hub without duplicating `CampaignHost`, `Scene`, or MphRead's native save format. A full 3D in-ship cockpit and actual takeoff/landing movies remain coupled to the existing renderer and media lifecycle; adding names and camera-sequence IDs alone does not prove those features.
