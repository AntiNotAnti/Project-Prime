# Native replay timeline source checks

Run each nested project with `dotnet run --project <path> -c Release -p:BuildProjectReferences=false`; add `--no-restore` after first use.

- `view/timeline-check.csproj`: exact DOM/picture/metrics tickets, window math, group preview and readonly density lanes.
- `presenter/presenter-check.csproj`: actual presenter/RML/shared activation and concrete root presentation helper; synthetic native host/window only.
- `camera-owner/owner-check.csproj`: actual production matrix capture/cache, strong source identities and binary publication owners.
- `playback-owner/playback-owner-check.csproj`: actual retained player, transport and durable owner across seek-driven replica replacement; synthetic scene boundary.
- `editor-owner/editor-owner-check.csproj`: retained Editor/storage plus exact-frame seek and race regression.
- `python3 native-source/check.py`: extracted production queue-source/accessibility/focus guard, stub DOM; does not build native bridge.

All tests use isolated temporary/synthetic data. They do not claim actual GL, a replay world, or native window acceptance. Actual replay-window qualification is separate and uses production Shell, DemoPlayback and Current backends on a temporary copy of a validated synthetic archive.
