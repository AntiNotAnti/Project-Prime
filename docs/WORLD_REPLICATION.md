# Authoritative world replication

The authoritative protocol carries periodic full world frames in `NetMessageType.World` (12).
Gameplay runs on the server. Client entity hooks only present received item,
objective and mutable collision state. Offline entity processing is unchanged.

Each body starts with a 20-byte little-endian header:

| Offset | Width | Meaning |
| --- | --- | --- |
| 0 | 4 | Match identity |
| 4 | 4 | Monotonic wrapping world revision |
| 8 | 4 | Server simulation tick |
| 12 | 2 | Total records in this complete frame, at most 256 |
| 14 | 2 | First record in this batch, a multiple of 24 |
| 16 | 1 | Record count in this batch, at most 24 |
| 17 | 3 | Reserved zero |

Records have a one-byte kind, one-byte slot, two-byte flags, four-byte entity
identity, three float position values, and five four-byte scalar fields. Each
record is 40 bytes. A full batch occupies 980 bytes; the connection header brings
the UDP application payload to 1004 bytes, below the 1024-byte limit.

The first 17 records contain the match and all eight score/time rows. Other
records contain:

- Spawner activation, respawn countdown, spawn generation and last consumer slot.
- Live item identity/type/position, owner spawner and server despawn countdown.
  IDs do not recycle within a match. Consumption is removal from the complete
  live set; respawn creates a new identity. Reapplying a revision creates no
  duplicate item. Health/ammo grants come from player snapshots.
- Node ownership, occupying players/team, captured player, progress, contest and
  visual rotation; flag carrier, base/drop state and reset timer.
- Match phase/clock/goals/Prime Hunter plus team and player scores/times. An older
  world frame cannot overwrite a more recent player snapshot's score table when
  the caller supplies its tick to `ClientWorldState.Apply`.

`ClientWorldState` validates complete batches before staging, rejects duplicate
entity identities, and swaps the presentation
state only after every record of a revision arrives. Reordered or repeated old
batches never roll back a completed frame. Loss leaves the previous complete
frame visible until a later full frame completes. Loading and late join use the
same full-frame path. Match transitions must reset the assembler after the old
scene closes.

Gameplay RNG is not restored from world frames. Server simulation chooses
spawns/drops/gameplay outcomes. Cosmetic client animation can use local state.
All world capture and application happen on the simulation/render owner thread;
the transport worker never accesses entities.

## Verification

Run the deterministic codec/assembly tests:

```sh
dotnet test src/MphRead.Tests/MphRead.Tests.csproj -c Release -p:MphReadServer=true --filter FullyQualifiedName~WorldTests
```

The asset-backed probe uses an already extracted AMHE1 directory, not an NDS ROM.
It selects a room whose active multiplayer layer supplies the requested mode
objectives, simulates eight bots for 3600 ticks, and round-trips 600 complete world
frames. It reconstructs late-join items and checks repeated application:

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -- --world-check /path/to/AMHE1 Nodes
```

Run once per process for each multiplayer mode. The process-global game engine
supports one active scene. This probe verifies headless state and presentation
construction; it does not substitute for rendered clients or WAN acceptance.

## Supported collision geometry

An all-layer entity inventory of the supplied AMHE1 revision found no Door,
ForceField or Platform entities in any of the 29 registered non-First-Hunt
multiplayer room definitions (27 retail definitions plus TEST ARENA and DUST2).
`Read.GetEntities` with layer `-1` includes every layer, so the result
is independent of mode or player count. `SceneSetup.GetExtraEntities` returns
without adding entities for multiplayer, and the custom map generator adds none
of these types. These rooms use static room collision and the replicated
pickups/objectives described above.

`WorldStateCapture.ValidateRoom` rejects a future room containing these mutable
collision entities with a specific error. Supporting them requires an explicit
replication implementation and acceptance tests; clients must not silently run
an independent platform/door/forcefield simulation.

Reproduce the complete source-data inventory with:

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -- --world-check /path/to/AMHE1 inventory
```

Disconnect/reconnect releases objective ownership before the player slot is
reused. A carried flag drops (or resets according to the match rule), Prime
Hunter is cleared, and a captured node transfers score credit to a remaining
teammate or becomes neutral. The world probe exercises this release path as well.

## Network match lifecycle probe

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -- --match-lifecycle /path/to/AMHE1
```

This owns an actual local server process and three bounded UDP proxies. It runs
Sanctorus Battle, resets Sanctorus into Nodes, rotates to Proving Ground Battle,
and wraps. Two initial clients and a third late joiner verify match identities,
input resets, full world state and stable connection identities. The proxies
apply seeded 250 +/- 50 ms RTT, 3% loss and 2% duplication independently in both
directions, then explicitly replay old snapshots/world/events/inputs. A client
pauses its owner-thread polls for 2.5 seconds during loading; stale Ready and
freshly enveloped old-match inputs must not activate or mutate it. The final
check closes the server's parent stdin and requires a successful shutdown.

The objective admission gate has a separate asset-backed negative check:

```sh
dotnet run --project tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -- --world-check /path/to/AMHE1 reject-empty-objectives
```

Sanctorus has no Bounty objective in the eight-player entity layer. The server
must reject that configuration before accepting players and release its scene.
