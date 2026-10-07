# RmlUi home proof of concept

Project Prime's player-facing frontend is being evaluated on RmlUi before any broad Avalonia removal. The proof keeps one physical game window and preserves the existing launcher, network, replay, editor, setup and settings authority underneath it.

The native bridge is pinned to **RmlUi 6.3** at `ba95ffe8bfb6370efb2cdcca927eaad4710c5413`. FreeType 2.13.3 is pinned to `42608f77f20749dd6ddc9e0536788eaad70ea4b5`. Both build locally under the ignored `artifacts/` tree.

## Current proof

When built with `MphReadRmlUiPoc=true` and launched with `-rmluipoc`:

- RmlUi renders directly into Project Prime's OpenGL back buffer. The front screen does not rasterize or upload an Avalonia full-window bitmap.
- Project Prime renders a procedural Deployment Chamber and the real animated Hunter. RmlUi renders only the interactive chrome above them.
- `HubState` remains the presentation-neutral source for player, Hunter, suit and setup state.
- Mouse, keyboard and controller navigation feed the RmlUi context.
- PLAY / HUNTERS / COMMUNITY / STUDIO / SETTINGS still hand off to the existing Prime shell, so this proof does not duplicate gameplay or service state.
- Failure to load the native bridge, RML, RCSS or fonts falls back to the established frontend for an interactive run. The automated capture fails instead of hiding the error.

## Slice 2: Retina and menu-stage composition

The first real macOS capture proved the renderer seam but also exposed two presentation faults: CSS pixel units made the UI tiny on a Retina framebuffer, and the locally generated room image still read as a first-person gameplay frame.

Slice 2 changes those contracts:

- normal UI dimensions, type, spacing and borders use RmlUi `dp` units;
- the RmlUi context continues to receive the framebuffer/client density ratio, so a 2x Retina display gets a 2x physical raster for the same logical layout;
- the home screen is composed around a 1920x1080-class logical canvas, with larger activity controls, header, profile and CTA;
- the Hunter stage moves right and uses a closer presentation-only preview camera;
- the old proof diagnostics panel is removed from the normal composition and replaced by a restrained session card; F10 toggles a developer-only telemetry panel when the render path needs inspecting;
- backdrop scrims, vignette and floor fade separate the UI from busy room art;
- entrance and focus motion is short and presentation-only, and the existing Reduce menu motion preference disables the page-entry animations;
- generated room previews now suppress the local player, arm cannon, local shadow and pickup presentation while retaining the authored intro camera and environment;
- old cached menu-stage thumbnails are recognized by a presentation-version marker. The active room is regenerated once in a background worker, then its GL texture is swapped on the render thread without blanking the current screen.

Generated room images still come only from the player's locally extracted files. No cartridge-derived backdrop is added to the repository or release.

## Slice A: cinematic integration pass

The first real menu-stage captures still read as a sharp character composited over a flat room image. Slice A keeps the same renderer boundary and fixes the presentation cues without inventing a new gameplay scene:

- the locally generated room photo receives a menu-only focal softening pass, with the center-right stage kept clearer than the chrome-heavy outer field;
- the backdrop is slightly desaturated/cooled, with restrained highlight glow, left/right scrims, vignette and floor fade applied in the native GL photo shader rather than over the Hunter;
- a procedural under-Hunter pass draws a broad cool light field, a subtle floor bounce and a soft contact shadow before the preview model;
- the Hunter preview swaps from the neutral picker light to a warmer key plus stronger cool fill while the RmlUi stage owns the frame, creating silhouette/rim separation without changing gameplay lighting;
- the old RmlUi wash/glow rectangles are removed so the vector layer no longer tints or flattens the character after it has been rendered;
- every effect is presentation-only and disposable: no room state, gameplay light, collision, thumbnail source image or network state is modified.

The background blur is intentionally a lightweight focal softening over the already-rendered room image, not a depth-buffer DOF effect. A true depth-aware menu scene remains a later Menu Stage renderer slice if the POC is promoted.

## Slice B: authored Menu Stage system

Slice B turns the one-off visual treatment into a data-driven presentation layer. The room image is still generated from Project Prime's real room renderer in an isolated preview worker, but the result is no longer an arbitrary thumbnail with global styling:

- `LauncherMenuStage` owns an authored profile per built-in stage room: intro-camera sample frame, crop/focus, drift, post-process values, atmosphere, Hunter framing and visibility policy;
- built-in rooms sample a deliberate frame on their original multiplayer intro-camera path. Custom maps continue to use their explicit `MapDefinition.Preview` camera;
- menu-stage capture hides the local player/viewmodel, pickups, projectiles, bombs and enemies while leaving doors, platforms and architectural entities intact;
- the launcher photo shader receives the active profile's softness, saturation, cool shift, vignette, side scrims, floor fade and highlight glow instead of hard-coded global numbers;
- the under-Hunter native pass reads the same profile for low fog and deterministic dust motes. Reduce menu motion freezes atmospheric drift without removing the composition;
- Hunter lighting changes with destination: the multiplayer/play stage keeps the balanced cinematic key/fill, Adventure warms the key, and Studio uses a cooler technical treatment;
- QUICK PLAY, SERVER BROWSER, OFFLINE BATTLE and ADVENTURE now emit stage-selection events before deployment, allowing the environment, camera recipe and Hunter composition to change while the RmlUi home remains active;
- when a selected stage has not been generated yet, the current stage stays visible until the background worker finishes instead of flashing to black;
- cinematic cache version 3 forces old captures to be regenerated with the authored camera and expanded visibility policy;
- `-menustagecheck` validates all shipped profiles without requiring game assets, and the RmlUi CI gate runs it before native rendering checks.

The architecture deliberately keeps room simulation isolated in the preview worker for this POC. Loading a second full multiplayer `Scene` into the long-lived launcher process would currently share compatibility-era static match state with the player's next real match. Promoting Menu Stage to a continuously live 3D room should happen only after that state boundary is made explicitly scene-local, rather than hiding a second gameplay world behind the menu.

## Slice C: grounding and depth hierarchy

The authored stage system solved the broad composition, but the first real post-Slice-B capture still left one tell: the Hunter was lit more intensely than the room and its old single ellipse shadow sat too low, so the model could still read as a separate render layer.

Slice C tunes the integration instead of adding more decoration:

- Proving Ground's authored crop moves left and slightly down so the bridge/wall perspective leads toward the Hunter instead of cutting horizontally through the torso;
- a localized elliptical haze field is applied to the room directly behind the Hunter. The surrounding architecture remains readable while the immediate background receives stronger softening and atmospheric perspective;
- stage profiles now separately author `HunterBackdropHaze` and `ForegroundHaze`, with bounded acceptance checks;
- the old single contact ellipse is replaced by a broad low-alpha penumbra plus two small foot cores. Ground position is adjusted per Hunter silhouette, including Trace's unusually tall/wide stance;
- floor bounce is pulled up to the same contact plane instead of sitting below the model;
- a very light post-Hunter atmospheric veil is drawn over the lower body so extreme suit saturation/contrast shares the room's air without modifying skins, recolors, or gameplay materials;
- menu-stage Hunter lights use a more neutral fill. Adventure remains warmer and Studio remains cooler, but the default blue fill no longer makes red/orange suits look electrically detached from a muted room;
- Reduce menu motion continues to freeze fog/dust drift. All grounding and haze remain presentation-only and deterministic.

This is still not true depth-buffer DOF or a physically shared shadow map. Those belong to a later fully live Menu Stage renderer. Slice C is the POC-safe version: it improves depth cues without loading another gameplay world or changing the selected Hunter's real material.

## Deployment Chamber background system

The POC no longer uses a literal multiplayer map as the normal RmlUi home background. The room-thumbnail path remains available to the production Avalonia shell, but the RmlUi home now renders a purpose-built **Project Prime Deployment Chamber** directly in the game window.

The chamber is deliberately universal:

- a procedural far background, chamber ribs, technical rails, angled braces and a central hero bay replace the raw arena photograph;
- a perspective floor grid and dedicated hero platform give every Hunter a clean physical anchor;
- the background has explicit left and right readability zones so activity/session chrome does not have to fight environment detail;
- activity ambience is data-driven:
  - Quick Play is the brighter, more energetic multiplayer treatment;
  - Server Browser is cooler and more technical with reduced particle motion;
  - Offline Battle emphasizes the simulation floor grid;
  - Adventure increases fog/particles and introduces a restrained warm distance bias;
- Hunter identity is also data-driven. Samus, Kanden, Trace, Sylux, Noxus, Spire and Weavel each provide halo, rim, floor and particle accents without requiring a separate environment;
- activity-drawer hover/focus changes the chamber ambience immediately because it already drives `LauncherBackdrop.Scene`; selecting the entry commits the same mood;
- Reduce menu motion freezes shader pulsing and atmospheric drift while preserving the composition;
- the old background-thumbnail regeneration worker is no longer started by the RmlUi home, so switching activities does not spin up a hidden room render merely to change menu mood;
- `-menubackdropcheck` validates all shipped activity/hunter style contracts without game assets, and the real RmlUi capture gate exercises the chamber shader across 720p, 1080p, 1440p, 4K and Retina simulation.

The render stack for the RmlUi home is now:

```text
procedural deployment chamber
  -> activity ambience
  -> hunter identity halo / floor / particles
  -> grounding + contact shadow
  -> real engine Hunter
  -> subtle foreground atmosphere
  -> RmlUi
```

No cartridge-derived image is needed for this background. The selected Hunter remains the actual Project Prime model and uses the existing preview/material pipeline.

## macOS

```sh
git checkout feature/rmlui-deployment-chamber
bash tools/rmlui/build-native.sh auto

dotnet run --project src/MphRead/MphRead.csproj \
  -p:MphReadRmlUiPoc=true -- \
  -rmluipoc -renderer opengl
```

The RmlUi home no longer needs a generated room image; the Deployment Chamber is procedural and appears immediately.

## Linux x64

```sh
sudo apt-get install build-essential cmake git libgl1-mesa-dev libx11-dev
bash tools/rmlui/build-native.sh linux-x64

dotnet run --project src/MphRead/MphRead.csproj \
  -p:MphReadRmlUiPoc=true -- \
  -rmluipoc -renderer opengl
```

## Deterministic layout captures

`-rmluipocshot` opens the real GLFW shell without cartridge data, draws the direct RmlUi composite, captures the final backbuffer and exits.

Two diagnostic-only arguments make DPI/layout checks deterministic:

```sh
ProjectPrime \
  -rmluipocshot /tmp/rmlui-proof \
  -rmluisize 3008x1692 \
  -rmluidensity 2 \
  -renderer opengl -windowed -noupdate
```

The Linux CI gate captures these layouts:

| Capture | Framebuffer | RmlUi density |
| --- | ---: | ---: |
| 720p | 1280x720 | 1x |
| 1080p | 1920x1080 | 1x |
| 1440p | 2560x1440 | 1x |
| 4K | 3840x2160 | 1x |
| Retina simulation | 3008x1692 | 2x |

macOS arm64 and x64 continue to compile the native bridge and managed host separately.

## Acceptance gate

The proof is ready to expand only when:

- type and controls have comparable logical size on standard-DPI and Retina displays;
- 720p through 4K captures stay inside safe bounds with no clipped CTA, nav or profile controls;
- the real Hunter reads as the avatar focal point rather than an enemy placed in the center of a gameplay screenshot;
- the Deployment Chamber contains no gameplay room/viewmodel/pickup imagery;
- mouse and controller focus have no dead states;
- resize/fullscreen changes preserve pointer alignment and RmlUi density;
- reduced-motion mode removes page-entry animation;
- UI submit cost remains comfortably below the moving Avalonia surface path;
- switching to an unmigrated destination returns to the current authoritative Prime shell cleanly;
- shutdown releases RmlUi and the preview scene without leaving a native resource behind.

## Deliberate limits

This proof still targets the desktop compatibility OpenGL renderer. It does not yet migrate Lobby, Settings, Replay Studio, Map Studio, pause/results, Android, Metal, Vulkan or DX12 presentation. The next renderer slice, if this gate passes, should implement a Project Prime-owned RmlUi render interface over the retained modern graphics layer so the same RML/data model can move to those backends without redesigning the frontend again.
