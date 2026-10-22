# Native RmlUi layout qualification

`check.py` loads the supplied native bridge with Python `ctypes`. It opens, updates,
renders and closes every packaged `pages/**/*.rml` document using the real CPU
draw-list backend. It records library SHA-256, markup hashes, lifecycle results,
first open/update/render timings and three immediate idle update/render timings.
The 35 populated fixtures and 9 alternate states exercise actual markup IDs, focus-based action
reachability and horizontal border-box containment. These service publications
are synthetic fixtures; the harness does not claim physical controller/touch
input, game-model rendering, or end-to-end Android/device qualification.

The density-1 framebuffer matrix is 1280x720, 1920x1080, 2560x1440, 3840x2160,
1280x800, 2560x1080, 3440x1440, 800x600, 640x360 and 480x320. HiDPI cases are physical
1280x720, 2560x1440 and 3840x2160 at density 2 (logical 640x360, 1280x720 and
1920x1080). A fractional-density case uses physical 1920x1080 at density 1.5
(logical 1280x720). Reports explicitly label both dimensions. Touch fixtures run at
all three densities. Large-text fixtures run at logical 1280x720 and 640x360.
480x320 density 1 is the smallest ordinary layout case; 640x360 is the primary
short touch case, with the smaller case retained as an additional stress test.

Use packaged assets with lowercase `pages/`, `components/`, `themes/`, the shipped
fonts, images and `prime_home.rml`. Use the same script revision and native
library for before/candidate asset comparisons:

```sh
python3 tools/rmlui-layout-check/check.py --library /absolute/path/to/native-library --assets /absolute/path/to/before-assets --output /absolute/path/to/before.json --allow-failures
python3 tools/rmlui-layout-check/check.py --library /absolute/path/to/native-library --assets /absolute/path/to/candidate-assets --output /absolute/path/to/after.json
```

Assertions are always collected into JSON. Candidate runs exit nonzero on any
assertion, missing fixture ID, initialization or lifecycle failure. Baselines
must explicitly pass `--allow-failures` to allow known failures. Fatal errors
always exit nonzero.

On Windows or macOS, add `--glfw /absolute/path/to/glfw-library` to capture all
35 fixtures and 9 alternate states at every density-1 matrix size, plus
physical 2560x1440 at density 2 with touch styling and physical 1920x1080
at density 1.5 with desktop styling. Windows uses `glfw3.dll`;
macOS uses `libglfw.3.dylib` and the system OpenGL framework. A real hidden 64x64 OpenGL 2.1 context renders
into an RGBA8 framebuffer object with a stencil attachment at the requested
size; desktop/window size limits do not constrain these PNGs. These are actual
native UI GL renders into an offscreen target, not screenshots of a physical
display or an interactive game scene. PNGs use only Python standard-library
encoding. FBO capability errors are reported and fail candidate runs.

Border-box assertions establish viewport or known-container containment. They
do not prove pixel occlusion, correct z-order, every scroll hit target or every
workflow. Preserve the separate interactive native and application harnesses.

Theatre, the replay workspace and Journal also exercise combined large text and
touch classes at logical 1280x720 and 640x360, including HiDPI equivalents. The
Windows interaction suites independently verify full initial archive rows,
header overlap, horizontal focus scrolling, and timeline picture-ticket clips.

For a targeted native CPU comparison, repeat `--page` with any known fixture:

```sh
python3 tools/rmlui-layout-check/benchmark.py --library /absolute/path/to/native-library --before /absolute/path/to/before-assets --after /absolute/path/to/after-assets --output /absolute/path/to/abba.json --page pages/theatre/library.rml --page pages/theatre/playback.rml --page pages/theatre/studio-workspace.rml --page pages/studio/native-entry.rml
```

Omitting `--page` benchmarks every fixture. The receipt records the exact selected
paths. These are warmed native CPU call costs, not FPS or game-world rendering.


Additional qualification covers a populated host rules dialog and Aim Lab
options, the standalone Theatre delete dialog, 20 Settings rows with long hints, actual native focus identity, focus
retention during equivalent publication, and focus scrolling back to the first
control. Pinned action assertions run before focus can scroll an action into
view. Alternate states cover Aim Lab setup, create lobby, License account feedback, empty/loading/paginated Community
collections, and parties with zero, one or four members. The default party
fixture has eight members; the one-member variant uses a short name.

JSON includes initial semantic scroll regions and clipped/offscreen controls.
Those are diagnostic receipts, not assertions that every scroll is avoidable.
Settings' 20-row case intentionally requires scrolling. Accessibility bounds do
not prove text glyphs fit, so inspect the GL captures too. Font sizes and color
contrast need source/pixel review; this bridge exposes no computed-style API.

Use `--gl-only` to capture without repeating CPU tests, `--gl-page` to choose
pages (including their alternate states), and `--gl-case WIDTHxHEIGHT@DENSITY`
to choose framebuffer/density pairs. `--gl-touch --gl-large-text` captures the
combined accessibility case. For example:

```sh
python3 tools/rmlui-layout-check/check.py --library /absolute/path/to/native-library --assets /absolute/path/to/candidate-assets --output /absolute/path/to/compact/gl.json --glfw /absolute/path/to/glfw-library --gl-only --gl-page pages/hunters/native-picker.rml --gl-case 480x320@1 --gl-case 1280x720@2 --gl-touch --gl-large-text
```

Keep before/candidate screenshot outputs in separate directories. Reports record
the harness, native library, markup and PNG SHA-256 values. macOS GLFW may change
the working directory during initialization; report and asset paths are resolved
before initializing it.
