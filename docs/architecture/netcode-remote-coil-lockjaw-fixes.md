# Remote Shock Coil and Lockjaw bomb-jump fixes

Requested on 2026-10-11 as follow-up work on the existing plan branch, now
`fixes`. Changes are local and unreleased.

## Remote Shock Coil

Accepted continuous pulses must resolve immediately because multiple queued
pulses can reuse one native beam in a single step. The normal entity pass then
processed that same beam again. Native continuous-beam expiry sees positive age
and removes it, so the targeted Shock Coil never reaches drawing.

A per-beam runtime frame marker now lets the ordinary pass retain the already
resolved beam for drawing. Each newly accepted pulse still resolves separately;
Spawn resets the marker on pool reuse. An unrefreshed beam expires on the next
step. Replica replay processing ignores this live scheduling marker, and the
explicit checkpoint field schema is unchanged. Native untargeted-beam visibility
policy is unchanged.

The asset fixture reproduces the failure with the prior engine DLL (SHA-256
`beb7b66d62b31992b00f7e2cc5c6ea8855b5346d39e5cea72e1791c1edcd4316`).
Authored target and payment checks pass before the ordinary entity pass returns
false. With the fix, the suite checks retained scene membership, finite native
Draw09 trail geometry and positive alpha, no second damage/heal payment, two
queued positive pulses, next-step expiry, and actual accepted spawning on a
client with authority disabled.

## Moving Lockjaw bounce

The authority already computes native owner contact when detonating a bomb.
The old client instead tested its *current* position when the reliable terminal
arrived. Moving away during delivery lost a valid bounce, while standing still
continued to work. The asset fixture reproduces that distinction.

The terminal now carries the actual authority owner-contact result. The local
owner applies the native vertical velocity floor without another proximity gate.
Horizontal velocity and higher existing upward velocity remain intact. Existing
stream/life/generation, local-owner, alive/in-play, alt-form, and spectator gates
prevent unrelated or stale impulses; terminal sequencing prevents duplicates.
Nearby camera shake remains cosmetic and cannot gate the movement response.
Detached replay worlds do not apply this live local-owner response.

Protocol **51** assigns bit 0 of a detonated BombEvent's existing reason byte to
this result. Its encoded size remains 98 bytes and snapshot layouts are unchanged.
Unknown detonation outcome bits are rejected. Clients and servers must both use
protocol 51; the existing handshake rejects older live peers. Historical protocol
48–50 terminal records with zero reason still decode, and replay movement remains
owned by the recorded poses. This does not implement pickup receipts or exact
freeze timers from the earlier plan.

## Validation

Final results and commands are stored under `artifacts/fixes-netcode/` (ignored
local evidence). Both regression fixtures use real game assets and production
admission/processing paths, with controlled roles, positions and event delivery.
They do not claim a physical multi-device or lossy UDP playtest. Draw09 validation
inspects submitted native geometry, rather than a GPU screenshot.

Final Release validation (all passed):

- Clean Release rebuild: 0 errors; existing compiler warnings only.
- Continuous combat: 667 assertions, including real nonauthority client playback.
- Bomb scene: 70 assertions; bomb protocol: 52 assertions.
- Existing self-splash mechanics: 353 assertions.
- Continuous snapshots: 337 assertions, maximum packet still 1172 bytes.
- Replay format: 3013 checks, including historical formats and malformed inputs.
- Lobby: 7745 assertions including real UDP lifecycle and protocol mismatch refusal.
- Protocol 17/19, targeting and architecture checks also pass.
- `git diff --check`: pass. CI now runs the portable bomb protocol suite.

`artifacts/fixes-netcode/results.json` records commands, exit codes, engine hash,
and source hashes. The before-fix failure logs and final build logs are alongside it.
