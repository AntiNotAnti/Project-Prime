# Fruity PR00 reproducible baseline contract

`tools/fruity-baseline.py` records a campaign before performance comparisons.
It never builds the native tool. Plan-only writes commands and explicit
`NOT RUN` evidence; run mode executes those commands sequentially and embeds
the native results without converting completion into FPS acceptance.

```sh
python3 tools/fruity-baseline.py --plan-only \
  --tool /absolute/built/native-render-check.dll \
  --dotnet /Users/jarrett/.dotnet/dotnet \
  --assets-data-dir /absolute/user-data-with-paths-txt \
  --game-data-version 'operator-supplied extracted-game revision or hash' \
  --camera '0,0,0' --facing '0,0,-1' \
  --room 'MP1 SANCTORUS' --size 1920x1080 \
  --ab legacy-list --ab legacy-bindings --ab batch-off \
  --ab legacy-depth --ab legacy-state \
  --output-dir /absolute/new-campaign
```

The example camera is a command-format example; choose and record a useful
room-specific camera before execution. Use `--run` instead of `--plan-only`
only after building the tool and preparing extracted assets. The game-data
version is operator supplied; the runner also records the exact `paths.txt`
contents/hash and user-data directory. It does not verify a ROM revision or
hash every asset. The runner hashes the tool's deployed sibling `ProjectPrime.dll`, which is
loaded by its normal project-reference runtime contract.

Each selected arm defaults to 60/120/144/240 for eight requested seconds in solo
(`-players 1 -idle`) and one-bot (`-players 2 -idle`) profiles. `-idle` disables
the main player's bot behavior; the second player remains the native bot.
The native harness fixes Original preset, Battle mode, RNG seeds 12345/98765,
and input snapshots; the runner explicitly fixes room, hunter, size, camera,
facing, shadows Off and outlines Off. The harness excludes the first 240
simulation steps. `--hz 60` selects just 60; repeat `--hz` to select more caps.
`--seconds` defaults to 8 and accepts the harness's 8..120 range.
Capture and lifecycle flags are excluded from the timed command. Each profile
also records and, in run mode, executes a separate image-evidence command with
`-shots` pointing at its own directory. Capture timings are not benchmarks.

The default arm uses current renderer defaults. Each repeatable `--ab` switch
adds an arm differing independently from default: legacy lists, legacy
bindings, batching off, legacy depth replay, legacy state requests,
`--ab legacy-room-vis`, or `--ab legacy-item-vis`. Known
renderer environment overrides are pinned to avoid inherited switch pollution.
This does not enumerate all combinations of switches.

`manifest.json` records source commit, status, binary dirty-diff SHA256,
nonignored untracked file hashes, platform, machine, SDK version, tool and
engine DLL SHA256, arguments/environment, and native output. Native MVID and
GPU fields are adopted only if present in native JSON. The native harness now
records actual GL vendor, renderer and version during `OnLoad` and includes
them in result JSON. Older prebuilt binaries without those fields produce
`NOT RUN` GPU evidence instead of inferred hardware. Preserve associated logs.

`EXECUTED` means the native process returned zero and produced readable JSON.
It does not prove strict frame caps, low jitter, visual parity, networking,
cross-platform results, or an optimization's benefit. Compare completed-frame
intervals, CPU submission/swap profiling, allocation/GC, and binary MVIDs
before claiming a performance result. The short run may not have enough samples
for the native 0.1% low estimate. PR01 was declared complete by the user and is not reimplemented here.

The plan-only contract test is `python3 tools/test-fruity-baseline.py`; it uses
missing tool/SDK paths, verifies independent arms, and launches no renderer.
