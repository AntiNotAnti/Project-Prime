# PORT-12: opt-in world-render previews for all six locally owned Echoes arenas

This integrates the already verified MREA GX reader and independent
source-visual vs. native-collision compiler with the six-arena local
import path.

## Developer workflow

```sh
ProjectPrime -importechoesarenas "/path/to/owned/Echoes.iso"
ProjectPrime -importechoesvisuals "/path/to/owned/Echoes.iso"
```

The first command creates the original six simple collision previews.
The second opts into six separate `VISUAL ECHOES ...` local projects
with model geometry from the player's source MREA, existing fallback
MPH materials, and unchanged original source collision.

Visual and collision variants have independent MapIds and filenames
(`spires.json`, `visual-spires.json`, etc.), preserving old local
projects and historical references. Every output stays marked
`LocalSourceOnly` and cannot enter a Community `.ppmap` archive.
The previews are limited to solo Battle while dynamic Echoes objects,
water, moving platforms and network correctness remain unverified.

The desktop Settings button remains on the conservative collision
preview. Full Echoes texture/TEV conversions and real-device
acceptance remain separate milestones.

Synthetic acceptance verifies coexistence and unchanged map identities;
no Nintendo assets are checked in or transferred.
