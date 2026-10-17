# RML-25: native Offline Battle with retained launch authority

## Runtime boundary

The authored `pages/offline/setup.rml` and `pages/offline/arena.rml` now compose inside the active native Home/Play/Lobby page manager. There is no additional scene, render context or offline gameplay implementation.

The UI owns only a `RmlUiOfflineDraft`, not `MenuSettings`, `LauncherPrefs` or `NetSession`. Its pure model uses the existing `OfflineLaunch.Modes`, Hunter and suit choices, bounded bot count (0 to 7), and difficulty (0 to 3). The integrated arena picker displays 16 choices per page with bounded case-insensitive search and focus/Back lifecycle. It sources installed built-in arenas from `Shell.RmlUiBuiltInRooms`, not unverified names.

Before launch, the existing `MapModeCapabilities.Supports` checks game type, player count and map content on a worker, with cancel/epoch fencing. A main-thread `StartScreen.RmlUiLaunchOffline` verifies no concurrent multiplayer or replay and reuses `OfflineLaunch.Create` and the original `Finish` event. New `OfflineLaunch.Preview` builds the same plan without side effects, allowing modifier validation **before** changing global preferences.

The current training setup and advanced offline rule sheets still fall back to their existing workspaces until their specialized validation and save contracts are migrated. No dead-end native controls are presented as completed.

## CI and acceptance

- Native menu fixture verifies independent offline draft, bounded choices, map search and paging, cancellation of old worker results, single-flight start and one successful handoff.
- Runtime fixture verifies actual Offline page/modal composition, document scoping for search and typed events rejected from covered background pages.
- Build, no-game-assets checks, software OpenGL acceptance and platform CI must complete before merge.

Settings is deliberately **last**, after Offline/Adventure/Replay/Community/Social and the remaining UI/rendering/device parity slices.
