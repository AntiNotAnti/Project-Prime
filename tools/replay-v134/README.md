# v0.1.34 checkpoint compatibility fixture

The production reader has a frozen historical field, nested value-field and type-ID schema. The historical snapshot comes from tag `v0.1.34`, commit `f0e01e09e08467974b8a5ea359d690ebe0a7e3a4`, built with `Version=0.1.34`. Its contract is `455910DAF1D71841346B1B2692A3ACC3A4B742660CC34A485CCFF66999C2E273`.

Generate a real checkpoint with the old producer in a separate checkout:

```sh
python3 tools/replay-v134/export.py /absolute/v134-checkout /absolute/new-output --dotnet /absolute/dotnet
dotnet run --project tools/nettest -c Release -- --replay-v134 /absolute/data-directory /absolute/new-output
```

The exporter adds a diagnostic command to the historical checkout, refuses changes to its serializer/schema, and never synthesizes historical bytes using the current serializer. The producer needs the user's extracted game files configured in the app's normal `paths.txt`. The restore command's data directory contains `paths.txt`. Save the whole output: source replay, checkpoint, old-producer state oracle, exact type ordering and SHA-256 provenance. Generated asset-backed checkpoints remain local artifacts; no cartridge files are included here.

The restore test checks all eight players' position, velocity, facing, health and flags, plus scores/frame, constructor defaults for newly added chamber/game-mode state and continued playback. The ordinary `-replaycontrolcheck` also verifies contract selection and unknown-contract refusal. Run the asset-backed fixture test whenever the current schema changes; metadata-only CI cannot replace it.
