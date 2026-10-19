# Protocol 58 replay compatibility follow-up

Merged #708 raises live protocol to 58 for its match-owned third-person camera.
Its checkpoint/metadata writers therefore emit protocol 58, but four archival
allowlists still accept only 56/57. Current v12/v13 replica checkpoints cannot
restore, and format-v6 timed-shot metadata cannot encode/decode. This follow-up is
independent of Momentum gameplay and is a prerequisite for continuing its
latest-main compatibility acceptance.

## Control and change

Clean main control: `862acd4317667735df29df9668ba48d51bcdf98a`. A fresh Release
build passed with zero warnings/errors. `--architecture`, `--timed-shot-replay`,
`--pickup-replay` and `--grant-replay` all failed on that exact clean checkout.
The archive suites report `Unsupported replica checkpoint layout/features`;
architecture also has a stale exact-current-protocol assertion. Control logs are
retained locally under `artifacts/momentum-compat58/control-*.txt`.

The fix adds **literal 58** alongside 56/57 to both v12/v13 layout clauses and
both format-v6 timed-shot metadata clauses. It retains exact feature masks,
versions, tail lengths, capability magic, policy checks and historical 53/54/55
adapters. It does not use a broad `>= 56` or automatically accept a future protocol.
No wire layout, protocol constant, checkpoint/hash schema or movement behavior
changes. Protocol-57 status and handshake hex fixtures remain intact. The
historical handshake is now raw bytes with explicit current-reader refusal,
because the generated current writer correctly refuses to emit a protocol-57
live handshake. The current protocol-58 encoding differs only in its version byte.

Stale current-protocol checks now expect 58. Positive tests also preserve the
third-person flag across session packing, checkpoint restore, match application,
scene copy and legacy game-mode normalization. No camera implementation changes.

## Validation

Host: Apple M4 Pro, macOS 27 ARM64, SDK 10.0.401/runtime 10.0.12, Release.
No game files, rendered scene or network transport is used by these checks.
`dotnet` below denotes the pinned local SDK.

```sh
dotnet build tools/nettest/nettest.csproj -c Release
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --protocol58-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --architecture
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --timed-shot-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --pickup-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --grant-replay
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --compact-intents
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --protocol17
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --player-names-chat
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --continuous-targets
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --pickup-protocol
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --enhanced-retirement
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --lobby
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --advanced-rules
dotnet run --project tools/server-browser-party-source-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true -p:OutputPath="$SERVER_OUTPUT/"
python3 -m unittest discover -s tools/nettest -p 'test_*.py'
git diff --check
bash tools/check-no-game-assets.sh
```

Release desktop/test and separate-output server builds passed with zero
warnings/errors. New compatibility suite: **142 passed**. Architecture, compact
intents and protocol transport passed. Existing timed-shot archive **4,668**,
pickup replay **83**, grant replay **51**, pickup codecs **355**, names/chat
**1,053**, retired-variant **81**, targeting/impairment tests, lobby **7,750** and
advanced-rule **131** checks passed. Actual-DLL Party checks: **106 passed**.
Python: **639 passed, one skipped**. Diff whitespace and tracked-file asset guard
passed. Local logs are retained under `artifacts/momentum-compat58/`.

The new byte tests cover explicit v12/v13 protocol-tagged 56/57/58 fixtures,
restore/recapture, retained match bits, future protocol 59 refusal, unknown version
and feature bits, mismatched features, truncation/trailing bytes and transactional
failure. Historical producers are not assigned the new third-person bit.
Format-v6 codec and actual file writer/reader cases cover all three protocols and
all three continuation policies. They also reject unsupported 55/59 metadata,
bad capability magic/revision/policy and malformed tails. Existing historical
fixtures and populated timed-shot, pickup and grant archive tests remain in use.

## CI, limitations and next work

A separate asset-free Windows/Linux/macOS CI matrix builds the real production
library and runs the archive/protocol suites, so they do not depend on unrelated
asset/package/UI gates. Existing broad CI remains enabled. CI is pending at
publication; committing does not wait for CI/review. The separate generator/UI/
Windows allowlist failures are tracked by #706 and are not fixed here.

These are byte/archive compatibility checks. They do not certify native replay
rendering, seeking a real match, third-person visual behavior or physical devices.
No performance claim is made; there is no new gameplay hot-path operation.
Momentum PR-03 can resume after this compatibility prerequisite, with its earlier
movement evidence preserved and archival checks rerun on the integrated code.
