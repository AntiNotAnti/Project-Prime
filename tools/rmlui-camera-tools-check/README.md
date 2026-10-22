# Camera track tools source checks

Run from the repository root:

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-camera-tools-check/rmlui-camera-tools-check.csproj -c Release -p:BuildProjectReferences=false
/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-camera-tools-check/owner/rmlui-camera-tools-owner-check.csproj -c Release -p:BuildProjectReferences=false
```

The first project links the production controller, optional backend contracts, presenter partial, authored RML, preserved action enum and host registry. A small synthetic native-host seam checks document/sequence/confirmation behavior without claiming a rendered native UI.

The owner project links the production camera adapter, retained camera binary format, strict source/descriptor witnesses, safe binary publication helper and successful-picture matrix cache. Its generated sources extract the actual production native capture and persisted type definitions at build time. Only world/transport/global input boundaries are synthetic. Tests write isolated temporary bytes, never load a recording or render a world.

Checks cover complete group spacing, collisions/capacity, immutable retry, stale scene/transport/seek/source/sidecar reviews, same-size/same-stamp descriptor/source ABA, no polling I/O, actual matrix-derived pose capture, retained v3 byte equivalence, same live track update, no-overwrite creation, and recoverable raced binary replacement. Runtime toggles and preview cannot authorize persisted edits until a newer successful picture plus explicit refresh.

`tools/rmlui-opengl-check/CameraToolsGlAcceptance.cs` is separately wired into the existing `--playback` fixture. It exercises actual native controls with a memory backend. Central builds and GL runs remain coordinated; this source suite does not launch GL, real playback, accounts, encoders, or the main application.
