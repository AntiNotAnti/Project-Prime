# Offline setup contracts

The default harness exercises the toolkit-neutral controller with a fake launch
and persistence backend. It validates the real match catalog/modifier rules,
save rollback, training constraints, owner lifetime and exactly-once launch
handoff without starting a renderer, network session or local match.

```sh
dotnet run --project tools/offline-controller-check -c Release
```

Native mode opens the actual authored setup/arena/rules/training documents using
the real P/Invoke host and shared page manager. It drives keyboard focus/action
dispatch, preserved input drafts, validation/apply, modal retirement, stale
document rejection, positive heading/description gaps and disabled launch controls with the same fake service
boundary. Build the current bridge and package the assets before running it:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadRmlUi=true
dotnet run --project tools/offline-controller-check -c Release -p:MphReadRmlUi=true -- \
  --native artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  src/MphRead/bin/Release/net10.0/rmlui
```

The bridge must expose DrawList mode and all current typed Offline actions. This
check requires no OpenGL context. It establishes native DOM/controller behavior,
not GPU pixels, game scene loading, real save writes or packaged platform parity.
