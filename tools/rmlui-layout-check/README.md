# Native RmlUi layout qualification

`check.py` loads the supplied native bridge with Python `ctypes`. It opens, updates,
renders and closes every packaged `pages/**/*.rml` document using the real CPU
draw-list backend. It records library SHA-256, markup hashes, lifecycle results,
first open/update/render timings and three immediate idle update/render timings.
The eight populated fixtures exercise actual markup IDs, focus-based action
reachability and horizontal border-box containment. These service publications
are synthetic fixtures; the harness does not claim physical controller/touch
input, game-model rendering, or end-to-end Android/device qualification.

The density-1 framebuffer matrix is 1280x720, 1920x1080, 2560x1440, 3840x2160,
1280x800, 2560x1080, 800x600, 640x360 and 480x320. HiDPI cases are physical
1280x720, 2560x1440 and 3840x2160 at density 2 (logical 640x360, 1280x720 and
1920x1080). Reports explicitly label both dimensions. Touch fixtures run at
both densities. Large-text fixtures run at logical 1280x720 and 640x360.
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

On Windows, add `--glfw /absolute/path/to/glfw3.dll` to capture all eight fixtures
at every density-1 matrix size. A real hidden 64x64 OpenGL 2.1 context renders
into an RGBA8 framebuffer object with a stencil attachment at the requested
size; desktop/window size limits do not constrain these PNGs. These are actual
native UI GL renders into an offscreen target, not screenshots of a physical
display or an interactive game scene. PNGs use only Python standard-library
encoding. FBO capability errors are reported and fail candidate runs.

Border-box assertions establish viewport or known-container containment. They
do not prove pixel occlusion, correct z-order, every scroll hit target or every
workflow. Preserve the separate interactive native and application harnesses.
