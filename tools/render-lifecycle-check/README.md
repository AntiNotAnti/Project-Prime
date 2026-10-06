# Render and audio ownership checks

Run `dotnet run --project tools/render-lifecycle-check -c Release`.

This dependency-free executable links production helpers and checks:

- CPU cleanup errors still execute graphics cleanup, including an error-sink fault.
- Floor-sized odd HiZ chains preserve each far-edge, central, and corner clear-depth texel; every source texel reaches a parent footprint.
- 20,000 seeded atlas rent/return operations never overlap live spans, preserve a shared survivor, and coalesce back to full capacity.
- Failed character texture transactions release distinct new admissions once, preserve pre-existing pins/bindings, and retain committed admissions.
- RGBA, RGBA16f, and compressed block mip-chain logical capacity math.
- Controlled concurrent music requests serialize constructors; stale and cancelled results never publish; old workers cannot clear the latest Loading state; decode failures settle; shutdown drains the complete chain.

These checks do not claim native GPU or audio correctness. Validate `-renderwindowcheck -renderer metal` on macOS, DX12/Vulkan on Windows, Vulkan on Linux, and both Vulkan/GLES on physical Android. Android device checks must cover repeated match enter/exit, a destroyed surface during a slow load, pause/resume, rotation, changed dimensions, and load/prewarm failures. Check CPU/GPU live resource counts and capacity before/after cycles. Use a physical mouse to verify grabbed raw motion and UI/absolute stylus behavior. Moving doors/platforms/characters must remain visible because history depth rejection is currently disabled pending static occluder provenance.
