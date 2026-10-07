# Shared shader generation

World, deferred material and post-process WGSL are generated from the existing
GLSL formulas. This keeps AA, shadow filtering, material maps, dynamic lights,
depth effects and TAA rejection logic in one source of truth.

The five post-process color inputs are single-level scene, history and G-buffer targets,
so generated helpers use explicit level 0. This avoids FXC trying to unroll the
varying screen-reflection loop for implicit texture gradients. World and
material helpers retain their authored mip and anisotropic sampling. The check
also validates the actual post-process WGSL with Naga and emits HLSL, requiring
five base-level color samples and no implicit `Sample` operation for those
targets. Other sampler names retain their canonical mip behavior.

Run `tools/modern-shaders/check.sh` with .NET 10, Python 3 and Rust available.
The exporter reads the shader properties from the application; the translator
uses the locked Naga 0.19.2 dependency. Generated WGSL and layouts are committed
and embedded in desktop and Android assemblies. Rust is a build-time tool only.

The translation adds explicit uniform layout, separate textures/samplers,
framebuffer texture orientation, OpenGL clip-depth conversion, viewport mapping,
draw-time display-list color/normal inheritance, invariant position outputs and alpha testing. Depth textures use explicit loads
for the shared nearest-depth/PCF formulas. Native validation and pixel checks run
through `-renderfullcheck -renderer metal|vulkan|dx12`.
