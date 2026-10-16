# PORT-11b: independent Echoes visual/physics mesh compilation

This implements the source-to-native conversion used after the opt-in
MREA world reader has been verified with real user-owned media.

- Collision triangles from the Echoes map remain **collision only**.
- Actual source world triangles become **render only** geometry, lowered to
  Project Prime's existing MapMesh/MapBuilder renderer.
- Native geometry identities are distinct from the earlier collision-preview
  map and deterministic across imports, preserving old local-replay references.
- Visibility uses an existing MPH fallback material until legal local-only
  texture extraction and TEV/shader conversion are implemented.
- Invalid geometry, out-of-bounds positions or ordinary shareable maps are
  rejected; private source-derived maps cannot be packaged to Community.
- No matchmaking, Android file selection, moving props or water mechanics
  have been enabled yet.

Synthetic acceptance covers geometrical separation and stable identity;
real-media and graphics performance validation remain pending.
