# Android native UI allocation and GL state improvements

The experimental Android RmlUi compositor reuses a snapshot object and all
of its OpenGL query arrays across frames. The original capture contract stays
intact: every pass reads **all** the state it will change, even on re-entry
from a different context, and restores it in `finally`.

Resource-list scratch storage is reused. New vertex/index buffer data uses
pooled managed scratch instead of allocating two new byte arrays per geometry
upload. Direct native ByteBuffers still own the copy passed to the driver and
are disposed after the upload returns. Geometry and texture limits are
unchanged.

The terminal pass skips unchanged consecutive scissor rectangles, texture binds,
and VAO binds; depth, stencil, blend, and clip operations are left unchanged.
This does not attempt to suppress GL state queries without a proven owner
contract. Failed construction/GL errors must still propagate and trigger the
existing fallback instead of silently losing the UI.

## Validation

- Build with Android native UI experiment enabled and run existing draw-list,
  clip-mask, state-restore, malformed data and ABI tests.
- Repeatedly enter/exit launcher, pause, chat, scoreboard, map/hunter preview.
- Compare p95/p99 frame time, GC allocated bytes per frame, state query counts,
  UI draw calls, visual correctness, and GPU memory on Adreno and Mali.
- Exercise EGL context loss, UI plan generation changes, texture removal,
  visible stencil clip paths and resize. Always verify every restored GL binding.
- Rollback is the independent compositor change; do not disable the core game
  renderer to address an optional UI failure.
