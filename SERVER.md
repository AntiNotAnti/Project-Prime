# Running a Project Prime server

You do not need any of this to play online. **Create Lobby → Hosted lobby** asks
an always-on allocator to start one isolated authoritative lobby process only
when it is needed. That child stays alive through Lobby → Match → Results →
Lobby and exits when the hosted lobby is abandoned.

The normal public deployment therefore keeps the lightweight **master/allocator**
running and does not keep an empty gameplay world simulating all day. A
permanent Continuous game server is optional for an always-visible official or
rated lane.

Every child/game server **runs the match itself**, so the host machine still
needs the game files. Authoritative servers record canonical match replays by
default, with bounded retention.

```bash
# Linux
./ProjectPrime -server -port 27888 -players 8 -servername "My server"
# Windows -- the console binary, not ProjectPrime.exe
ProjectPrimeServer.exe -server -port 27888 -players 8 -servername "My server"
```

## Movement rules

Classic (the `Native` configuration value) remains the default. An operator can
opt in a standalone game server with `-server -movement Momentum` or
`-server -movement Quake3` or `-server -movement Traversal` or `-server -movement Parkour`; use
`-movement Native`, or omit the option, for original Hunter movement. Unknown
values are refused. This option does not change master/allocator policy.

In an editable lobby, its owner can choose **Movement: Classic / Momentum / Quake III / Traversal / Parkour** in
Match Rules and apply the draft. The server validates the change before start;
guests see the effective choice and cannot apply it. The choice freezes when
the match starts and is retained in session/replay facts. Roll back by selecting
Classic before the next start, or restarting a standalone server with Native.

Momentum changes ordinary biped acceleration, air-strafing and manual bunny-hop
momentum. Native jumps, alternate forms and abilities remain native. Balanced
and Enhanced Hunter variants cannot be combined with optional movement profiles.
Quake III enables full-speed air projection and strafe-jump speed building with
no artificial total-speed cap. Its profile v4 also chains held jumps on legal
landings and supports additive self-missile rocket jumps with normal self-damage.
Traversal adds stronger air control, ramp jumps, Magmaul jumps, crouch/power sliding and tagged training-map
surfing; see the [Traversal guide](docs/movement/traversal-movement.md).
Parkour adds optional airborne crouch tuck and bounded wallstrafe; see the [Parkour guide](docs/movement/parkour-movement.md).
All live peers must use protocol 78; older clients are refused
rather than assigned different movement rules.
Older Classic and Momentum recordings remain readable with their original rules. It remains an
experimental opt-in while the final platform, combat and performance matrix is
being completed; see [movement modes](docs/movement/movement-modes.md).

## Game files are required

**This changed, and it is a breaking change.** A server used to be a relay: the
first client to connect ran the match, and the server only forwarded packets,
so it needed nothing. Now the server runs the match, which means it runs the
engine, which means it needs the files.

Put a `paths.txt` beside the binary pointing at them, the same file a client
uses:

```
0.35.1.0
AMHP1=/home/you/projectprime-server/files/AMHP1
```

A server without them **will not start**. It says so and exits:

```
[server] cannot run the match: game files could not be located
[server] a dedicated server runs the match itself now, so this one will not
         start. Put the game files on this machine and paths.txt beside the
         binary -- see SERVER.md
```

That is deliberate. The old alternative was to fall back to client-authority relaying: it put the match on a player's machine, where that
player's own shots resolved instantly while everybody else's took a round trip,
and where a disconnection took the match with it.

`-simulate` and `-authority` are still accepted and now do nothing -- an
existing systemd unit or launch script keeps working unchanged.

| Flag | |
|---|---|
| `-port N` | UDP port. Default 27888 |
| `-players N` | slots. Default 4, use 8 |
| `-servername "NAME"` | the name shown in the browser |
| `-rotation FILE` | default `maprotation.txt`, written beside the binary on first run |
| `-friendlyfire` | team damage on |
| `-powerups` | enable native power-up pickups in server rotation defaults; disabled when omitted |
| `-nomaster` | stay off every server list |
| `-master HOST` `-masterport N` | use a server list other than `51.161.113.128:27889` |
| `-serverreplays on\|off` | canonical authoritative replay recording. Default on |
| `-noserverreplays` | shorthand to disable canonical server replay recording |
| `-serverreplaystoragegb N` | replay storage soft cap in GiB. Default 25; 0 = unlimited |
| `-serverreplayretentiondays N` | delete eligible recordings older than N days. Default 14; 0 = forever |
| `-serverreplaykeeplast N` | always protect the newest N finalized recordings. Default 100 |

## Canonical server replays

A standalone authoritative server records the stream it treats as canonical:
accepted player intents, its authoritative snapshots, roster/match state and
replay events. Recording starts when a player is present and the server produces
its first authoritative snapshot. Map rotation finalizes the previous replay.

The default policy is:

```
server replays:   on
storage cap:      25 GiB
retention:        14 days
keep newest:      100
```

Cleanup runs at server startup and after each finalized match. The newest
`-serverreplaykeeplast` files and any replay with a `.favorite` sidecar are
protected first. Age pruning runs next, then the storage cap removes the oldest
remaining eligible files. If protected files alone exceed the cap, they are
kept and the server logs the remaining overage.

Canonical files live under the server export tree in `_demos/server/`; the
server logs the exact replay path when recording starts. Interrupted `.part`
files are not automatically deleted by retention.

The compact flags above also accept the configuration-style aliases
`-server_replays`, `-server_replay_storage_gb`,
`-server_replay_retention_days`, and `-server_replay_keep_last`.

Example for a smaller public server:

```bash
./ProjectPrime -server -players 8 \
  -serverreplaystoragegb 10 \
  -serverreplayretentiondays 7 \
  -serverreplaykeeplast 50
```

## Ports

UDP only.

- **27889** — always-on master/directory + lobby allocation requests.
- **27900-27919** — default on-demand hosted lobby pool.
- **27888** — optional permanent Continuous game server.

The allocator must be started with its public address, for example
`-public 51.161.113.128`, because hosted children report back over loopback and
the browser must be given the internet-reachable address instead of
`127.0.0.1`.

## Map rotation

`maprotation.txt`, one match per line, `#` for comments:

```
MP1 SANCTORUS      | Battle | 7 | 7
MP3 PROVING GROUND | Battle | 7 | 7
```

`ROOM KEY | mode | minutes | points`. Only the key is required.

`ProjectPrime -rooms` lists every key — the 27 cartridge rooms and any custom map. It reads the game
files to do that, so run it on a machine that has them, not necessarily on the server.

## As a service

`deploy-server.sh` now deploys the **master/allocator only by default**. It
keeps UDP 27889 resident and creates lobby children in the configured host-port
range when players ask.

Set `MPH_DEPLOY_GAME_SERVER=1` only when the machine should also run the
permanent Continuous official/rated server on UDP 27888.

systemd templates remain in `tools/systemd/`. For a manual allocator install,
replace all three placeholders:

```bash
sed -e 's|__USER__|youruser|' \
    -e 's|__DIR__|/home/youruser/projectprime-server|' \
    -e 's|__PUBLIC__|51.161.113.128|' \
    tools/systemd/mphread-master.service | sudo tee /etc/systemd/system/mphread-master.service
sudo systemctl enable --now mphread-master
```

Stop the service before replacing the binary — systemd holds the file open, and .NET maps it into
memory, so copying over a running one takes the process down in a way nothing explains.

`deploy-server.sh` does build, upload, units and restart against a remote box in one go.

## Your own server list

The list players' browsers ask is the same binary:

```bash
ProjectPrime -masterserver -port 27889
```

Add `-public HOST` if a game server shares the box (its heartbeats arrive over the loopback, and the
address published for it has to be the one the internet can reach), and `-hostports A-B` for the
port range it may run matches on for players who cannot open one.

Point servers at it with `-master HOST`, and players in **Settings → Servers**.

## Versions must match

A server refuses a client built against a different protocol, at the first packet, with a line in
its log. That is deliberate: the wire format does not move between versions, so an old client would
read every byte correctly and then play a different game. Update the server before handing out a
client built from a newer release.

## Hunter License career reporting

Career stats are written only by an **authoritative dedicated server**. The
client never submits its own kills, wins, damage, standings, or rating.

A provisioned server receives a reporter key out of band and stores it in
`career.env` beside the server binary:

```ini
PROJECT_PRIME_CAREER_SERVER_KEY=ppsrv_...
```

The systemd templates load that file with `EnvironmentFile=-...`. It must
remain private and should be mode 600. Do not put this value in a unit's
`ExecStart`, a shell history, a release archive, or the repository.

`deploy-server.sh` can install/update the file without exposing it in source:

```bash
PROJECT_PRIME_CAREER_SERVER_KEY='ppsrv_...' ./deploy-server.sh
```

When no reporter key is configured, gameplay is unchanged and the server logs
that career reporting is disabled. A provisioned server writes each completed
report to a durable local `career-outbox` before making the HTTPS request.
Transient Supabase/network failures therefore delay stats rather than losing
them; accepted files are deleted and permanently rejected reports are retained
with a `.rejected` suffix for diagnosis.

Each player sends only a short-lived, career-only ticket over the game socket.
The Supabase access/refresh tokens never travel through the UDP protocol. The
ingestion service verifies the server credential and every participant ticket
before it can touch the existing `prime` career/rating tables.

Continuous provisioned servers may apply the existing pairwise rating policy.
Player-created persistent lobbies are career-history only and cannot modify
rating points.

### Verify career reporting

After restarting a dedicated server, the log should contain both:

```text
[career] reporting enabled: https://hwcjaygoistufktorbmf.supabase.co/functions/v1/career-report
[career] reporter credential accepted
```

Check with:

```bash
systemctl cat mphread-server | grep EnvironmentFile
journalctl -u mphread-server -n 80 --no-pager | grep '\[career\]'
```

If the first command prints nothing, the installed systemd unit predates career reporting and is not loading `career.env`. Re-run `deploy-server.sh` or add `EnvironmentFile=-/home/<user>/mphread-server/career.env` under `[Service]`, then run `sudo systemctl daemon-reload && sudo systemctl restart mphread-server`.

## Anonymous networking telemetry

Dedicated servers can record anonymous network/combat/performance measurements for multiplayer quality studies. No player names, IP addresses, account IDs or chat are stored by this subsystem. Aggregate recording is enabled by default; uploads are disabled. Use `-notelemetry` to disable, or `PRIME_TELEMETRY_CONFIG` to select a configuration file. See [Protocol 19 combat and telemetry](docs/network/combat-telemetry.md) for retention, study modes and optional aggregate upload.

## Hosted server resource admission

The host pool keeps each authoritative match in its own child process. New
matches are admitted only if a port is free **and** recent sampled CPU load
leaves headroom. By default, admission stops when sampled parent + children
CPU usage reaches 85% of the `.NET Environment.ProcessorCount` budget.
Cold children are limited to at most `max(2, processor count)` simultaneous
unmeasured starts until the one-second resource sampler runs. An already
running match is **never** interrupted by resource-admission decisions.

Optional environment limits on the allocator's service:

| Environment variable | Meaning |
| --- | --- |
| `PROJECT_PRIME_HOST_MAX_MATCHES` | Max child matches (positive integer); otherwise bounded by ports/CPU |
| `PROJECT_PRIME_HOST_MAX_CPU_CORES` | CPU core-seconds per wall-second budget; overrides the default 85% limit |
| `PROJECT_PRIME_HOST_MAX_TOTAL_RSS_MB` | Combined parent/children resident MiB ceiling; zero/unset disables this optional limit |

The RSS admission gate reserves at least one estimated additional child's RSS
(maximum of 256 MiB and current child RSS), plus 128 MiB headroom.
RSS can count shared mappings more than once. It is a conservative admission
guard, **not** a memory availability probe or proof that a host has sufficient
RAM. Calibrate these settings on the target machine with a verified **active**
server-capacity run; do not infer them from idle children. Unrecognized and
nonpositive settings use default/disabled behavior. Rejection explains which
resource is constrained; retry after workloads complete.

### Hosted child retirement

The host pool unlists a retiring match immediately, then requests a graceful
child shutdown on an independent worker instead of blocking the allocator's UDP
loop. Linux/macOS sends `SIGTERM` through the system `kill` utility, which
the game already handles via `ShutdownSignals`. The child has up to 8 seconds
to finish replay/archive/network disposal before a process-tree kill fallback
(another 2 seconds). Windows attempts a normal window-close request before
the same fallback. A missing utility or unsupported console signal falls back
to forced termination. The master waits for its owned children with a 12-second
aggregate bound during master shutdown, unlike ordinary per-match retirement.


## Capacity benchmarking

Dedicated server publishes use ReadyToRun and prewarm combat/network hot
paths. The Linux real-process capacity harness now defaults to **active**:
each isolated continuous server is joined by two independent real UDP
`-netcheck -nographics` AI clients. A listening server with no clients does
**not** simulate a world, and its idle CPU/RSS must never be advertised as
active capacity.

```sh
python3 tools/server-capacity-benchmark.py \
  --binary publish/linux-x64-server/ProjectPrime \
  --workdir /path/to/configured/server \
  --client-binary /path/to/netcheck-capable/ProjectPrime \
  --client-workdir /path/to/configured/client \
  --mode active --clients-per-match 2 \
  --instances 1,2,4,8 --seconds 120 \
  --output capacity-results
```

The default client binary/workdir are the server binary/workdir when omitted,
provided that binary supports `-netcheck -nographics`. Use the standard
non-server client publish when necessary. Each active arm waits for actual
admissions, performs a warmup, measures server process CPU/RSS from `/proc`,
and requires a nonzero server simulation step report. A missing report or
early client exit fails the arm rather than silently claiming capacity. Raw
server/client logs and per-arm JSON are preserved.

For an explicit **idle** comparison, pass `--mode idle`. That mode does
not validate active simulation. The client processes share CPU and memory
bandwidth with the tested server processes: this is a *co-located load*
measurement, not a production capacity guarantee with remote clients.
Use separate load-generating hardware for deployment qualification and
check 60 Hz deadline violations, p99 steps, UDP drop/retry counters, GC
pauses, and RSS alongside throughput. Record the binary/runtime/source
hashes and retain all runs, including failures.


## Advanced Match Rules

Game type controls scoring and objectives. Advanced Match Rules can be combined
with Battle, Survival, Capture, Bounty, Defender, Nodes, Prime Hunter and their
available team variants:

- **Insta-Gib:** Imperialist-only loadout, infinite ammo, 99 displayed UA.
- **Low Tier:** Kanden, Spire, Noxus and Weavel only. Disallowed selections become
  Kanden; Random and bots use the allowed pool. The server enforces this.
- **No Imp:** Imperialist spawns become Volt Driver, Battlehammer, Judicator,
  Magmaul or Shock Coil. A stable room/spawn hash reproduces the layout across
  authority, clients and replay seeks. Drops and Trace affinity cannot grant
  Imperialist. No Imp and Insta-Gib cannot be combined.
- **Shadow Freeze** and **Spawn Protection (3s)** both default **Off**. Explicitly
  saved On values remain enabled.

Online settings come from the server before world construction. Offline rules
are captured in the launch plan shared by desktop and Android. Legacy Insta-Gib
mode data is interpreted as Battle plus the modifier. Protocol **28** introduces
these rules (25–27 were already used by map identity, names and online bots).
Older live clients are refused during the version handshake.

Dedicated-server flags: `-instagib`, `-lowtier`, `-noimp`, `-shadowfreeze`, and
`-spawnprotection`. The last two enable their otherwise disabled rules.

### Netcode performance and protocol 30

Protocol 30 adds lifecycle-fenced shot IDs and a repeated 16-entry fire-event
history. Upgrade server and clients together. Launch/ACK frames describe timing;
they no longer identify projectile shots. Existing owner-reported movement and
the server's authority over damage are unchanged.

The diagnostic counterfactual sampler defaults to 1/16 of shots. Use
`-shadowsampling off|production|study|full` to select 0, 1/16, 1/4 or all shots.
This changes diagnostic simulation only; all compensated shots retain cheap
rewind telemetry. Use full sampling for exhaustive studies.

Performance checks (run from the repository with a .NET 10 SDK):

```sh
dotnet run --project tools/nettest -- --netcode-performance
dotnet run --project tools/nettest -- --replication-decode-benchmark decode.json
dotnet run --project tools/nettest -- --network-benchmark-json network.json --extended
dotnet run --project tools/nettest -- --server-performance ASSET_DIRECTORY 'MP1 SANCTORUS' 8 server.json 30
dotnet run --project tools/nettest -- --transport-stress
```

The server benchmark requires locally extracted game assets. It warms 300 steps,
then measures a synthetic-input simulation without UDP. Run 2, 4 and 8 players.
The separate virtual network matrix covers RTT, jitter, loss and reorder; it does
not measure asset-backed combat under those impairments. Do not combine these
results into a claim about real deployed capacity. Compare on the same machine,
runtime and workload. The decoder benchmark compares canonical assembly plus
parse with direct decoding and reports allocations and canonical bytes copied.

`-netdebug` reports sampling, historical cache, geometry, rescue lookup and
replication counters. Server-step telemetry uses batches of 60 samples, with
exact counts, totals and maxima and a bounded quarter-millisecond histogram.
Partial batches flush at stop, match end, exception and telemetry shutdown.
Routine continuous-target repetitions are counted; transitions and individual
combat outcomes remain individually correlated.

See [implementation and measurements](docs/network/netcode-performance.md).
