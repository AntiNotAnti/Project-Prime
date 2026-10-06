# RmlUi home proof of concept

This is an **opt-in desktop prototype**, not a replacement for the current shell yet. It answers one question before Project Prime commits to a UI renderer migration:

> Can the front screen render as a native-resolution, GPU-drawn game UI over Project Prime's own cinematic backdrop and animated Hunter, while preserving the existing launcher/network/editor/replay authority underneath it?

The POC uses **RmlUi 6.3** with its OpenGL 2 compatibility renderer. The version is pinned to commit `ba95ffe8bfb6370efb2cdcca927eaad4710c5413`. FreeType 2.13.3 is built locally from commit `42608f77f20749dd6ddc9e0536788eaad70ea4b5`. Neither dependency nor any native build output is committed.

## What the prototype changes

When built with `MphReadRmlUiPoc=true` and launched with `-rmluipoc`:

- the normal front screen is replaced by `rmlui/prime_home.rml`;
- RmlUi draws **directly into the game's OpenGL back buffer** after the cinematic launcher backdrop;
- the existing engine-rendered Hunter preview is drawn between the backdrop and RmlUi, with its old opaque preview clear disabled for this route;
- the Avalonia `UiSurface` is not ticked or uploaded while the RmlUi front screen owns the window;
- mouse, keyboard, controller focus, hover, and button activation are forwarded to RmlUi;
- player name, preferred Hunter, suit, game-data readiness, and build version come from the existing renderer-neutral `HubState` contract;
- RmlUi reports its measured CPU submit time on the proof-status panel;
- PLAY / HUNTERS / COMMUNITY / STUDIO / SETTINGS hand back to the existing Prime shell. Existing networking, lobby, replay, Map Studio, settings, and launch contracts remain authoritative.

If the native bridge, assets, font load, or RmlUi initialization fails, Project Prime logs the reason and falls back to the current Avalonia front screen.

## Run it on macOS

Prerequisites are Git, CMake, and the Xcode command-line tools. CMake can be installed with Homebrew if it is not already available.

```sh
git checkout poc/rmlui-home
bash tools/rmlui/build-native.sh auto

dotnet run --project src/MphRead/MphRead.csproj \
  -p:MphReadRmlUiPoc=true -- \
  -rmluipoc -renderer opengl
```

The native build stays under `artifacts/`, which is already ignored by Git. `dotnet run` copies the matching bridge plus the RML, RCSS, and existing Project Prime fonts into the output directory.

## Run it on Linux x64

Install a C++ toolchain, CMake, Git, and OpenGL development headers first. On Debian/Ubuntu:

```sh
sudo apt-get install build-essential cmake git libgl1-mesa-dev
```

Then:

```sh
bash tools/rmlui/build-native.sh linux-x64

dotnet run --project src/MphRead/MphRead.csproj \
  -p:MphReadRmlUiPoc=true -- \
  -rmluipoc -renderer opengl
```

## Automated real-window capture

The branch CI also runs the proof without cartridge data:

```sh
ProjectPrime -rmluipocshot /tmp/rmlui-proof -renderer opengl -noupdate
```

That path bypasses the ordinary game-file setup check only for the diagnostic, opens the actual Project Prime GLFW shell, loads the native RmlUi bridge and document, draws 30 frames, captures `rmlui-home.png` from the final backbuffer, then quits. A missing native bridge, failed RML parse, font failure, or missing output makes the job fail.

## Why OpenGL only for this first gate

The goal is to validate RmlUi's authoring model, input behavior, visual quality, and direct-render performance without also building four renderer adapters at once. Project Prime already has a compatibility OpenGL context on desktop, and RmlUi ships a compact GL2 render interface that can draw into that existing context without creating another window.

If this gate is successful, the next renderer slice should implement Project Prime-owned RmlUi render interfaces for the retained modern graphics layer so the same RML can run on Metal, Vulkan, DX12, and Android Vulkan. The RML/data model should not need to change.

## Acceptance gate

Do not replace Avalonia globally just because this screen looks better. The POC is successful only if all of these hold on real hardware:

- native-resolution type remains sharp at 1080p, 1440p, and 4K;
- animation and focus remain fluid at 60/120/144 Hz;
- the proof-status `RMLUI DRAW` time remains comfortably below the current moving Avalonia surface cost;
- no full-screen CPU-raster UI texture is uploaded while the POC owns the front screen;
- mouse and controller navigation have no dead focus states;
- resizing/fullscreen transitions preserve layout and pointer alignment;
- the real Hunter can idle over the cinematic background without an opaque preview rectangle;
- choosing an existing destination returns to the current shell without changing gameplay/network authority;
- quitting and renderer teardown release the RmlUi context cleanly.

## Deliberate limits

This first POC is desktop OpenGL only. Linux is compiled and opened under Xvfb in CI, producing a real final-composite screenshot; macOS arm64/x64 are compile-gated separately. It does not migrate Lobby, Settings, Replay Studio, Map Studio, pause/results, Android, or the modern graphics backends. It also does not add a second source of truth for game state. Those are follow-up slices only after the front-screen gate is measured and accepted.
