# Final composite readback check

Run `dotnet run --project tools/final-composite-check -c Release -- opengl`
(or `metal` on a Mac with the supported native runtime). This requires a graphics
session but no game files. The check deliberately uses an odd-width backbuffer,
adds an overlay after the backdrop, changes the read framebuffer and pack
alignment, and verifies pixels plus state restoration through the production API.
It also verifies `ScreenCapture.SaveWindow` uses that same contract.

Local evidence, October 1 2026: this check passed on Apple M4 Pro with OpenGL
2.1 and native WebGPU Metal. The actual adapter/backend is printed. The existing
`-shellshot` application sequence separately completed on both backends with 31
PNG/evidence pairs each, including a real match, pause/settings/results, window
resize and fullscreen transitions. Representative Metal match and settings
captures were visually inspected. Game-content captures are local artifacts,
not public repository fixtures. Android managed compilation passed; this does
not establish Android physical readback or cross-platform visual acceptance.

The animated shell script does not freeze state for pixel equivalence. Its
successful completion proves capture and lifecycle execution, not universal
parity, leak freedom, hardware performance, or device-recovery acceptance.

The focused native `-renderwindowcheck -renderer metal` also passed injected
device reconstruction with resource restoration, fresh OpenGL fallback after
repeated failure, texture/HDR updates, and 120 warm-cache resize/resource cycles
on the same adapter. Its no-growth check includes bind groups. This bounded
synthetic loop does not replace the full gameplay/replay/editor transition matrix.

The October 6 renderer implementation adds a separate shared UI compositor
fixture to this command. It checks premultiplied alpha over a known backdrop,
CPU row origin, viewport/program/framebuffer/texture/depth/scissor restoration,
and same-size upload after release. The previous October 1 results above do not
cover this new fixture. Android's debug acceptance activity additionally routes
an overlay through the production Vulkan-only dispatcher; physical execution
is required to establish that platform's pixel and lifecycle acceptance.
