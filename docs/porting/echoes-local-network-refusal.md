# Refuse locally imported Echoes game geometry from network matches

Project Prime permits locally derived Echoes preview maps for an explicitly
offline play path, but the **same** room names may appear in the shared
custom-map catalog queried by multiplayer lobby controls and dedicated
servers. A one-player cap alone is insufficient protection: an online
host can start a match with one occupant.

`MapModeCapabilities.Supports(..., allowLocalSource: false)` now denies
all `LocalSourceOnly` entries by default before reading entity data.
Only OfflineWorkspace and the legacy offline PlayScreen explicitly set
`allowLocalSource: true`. Existing online callers (including dedicated
server, lobby, map vote, netcode tests and server browser) keep the
default false and therefore reject locally derived Echoes collision,
visual and cannon previews.

No network packet, map identity or historical replay format changes.
Native arenas and legally shareable Community maps behave unchanged.
Synthetic tests verify explicit offline allowance and default network
refusal for all three local tiers.

This does not make imported Echoes gameplay accurate. Real game media,
device performance, missing dynamics and campaign compatibility remain
separate acceptance requirements.
