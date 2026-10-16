# PORT-07: local Dark Samus mesh geometry and skin weights

This slice advances the local-only Dark Samus import from an ANCS/CINF skeleton inventory to *actual model vertex and triangle decoding*.

- Adapts Second Hunt's MIT-licensed Retro CMDL v2/v3 display-list/material parser under a distinct Project Prime import namespace.
- Decodes corresponding CSKR per-vertex skin weights with explicit input budgets.
- Extends `--probe-darksamus` to validate a supported local Echoes source, load CMDL + CSKR + CINF and report geometry counts without writing the source data.
- Adds synthetic asset-free regression coverage for CSKR, source offsets and invalid CMDL.

Not yet integrated into the renderer. Native animation parsing, model/skin/rig conversion, encrypted Wii Corruption sources, first-person cannon, morph ball, matchmaking identity, shader/material fidelity and device performance remain subsequent work. No Nintendo geometry, textures, sound or disc data is bundled.
