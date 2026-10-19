# Packet comparison and saved-world indexes

RPS-06 attempted to accelerate detached packet comparison with a durable saved-world checkpoint. Those checkpoints do not declare the same decoder contract as a complete packet-only pass. `ConstructionState()` is the initial scene baseline, and the later decoder component also includes scene projection effects: `ReplaySceneServices` advances timed grants and marks authority-world application. Rewritten archives can add quiet-frame markers, changing the packet-only ignored-record count relative to the producer's checkpoint. Neither component can replace the exact packet history.

Packet comparison now declines these indexes, scans the complete packet history and reports `IndexFallbackReason`. `RestoredCheckpointFrame` stays null. The optional `preferIndexed` argument remains available; genuine packet-only index acceleration requires a separately specified compatible baseline. This repair changes no archive schema, world-seek restoration, playback ownership or movement simulation.

The durable regression retains exact decoder hash and player equality between requested-index and explicit-linear paths, and adds checkpoint frame 300 alongside 301, 799, 1250, 1500 and 1800. It requires an explicit fallback reason; a future compatible index must still reduce scanned frames. Native and Momentum actual recording validation is functional evidence only; elapsed timings on the shared Mac are not performance acceptance.

Like the existing detached spatial analyzer, packet comparison now releases validated BombWorld draw packets after each pumped frame through `DiscardDetachedDrawPackets()`. It has no scene to consume that queue; retaining it hit the existing 2048-packet budget on actual recordings. Shot facts, pickup history, objective state and lifecycle facts remain in the compared decoder. The queue budget and malformed-packet checks remain unchanged.

## Functional validation

Release Core and nettest builds: zero warnings/errors. Both actual Native and Momentum `-replaydurablecheck` runs passed all six equality targets, world cold/backward seeks, 801-frame and 351-frame nested clip fidelity, corrupt-checkpoint fallback and recovery. `--momentum-replay-rules` passed 82 assertions; `--protocol58-replay` passed 204 archive/byte checks.

`--momentum-replay-scene` retained the previously recorded gameplay/presentation digest in both models:

| Model | Assertions | Frames | Unchanged digest |
|---|---:|---:|---|
| Native | 85673 | 2982 | `BAE5662C01088E7195A093652563122EAB1B2F6C1E9169D5410BC54FA87618B6` |
| Momentum | 85586 | 2979 | `C866B47544BB8ADB6775B624237EE7D319C4796719A00E24C7A22B9016EA3A5B` |

Local reproduction uses operator-supplied assets and recordings:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:UseSharedCompilation=false
dotnet build tools/nettest/nettest.csproj -c Release -p:BuildProjectReferences=false -p:UseSharedCompilation=false
PROJECT_PRIME_USER_DATA="$isolated_data" dotnet src/MphRead/bin/Release/net10.0/ProjectPrime.dll -replaydurablecheck "$durable_recording" -output "$isolated_output"
dotnet tools/nettest/bin/Release/net10.0/nettest.dll --momentum-replay-scene "$isolated_data" "$original_recording" Native "$result_json" "$durable_recording"
```

Repeat the final two commands with the corresponding Momentum recordings and `Momentum` label. The isolated data directory contains only the operator's `paths.txt`; generated outputs and logs remain ignored artifacts.
