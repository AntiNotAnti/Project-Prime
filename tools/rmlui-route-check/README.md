# Native RmlUi routing and colour regression

This opt-in, display-dependent check creates a real GLFW OpenGL compatibility
context and loads the built RmlUi bridge. It has no game or Studio project
reference. It uses the shipped RML/RCSS/fonts, actual native Shift-Tab/Enter and
mouse events, the STUDIO control's bounded native layout query, and the action
queue. Every activation must emit exactly one `studio:open`; shutdown must clear
pending actions.

Build the optional bridge first, then run on a desktop with a working display:

```sh
bash tools/rmlui/build-native.sh auto
dotnet run --project tools/rmlui-route-check -c Release -- artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib --capture /tmp/prime-rmlui-proof
```

On Linux, replace the bridge path with
`artifacts/rmlui-native/linux-x64/libProjectPrime.RmlUi.Native.so` and run under
Xvfb when there is no physical display. The Linux RmlUi workflow invokes this
check after building that bridge. Missing native libraries, context failures,
incorrect routing and failed captures are errors; this check does not skip them.
The tool copies the canonical source document, stylesheet and licensed fonts to
its output without building the game. `--assets <directory>` can instead inspect
the RmlUi assets of an immutable published build.

The measured physical targets are 1280×720 at density 1, 2560×1440 at density 2,
and 2560×1440 at density 1. An explicit framebuffer read verifies the existing
ScreenCapture threshold: at least 1% of pixels must have an RGB component over 8.
No additional background is drawn. Optional PNGs also preserve failed captures.
This specifically catches RmlUi's integer/percentage alpha syntax and default
inline tag layout: the original stylesheet failed the last target at 0.866% lit;
the corrected stylesheet passed at 18.060% in a controlled native macOS run.

This probe validates the native UI overlay and input routing. The existing full
engine menu-stage composite capture remains a separate, unchanged CI gate.
