# GPU timing lifecycle checks

Run `dotnet run --project tools/gpu-timing-check -c Release`.
This package-free harness links the production timestamp conversion, readback
lease, and bounded sample queue. It verifies finite timestamp conversion, exact
opaque callback tokens, eight busy slots without overwrite, out-of-order frame
completion, cancellation/reuse, shutdown with late callbacks, bounded completed
results, callback/shutdown races, and phase-aware native unmap eligibility.
Free/recording/submitted/failed maps skip unmap; accepted pending maps and
completed successful maps require it. A map-start throw before acceptance skips
idle unmap, while a synchronous successful callback proves mapped storage.
It needs no native graphics device.

The opt-in `-renderwindowcheck -renderer <backend>` command enables timestamp
features before device creation and runs a separate native fixture. That fixture
times real clear/draw/present submissions, polls readback without a production
GPU wait, verifies frame IDs and owned ring storage, and retires a queued map
before consuming its result. Acceptance polling has a five-second deadline;
normal gameplay never waits to recover a missing timing sample. A device without
the enabled timestamp feature prints an explicit `SKIP`. A managed test pass
does not establish native query or presentation correctness.
