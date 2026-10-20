# Async offline Rules and Adventure source checks

```sh
dotnet build tools/rmlui-adventure-async-check -c Release -p:BuildProjectReferences=false
dotnet tools/rmlui-adventure-async-check/bin/Release/net10.0/nettest.dll --offline-rules-async
```

This compiles current Core/Host source and existing live-menu acceptance fixtures
against a centrally built client engine. Source owners intentionally shadow its
older menu/Host types, so CS0436 alone is suppressed. It never builds the main
project graph. Build the coordinated main engine first. The focused route runs67
offline Rules controller checks and36 linked Android Rules dispatcher/cache checks,
plus the existing36 Adventure async
assertions for exact proposals, no optimistic writes, busy fences, accessible
Cancel, cancel/refresh/route/dispose retirement, stale or mismatched results,
fresh exact readback, fixed exception diagnostics and retry.

The production stateless Android dispatcher is linked and passes7 fake UI
queue/admission/writer checks. No Android runtime, GameView, native/GL, scene,
user files, serializer, network or real save mutation is exercised. Actual byte
preservation is covered by the separate temporary-file save check. Native
pending/cancel rendering and Android managed compilation remain centrally
coordinated acceptance work. Ordinary live-menu journeys run separately through
`rmlui-live-menu-check`. The source-linked tool's broad default route includes an
unrelated theatre fixture that reflects engine-only types through the linked
controller assembly; it is not the focused async acceptance entry point.
