# Frame profiler lifecycle check

Run `dotnet run --project tools/frame-profiler-check -c Release` from the repository root.

Links the production frame timing, profiler and capture implementation. Asset-free stubs supply scene metadata and rendering dependencies; the real profiler executes BeginFrame/BeginRender/EndRender/BeginPresent/EndPresent/CancelFrame/StopCapture and writes its real receipt. The Windows regression waits through a 750ms continuous hitch, drives the production accumulator through 200ms catch-up overflow and a 500ms stall, and proves both trigger and following intervals survive. It verifies explicit first/last, focus, cap, scene, cancellation and failed-Present boundary events. It uses no native rendering or game assets.

This regression check does not measure hardware performance, renderer scope integration, or native overhead.
