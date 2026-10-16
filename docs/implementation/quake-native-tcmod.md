# Native Quake single-stage texture animations

Imported FPTX shader ordering is preserved. Supported source shaders with one texture stage and compatible `tcMod scroll`, `tcMod rotate` and `tcMod scale` are converted into the existing native 30 Hz map material animation format. This does not change `.ppmap`, replay or network packet schemas.

Only source shader curves that fit all native 16-bit lookup-table budgets and the common maximum 6000-frame loop are compiled. An existing authored native material animation takes priority. Duplicate source material names, unsupported stages, excessive tracks and incompatible periods fall back to static imported materials rather than breaking a map.

Set `PROJECT_PRIME_Q3_NATIVE_TCMOD=0` before compiling a custom map to disable these source conversions, then regenerate the map's build outputs. Shipped/installed map identities still follow the existing immutable package and build-manifest rules.

**Not implemented by this slice:** volumetric fog, Quake alpha/RGB waves, `tcMod turb`, sky-stage compositing, multi-pass depth blending, moving stage transparency and original Quake lightmap blending. The import warning remains explicit.

Real OpenGL/GLES screen captures and physical GPU benchmarks have not been obtained. Source-code and headless-test availability alone do not establish visual fidelity.