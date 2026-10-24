# Camera track tools source checks

Run from the repository root:

```sh
dotnet run --project tools/rmlui-camera-tools-check/rmlui-camera-tools-check.csproj -c Release -p:BuildProjectReferences=false
dotnet run --project tools/rmlui-camera-tools-check/owner/rmlui-camera-tools-owner-check.csproj -c Release -p:BuildProjectReferences=false
```

The first project links the production controller, optional backend contracts, presenter partial, authored RML, preserved action enum and host registry. A small synthetic native-host seam checks document/sequence/confirmation behavior without claiming a rendered native UI.

The owner project links the production camera adapter, retained camera binary format, strict source/descriptor witnesses, safe binary publication helper and successful-picture matrix cache. Its generated sources extract the actual production native capture and persisted type definitions at build time. Only world/transport/global input boundaries are synthetic. Tests write isolated temporary bytes, never load a recording or render a world.

Checks cover complete group spacing, collisions/capacity, immutable retry, stale scene/transport/seek/source/sidecar reviews, same-size/same-stamp descriptor/source ABA, no polling I/O, actual matrix-derived pose capture, v6 byte equivalence, same live track update, no-overwrite creation, and recoverable raced binary replacement. Legacy non-memory backends retain the newer-picture refresh fence. The production memory backend commits edits without a modal, preserves 100 undo/redo transactions, rebinds dirty state after seeks, and publishes only through explicit Save. Its source-bound recovery journal runs on a bounded background writer. Owner projects select `python` on Windows and `python3` elsewhere; override with `-p:PythonExe=/path/to/python` if necessary.

The expanded `tools/rmlui-timeline-check/camera-owner` suite covers v1-v5 sidecars, portable state v1, 1024-key v6/state-v2/cropped paths, memory-only frozen preview, restart recovery, source/sidecar conflicts, tombstones and writer retirement races. The controlled CPU comparison is `tools/replay-camera-benchmark/compare.ps1`. See `docs/architecture/replay-camera-editing-20261022.md` for measurements and outstanding device evidence.

`tools/rmlui-opengl-check/CameraToolsGlAcceptance.cs` is separately wired into the existing `--playback` fixture. It exercises actual native controls with a memory backend. Central builds and GL runs remain coordinated; this source suite does not launch GL, real playback, accounts, encoders, or the main application.
