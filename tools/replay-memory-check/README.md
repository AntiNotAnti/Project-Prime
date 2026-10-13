# Headless replay memory isolation

This diagnostic uses the existing passive replay player, real recording reader,
replica scene and seek/checkpoint/disposal path. It calls the existing
`Headless.Enter` contract before creating any scene, so it has no GL window or
native audio device. It is reader/replica isolation, not rendered replay, full
Studio UI, export or screenshot acceptance.

```sh
dotnet build tools/replay-memory-check -c Release
PROJECT_PRIME_USER_DATA='/absolute/operator-owned/user-data' \
dotnet tools/replay-memory-check/bin/Release/net10.0/replay-memory-check.dll \
  -replay '/absolute/recording.ppdemo' -cycles 120 \
  -output '/absolute/fresh-replay-memory.json'
```

The recording must support frames 120, 30, 240 and 120. Every cycle opens a fresh
player and seeks to those exact targets. The repeated frame-120 presentation
state hash must match within the cycle and across cycles. GPU/camera/audio are
omitted by the existing presentation-state projection; no visual parity claim
is made. Every player is disposed twice to exercise idempotence; active readers,
checkpoint bytes/counts and unexpected native ownership fail the fixture.

Weak references observe players, replaced/current scenes, sessions and readers.
A NoInlining helper returns before the diagnostic GC; result records contain
scalar/hash data, never the actual owners. Full GC/compaction runs after each
helper and at final drain, followed by 30 one-second samples. Those collections
are diagnostic policy, not a production GC change. Retention observations do
not identify root paths or enumerate every application cache.

Output includes DLL hashes/MVID, actual recording SHA-256, commands, environment,
fixed seek targets, owner weak observations, process/GC/LOH/worker counters,
actual seek wall time and cumulative GC pause deltas. The recording path and its
historical compatibility warnings remain explicit. Keep recording/game assets
outside Git. Paging/compression can decrease RSS without freeing allocations;
no FPS or global boundedness claim follows from these counters.
