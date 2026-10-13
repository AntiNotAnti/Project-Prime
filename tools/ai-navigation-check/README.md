# AI navigation scratch characterization

This asset-free diagnostic binds the existing private navigation query directly,
checks traversal-order ties, ten-candidate capacity, exclusions, NaN/infinity,
distance overflow, signed zero and fallback behavior, then measures the actual
method over five identical 100,000-query rounds after 20,000 warmup calls.
It creates no GL window or audio device. Reflection is confined to test setup;
the measured query uses a cached delegate rather than reflection invocation.

```sh
dotnet run --project tools/ai-navigation-check -c Release
```

Output includes the engine MVID, runtime version, node count, allocated bytes,
nanoseconds per query and an observable selected-node checksum. This is a
synthetic query microbenchmark, not FPS or end-to-end AI performance. Run with
other builds/workloads stopped before drawing timing conclusions. The source
can retain `new float[10]` while the runtime promotes the non-escaping array to
the stack; inspect the actual generated code rather than assuming an allocation.

To inspect the pinned runtime's optimized native code:

```sh
DOTNET_TieredCompilation=0 DOTNET_JitDisasm=Func2138D28 \
  DOTNET_JitStdOutFile=/absolute/path/navigation.asm \
  dotnet tools/ai-navigation-check/bin/Release/net10.0/ai-navigation-check.dll
```

Optional **diagnostic-only** query frequency measurement requires an isolated
checkout. Add `private static int _navigationQueryInvocations;` next to
`PlayerAiData.Func2138D28` and `_navigationQueryInvocations++;` as its first
statement. Build once and run independent headless processes with legal assets:

```sh
PROJECT_PRIME_USER_DATA='/absolute/path/Project Prime' \
  dotnet run --project tools/ai-navigation-check -c Release -- \
  profile '/absolute/path/Project Prime' 8 'MP1 SANCTORUS'
```

Repeat with 1 and 4 bots. Each run advances 1,800 fixed simulation steps with
all declared slots as bots, slot-modulo hunter/difficulty and native default RNG
seeds. It reports query calls, simulation failures and descriptive total step
allocation/time. The temporary counter is not a production feature: restore
the source after measurement. Profile mode refuses to run without it.

Keep raw logs and disassembly outside source control; retain concise summaries
with exact SHA/SDK/runtime/MVID and limitations. A low query count or zero warmed
allocation is evidence to leave this candidate alone.
