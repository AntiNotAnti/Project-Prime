# Modern material correction experiment (R22 partial)

2026-10-21. R22 is **PARTIAL / NO_ROLLOUT**, not a completed linear color pipeline.

`PROJECT_PRIME_GL_MATERIAL_CORRECTIONS=1` explicitly enables corrected modern
material math. The default is off, and `PROJECT_PRIME_GL_FORCE_LEGACY=1` wins.
The experiment does not inherit Android production performance enrollment or
change saved appearance presets. Original/native programs omit the advanced
material stage; advanced materials retain the prior math when this experiment
is disabled.

Implemented: a world-space camera uniform derived from the actual uploaded view
matrix, camera-relative half vectors, finite normal/half-vector fallbacks,
Gram-Schmidt tangent orthogonalization and handedness derived from the UV basis.
Degenerate UV/world derivatives and zero normals fall back to a geometric normal
(or deterministic +Z when no usable normal exists). Both GLSL 1.20 and GLES 3
sources use the same expressions. Camera uniform uploads follow world, replay,
preview, temporary pause-map/viewmodel and shader-variant view changes.

Encoded color output and existing alpha/cutout behavior are preserved. There is
no unqualified global sRGB conversion or retired post-effect activation.
The full R22 texture color/alpha metadata, authored mip/filtering, normal
renormalization across mip residency, linear lighting/blending and exactly-once
output conversion contract is **NOT_IMPLEMENTED / NO_ROLLOUT**. It requires its
own asset/settings migration, image corpus and incremental cost measurements.

Native Windows synthetic `-glvisualcheck` fixtures use production fragment
helpers and invented geometry/textures to check:

- Camera movement changes modern highlights; opt-out and Original remain exact.
- A skewed geometric normal yields an orthogonal mapped tangent.
- Mirrored UVs retain negative bitangent handedness.
- Degenerate UV/world derivatives and zero normals remain finite.

These fixtures do not qualify retail materials, scene coverage, Android pixels,
sustained performance or artist intent. Native Windows execution PASS: `dotnet
src/MphRead/bin/Release/net10.0/ProjectPrime.dll -glvisualcheck
C:\Users\Jarrett\AppData\Local\ProjectPrime\renderer-next-20261021\materials-visual`
completed all 444 integrated synthetic checks and 17 captures on NVIDIA OpenGL
4.6.0 / driver 617.42, including the listed material fixtures. Receipt:
[materials-visual.log](materials-visual.log),
[hash manifest](materials-visual.json). Invented raw RGBA captures remain outside
the repository at that absolute output directory. Both opt-out and Original
pixel equality controls passed; this is synthetic invariant evidence only. Full scene still/
motion comparisons and per-feature CPU/GPU cost remain UNVERIFIED; the explicit
experiment remains disabled by default.
