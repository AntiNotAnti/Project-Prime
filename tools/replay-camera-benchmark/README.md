# Camera CPU evaluator comparison

Run `./tools/replay-camera-benchmark/compare.ps1 -Dotnet /path/to/dotnet` from PowerShell.
The script compiles the exact `HEAD` camera evaluator in a separate temporary project and compares it with working-tree source. It does not modify the SDK pin or source files. Temporary baseline sources remain available for inspection.

Both processes use .NET optimized JIT from startup (`DOTNET_TieredCompilation=0`) so tiny loops do not compare different asynchronous JIT promotion phases. Each uses identical 64-key curved Spline/Bezier tracks, constant speed, warmup, and 400 batches of 128 fractional samples. The JSON reports per-sample batch p50/p95/p99, allocation, and a checksum that must match between revisions. These are synthetic CPU evaluator measurements, not render/device/whole-editor responsiveness evidence. Default tiered cold-start behavior remains a separate measurement.
