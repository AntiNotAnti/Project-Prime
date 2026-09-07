# Authoritative multiplayer migration

Online play uses authoritative wire family 2, protocol 6 and a dedicated, single-writer simulation. Every
player joins as an ordinary client, including the player who starts a local
server. The directory can run without game files; game servers require extracted
game data or a validated server content package.

## Scope and invariants

The target is a single-writer, 60 Hz dedicated simulation using the existing UDP
transport and gameplay code. Offline play, .NET 10, the MphRead namespace,
directory service, network impairment tools and existing diagnostics are retained.
Client input replaces owner-reported position and ammo. Prediction, interpolation,
authoritative combat, lag compensation and world replication were exercised before
removing live relay admission and player-authority migration.

No commercial game data is included. No deployment or public-server change is
part of the local validation recorded here.
Optional P11 regional orchestration remains outside this core migration.

## Original migration phase status

This table records the migration baseline. Current protocol, view timing and
projectile upgrades are recorded in [NETWORK_POST_UPSTREAM.md](NETWORK_POST_UPSTREAM.md).

| Phase | Implementation and evidence |
| --- | --- |
| P0 baseline | Checked packet readers, sequence/ACK primitives, traffic/arrival/work metrics, xUnit tests, reproducible UDP baseline matrix and CI job. Original and instrumented relay passed all 16 matrix cases. Rendered offline report matched; two real WAN clients passed. |
| P1 protocol foundation | Version-5 header, random session IDs, nonce-based admission/reconnect, ACK bitfields, bounded reliable channel, Loading/Ready states, rate limits, monotonic clock and independent loading keepalives. Actual UDP impairment tests include 8 clients and 33-second client/server loading stalls. |
| P2 headless foundation | Presentation-free scene loading, fixed scheduler and content packaging. Eight bots exercised all 12 modes; node ownership and occupancy now cover eight slots. |
| P3 movement | Redundant input commands carry controls and aim, never position, health or ammo. Server simulation selects spawns and publishes recipient input acknowledgements. Real UDP abuse tests cover forged state, input floods, starvation, reconnect and stale identities. |
| P4 prediction | Fixed 256-entry history, historical error reconciliation, discrete hard corrections and decaying camera offset. No arbitrary engine rollback or repeated gameplay side effects. |
| P5 interpolation | Fixed 32-snapshot history, 100 ms presentation delay and at most 50 ms extrapolation. Identity/life/form discontinuities are discrete; temporary render poses restore authoritative physics. |
| P6 combat | Server engine owns legal shots, projectiles, damage, death, ammo and score. Bounded reliable events preserve immutable connection/life ownership and drive client presentation. Real UDP duels verified wall occlusion, damage and death under impairment. |
| P7 rewind | Bounded Imperialist hitscan history uses source-backed player colliders and current world collision. Server-measured RTT caps rewind; traveling projectiles remain on the live server timeline. |
| P8 world | Atomic, bounded multipart world updates cover pickups, spawners, objectives, score and match state. All 12 modes passed headless replication checks. Unsupported mutable collision and missing objectives fail explicitly. |
| P9 cutover | Public joins/hosting use the authoritative path; live relay and player-authority admission are removed. Historical demo decoding is passive and has no socket. Public hosting/join/stop, rendered rotation/rejoin and recorded demo replay passed. |
| P10 hardening | Bounded queues, process-isolated hosting, data validation and measured diagnostics are implemented. All 16 gameplay WAN cases and a five-minute eight-client run passed; final 60-second LAN/extreme checks also passed after roster diagnostics changed. |

## Reproduction

Build and test with a .NET 10 SDK:

```sh
dotnet test src/MphRead.Tests/MphRead.Tests.csproj -c Release -p:MphReadServer=true
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true -o /tmp/fruity-server
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true -o /tmp/fruity-nettest
tools/check-dedicated-server.sh /tmp/fruity-server
python3 tools/run-network-baseline.py --dotnet dotnet \
  --server /tmp/fruity-server/FruityPrime.dll \
  --nettest /tmp/fruity-nettest/nettest.dll --output /tmp/fruity-baseline
```

`run-network-baseline.py` requires a new output directory. It owns and cleans up
its server/proxy subprocesses. Its clients send real wire traffic, but do not run
the game: delivery/bandwidth results are not gameplay or visual acceptance.

The game data directory is the extracted version folder, for example
`files/AMHE1` for the USA revision-1 ROM. It contains `levels`, `models` and
`_bin/arm9.bin`. It is not the `.nds` file or its parent Downloads directory.
The existing ROM extraction entry point creates that folder and `paths.txt` next
to the executing assembly. The headless path instead takes an explicit directory
and version without modifying `paths.txt`:

```sh
dotnet /tmp/fruity-server/FruityPrime.dll -headlesscheck "MP1 SANCTORUS" \
  -data /path/to/files/AMHE1 -dataversion AMHE1 -players 8 -frames 36000

# Continuous simulation, stopped with Ctrl+C or SIGTERM; no network admission yet.
dotnet /tmp/fruity-server/FruityPrime.dll -server-sim "MP1 SANCTORUS" \
  -data /path/to/files/AMHE1 -dataversion AMHE1 -players 8
```

`-mode Nodes` also probes capture attribution and occupancy clearing for slots 4
and 7. `-server-sim ... -frames 3600` performs a bounded one-minute scheduler run.

The normal game server accepts authoritative input commands:

```sh
dotnet /tmp/fruity-server/FruityPrime.dll -server "MP1 SANCTORUS" \
  -port 27015 -data /path/to/files/AMHE1 -dataversion AMHE1
dotnet /tmp/fruity-nettest/nettest.dll --simulation 20 27015,27015
```

The simulation fixture drives real players on the headless server, exercises
reconnect, and checks server-reported movement and input acknowledgement. It does
not render a client or establish prediction, interpolation or combat acceptance.

## Loading dependency boundary

| Path | Required on the server |
| --- | --- |
| SceneSetup.SetUpRoom / LoadEntities | Collision, node layers, gameplay entity construction and relationships; unchanged. |
| Room models and hunter models | CPU metadata, hierarchy, animations and attachment transforms remain loaded. They are not replaced with guessed collision shapes. |
| GeneratePlayerVolumes / Kanden node distances | Gameplay geometry; retained. |
| Weapon metadata, AI personality and navigation | Retained. |
| InitTextures / GenerateLists / BindGetTexture | GPU upload/display lists; skipped for headless scenes. |
| LoadEffect / SpawnEffect | Visual particle graph; skipped. Existing nullable effect-handle behavior is retained. |
| Sfx.Load | Uses the existing silent SfxInstanceBase; no native audio device. |
| Music, intro/results cameras, HUD | Presentation initialization/calls guarded; gameplay clocks and mode transitions retained. |

The content baker projects required CPU model metadata and strips textures,
palettes and render commands. The validated AMHE1 retail package covers 26 rooms
and 245 map/mode combinations, using the same entity layer as live clients.
It contains 6,709,938 bytes including its manifest, 93.11% less than the full
extraction. Independent full bakes produced identical manifests and payload hashes.
See [content packaging](NETWORK_SERVER_CONTENT.md) for coverage and reproduction.

The engine uses process-global GameState, PlayerEntity and collision storage.
Each server process must own exactly one Scene on one logical writer. Running
multiple independent matches in threads of one process is not supported.

## Recorded baseline, 2026-09-06

Original and P0 relay ran 20-second cases for 2, 4 and 8 clients at LAN,
50/100/150/250 ms RTT, with increasing jitter and 0/1/2/3/3% loss, plus a four-client
20/60/120/200 ms asymmetric case. All 16 cases passed in both builds.

Eight-player latest snapshot deliveries per second, before/after:

| Case | Before | After |
| --- | ---: | ---: |
| LAN | 60.00 | 60.00 |
| 50 ms RTT | 54.11 | 54.53 |
| 100 ms RTT | 42.36 | 42.51 |
| 150 ms RTT | 34.37 | 34.81 |
| 250 ms RTT | 25.95 | 25.69 |

Reordering causes older snapshots to be discarded; these rates count accepted
newer snapshots, not raw datagram arrivals. Peak per-client incoming plus outgoing
LAN traffic was approximately 66.4 decimal kB/s in both builds, excluding IP/UDP
headers. This is a relay baseline, not an optimized server result.

Both original and modified builds passed the existing 90-second two-client
gameplay check at asymmetric 20/100 ms latency with 2% loss. The eight-player
rendered Sanctorus map report was identical before/after: 2,823 frames, all eight
spawned, seven scripted players moved/morphed/fired, six deaths. Its original
affliction probes reported freeze/burn failures and disrupt no-hit; those are
inherited gaps, not newly verified combat features.

Native macOS could not create the existing compatibility OpenGL context. Rendered
checks ran under a temporary Linux ARM64 container with Mesa/Xvfb; headless
checks run natively on macOS. Neither proves Windows or a deployed server.

Initial one-minute headless run: 3,600 ticks, eight bots, zero dropped or catch-up
ticks, 0.371 ms mean tick duration, 28.581 ms worst tick including warm-up,
1.264 ms mean scheduler drift. Fast and real-time runs ended with identical
player positions, health and scores. These are local observations, not capacity
or release guarantees.

The broader mode run exposed four-player node ownership/occupancy assumptions and
headless-only music calls. Node neutral ownership now uses SlotCapacity; capture,
occupancy and HUD arrays cover all eight slots. Survival may continue with only
bots on a headless server; the original offline last-human behavior remains.

## Combined gameplay evidence

The integrated eight-client rendered check used 20 ms versus 100 ±20 ms latency
settings and 2% loss. All eight clients rendered a lit scene, moved, observed
seven moving peers, received complete world state and received actual server
combat/damage events. Historical mean movement error was 0.087–0.110 world units;
no client required a hard correction. This was a 30-second-per-client check, not
a long soak or subjective human gameplay assessment.

The real-server authority harness passed with 2 and 8 LAN clients and asymmetric
20/200 ms RTT with 2/3% loss. Six seconds of input flooding advanced 360–362 server
ticks while independent observers continued. A client survived 33 seconds without
game-thread polling, movement neutralized without death/respawn, and reconnect
changed connection identity. Stale input and disconnect replay did not affect the
replacement player. Server queue drops and dropped simulation ticks were zero.

The final public-path rendered check passed with eight clients, four rotations,
spectating and server-controlled rejoin under latency/loss. Two recorded demos
replayed 2,487/2,524 rendered frames through 3/4 matches, applied 213/210 world
updates and 174/162 combat events, and finished with zero player-state mismatches.
The final offline Sanctorus check used eight players and 22 seconds plus its
existing probes: its full 2,823-frame report was byte-identical to the baseline.
The inherited offline affliction-probe failures described above remain unchanged.

A separate rendered two-client run passed rotation through Nodes, Bounty and
Capture, including spectating/rejoin and complete world updates. The migrated
batch runner also passed a two-client, 25-second check at 200 ms RTT with jitter
and 2% loss. It stopped only its owned processes and released their ports.

The final shot-spread fix transmits a server-selected seed and isolates pellet
randomness from rendering effects and recycled projectile callbacks. Six combat
events occupy 493 bytes, within the existing reliable payload limit. An actual
three-projectile Judicator spawn reproduced bit-exact velocities after wire
encoding/decoding, despite a different starting pool slot and 45 unrelated RNG
calls during recycling. Fresh WAN
clients passed with 196/187 combat events and 30 damage events each. A newly
recorded demo replayed 2,693 frames across three matches, applied 212 world updates
and 196 combat events, and finished with zero state mismatches. The impaired
Judicator collision duel passed again, and the offline report remained identical.

Native macOS headless and Linux ARM64 rendered evidence do not establish Windows,
mobile, Raspberry Pi, subjective human gameplay quality or public deployment
performance. Nothing was deployed or listed publicly.

## Final measured performance

Five-minute native macOS AMHE1/Sanctorus workload: eight actual UDP clients moving,
jumping and firing through the server simulation; one reconnect midway. The
server used the baked package, 60 Hz simulation, 30 Hz full player snapshots and
5 Hz complete world state. A separate full 16-case matrix covered 2/4/8 clients at
LAN, 50/100/150/250 ms RTT with jitter/loss and asymmetric 20/60/120/200 ms RTT.
Every case passed. These are socket-client gameplay checks, separately from the
rendered checks and data-free synthetic connection matrix.

| Measurement | Observed result |
| --- | --- |
| Duration/ticks | 300 seconds; 18,008 ticks including admission/shutdown |
| Tick mean/worst | 0.387 / 36.485 ms, worst includes warm-up |
| Dropped/catch-up ticks | 0 / 2 |
| Transport/combat queue drops | 0 / 0 |
| Process CPU after warm-up | 0.026–0.046 CPU cores |
| Aggregate input/output | approximately 142.6 / 271.5 decimal KB/s |
| Combined traffic averaged over clients | approximately 51.8 decimal KB/s per client, excluding IP/UDP headers |
| Whole-process allocations | approximately 3.2–3.5 MB/s, including the existing gameplay engine |
| Combat delivery | 7,025 shots, 277 damage events, 15 deaths; reconnecting client missed one event while disconnected |

Two resident-memory samples during the run were 45.7 and 62.6 MiB. This short
observation does not establish long-term heap stability. Fixed input, snapshot,
rewind, interpolation and reliable buffers have deterministic capacity tests.
The hottest value/span codecs and interpolation history have focused allocation
checks. Socket arrays remain: measured server CPU and tick results do not justify
adding a pooled socket ownership system. No delta compression or interest
management was added.

After final roster-ping publication changes, separate 60-second eight-player LAN
and extreme-WAN runs passed. Mean ticks were 0.606/0.626 ms with zero dropped
ticks and queue drops. LAN combined traffic averaged 51.9 KB/s per client;
extreme WAN averaged about 55.0 KB/s including retransmissions. Synchronous map
loading can exceed one tick; loading keepalives preserve peers and bounded
catch-up prevents a backlog. The steady-state measurements above exclude map
rotation workloads.

## Additional reproduction commands

```sh
# Compact package: output must be a fresh directory.
dotnet FruityPrime.dll -servercontent /srv/fruity/content-amhe1 \
  -data /path/to/files/AMHE1 -dataversion AMHE1 -allrooms

# A game server is unlisted unless an explicit -master address is supplied.
dotnet FruityPrime.dll -server -data /srv/fruity/content-amhe1 \
  -rotation /path/to/maprotation.txt -players 8 -nomaster -noupdate

# Complete gameplay matrix, with a fresh output directory.
python3 tools/run-network-baseline.py --dotnet dotnet \
  --server /path/to/FruityPrime.dll --nettest /path/to/nettest.dll \
  --simulation --data /srv/fruity/content-amhe1 --seconds 20 \
  --output /tmp/fruity-gameplay-matrix

# Real collision/rewind duel; use Judicator, Magmaul or VoltDriver for affinities.
dotnet FruityPrime.dll -combatduel "MP1 SANCTORUS" \
  -data /path/to/files/AMHE1 -weapon Imperialist -seconds 30 \
  -netlag 50:10 -netloss 2 -noupdate

dotnet nettest.dll --match-lifecycle /path/to/files/AMHE1
dotnet FruityPrime.dll -netcheck localhost -seconds 45 \
  -spectate 12 -rejoin 18 -recorddemo -noupdate
dotnet FruityPrime.dll -democheck /path/to/match.fpdemo -seconds 60 -noupdate
```

The final xUnit suite passed all 152 tests, including custom-map argument
forwarding and shot-seed serialization. Ten Python tooling tests and the shell
checks passed. The server and desktop builds completed with zero warnings/errors;
deployment scripts were inspected and tested locally, never executed remotely.

Custom-directory regression: an eight-player, 600-frame TEST ARENA headless run
passed with `-mapdir` pointing outside the executable's default map folder.
Directory selection is initialized before server/content commands; local hosting
forwards the absolute directory through process arguments. Two isolated custom-map
servers and the normal host/join path also passed, including paths with spaces and
an apostrophe, authoritative snapshots and process cleanup.
