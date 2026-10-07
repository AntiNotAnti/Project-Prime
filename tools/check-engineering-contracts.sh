#!/usr/bin/env bash
# Cheap, asset-free contracts run on the exact revision being packaged.
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
python3 -m unittest discover -s tools/telemetry -p 'test_*.py'
python3 -m unittest discover -s tools -p 'test_dedicated_smoke.py'
python3 -m unittest discover -s tools -p 'test_release_policy.py'
node --test tools/edge-check/*.test.ts
# Run client contracts before the server compile, which changes the project
# surface in the same output directory. Avoid parallel builds of this project.
dotnet build src/MphRead/MphRead.csproj -c Release
for check in replaycontrolcheck replayformatcheck charactermodelcheck frametimingcheck; do
  dotnet run --project src/MphRead/MphRead.csproj -c Release --no-build -- "-$check"
done
dotnet run --project tools/nettest -c Release -- --authority-policy
dotnet run --project tools/nettest -c Release --no-build -- --architecture
dotnet run --project tools/nettest -c Release --no-build -- --input-edges
dotnet run --project tools/nettest -c Release --no-build -- --lobby
dotnet run --project tools/nettest -c Release --no-build -- --weapon-policy
dotnet run --project tools/nettest -c Release --no-build -- --health-shots
dotnet run --project tools/nettest -c Release --no-build -- --transport-stress
dotnet run --project tools/updatecheck -c Release
dotnet run --project tools/render-lifecycle-check -c Release
dotnet run --project tools/authored-mip-check -c Release
dotnet run --project tools/frame-trace-check -c Release
dotnet run --project tools/desktop-pacing-check -c Release
dotnet run --project tools/android-pacing-check -c Release
dotnet run --project tools/replay-timeline-check -c Release
dotnet run --project tools/hosted-cache-check -c Release
dotnet run --project tools/map-editor-check -c Release -- --roadmap-only
dotnet run --project tools/map-editor-check -c Release --no-build -- --replay-preparation-only
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
