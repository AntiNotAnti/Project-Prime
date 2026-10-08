# Community map contracts

The default harness runs the presentation-neutral controller with an injectable
fake map service and package-preparation boundary. It exercises copied catalogs,
search/sort/lifecycle paging, exact package/revision selection, installation
fences and engine-thread commit, reports/favorites, confirmed creator lifecycle
changes, optimistic publication conflicts, cancellation, deferred upload-file
cleanup and late-result retirement. It makes no live account or publication
requests and does not write the production map library.

```sh
dotnet run --project tools/community-controller-check -c Release
```

Native mode opens the actual authored browser/flow documents through the real
P/Invoke host and shared page manager. It drives real keyboard/DOM actions at
720p, 1080p, a smaller viewport and 2x density, including publication review,
conflict decisions, field-draft preservation, installation/host handoff, Back,
stale dialog rejection and positive geometry/typography assertions. The service
boundary remains fake in native mode. Build the current bridge and package fresh
assets before running it:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadRmlUi=true
dotnet run --project tools/community-controller-check -c Release -p:MphReadRmlUi=true -- \
  --native artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  src/MphRead/bin/Release/net10.0/rmlui
```

The bridge must provide DrawList mode and typed Community actions 210–229. This
check establishes native document/controller behavior. It does not establish
live service authorization, package installation into a production library,
physical input/IME, GPU pixel parity or complete platform parity.

`CommunityEngineBackend` reuses `MapCommunityClient`, the existing narrow Hunter
License map ticket authority with one forced refresh on 401, map build/package
validation and `PreparedMapInstallation` runtime/publication fences. A saved
project or `.ppmap` path is explicit; validating never publishes automatically.
Upload commands retain the exact prepared package and expected latest-parent
hash until success, an explicit conflict decision or cancellation.
