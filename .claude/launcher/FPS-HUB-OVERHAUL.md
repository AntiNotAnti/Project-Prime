# Modern FPS Hub overhaul

Status: **P0 shell + motion foundation complete; P1 presentation modernization in progress**

The goal is to turn the launcher/menu collection into a coherent game shell without
rewriting networking, replay, settings, or match startup logic at the same time.

## Framework decision

Keep **Avalonia** for the first overhaul.

The repository already has one-window desktop composition, Android reuse, controller
navigation, file setup, updater integration, lobby UI, replay UI and in-game overlays.
Replacing the toolkit before separating those concerns would turn a UX redesign into a
platform rewrite.

React remains viable later through game-oriented middleware such as Gameface, and
RmlUi/Noesis remain viable renderer alternatives. The shell must therefore keep game
state and navigation contracts independent from individual Avalonia controls.

## P0 — shell foundation

Implemented:

- New flat tactical hub visual language in `HubTheme`.
- New controller-focusable `HubNavButton` with no idle animation.
- New responsive `HubHomeView`:
  - Play
  - Map Editor (placeholder)
  - Replay Studio
  - Settings
  - Quit
- Live local profile/hunter/game-data status.
- Existing `HunterStand` reused rather than introducing another preview path.
- Desktop three-column layout and compact Android/small-window navigation.
- Existing Play/Lobby/Settings/Replay screens remain the source of truth.
- Existing game-file setup and updater paths remain owned by `StartScreen`.
- Screenshot automation updated to address the new hub controls.
- Renderer-neutral `HubState`, `HubSnapshot` and destination enums.
- Explicit controller IDs/neighbours for desktop and compact hub layouts.
- Headless controller checks for hub, deployment and modern server-browser actions.
- CI now preserves `-uishot` layouts as a `launcher-layouts` artifact.
- Full Windows/Linux/macOS/Android/server matrix green on the corrected hub baseline.
- Launcher-wide display typography moved from Pixelify Sans to **Inter**; JetBrains Mono is
  reserved for technical/status data.
- Shared dark/cyan tactical palette applied to legacy controls during migration.

Remaining P0/performance work:

- Add high-contrast theme tokens.
- Baseline UI composition cost at 1080p/1440p/4K.

Implemented visual-motion system:

- **Reduce menu motion** is persisted in `LauncherPrefs` and suppresses shell motion
  plus cinematic GL drift; `Deck.Still` also disables it for deterministic captures.
- `PrimeMotion` provides finite, frame-clocked button, page and modal motion through
  `Deck.NextFrame`, preserving the event-driven off-screen desktop renderer at rest.
- `PrimeWorkspaceHost` crossfades/slides destinations and `PrimeOverlayHost` fades the
  scrim while panels lift/scale into place. No permanent Avalonia animation loop exists.

## P1 — Play and multiplayer

Play now has three product-level destinations:

1. **Multiplayer**
   - one integrated workspace rather than a chooser followed by another screen
   - live server directory is the main body
   - **Quick Play** is one primary action that selects/joins the best compatible open server
   - **Create Lobby** opens lobby creation; there is no separate player-facing "Custom Match" concept
   - direct address, Refresh, Join Server, hunter/suit and selected-map artwork remain in the same workspace
   - selected-server map thumbnails provide contextual game artwork instead of decorative filler
2. **Offline / Training**
   - hub-native map browser and selected-map preview
   - mode, hunter/suit, bot count and bot skill
   - direct Start Match
3. **Adventure**
   - hub-native save cards and progression summary
   - hunter selection
   - Continue / New Game

Current implementation:

- `PrimeShell` provides persistent HOME / PLAY / HUNTER LICENSE / REPLAY STUDIO /
  MAP STUDIO / OFFLINE / SETTINGS navigation.
- `NewsWorkspace` is the HOME command deck: cinematic hero, category filters,
  selectable transmission cards and the existing detail/Discord actions.
- `PlayWorkspace` is the full multiplayer workspace and uses
  `ServerBrowserService` for discovery, Quick Play and joins. It now presents a
  cinematic multiplayer hero, live-directory browser and deployment inspector while
  leaving networking/session ownership unchanged.
- `CreateServerScreen` is presented to the player as **Create Lobby**. Its
  hosted-vs-dedicated behavior, map rotation, host discovery and server package
  installation remain shared with the existing network implementation.
- `ServerBrowserService` owns renderer-neutral discovery, probing, endpoint
  parsing and joining; refresh/Quick Play cancellation is contained inside the
  service instead of escaping into the UI.
- Deterministic desktop/phone Multiplayer captures use sample directory data,
  including selected-map artwork, without touching the live network.
- Map Editor intentionally remains a placeholder while the editor itself is out
  of scope for this UI overhaul.
- Adventure uses the portable `AdventureSave` / `AdventureLaunch` contract.
- Offline uses the shared `OfflineLaunch` contract.

Visual polish now includes cinematic artwork-backed hero surfaces, contextual
map/hunter artwork, interactive selection motion and short one-shot page/modal transitions. The old lava/wireframe launcher JPEG
has been removed from the normal player-facing path: `LauncherBackdrop` selects a
cinematic room per destination, desktop GL pans the locally generated map render, and
Android/headless bake the same `MapShot`. Multiplayer, Offline, Replay Studio and Lobby
follow the selected room. Missing art falls back to a graded field, never debug geometry.
Transitions and GL drift are disabled by `Deck.Still` for deterministic captures and by
the user's **Reduce menu motion** setting. No perpetual Avalonia animation was added to
the CPU-rasterised desktop surface.

Still to add: server sorting/recent history and deeper Replay Studio
filtering/timeline/analytics refinement. The next presentation slices are Lobby,
Settings, Replay Studio, Map Studio, then pause/results.

## P1 — Lobby

Rebuild `LobbyScreen` around three regions:

- roster / teams
- selected map + match preview
- rules / owner controls

Implemented in-place over the existing authoritative `LobbyScreen`:

- roster, arena/match context and comms/session actions remain three clear regions;
- the arena is now an artwork-backed cinematic hero driven by the selected map;
- match configuration owns mode/format/limits and the Advanced Rules sheet;
- owner player administration moved out of match parameters and into a contextual
  **Manage Selected Player** action in the roster;
- the player-management sheet retains controller-accessible player selection, handicap,
  team assignment, bot management, transfer-owner, kick and close-lobby commands;
- arbitrary player names stay on platform-fallback text rather than the tactical font;
- deterministic captures cover the lobby, rule sheet and player-management sheet.

The server remains authoritative for every roster, team, rule and owner command.

## P2 — Settings, clips and pause

In progress:

- `HubSettingsView` provides a modern Display / Graphics / Audio / Controls /
  Replays / Profile / Credits landing surface while `SettingsView` remains the
  single transactional save/apply implementation.
- `SettingsView` now uses the same tactical hub chrome for its detail pages:
  selected category rail on desktop, horizontal category navigation on compact
  layouts, and hub-native Back / Save / Apply actions.
- `PauseMenuView` now uses the hub action language and explicitly says when a
  live network session continues behind the menu.

### Settings

Current information architecture:

- Display
- Graphics
- Audio
- Controls (Keyboard / Gamepad / Stylus)
- Replays
- Profile / Network
- Credits

Graphics now includes 25–300% internal render scale (above 100% is
supersampling), lighting, fog, bilinear/trilinear filtering, anisotropic
filtering up to 16x, cel shading bands and outline strength. Display keeps window/view/frame-pacing/HUD/accessibility controls.

Implemented Settings presentation slice:

- global Settings search indexes the existing authoritative controls and jumps/focuses
  the actual setting instead of creating a second settings model;
- focus surfaces an inline per-setting description, with richer guidance for renderer,
  resolution, frame pacing, HUD, visibility and audio controls;
- **Basic** keeps the everyday categories visible while **Advanced** reveals System and
  Maintenance; search can promote the view to Advanced when a result requires it;
- the command card reports clean, unsaved-draft and restart-required states, including
  renderer changes that only activate after restart;
- **Reset Category** restores only the active category to the snapshot captured when
  Settings opened, using the same `SettingsDraft` that already powers Discard;
- Controls category reset also restores keyboard/mouse bindings and tracked controller
  mappings without disturbing the other settings categories.

Next presentation slices: Replay Studio, Map Studio, then pause/results.

### Replay Studio

Implemented as a first-class Home destination in `HubReplayStudioView`:

- recording + virtual-clip library
- selected replay/map preview and metadata
- watch/import/rename/favorite/delete
- integrity checks and interrupted-recording recovery
- export and desktop folder reveal
- responsive desktop/phone layout
- direct playback into the existing Replay Studio in-match controls

Implemented Replay Studio presentation slice:

- archive rows use a richer two-line card presentation while remaining backed by the
  existing virtualizing `ListBox`; the 5,000-item library check still bounds realized
  controls instead of eagerly constructing the archive;
- Smart View adds dedicated highlight, bookmark, long-session and short-clip filters
  on top of the existing replay/clip/favorite/recent/map/player/annotation views;
- the selected replay is presented as a cinematic artwork-backed review hero with
  replay type, duration, arena and recording-time context;
- up to three deterministic replay thumbnails form a selectable timeline-still strip
  without adding an idle slideshow or perpetual Avalonia redraw;
- archive insights summarize full replays, clips, favorites, named highlights,
  bookmarks, recovery items and current-view duration;
- batch **Favorite Filtered** and **Check Filtered** actions operate on the current
  in-memory result set with explicit 200/50-item safety bounds;
- rename, tags/collections, integrity, recovery, export, delete, folder reveal and
  launch into the existing deterministic cinematic editor remain the same authority
  paths as before.

Next presentation slices: Map Studio, then pause/results.

### Pause/results

Use the same shell language without hiding the still-running network match. Keep
game-owned scoreboard/results state authoritative.

## P3 — renderer/performance

The current UI path can spend tens of milliseconds committing a moving off-screen
Avalonia surface because the full UI is rasterized on CPU. Do not paper over that with
lower-resolution UI.

After P0-P2 are structurally separated:

1. benchmark the same hub on all target resolutions;
2. move continuous visual motion to the OpenGL scene;
3. keep Avalonia redraw event-driven;
4. prototype one representative screen in:
   - NoesisGUI,
   - RmlUi,
   - React + Coherent Gameface if React/TypeScript authoring is a priority;
5. compare CPU frame cost, package size, Android support, controller input, text quality,
   build complexity and licensing before choosing a renderer migration.

## Non-negotiable acceptance criteria

- One physical game window on desktop.
- Android remains supported by the shared UI/application contracts.
- Mouse, keyboard, controller and touch remain first-class.
- No UI view becomes a second source of truth for lobby/match/network state.
- Existing game-file setup, updater, replay and launch flows remain reachable.
- 16:9, ultrawide, 4K and phone-sized layouts remain usable.
- UI screenshots/checks cover every shell destination.
- No continuous Avalonia animation is added without measuring redraw cost.
