# Networking and server implementation slices

Implementation branch: `codex/network-server-slices`, based on `18ac50d5`.

This implements the required changes from the October 6 networking/server audit.
Live protocol is **42**: clients and servers must update together. Historical
replay conversion remains an offline boundary, never a live protocol downgrade.

## Movement behavior

Movement remains owner driven. The server continues to own lifecycle, damage,
resources and match rules. The interpolation estimator measures differential
transit time, so regular 30 Hz coalescing no longer looks like packet loss and
pushes presentation to the eight-frame ceiling. Genuine starvation still raises
bounded buffering. After 30 frames without fresh input, held digital/analog
controls are neutralized and old owner positions stop overriding native motion.

The clean-stream timing check holds at 1.25 frames (about 20.8 ms at the 60 Hz
simulation clock) across 30, 60, 120, 144 and 240 FPS. This removes the artificial
buffer increase; it is not an end-to-end rendered latency measurement.

Freeze position/velocity are corrected once per generation/life/freeze event.
Repeated snapshots do not pin environmental falling. Respawn presentation uses
the authority's eligibility frame and elapsed snapshot age. Hunter choices are
queued and committed before the next life, without reinitializing a living actor.
Pending hunter/color choices stay queued until both the matching life and roster
arrive, so a delayed roster cannot send the previous choice back to the server.

## Implemented boundaries

| Slice | Result |
| --- | --- |
| 1 Transport correctness | Lobby close retires all connections; terminal reliable messages retain a bounded retry tombstone; failed client startup cleans up; the oversized pre-Welcome canonical snapshot is removed; post-admission load timeout carries start and recipient generation fences. Live endpoint identity is retained until normal leave/timeout. |
| 2 Readiness and shutdown | Required extracted assets and all rotation entries are checked before readiness/registration. Headless scene services avoid audio initialization. Owned children receive private cooperative stop requests, clients receive Bye, and children withdraw their own directory entries before bounded force fallback. The directory alone accepts its existing reporter-endpoint farewell format. A second termination signal takes the force path. |
| 3 Combat | Immediate and recovered native emission use immutable authored origin/direction/aim/view/scope/charge/phase/target. Muzzle geometry is coupled to the original firing body or native turret. Fractional world time survives claims and ordering. New evidence uses one rewind cap; admitted immutable witnesses survive grace without rereading expired history. Full 16-entry reply batches flush before another terminal verdict is queued, preserving burst feedback within bounded storage. |
| 4 Movement | Transit estimator, stale control/position policy, one-event freeze correction, next-life hunter choice and authoritative respawn presentation. |
| 5 Input efficiency | Current live intents are exactly `103 + 84 * eventCount` bytes, for 0–16 retained events. All client/server/bot send and receive paths use the compact codec. Maximum relayed input is 1,448 payload bytes / 1,472 UDP bytes. Canonical checkpoint records retain a fixed padded encoding. |
| 6 Lobby and career | Spectator role is explicit in roster and replay state. Observers do not consume combat teams, readiness or minimum-player eligibility; the total eight-connection limit remains. Quick Play checks phase/JIP. Career reports freeze identity per participation segment, exclude observer-only admissions, bound cumulative segments at 128 and verify/merge only signed non-overlapping account segments in the backend. A 129th segment downgrades the retained report to Practice. Legacy version-1 normalization preserves previously accepted payload hashes for retry idempotency. |
| 7 Join in progress | Version 5 authority-world facts include generation/life-fenced ammo, weapon/charge inventory, slots and powerup timers, plus all spawn availability, doors and stable dynamic drops. Bootstrap applies these before WorldReady. Prediction subtracts unacknowledged native shot costs, holds ambiguous overflow balances, and resolves local pickup prediction when the next carrier is acknowledged. Ownership follows the admitted local slot in headless/free-camera modes as well as the normal camera. Duplicate world application cannot refresh powerup/drop timers. |
| 8 Maps | Preparation stages archives/runtime outputs off the owner thread. Publication uses immutable file identity checks and owner-thread renames with the manifest last, rolling back failure/cancellation. It does not promise atomic recovery from a process crash; blocked rollback retains recovery files. Shared reservations, active pins and eviction bound hosted archive/download/fallback-copy payloads to 2 GiB, excluding compiled runtime/metadata disk usage. Stock gameplay digest covers collision/entities/nodes/limits while excluding presentation variants. |
| 9 Resilience | Stale fixture assumptions are repaired. Normal production UDP loopback includes full quadratic relay and 1/2/4/8 peers. The native runner supports a seeded RTT/loss/jitter/reorder/duplicate grid and an owned-process suspension gap. Desktop focus and Android render-owner pause/resume cancel held charge, clear controls/input/predicted hits and silently retire stale beams/bombs. The original socket and connection sequences survive; cached arrivals/pickup predictions are discarded and a fresh generation-fenced bootstrap must reach WorldReady. |
| 10 Architecture | Token-scoped load generations give healthy child loading a bounded lease. Duplicate markers cannot extend it indefinitely; a hard deadline remains. Normal responsiveness/idle policies return after loading. The existing dedicated/hosted hybrid is retained. |
| 11 Diagnostics | Optional actual UDP send/receive counters by kind, including retries and ACKs; bounded whole-loop phase histograms and replay state/attempt/queue/error diagnostics. Disabled instrumentation stays off the hot path. |
| 12 Boundary work | Exact match/room replay identity is computed once and reused; initial same-frame immutable checkpoint work is shared. Optional recorder failure latches once per match rather than rebuilding replay machinery every tick. Existing optional world-capture demand gates remain. Alternating capture buffers preserve the previous frame; replay markers use only active entity prefixes. |

Combat source-body reports retain the existing owner-movement trust model. The
new muzzle coupling does not turn player position into independent server
simulation. Old protocol-41 replay poses remain readable; authoritative
recomputation of old attacks lacks the new admitted source-body evidence and may
refuse them. Missing evidence is not invented during conversion.

Muzzle validation uses the native maximum landing-camera offset, since the
authority's current bob coefficient can differ from the original firing frame.
The regression generates the muzzle through native camera/emission methods;
it does not substitute a projectile origin or claim a complete falling-physics
network test.

Full-capacity bootstrap staging is derived from the current lane layouts:
1,216 / 192 / 827 bytes for fast / slow / world baselines. The fast reliable
baseline occupies 1,244 UDP bytes including its envelope and event ID. Objective
fragments reserve that four-byte event ID too, so a full fragment fits the
unchanged 1,472-byte UDP ceiling. Both boundaries are checked over the actual
reliable transport; simultaneous eight-peer admission no longer overruns the
old staging buffer and stops the server owner thread.

## Deployment and conditional work

The backend change accepts legacy report version 1 and new segment version 2.
Apply `supabase/migrations/20261006163506_career_cumulative_admissions.sql` and
update the career-report Edge Function, including `_shared/career-segments.ts`,
before deploying servers that submit version 2. This implementation does not
deploy to production.

A reusable warm process pool, relevance/PVS, damage delta masks and additional
optional-fact capability negotiation remain separate measured experiments. The
audit conditioned those changes on evidence; no broad transport or authority
rewrite is introduced here. Required global objective/resource facts continue
at 10 Hz even when optional disk replay is disabled.

## Validation

Preserved summaries live in
[validation/network-server-slices-2026-10-06](validation/network-server-slices-2026-10-06/).
Raw execution logs remain in the local ignored audit evidence directory.

| Check | Recorded result |
| --- | --- |
| Final server / desktop builds | .NET SDK 10.0.401 / runtime 10.0.12: zero errors; 39 / 104 warnings respectively. |
| Functional matrix | 56 distinct modes pass across the full matrix and affected followups. The full 54-mode run initially passed 53; the replay fixture was repaired, then affected modes were rerun. Each result retains its exact stage and runtime hash. |
| Movement / resources | 43 movement timing/lifecycle checks and 37 native resource/world checks pass. |
| Native combat evidence | 156 authored-fire-context checks, 57 authority-combat checks, 162 combat-scene checks and 3,338,739 health/shot assertions pass. These are separate from the native fault-grid hit observations. |
| Bootstrap and transport | Final runtime: 136 transport assertions, 48 network lifecycle assertions, 38 maximum-layout checks and 1,295 eight-peer bootstrap checks pass. Every bootstrap peer receives 120 continuing full-eight-player snapshots after WorldReady. A valid 96-door world crosses the full reliable fragment boundary and reconstructs exactly after reverse and duplicate delivery. |
| Native fault profiles | Earlier full 35-profile grid passes, covering RTT 0–500 ms and loss 0–10%, with jitter, reorder and duplication. Final runtime subset passes 3/3 (0/0, 100/2, 500/10), including a 4.026-second owned-client suspension. Full grid and subsequent subsets have distinct source/runtime provenance. |
| Replay compatibility | Legacy version-2/3 and current durable clips, nested extraction, seek, frame zero, EOF, match reset and 251 frozen frame comparisons pass; 1,170 accepted-fact frames are compared. |
| Map / career helpers | 29 helper assertions and 442 map editor assertions pass. A prepared 16 MiB publication sample takes 0.222 ms and allocates 1,872 bytes on the owner thread; this is a single local sample. |
| Backend | 18 handler tests, frozen Deno checks, all nine migrations and both SQL acceptance gates pass in disposable PostgreSQL. |
| Real process lifecycle | Valid extracted assets reach Ready/Welcome, send Bye, exit zero, withdraw the directory entry and release UDP. Five invalid asset/rotation cases exit one without Ready or registration and release UDP. This predates the final capacity fixes; unchanged lifecycle source hashes are retained. |

The full native grid observes 557 trigger attempts and 10 authority-confirmed
non-self hits across eight profiles. The final three-profile subset observes 44
attempts and one confirmed hit. These runs demonstrate native scenario and
connection recovery; they do not establish a hit-accuracy rate for every profile.

### Whole-server loads

All nine scenarios pass on the final runtime, comprising eleven measurement
cycles. The eight rows below have a two-second warmup followed by approximately
12 seconds of normal admission, native simulation and production UDP traffic.
Inputs are synthetic idle controls with the full peer relay fanout.

| Peers / mode | Receive B/s | Send B/s | Step p99 / p99.9 ms | Allocated B/step | Process CPU ms | Process RSS MiB, start → end |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 / LAN | 7,660 | 24,245 | 0.34 / 0.47 | 1,533 | 1,735 | 183.20 → 185.77 |
| 2 / LAN | 15,306 | 81,420 | 0.35 / 0.46 | 1,564 | 1,801 | 183.48 → 186.09 |
| 4 / LAN | 30,613 | 294,879 | 0.45 / 0.51 | 1,598 | 1,953 | 184.14 → 186.91 |
| 8 / LAN | 61,326 | 1,117,507 | 0.52 / 0.63 | 1,590 | 2,432 | 185.73 → 188.33 |
| 8 / moderate | 59,258 | 1,088,325 | 0.61 / 0.95 | 1,638 | 3,036 | 188.02 → 196.28 |
| 8 / severe | 56,205 | 1,067,083 | 0.65 / 1.08 | 1,698 | 2,906 | 189.78 → 197.91 |
| 4 / replay | 30,673 | 294,866 | 0.60 / 12.07 | 18,312 | 2,535 | 197.25 → 203.44 |
| 4 / replay storage fault | 30,620 | 294,666 | 0.41 / 1.09 | 1,602 | 1,953 | 192.38 → 195.19 |

Moderate applies 250 ms configured RTT, 0–50 ms additional jitter per direction,
5% loss, 3% reorder and 1% duplication; severe uses 550 ms RTT and 10% loss with
the same jitter/reorder/duplication. Only client transports are impaired. Lower
throughput in those arms reflects fewer accepted inputs. Three further two-peer
LAN cycles each measure two seconds and successfully rebind the server UDP port
after disposal. That verifies socket release, without claiming a heap soak.

Every measured cycle records zero simulation failures, warm dropped steps or
stalls, server queue drops, socket errors, invalid packets or oversized packets.
Cold lifetime samples separately contain up to six dropped steps and simulation
maxima reaching 162.8 ms; startup hitches have not been eliminated. Whole-loop diagnostics sample
every loop body, including idle spin, so their quantiles are separate from tick
cost. Their measured warm over-budget count is zero.

Bytes include connection envelopes, ACKs, retries and other lanes, and exclude
IP/UDP/link headers. Step allocation covers the synchronous `ServerSim.Step`
boundary, including its snapshot/replay work, and excludes ingress, maintenance
and the writer thread. CPU/RSS include the in-process synthetic peers, workers,
impairment queues and enabled diagnostics. They are not isolated dedicated-process
measurements. Histograms use 0.01 ms buckets with about 720 warm samples; p99.9 is
effectively the observed maximum here, rather than a reliable tail estimate.

The storage-fault arm reaches `Failed` with exactly one attempt and one failure,
zero queued bytes, 2,880 accepted intents and continuing authority snapshots.
Its warmed allocation is 1,602 B/step against 1,598 with recording disabled.
Healthy recording still adds substantial work: 18,312 B/step and a 12.07 ms warm
maximum. Its finalized 886,726-byte archive also passes decoding, playback,
durable/nested clip extraction, seek and match-reset checks, comparing 859
accepted-fact frames and 251 frozen frames.

The matching historical four-peer LAN sample receives 287,744 B/s and sends
1,048,078 B/s; the final sample is lower by 89.36% and 71.86% respectively.
These are idle-workload samples from distinct pinned runtimes. Added resource
facts and diagnostics increase the new recording-off allocation baseline by
about 34%; this implementation does not claim a general allocation reduction.
Exact per-kind byte counts, cold values, cycles, archive result and comparison
provenance are in
[loopback-loads.json](validation/network-server-slices-2026-10-06/loopback-loads.json).

### Reproduce the checks

Use .NET 10 and build the server and graphical variants separately:

```sh
dotnet build tools/nettest/nettest.csproj -c Release -p:MphReadServer=true \
  --artifacts-path /tmp/prime-network-server -p:UseSharedCompilation=false -m:1
dotnet build src/MphRead/MphRead.csproj -c Release \
  --artifacts-path /tmp/prime-network-client -p:UseSharedCompilation=false -m:1
```

The nettest binary exposes `--transport-lifecycle`, `--network-lifecycle`,
`--server-engineering` and `--replay-protocol42` without licensed assets.
`--accepted-fire-context`, `--movement-network`, `--resources-network` and
`--eight-peer-bootstrap` take a
directory containing `paths.txt` for a valid extracted installation. Set
`PROJECT_PRIME_USER_DATA` to an isolated test directory. The actual normal
server/UDP load command is:

```sh
dotnet /tmp/prime-network-server/bin/nettest/release/nettest.dll \
  --loopback-load /absolute/isolated/test-data 8 /tmp/prime-load.json 12 lan 1
```

Its modes also include `moderate`, `severe`, `replay` and `replay-failure`.
The ordinary native grid runner and owned-process pause commands are documented
in `tools/hitrig/NETWORKING-SLICES.md`. The map/cache/career helper commands are in
`tools/maps-career-check/README.md`. Backend checks are:

Use Node 24 or later and Deno 2.9.7 for the handler and frozen dependency checks.

```sh
node --test tools/edge-check/*.test.ts
bash tools/edge-check/check-deno.sh
node tools/edge-check/check-career-sql.mjs
```

### Scope and remaining platform checks

Native headless checks exercise gameplay and UDP, with fixed seeds and client
impairments. They do not certify rendered input-to-remote latency, a physical
Android pause/resume, Windows/Linux execution, network-interface/NAT migration,
public deployment or every weapon's accuracy under every fault profile. Resource
bootstrap coverage does not establish visibility of every projectile already in
flight at join. Map publication/file-identity worker checks ran on macOS; the
capacity fixture scales the production accounting algorithm to small byte limits.

SQL executes all nine migrations and both unchanged acceptance gates on
PostgreSQL 18.3 / pinned PGlite 0.5.8, with a compatible disposable schema.
Handler tests use real HMAC verification with disposable identities. Original
EF deployment DDL, other PostgreSQL versions, concurrent transactions and the
Supabase relay/JWT gate remain deployment checks. Four missing statement
terminators in historical migrations were repaired for fresh migration replay;
the cumulative migration is the new production deployment change.
