# PR-04: Android driver-binary GL shader cache

Optional experimental packaging: `-p:AndroidShaderBinaryCache=true`.
The default Android and all desktop/server builds retain original shader
compilation. This switch intentionally does not alter render state, shader
content, gameplay or protocol.

The same EGL thread that owns linking may restore a previously linked program
via `glProgramBinary`. On miss/invalid/unsupported binary, existing GLSL
compile/link runs. The `GL_PROGRAM_BINARY_RETRIEVABLE_HINT` is set before
linking in experimental builds. The cache key covers both source texts and
GL_VENDOR/GL_RENDERER/GL_VERSION/GL_SHADING_LANGUAGE_VERSION; entries are
size-capped at 8MiB, written atomically under app-private files, and never
trusted without checking link status.

Expected impact: warm **scene startup**, not steady-state FPS. First-load
binary retrieval and synchronous disk writes can themselves cost time;
benchmark cold/warm startup across driver updates, scene swaps and context
loss on Adreno, Mali and emulators. The first experimental cold load compiles
RTT vertex text separately for three passes rather than sharing one compiled
vertex, so measure that cost rather than assuming it is negligible.

Run `dotnet run --project tools/android-pacing-check -c Release` for the
pure cache-key/size checks. Android physical devices must validate all passes,
post processing, outlines, resume and shader error fallback; do not turn on by
default before those tests.

Rollback: omit the MSBuild property. Existing shader owner lifetimes,
deletion paths and the default grouped shader-link implementation remain.
