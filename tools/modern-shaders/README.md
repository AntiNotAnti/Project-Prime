# Shared shader generation

World, deferred material and post-process WGSL are generated from the existing
GLSL formulas. This keeps AA, shadow filtering, material maps, dynamic lights,
depth effects and TAA rejection logic in one source of truth.

Run `tools/modern-shaders/check.sh` with .NET 10, Python 3 and Rust available.
The exporter reads the shader properties from the application; the translator
uses the locked Naga 0.19.2 dependency. Generated WGSL and layouts are committed
and embedded in desktop and Android assemblies. Rust is a build-time tool only.

The translation adds explicit uniform layout, separate textures/samplers,
framebuffer texture orientation, OpenGL clip-depth conversion, viewport mapping,
draw-time display-list color/normal inheritance, invariant position outputs and alpha testing. Depth textures use explicit loads
for the shared nearest-depth/PCF formulas. Native validation and pixel checks run
through `-renderfullcheck -renderer metal|vulkan|dx12`.
