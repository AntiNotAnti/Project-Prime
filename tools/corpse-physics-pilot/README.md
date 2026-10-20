# Non-shipping corpse backend pilot

Run `dotnet run -c Release --project tools/corpse-physics-pilot -- ./results --enable-experimental-physics`.
This runs standalone contracts and the shared CPU benchmark. It does not run the
game, authenticate death facts, load native models or grant a weapon. See
[design](../../docs/custom-weapons/09-physics-pilot.md) and
[evidence](../../docs/custom-weapons/evidence/CW-09.md).

`scheduledRenderHz` describes interpolation work at fixed60Hz simulation; it is
not achieved display refresh. Keep raw JSON, host/runtime metadata, cold/JIT
outliers and allocation/pool measurements when comparing runs. No target adoption
follows from a fast desktop result. The Android fixture is a separate application.
