# CW-12f11 — explicit resource-owned world12 Scenes

This slice connects the detached [world12 format](12f8-custom-world-contract.md)
to the [retained historical Scene](12f9-historical-scene-resources.md) and
[passive visual adapter](12f10-passive-presentation.md). Local qualification
is recorded in [the evidence receipt](evidence/CW-12f11.md).

`ReplayWorldCheckpoint.CaptureCustom` requires an active private custom inspection,
a complete CP15 accumulator and exact retained historical resources. It shares
the native graph writer but deliberately emits version12/flags15, construction
and current CP15 and the existing CWW1 descriptor/current-state appendix.
Its recording frame is the session's actual recording frame; callers cannot
relabel it. Native `Capture` continues to refuse a custom Scene even after Stop.

`RestoreCustom` requires a fresh, unstepped, unrestored inspection Scene with
the matching retained descriptor. It validates construction/current scope,
header/map/mode/clocks and CWW1 before restoring the common bounded graph and
its native appendices. Historical leases and decoded package resources remain
independent Scene owners and never enter the serialized graph. Native `Restore`
refuses both custom capsules and custom target Scenes.

`PassiveReplayScene.RestoreCustomInspection` constructs from the capsule's
original construction checkpoint, binds resources before room load, restores
the graph/current decoder, and publishes only after success. Any failure disposes
the unpublished Scene. `Session.RestoreCustomInspection` sets the exact current
recording frame and contiguous next-frame frontier without native reposition,
file access, host rewind or packet reapplication. The next accepted step is +1.

The service generation/RNG caches and player replication bridge are already
graph roots in the native contract; no guessed generation adoption is added.
Derived rifle/pickup/flight/contact presentation is rebuilt without a simulation
tick or health/RNG advance. Existing head cosmetic tracks are reconstructed from
the retained accepted contact history, without paying damage. Re-restoring the
same published custom Scene is refused; another restore creates a fresh owner.
Viewer camera and viewport are initialized independently by the consumer; they
are not restored gameplay state. This setup follows canonical comparison in
the GL harness rather than advancing the world to set up a picture.

Custom file Join, ordinary recording, killcam/export admission and archive seek
remain closed. Resource-owned world capture/restore alone does not establish
accepted two-client contacts, head/death continuity, bots, first-person optics,
audio or cross-process file playback acceptance.
