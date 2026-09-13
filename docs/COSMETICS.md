# Project Prime cosmetics

Project Prime cosmetics are optional presentation data. They never participate in
simulation, hit detection, movement, damage, spawning, scoring, matchmaking, or the
gameplay content identity. The Node validates and freezes a loadout for each roster
seat; the Worker only republishes those immutable IDs. `SnapshotPlayer` and the UDP
snapshot wire format remain unchanged.

## Identity and compatibility

- Stable keys are the authored and persistence identity.
- Explicit immutable `ushort` IDs are the Protocol 19 wire identity; zero always
  means base/none/default.
- The official catalog rejects duplicate keys and IDs. Published IDs must never be
  reassigned.
- Missing optional local content resolves to base skin, no armor effect, and the
  default death presentation. It must not disconnect a client or invalidate a replay.
- Protocol 19 adds only the three cosmetic IDs to reliable roster entries. Replays
  from protocols 8 through 18 use the fixed legacy 30-byte roster decoder and receive
  zero IDs.

## Ownership and lifecycle

Signed-in selections are stored per player and Hunter by the Backend. Guest/offline
selections use the local preference store. The Server Node owns mutable lobby
selection and clears readiness when a loadout changes. A match freezes the approved
IDs into its roster; reconnect reuses that frozen seat. Bots use zero IDs.

The client owns all cosmetic runtime state. Skin overrides compose after canonical
recolor/team appearance and before gameplay feedback. Damage, freeze, cloak, and
team-readability feedback therefore remains dominant. Armor and death effects use
presentation facts only and never read or mutate gameplay RNG. Seeds are derived
from immutable match, slot, life, effect-ID, and event-tick facts.

## Runtime bounds and accessibility

Quality is `Off`, `Reduced`, or `Full`. Independent controls may suppress other
players' cosmetics, particles, distortion, and rapid flashes, or force strong team
colors. Visibility, distance, first-person, spectator, alt-form, and death takeover
policies suppress work before submission. Fixed particle, ribbon, attachment,
distortion, and light budgets use stable slot-ID tie breaks.

Manifests are bounded data. Loaders reject oversized files and collections,
duplicate identities, invalid or absolute paths, traversal, URLs, scripts, DLLs,
shader source, and out-of-range values. Runtime shader compilation and reflection
type loading are not supported.

## Authored VFX atlas

`project-prime-vfx-atlas.png` is original Project Prime source artwork. Its adjacent
JSON manifest records the source SHA-256, dimensions, sampling policy, and twenty
deterministic pixel regions. The one shared `Client.Presentation` resource is embedded
for both desktop and Android; no generated cache or cartridge asset is committed.
The atlas is RGBA, 1254 by 1254 pixels, and 1,365,630 bytes. Linear sampling must use
the manifest's two-pixel inset and clamp addressing to avoid neighboring-cell bleed.

## Authored death animation boundary

Death clips are cooked offline and contain a Hunter, bounded duration, node-indexed
tracks, and a SHA-256 skeleton signature over the canonical Hunter/node/parent
description. Runtime rejects malformed clips, bad node indices, duration violations,
and signature mismatches. It never performs best-effort remapping or parses glTF in a
match. A death body is presentation-owned and starts from the last successfully
submitted alive interpolated pose. New life, slot/connection identity changes, replay
seek/reset, room disposal, or killcam exit cancels it immediately.

The first authored clip is `samus-backward-collapse.json`. Its 19-node hierarchy and
signature were derived from the exact AMHE1 `Samus_lod0` model with:

```text
ProjectPrimeTools -deathskeleton Samus_lod0 Samus -data AMHE1_DIRECTORY
```

Re-cook the checked-in 263-byte runtime artifact deterministically with:

```text
ProjectPrimeTools -deathanim ABSOLUTE_SOURCE.json -out ABSOLUTE_OUTPUT.pda
```

The client loads only the fixed embedded resource, validates the cooked bounds, then
requires the Hunter and SHA-256 skeleton signature to match. A mismatch returns to the
legacy/default death path; it is never remapped at runtime.

## Acceptance boundaries

Automated tests prove catalog, wire, persistence, lifecycle, security, and bounded
policy behavior. `CosmeticSubmissionAcceptanceTests` also runs deterministic CPU-side
submission captures named `samus-base`, `samus-skin`, `samus-lightning`,
`samus-inferno`, `samus-phase`, `samus-quantum-death`, `samus-spectral-death`,
`samus-inferno-death`, `eight-player-cosmetics`, `first-person-cosmetics`, and
`team-colors-with-skins`. These assert a body-stage submission, finite values,
bounded emission, and the hard particle, ribbon, distortion, and light budgets.

The same fixture benchmarks warmed steady-state CPU submission for zero cosmetics,
one local player, eight Full players, eight Reduced players, particle-heavy,
distortion-heavy, and eight-player death-burst workloads. It records CPU time,
primitive and budget counts, thread allocations, and GC collection deltas, and
enforces zero managed allocations in the measured frame loop.

These CPU-side captures do not manufacture graphics results. Nonblack pixels,
final artwork, live body visibility, bloom appearance, shader/backend errors, GPU
frame time, GPU memory, actual draw calls and texture uploads still require real
renderer telemetry and deterministic image captures from packaged desktop and
Android clients on representative hardware. Physical Android behavior, two-client
reconnect, and replay/killcam visuals likewise remain explicit release gates.
