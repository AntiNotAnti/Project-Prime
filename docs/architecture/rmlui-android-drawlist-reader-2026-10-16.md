# RML-18: Android draw-list reader foundation

Restores the historical managed vertex, clip/transform command, texture,
retained frame and native draw-list ABI reader from RmlUi 6.3, without
restoring the obsolete multi-backend GPU compositor.

The reader uses bounded command/vertex/index/texture counts and rejects
nonfinite coordinates, invalid references and unsupported native features.
It copies native memory before mutation and caches geometry and textures
only within the matching native generation.

The code compiles only with `MphReadRmlUiAndroidExperiment=true` on
the Android head or the isolated source-contract harness. It **does not
render anything** and cannot alter shipping Android Avalonia behavior.

CI tests primitive ABI shape, triangle indices, finite vertices and atlas
length, and compiles the Android experiment separately. The following
slice must implement GL ES draw submissions against GameView.RenderLoop's
existing EGL context (no second EGL surface), then validate clipping,
context loss, input, IME and physical-device GPU performance before launch.
