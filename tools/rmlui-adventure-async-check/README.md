# Async Adventure source checks

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-adventure-async-check -c Release -p:BuildProjectReferences=false --no-restore
```

This compiles current Core/Host source and existing live-menu acceptance fixtures
against a centrally built client engine. Source owners intentionally shadow its
older menu/Host types, so CS0436 alone is suppressed. It never builds the main
project graph. It retains synchronous desktop save journeys and runs36 async
assertions for exact proposals, no optimistic writes, busy fences, accessible
Cancel, cancel/refresh/route/dispose retirement, stale or mismatched results,
fresh exact readback, fixed exception diagnostics and retry.

The production stateless Android dispatcher is linked and passes7 fake UI
queue/admission/writer checks. No Android runtime, GameView, native/GL, scene,
user files, serializer, network or real save mutation is exercised. Actual byte
preservation is covered by the separate temporary-file save check. Native
pending/cancel rendering and Android managed compilation remain centrally
coordinated acceptance work.
