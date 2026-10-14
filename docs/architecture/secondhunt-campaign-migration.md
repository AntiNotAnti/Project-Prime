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

### CAMP-03: Gunship cockpit, star map, and file select flow (guarded launcher console implemented; 3D rendering/media still pending)

- Adapt SecondHunt `CampaignShip.cs` and native frontend interactions to Project Prime's active UI/scene service, rather than importing SecondHunt's Android/Windows GL shell.
- Build and verify planet-select transitions, cockpit sequence IDs, landing-site launches, locked destinations, scanning/navigation, file A/B/C, ship returns and cancel/back behavior.
- Keep original cartridge-sourced presentation assets local; do not publish them to Community Hub.

### CAMP-04: Campaign media and ending flow (implemented source slice; CI and native validation pending)

- Add/reconcile `CampaignMovie` sequences, landing/takeoff/intro/escape movies, skipping, pause/resume, ending variants, credits and return-to-file select.
- Keep existing video decoder/renderer and shared audio ownership; match original triggers and save boundaries.
- Run a room/portal/movie/ship loop on desktop and Android; test teardown/cancel/recreate.

### CAMP-05: One-screen HUD, scan visor and pause-map (campaign-only integration in review)

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

## CAMP-03 integration status

- The original `Scene.QuitGame(enteringShip:true)` is the authority for whether the player actually boarded a gunship. Captured departure reason and room ID are value-only fields on its `SceneGameState`. The existing immutable `PostMatchCommitContext` carries those values across teardown, and `MatchStart` publishes an ephemeral `CampaignDockReceipt` **only after a successful native Adventure save commit**. A normal quit, an ending, an unrecognized room or a save policy of Never never authorizes travel.
- The active `OfflineWorkspace` offers a new gunship console / star map modal once the receipt's save slot is selected. It presents all five destinations, lock/visit/clear state, Octolith and artifact counts, and original cockpit camera-sequence IDs; selection controls Exit Ship and Land Ship. `StartScreen.OpenCampaignShip()` returns to this UI after real ship boarding on desktop and Android.
- `AdventureLaunch.Create` and `LaunchPlan.AdventureDestination` carry an optional destination. `MatchStart.BeginAdventure` and `AndroidMatch.BuildAdventure` independently check the selected slot, the trusted in-process ship ticket and the latest saved planet unlock before overriding the launch's initial room. The native `StorySave` checkpoint remains untouched by this choice; entry into the real landing room uses the existing MphRead scene pipeline.
- **Important distinction:** the current cockpit is a functional Project Prime **UI navigation console**, not SecondHunt's 3D `CampaignShip.cs` view or original cutscene/menu texture renderer. Those visual/media assets and their lifecycle are deferred to CAMP-04. The functional gunship transition is now connected without bringing a second GL renderer, audio backend, movie player, or `CampaignHost` into Project Prime.
- The `--campaign-ship-flow` acceptance command exercises zero-ticket/locked/fresh/mismatched-slot refusals, both valid planet visits, successful commit publication, identical-policy normal quit, Never policy, Gorea ending, and invalid ship room. It runs with CAMP-01 and CAMP-02 contracts in scene-ownership CI.

Before merging CAMP-03, verify actual desktop and Android campaign boarding, file select, controller/touch focus, and planet loading with licensed user game files. This is a stacked draft until CAMP-01 and CAMP-02 are accepted. Do not claim rendered in-ship cockpit or movie acceptance on the strength of the headless tests alone.

## CAMP-04 source integration and acceptance boundary

- **Movie IDs and shared transitions:** The original landing and takeoff VX mappings remain owned by `Scene`. `CampaignMovieCatalog` provides one ROM-derived lookup and preflight for five landings, four takeoffs, the Gorea endings, local top/bottom movie paths and bounded decoder startup. This does not add or ship any video file, GL backend or separate player. The existing scene starts landing movies, ship-hatch takeoffs, Octolith pickups, boss intros and both endings. New Adventure runs explicitly chain the cartridge's `StoryIntro` and `CALanding` movies before opening the scene, unless the player has disabled planet intros.
- **Fail-safe movies:** `Scene.PlayMovie` now tests that the required local top/bottom VX streams exist, waits no longer than three seconds for decoder startup, accepts valid short videos, and follows the original `AfterMovie` path when a film is missing or corrupt. `StopMovie` cancels this playback's task token and frees acquired music/SFX mute ownership and audio resources even for endings and chained videos. The old repeated 10 ms unbounded wait could strand the owner thread indefinitely. A genuine corrupt or invalid file is a skip/fallback, not a successful visual playback claim. Error timing and audio ownership still need native-device validation.
- **Gorea completion:** A value-only `CampaignEndingSummary` captures the incomplete Gorea 1 / complete Gorea 2 result, available completion %, Octoliths, deaths and defeats before scene teardown, through the existing `PostMatchCommitContext`. It is published after the save handoff completes; a Never-save policy is labelled unsaved. Normal quit and ship boarding cannot publish it. Both desktop and Android return to an accessible Project Prime mission-results/credits modal rather than mistaking an ending for a gunship visit. Existing Project Prime credits are clearly identified as Project Prime credits, not falsely represented as the Nintendo DS frontend's original credits.
- **What remains visually unported:** SecondHunt's in-ROM animated ship menu, 3D preloaded cockpit, deep-space star-map animation, exact seven-page DS credits/screens/results and original cockpit music choreography are **not** copied into Project Prime by this PR. CAMP-03 still uses its functional launcher ship console. Movie startup, GL capture, codec/audio backends and native Android/desktop scenes must be verified with the player's own extracted cartridge files before declaring full visual-campaign parity.
- **Regression entrypoints:** `--campaign-movies` and `--campaign-endings` run without private game assets in `scene-ownership` alongside CAMP-01/02/03 checks. Then run build, actual game-file cinematic play/skip/chain tests, Android pause/resume and teardown, good/bad Gorea finishes, repeated startup, 16:9 and ultrawide UI, and the full lifecycle and replay compatibility gates. No protocol, save schema or shipped game content changes are intended.

## CAMP-05: Campaign HUD, scan archive and native pause-map status

**Implemented as a stacked source slice** over CAMP-04. No alternate native renderer, gameplay scan authority, historical replay writer, or second scene is imported from SecondHunt.

1. `CampaignJournal.Capture` creates a value-only snapshot from the actual foreground `StorySave`: original five-planet progress, *only unlocked previously visited rooms*, and *only scanned logbook entries* from the user's locally extracted ROM text. When a localized scan table is unavailable, the scanned bit count and discovered rooms remain visible without fabricating secret text. UI views do not retain a live `Scene` or mutable save arrays.
2. The existing `PauseMenuView` offers **MISSION MAP / SCAN LOGBOOK** only for an active local SinglePlayer Adventure (never connected multiplayer, replica simulation, or demo playback). Desktop `InGameMenu` and Android `StartScreen.ShowPauseMenu` open the same `CampaignJournalView` with map summaries, selected scanned category, descriptions, and controller/keyboard-friendly close action. Original in-game 3D map navigation and visor remain untouched.
3. `CampaignMapPresentation` keeps the original 4:3 map height and zoom, expands the horizontal orthographic view on 16:9/ultrawide displays, clamps handset portrait to the safe native 4:3 view, and fixes map hit tests and teleporter projections to correctly consume `Matrix.ProjectPosition`'s already-normalized 0..1 positions. Native visited-room/connector filtering, door lock colours, camera ownership and collision models are preserved.
4. The story-only `PlayerHud.DrawHudAdventure` renders a cached Octolith/scan-progress status (no per-render-frame string allocations), without adding a competitive HUD element to multiplayer or recorded demo playback. `CampaignScanPresentation.Meter` clamps render-only scan progress in existing `DrawScanProgress`; scan target eligibility, occlusion tests, the authoritative scan timer, and `StorySave.UpdateLogbook` are unchanged.
5. `--campaign-presentation` is an asset-free test for locked/visited room disclosure, scan-log privacy, detached journal values, exact 4:3 baseline, widescreen aspect framing, normalized projection, and malformed scan-meter input; it joins the existing scene-ownership CI.

**Unverified:** native rendering, 16:9/21:9 framing, scan visor and live scan-log localization with private game files, all desktop platforms, Android touch/controller/back-key navigation, and frame-time/alloc comparisons. Automated value-only tests do not prove those device-specific visual criteria. The ROM-source detailed collision map and exact 16:9 source-textured one-screen layout in SecondHunt remain future optional fidelity work, not features claimed by this PR.
