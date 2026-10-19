# CW-02 schema and simulation contract

This is an unused foundation until CW-03 supplies explicit offline admission. It grants no native
slot, consumes no native ammo, writes no save/replay/wire data and exposes no creator package execution.
Online acceptance still requires CW-04 source proof and CW-12 manifest/Ready sequencing.

## Schema 1

`WeaponMechanics.Parse` accepts at most 16 KiB UTF-8 and depth 8. Every field is required;
unknown/duplicate/missing fields, comments, trailing objects, non-integer numbers, unsupported
schemas/behaviors and invalid cross-field combinations fail. No silent clamping/default balance.
The engine allowlist currently contains `semi-auto-precision`, behavior version 1, one initial
projectile, no derived projectiles, splash or piercing. Other presets require new engine implementation
and independently tested admission before entering the allowlist.

| Field | Units / accepted values | Prototype fixture |
| --- | --- | --- |
| schema / behaviorVersion | Exactly 1 / 1 | 1 / 1 |
| damage.body / damage.head | Integer HP, 0–2000 / 0–3000 | 65 / 150 |
| magazineCapacity | 1–32 rounds | 4 |
| maximumReserve / initialReserve | 0–512 / 0–maximumReserve rounds | 64 / 16 |
| shotCooldownTicks | 6–3600 authoritative 60 Hz ticks | 75 |
| reloadTicks | 1–3600 authoritative ticks | 138 |
| projectileSpeed4096 | 1–4,096,000; 1/4096 world units per tick | 819,200 |
| projectileRadius4096 | 0–4096 | 41 |
| range4096 | 1–40,960,000 | 4,096,000 |
| lifetimeTicks | 1–600; speed × lifetime must reach range | 6 |
| hipSpreadMilliDegrees | 0–10,000 | 1200 |
| scopedSpreadMilliDegrees | 0–hip spread | 0 |
| maxActiveProjectiles | 1–8 per owner | 8 |
| opticsMilli | 1–4 unique ascending integers, 1000–8000 | [2500,4000] |
| splash / piercing | false / false | false / false |

The complete prototype is **test-only**, `MechanicsChecks.Json`. It is not automatically registered
or equipped. Canonical encoding uses explicit property order, integer-only UTF-8 JSON, no whitespace,
fixed schema/behavior versions and every retained mechanics field. Names/listings are excluded.
Independent SHA-256 vector: `3ee854525a9737a3acdaf6f994dd8da75e229b5f87d820c96d98f17c59512228`.
The actual archive-byte hash remains a separate CW-11 concern. Definitions reject a supplied identity
whose digest differs from validated mechanics. Canonical byte exports are detached copies; optic
collections cannot mutate their private storage.

## Per-life state

Owner identity fences full match ID, authority epoch, slot, generation and life. Inventory binding
and grants share a monotonic authority-time high-water, observing all retained states; late acquisition
starts at the grant tick. Same/old life and owner cannot be rebound to refill. A different match/epoch
requires a new frozen registry. Clearing or disposing invalidates all escaped states without erasing
life/time high-water. Native arrays, three-slot selections and resource journals are unchanged.

A fresh grant has a full magazine and declared reserve, initially unequipped. One accepted fire spends
one round and sets the exact cooldown deadline. Denied/duplicate/reordered request sequences cannot
spend, publish a shot, or become a later accepted request after cooldown/reload/capacity recovers.
Authority must reserve an emitter before accepting fire; capacity failure consumes its request sequence
but spends nothing. This rules layer never spawns a projectile or authors damage by itself.

Reload transfers `min(capacity - magazine, reserve)` once at its completion tick. No debit/transfer
occurs at start. Duplicate requests cannot restart it. Swap/form unequip cancels reload and scope,
preserves magazine/reserve and retains bolt deadline; reloading after return takes the full duration.
A reload may start during bolt cycling, but completion cannot shorten the shot deadline.
Scope index 0 is hip; 1–N selects validated optics. Scope can change during bolt but never reset it.
Reload returns to hip and prevents entering an optic until completion. Damage alone does not cancel.
Death/respawn/despawn/spectator/owner/match/rule invalidation permanently closes that state object.

Simulation code must call `Advance` on authoritative ticks independently of input arrival. Tick and
request serial comparisons support uint wrap with the usual less-than-half-range window. The future
network ingress must additionally bound source age, sequence jumps, pose, journal ownership, manifest,
capacity, packet rate and lag compensation. These pure state tests do not establish multiplayer proof.
