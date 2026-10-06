# RmlUi home proof of concept

Project Prime's player-facing frontend is being evaluated on RmlUi before any broad Avalonia removal. The proof keeps one physical game window and preserves the existing launcher, network, replay, editor, setup and settings authority underneath it.

The native bridge is pinned to **RmlUi 6.3** at `ba95ffe8bfb6370efb2cdcca927eaad4710c5413`. FreeType 2.13.3 is pinned to `42608f77f20749dd6ddc9e0536788eaad70ea4b5`. Both build locally under the ignored `artifacts/` tree.

## Current proof

When built with `MphReadRmlUiPoc=true` and launched with `-rmluipoc`:

- RmlUi renders directly into Project Prime's OpenGL back buffer. The front screen does not rasterize or upload an Avalonia full-window bitmap.
- Project Prime renders the cinematic room image and the real animated Hunter. RmlUi renders only the interactive chrome above them.
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

## macOS

```sh
git checkout feature/rmlui-retina-menu-stage
bash tools/rmlui/build-native.sh auto

dotnet run --project src/MphRead/MphRead.csproj \
  -p:MphReadRmlUiPoc=true -- \
  -rmluipoc -renderer opengl
```

The first run after this slice may regenerate the current menu-stage room image in a background process. The existing image stays visible until the clean replacement is ready.

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
- the menu-stage room art contains no local arm cannon, local body/shadow or pickup clutter;
- mouse and controller focus have no dead states;
- resize/fullscreen changes preserve pointer alignment and RmlUi density;
- reduced-motion mode removes page-entry animation;
- UI submit cost remains comfortably below the moving Avalonia surface path;
- switching to an unmigrated destination returns to the current authoritative Prime shell cleanly;
- shutdown releases RmlUi and the preview scene without leaving a native resource behind.

## Deliberate limits

This proof still targets the desktop compatibility OpenGL renderer. It does not yet migrate Lobby, Settings, Replay Studio, Map Studio, pause/results, Android, Metal, Vulkan or DX12 presentation. The next renderer slice, if this gate passes, should implement a Project Prime-owned RmlUi render interface over the retained modern graphics layer so the same RML/data model can move to those backends without redesigning the frontend again.
