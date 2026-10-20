# Frame profiler lifecycle check

Run `dotnet run --project tools/frame-profiler-check -c Release` from the repository root.

Links the production frame profiler and capture implementation. Asset-free stubs supply scene metadata and rendering dependencies; the real profiler executes BeginFrame/BeginRender/EndRender/BeginPresent/EndPresent/CancelFrame/StopCapture and writes its real receipt. The test injects a previous Present timestamp 750ms in the past to prove a continuous long hitch survives, then verifies an explicit cancellation starts a new segment and excludes the interrupted boundary. It uses no sleep, native rendering, or game assets.

This regression check does not measure hardware performance, renderer scope integration, or native overhead.
