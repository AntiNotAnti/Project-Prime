# PORT-06: Dark Samus source identity and native skeleton preflight

Adds distinct, *disabled-by-default* character IDs for Dark Samus and Space Pirate Trooper without changing vanilla Hunter enum numeric values or gameplay/network defaults.

- `darksamus` uses the Samus simulation, with Echoes and Corruption visual-skin keys reserved separately.
- `trooper` uses Weavel as its initial simulation parent, with four Pirate Trooper visual keys and new abilities deferred.
- ANCS source locator discovers the Echoes Dark Samus model, skin and skeleton resource IDs from a verified local Echoes PAK.
- Bounds-checked CINF loader reads bind skeleton nodes/names without exporting game assets.
- `--probe-darksamus /path/to/Echoes.iso` validates known Metroid1/Metroid5 hashes and prints only source IDs and skeleton node count.
- Synthetic asset-free tests check character IDs, root bone parse and malformed resources.

These are import preconditions, **not** a playable Dark Samus implementation. Remaining: full CMDL/CSKR, native animation playback, importer transaction, local presentation, first-person cannon, morph form, selector, replay/network character identity and platform performance. MP3/Wii support remains deferred.
