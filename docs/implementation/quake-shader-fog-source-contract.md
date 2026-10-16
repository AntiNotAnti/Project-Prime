# Quake shader and fog source contract

This source-stage PR parses bounded Quake 3 shader scripts from local author-provided archives (PK3 or map bundles) without shipping the texture assets. It extracts texture stage lists, UV `tcMod scroll/rotate/scale`, fog RGB/depth, blend/wave/multipass declarations, and rejects oversized files and nonfinite/out-of-range values.

The existing map compiler is still free to fall back to its baked first-image material; it no longer silently represents fog/multipass shaders as fully implemented. It emits a clear `FP-MAP-024` warning when these stages require runtime changes.

`Q3ShaderProgram.TryNativeUv` identifies source cases that fit Project Prime's existing MapUvAnimation native format, including cycle/loop budgets. A later PR must integrate those native UV tracks into the imported material list and pair real renders; this PR deliberately does not change map format or runtime graphics.

No device acceptance, volumetric fog rendering, stage depth blending or dynamic GLSL shader support is claimed. Dedicated output parity and renderer validation must precede a default-on full Quake shader setting.
