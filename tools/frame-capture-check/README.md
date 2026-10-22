# Frame capture check

Run `dotnet run --project tools/frame-capture-check -c Release` from the repository root.
This asset-free executable links the production `OpenGlFrameCapture.cs` directly and exits nonzero on failure. It requires the .NET 10 SDK; no game data, native renderer, or third-party test packages are required.

Checks cover nearest-rank percentiles (including the 1,000-sample minimum for p99.9), true slowest-one-percent mean versus the retained reciprocal-p99 metric, empty/partial summaries, numeric-budget boundaries, invalid and large timers, bounded records and cancellation accounting, conservative GC and inclusive UI/upload attribution, chronological schema-3 exports, existing-file preservation, and zero managed allocation in warmed disabled/storage/full Add loops.

The allocation check isolates the nullable capture call and value storage. Capture construction, summary generation, and export intentionally allocate. These synthetic checks do not establish native frame-time overhead, GPU performance, physical presentation latency, or benchmark qualification.
