Source-linked Studio entry checks; the game project is not built or launched by this tool.

```sh
dotnet run --project tools/rmlui-studio-check
dotnet run --project tools/rmlui-studio-check -- --native /absolute/path/to/libProjectPrime.RmlUi.Native.dylib
dotnet run --project tools/rmlui-studio-check -p:PrimeAssemblyDirectory=/absolute/path/to/frozen/client/output -- --native /absolute/path/to/libProjectPrime.RmlUi.Native.dylib
```

The first command checks map/package/replay/clip launch argument compatibility, recovery, real file-path validation, unavailable-picker guidance, owner-thread publication, cancellation and stale selections. The native mode additionally loads the shipped templates and Studio page at four framebuffer/density combinations, including 640×320, dispatches real pointer/keyboard DOM actions, preserves edited paths, shows launch errors and proves page replacement cancels pending picker ownership. It checks the Replay Library button is reachable inside the actual viewport and emits the typed Theatre route. The current count is 136 neutral assertions plus 152 native assertions, or 288 combined.

The optional `PrimeAssemblyDirectory` uses an already built game assembly and avoids a game-project build. Source-linked mode uses a diagnostic sink for the native picker's error logging; it never initializes the game's logging or session graph.

The native check injects a launch backend to avoid opening an external application or OS dialog during automation. Actual Studio process launch and OS picker behavior remain owned by `StudioApplicationLauncher` and `NativeFilePicker`.
