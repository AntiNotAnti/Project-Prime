# RmlUi draw-list compositor

The opt-in native bridge uses draw-list ABI 1 when the game selects a modern
graphics backend. `RmlUiGpuCompositor.DrawNativeFrame` runs after native layout
and render capture, after scene/HUD composition, and before the engine presents.
It copies native memory on the owner thread and submits through
`ModernGraphicsCompat`'s existing device, queue, command encoder, upload arenas,
and acquired surface. It creates no window, device, queue, or swapchain.

Compiled geometry and generated font/image atlases are cached by native handle
and lifetime generation. RmlUi 6.3 guarantees immutable compiled vertices until
release; its generated textures contain premultiplied RGBA. Native resource
enumeration removes released atlases, while geometry unused by the current
frame is pruned and can be uploaded again when shown. GPU buffers, textures,
shaders, and stencil attachments belong to the renderer and are released on
its ordinary disposal/recovery path. A new renderer recreates its resources
from the retained managed/native snapshots; resize recreates only the UI's
stencil attachment when dimensions change.

The compositor implements full homogeneous column-major transforms, local
geometry translation, framebuffer scissor rectangles, and Set, SetInverse,
and Intersect clip masks. It preserves premultiplied alpha with One /
OneMinusSrcAlpha blending and uses the existing surface color-management
convention, including linearization for an sRGB attachment. It uses a separate
stencil attachment so UI masks cannot corrupt world/HUD stencil values.

GPU layers, filters, and custom shaders are unsupported by this adapter. The
native bridge records their use and the managed reader rejects the frame with
an explicit error; it never silently claims parity. SVG support is governed by
the native RmlUi build/plugins and is not added by the compositor. The shipped
theme must use regular geometry, atlas textures, and clip masks. Clip nesting
is bounded by the attachment's eight-bit stencil reference.

Run the actual GPU regression with:

```sh
dotnet run --project tools/rmlui-compositor-check -- vulkan
dotnet run --project tools/rmlui-compositor-check -- metal --recovery
```

The tool opts its project reference into RmlUi. It reads actual final-composite
pixels for geometry, scissor, premultiplied textures, transforms, all clip-mask
operations, native generation changes, and optional destroyed-device recovery.
Backend availability follows the engine policy. Passing on one backend/OS is
not evidence of DirectX 12, other Vulkan drivers, or Android coverage.

Local validation on 2026-10-07 passed on an Apple M4 Pro using Metal and
Vulkan/MoltenVK, including destroyed-device recovery, surface resize,
zero-size suspension/resume, and UI shutdown/reentry. Windows DirectX 12/Vulkan, Linux
Vulkan, Android, forced sRGB surfaces, full native document screenshots, and
comparison benchmarks remain separate validation gates.
