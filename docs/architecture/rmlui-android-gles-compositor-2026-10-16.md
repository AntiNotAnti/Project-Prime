# RML-19: Android GLES 3 draw-list playback (opt-in)

## Boundary and provenance

This restores the rendering semantics of the October 9 backup's
RmlUi draw-list compositor, not its obsolete WebGPU/ModernGraphicsCompat GPU
ownership. The current GameView.RenderLoop alone owns the existing EGL
context and swap chain. The Android renderer does not create an EGL context,
GLSurfaceView, Android window, command queue, or render thread.

The Android experiment still requires both build properties:

- -p:MphReadRmlUiAndroidExperiment=true to compile the managed draw-list,
  planner, session and GLES compositor.
- -p:MphReadRmlUiAndroidPackage=true to package the exact pinned native
  libraries and RML/RCSS/font resources from RML-17.

The two properties intentionally do not enable shipping RmlUi. The default
Android launcher continues using Avalonia.

## Implemented playback

- RmlUiAndroidDrawListSession sequences the existing versioned RmlUiHost
  draw-list backend, native render, bounded native capture, then GLES draw.
- RmlUiGlesPlan validates geometry/texture references and translates
  rectangles to GL's bottom-left scissor convention without integer overflow.
- Native transforms remain column-major with local translations, and the
  vertex layout is the pinned position/RGBA8/UV ABI.
- GLES 3 uploads compiled geometry and generated premultiplied RGBA atlases
  once per handle/generation; removed handles are released. An unchanged native
  frame and viewport reuse an existing operation plan.
- Set, SetInverse, and Intersect stencil operations preserve the original
  eight-bit clip-reference model and fail closed beyond its nesting limit.
- RmlUi is first rendered to its own RGBA8 + stencil8 FBO. This protects
  world/HUD depth and stencil while preserving premultiplied alpha for the
  terminal overlay. The offscreen image is then blended into the existing
  game framebuffer.
- Game GL state, framebuffer bindings, viewport, scissor, renderbuffer,
  current program, vertex buffer/array, texture unit, blend, color/depth/stencil
  masks and clear values are restored in a finally block. RmlUi never assumes
  ownership of the game renderer's cached state.
- No native GPU layers, filters or custom shaders are claimed: the
  RML-18 native reader rejects unsupported features.

## CI acceptance and remaining gates

tools/rmlui-drawlist-check covers scissors with extreme coordinates,
invalid draw references and nonfinite data, and set/intersect/inverse stencil
references including eight-bit overflow. The existing Android opt-in compile
job compiles the compositor against the Android GLES bindings.

Not yet a shipping UI: The Android launcher needs a real native
presentation surface, verified APK-resource extraction to a private runtime
directory, touch/IME ownership, lifecycle callbacks and a call from the
current EGL rendering boundary. GameView is presently a match surface, not
the Avalonia launcher surface, so launching RmlUi there unconditionally would
misroute the UI and potentially break gameplay. The session is an explicit
owner-thread API for that upcoming integration and is not started by default.

The GPU shader paths and offscreen-stencil behavior still require an opt-in
APK on real arm64/x86_64 devices (including resize, pause/resume, surface
replacement, context loss and profiler comparison). Device acceptance and
the default-UI cutover are not claimed by this slice.

## Remaining historical presentation parity

The backup still has full native settings backend/presenter files and native
hunter/lobby presenters that have not been forward-ported. Current Quick
Settings, read-only Community Discover and Hunter License overview deliberately
route fuller workflows through existing screens. Separate independently
reviewable slices should migrate those against current authoritative services,
rather than copying stale network/auth/profile dependencies.
