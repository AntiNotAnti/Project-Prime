# Native lobby with a real local server

```
dotnet run --project tools/rmlui-lobby-live-check -p:MphReadRmlUi=true -- artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
```

Supply the native bridge for the current platform. The tool runs the shipped RML
lobby document through real native layout, focus, and mouse input. It dispatches
the resulting typed intents through shared lobby controllers backed by real UDP
clients and a disposable local dedicated server.

The batch covers 2, 4, and 8-client ready/start/load barriers, 50 fresh lobby
join/leave cycles, and 20 match returns on the same persistent controllers. Every match waits the real
server intermission deadline, so the full run takes roughly nine minutes. It
checks duplicate native packets, one match request per client, explicit pump
handoff, fresh authoritative start generations and controller reuse on every
return, and release of every real server seat.

All client documents run sequentially in one native context; the real UDP clients
and their controllers remain independently alive. The existing explicit empty
authority bootstrap fixture stands in for gameplay scene application. This tool
does not establish rendered gameplay or simultaneous multi-window acceptance.
It disables identity ticket loading and performs no authentication, account
mutation, production connection, launcher preference write, or ROM extraction.
