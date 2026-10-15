# macOS Studio OpenGL transport check

Run on a Mac with hardware OpenGL support:

```sh
DOTNET=/path/to/dotnet tools/studio-gl-check/run-macos.sh
```

The native C proof creates independent legacy (2.1) and core (3.2 requested,
4.1 returned on the test Mac) CGL contexts. Both import one public IOSurface,
attach it to complete framebuffers, and verify a legacy clear reaches core
with the expected RGBA pixel.

The managed check compiles the production `MacLegacyGlSurface.cs` directly,
without building the engine or Studio. It exercises 12 device lifetimes,
two simultaneously alive surfaces per device, and five different sizes per
surface. It checks the transferred pixel, presentation context restoration,
framebuffer/viewport/rectangle-texture restoration, scissor/sRGB restoration,
shared device identity, producer-failure context restoration, and GL errors. These are clear
draw callbacks, not the engine scene or a full native UI acceptance test.

`GraphicsApiStub.cs` explicitly substitutes only the canonical facade lifecycle
hooks in this transport-only project. It verifies native resolver availability,
one binding initialization per shared device, and profiler release while the
legacy context is current. An injected binding initialization failure verifies
constructor context restoration and reference rollback. It does not substitute
for testing the real engine facade caches, capabilities or retained geometry.

`glReadPixels` appears only in these tests to verify transport. The production
bridge uses CGL/IOSurface shared GPU storage plus framebuffer blits and has no
CPU pixel readback. It currently uses `glFinish` in both producer and consumer
contexts for conservative cross-context synchronization; sustained viewport
performance still needs measurement in the native application.
