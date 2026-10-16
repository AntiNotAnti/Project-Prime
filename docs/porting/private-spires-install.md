# PORT-08: Opt-in local Spires import into the game's own map catalog

This is the first installation bridge from the decoded user's **own**
Metroid Prime 2: Echoes GameCube ISO/GCM into Project Prime's runtime
map catalog. It remains a **collision-surface graybox**, not the HD
Echoes arena and not yet validated against real media.

## Usage

Close any active match, then run:

```shell
ProjectPrime -importechoesspires "/path/to/your/Echoes.iso"
```

On next launch, a new custom-room entry `LOCAL ECHOES SPIRES` is eligible
for discovery from the existing `CustomRooms.UserMapDirectory` map catalog.
This requires the game's own MPH cartridge setup to already be usable.

The tool reads the local ISO/GCM in place, verifies Metroid6.pak against
the pinned source recipe, decodes MREA collision, spawns and standard
pickups, validates the native map draft, and atomically replaces
`user-maps/local-echoes/spires.json`. It does not enable an in-place
scene/catalog mutation during a match.

**Nonredistributable:** This JSON is locally derived game geometry and is
stored only on the importing user's device. The `LocalSourceOnly` flag
causes the ordinary `.ppmap` package writer and reader to refuse it;
the Community Hub is not a distribution path for these assets. Dedicated
servers running the map must derive an equivalent local map from their
own user-provided Echoes source. Other clients must do likewise; no
download/fallback substitution is offered.

Current limitations: generated collision as a simple native-sourced
visual proxy rather than actual Echoes textured render mesh; no jump-pad
tuning, dynamic actor state, water/Gravity Boost, authored arena sound or
real-media/build/performance acceptance yet. Mode support beyond
basic offline Battle remains unverified.

## Content-free CI

`dotnet run --project tools/external-import-check -c Release`

Synthetic tests verify atomic JSON serialization, local identity
roundtrip and refusal of community packaging. These do not substitute
for a legally obtained Echoes import and live game/device acceptance.
