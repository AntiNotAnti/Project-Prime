# CW-05n — offline world pickup acquisition

The user requires weapon acquisition through world pickups. Public offline Battle
now starts each local human life without M7 ownership. C can equip an acquired
M7; it cannot create one. The explicit developer fixture grant remains separate.
There is no menu loadout selection or public offline spawn grant.

Each of the 27 stock multiplayer room keys has one additive, authored station.
`StockRiftlinePickups` stores exact fixed-point centres; runtime never searches
for a location, moves native items/spawns, or reads an external placement file.
The station starts available, respawns after 1,800 simulation ticks (30 seconds)
and gives four magazine rounds plus sixteen reserve on first acquisition.
Subsequent collections add sixteen reserve, capped at64, preserving magazine,
reload, optics and cadence. Custom maps refuse activation until their own
placements exist. Ordinary availability-off matches retain their existing path.

Contact runs beside the native item ledger in `PlayerEntity.PickUpItems`, using
the current living actor and the same native biped/alt-form geometry. The trusted
funding callback is guarded by that synchronous actor scope. First ownership and
repeat reserve become usable on the next simulation tick. The ledger consumes
one incarnation and synchronously acknowledges its retained offline outcome.
A funding fault retires the runtime instead of duplicating an uncertain grant.

A first acquisition attempts auto-selection once at the next fresh input phase,
using native Imperialist selection priority only. It requires a usable biped,
no held/current firing and no simultaneous explicit selection. A blocked attempt
never retries later. The M7 retains its custom identity and separate ammo; no
runtime ID enters native weapon arrays or native packet fields.

Death clears inventory, pending controls and funding. Respawn binds a fresh empty
life and releases old custom native-fire suppression. Already emitted public
flights keep their original life provenance and canonical damage path. Scene
world/owner identity includes a fresh nonzero cryptographic epoch, independent
of gameplay RNG. Android's normal offline scene builder now activates the same
public runtime after shared preflight and frozen rule application.

The visible station uses original procedural receiver/barrel/optic boxes until
CW-06 assets. Submitted vertex arrays transfer to Scene's normal render pool;
rendering never advances availability or resources. This is a presentation proxy,
not final rifle art.

The reproducible `--stock-pickup-scene DATA ROOM` check uses actual fixed steps,
synthetic native walking and `PickUpItems`, with an explicitly declared initial
placement/orientation at a native spawn. `check-stock-pickups.py` runs all27 in
fresh processes with isolated user data and stable main/tool DLL hashes. It
never builds, calls grants, accelerates frame counters or substitutes collision.
The Proving Ground fixture additionally covers natural full cooldown expiry,
refill during reload, death, old-flight damage, empty respawn and reacquisition.

Online spawn grants are still transitional. Public online pickup activation,
item baseline/delivery, acquisition-knowledge fencing, normal wheel/cycle entry
and final device validation remain subsequent CW-05 work. A source counter alone
cannot prove that delayed remote controls were authored after a pickup; see
[the online dependency](05m-world-pickup-ledger.md). This slice does not qualify
online pickups, physical controllers, mobile usability or custom replay support.
