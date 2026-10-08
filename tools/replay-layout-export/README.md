# Replay world layout archive

World capsules version 1–3 use positional fields, reflection-ordered structs and
numeric object IDs. Adding fields to `ReplayWorldSchemas` shifts these contracts.
Packet protocol conversion alone cannot restore an older clip's world origin.
`ReplayWorldLayouts.json` preserves historical field types/order and object IDs;
only known fingerprints are admitted. Original recordings are never rewritten.

The catalog covers the checkpoint schema history beginning at `42847844`, with
metadata from compiled revisions `56640679` (protocol 16), `a96ab5b6` (protocol 17),
`eab6f234` (protocol 41), and the current build. `archive.py` reads historical schema
sources from Git. Its explicit migrations retain the older press-history and
homing field types, the three structs whose layouts changed, and the retired
spawn-protection report byte. The regression suite freezes fingerprints from 13
shipped recording layouts, including assembly-version-dependent fingerprints.

To regenerate, build those revisions in disposable checkouts, then export each
assembly's actual field metadata (no game window or game data is needed):

```sh
dotnet run --project tools/replay-layout-export -- /path/to/ProjectPrime.dll /tmp/layouts/56640679.json 56640679
# Repeat for a96ab5b6.json, eab6f234.json and current.json.
python3 tools/replay-layout-export/archive.py /tmp/layouts src/MphRead/Mods/Replay/ReplayWorldLayouts.json
dotnet run --project tools/nettest -- --replay-protocol42
```

Before changing a future checkpoint layout, preserve the outgoing compiled
metadata, update the generator's source revision inputs, and add a frozen binary
regression. Do not replace old tables with today's reflection order. Check
actual restoration and backward seeking as well as fingerprint admission.
