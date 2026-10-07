# Studio diagnostics checks

Run `dotnet run --project tools/studio-diagnostics-check` with .NET 10.

This test compiles the actual BCL collector, formatter and Studio job manager. It measures collector CPU cost, allocation and process memory, then checks provider release, bounded job history, cancellation, immutable snapshots and unknown GPU timing. It does not initialize a native graphics device or validate GPU counters; `studio-render-check --gpu` and native Studio UI checks cover that boundary separately.

HUD counters describe the latest submitted map viewport, with resident geometry distinguished from cumulative geometry uploads. Replay draw, batch and GPU timing counters remain unavailable until their renderer exposes measurements. Checkpoint capture timing remains unavailable in the existing replay core. No unavailable value is replaced with zero.
