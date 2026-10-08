# Native host phase profile

Run with the native bridge being evaluated:

```sh
dotnet run --project tools/rmlui-host-profile -- /absolute/path/to/native/bridge
```

The fixture opens the real composed Home document, retains the hidden legacy
document, allows its entry animations to finish, then measures 1,000 owner-thread
frames. JSON includes phase mean/p95, allocation totals, actual/skipped native
updates, accessibility capture counters, and the optional native update state.
An infinite deadline is serialized as `"Infinity"`, meaning input or another
mutation must wake the retained UI. Older native libraries remain compatible
and report no optional state.

This isolates native host phases. It excludes the game scene, GPU composition,
OS accessibility providers, and desktop IME. It is not a process CPU comparison
or a shipping performance acceptance result. Use `tools/rmlui/performance-check.py`
for the alternating legacy/native game benchmark.
