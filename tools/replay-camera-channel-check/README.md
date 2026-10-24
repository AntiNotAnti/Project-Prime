# Camera channel checks

Run `dotnet run --project tools/replay-camera-channel-check -c Release` with the pinned SDK. This links production track/evaluator/format owners and uses isolated temporary synthetic replay bytes. It performs no asset loading or GL calls.

The suite checks opt-in channel isolation, canonical time independent of position arc mapping, time-aware angular/scalar continuity, strongly unequal intervals, noncommuting near-half-turn rotations and quaternion sign invariance, dense spherical branch safety, monotone PCHIP bounds, explicit easing and Hold cuts, extreme scalar handles, cache invalidation, v7/state-v3 compatibility and malformed metadata, 1024-key/cropped preservation and exact preview/export sampling at 30/60/120/240 FPS. It prints a warmed 1024-key advanced-channel CPU batch benchmark; set `DOTNET_TieredCompilation=0` when comparing optimized steady-state runs.

See `docs/performance/replay-camera-channels.md` for the mathematical contract, fallback safety argument, measurements and remaining real-device evidence boundaries.
