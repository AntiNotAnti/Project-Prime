# Network/server hot-path characterization (Phase 5 gate)

Production engine source is main `91b1c0c6c44e8c90effd250729709f791a84765e`, protocol 53, .NET SDK 10.0.401/runtime 10.0.12, Apple M4 Pro/macOS arm64. This PR adds a headless profiling helper and delivery gates; **no production optimization is supported or implemented by this evidence**. Source/assembly/artifact hashes are in `network-profile/manifest.json`. All measurements below characterize existing main behavior, including existing direct decoding and queue coalescing.

## Ownership/implementation evidence matrix

| Path | Current source evidence | Decision |
|---|---|---|
| Encode/decode | Production CompactIntentCodec, NetReplicationLanes and NetReplicationReceiver already use spans and direct live snapshot decode; existing byte/field-equivalence checks cover the canonical path | Keep existing direct path; report its costs without claiming new gains |
| Dispatch buffer | NetTransport.Dispatch consumes span synchronously in clean SendNow; injected lag owns an exact ToArray copy crossing stack lifetime | Removing delayed copies would be incorrect |
| Receive buffer | Pooled receive buffer is released by Drain try/finally only after caller processes yielded packet; held-arrival promotion copies into owned pool buffer | Preserve lifetime and early-disposal release contract |
| Pump/queues | NetPacketQueue reserves128 critical slots; bounded live queue2048 and cosmetic64; default work budget128/256/32 plus cosmetic8; clean real-time snapshot coalescing is explicitly disabled with lag worker | Preserve bounded admission, priority/order and latest-state ownership |
| Connection locks | Existing per-connection locks, state/held locks, lock wait/hold telemetry | Actual 2/4/8 socket runs show short waits; no contention rewrite justified |
| Empty Drain | Compiler iterator allocates240B per empty call after256 warm calls;10,000call calibration .4595us/call | Observable allocation cost (~129,600B/s for9pumps at60Hz), but no demonstrated deadline bottleneck. A manual enumerator must preserve packet-release finally, playback, category budgets, coalesced snapshots, cosmetics and early disposal; complexity is not supported by this profile |
| Dedicated world | Existing source-owned60Hz simulation and validated owner movement/server combat authority | Step-only synthetic workload profiles cannot justify movement/combat/kernel changes |

## Codec, copies and impairment queues

`--replication-decode-benchmark` warms10,000 iterations then measures200,000 per arm. Existing direct live decode vs existing canonical assembly/parse (microseconds/frame):2players .6394 vs1.4049;4players .9925 vs1.8018;8players1.3818 vs2.4513. Both paths allocate0B/frame. Canonical copy sizes436/782/1474B; existing direct path0 canonical copied bytes. This is characterization of an already-implemented optimization, not a PR before/after gain. Single-arm timing order and a shared busy workstation limit performance inference.

Existing virtual-time `--network-benchmark-json` exercises16 codec/queue cases covering2/4/8 players, RTT0–400ms, jitter0–80ms, loss0–5%, reorder0–3%, duplicate0–1%. Every case completes600 scheduler ticks, drops0 scheduler ticks and0 transport packets; queue high-water8–302. This combined encode/decode/queue harness excludes sockets, world simulation, smoothing, rewind and physical scheduling. The existing actual eight-peer `--transport-stress` passes1728 intents and128 exact-once controls with mixed quality and400ms client pump stall;25/25,138 lock acquisitions contended, total wait.614ms, max wait.059ms, total hold15.187ms, max hold.398ms. Full preserved output is adjacent.

## Actual loopback production pump matrix

New `--transport-profile-json <path>` creates independent production UDP transports, admits2/4/8 peers, sends120 frames at nominal60Hz for2seconds, drains3seconds, and stalls the last client's pump400ms. Runs clean and configured client fault injection (added RTT400ms (200ms fixed each way), plus jitter up to40ms each way,5%loss,3%reorder,1%duplicate, seed431). Control IDs are transport-only synthetic SessionState bodies, deliberately not proof of application authority/session semantics. Production intent and snapshot codecs carry generated fixture fields; no assets or world simulation.

All six cases pass: each peer consumes16 reliable controls exactly once, snapshots continue, server intents arrive, reliability pending returns0, queue drops0 and high-water stays below2048. Main-thread allocation excludes admission, preallocated fixture buffers/control sets/timing storage and result serialization; socket worker allocations are excluded. Connections and30empty pumps warm before measurement, payload first-use work remains in the measured window. Whole-run telemetry includes admission/warmup; contention resets before measurement. This distinction is explicit rather than presenting these as complete process-allocation numbers.

| Peers | Faults | Measured main allocation (5s) | Pump p99 ms (all transports per tick) | Server queue high-water |
|---:|---|---:|---:|---:|
|2|clean|213312|.2084|4|
|2|impaired|328608|.0804|6|
|4|clean|360384|.0740|7|
|4|impaired|590976|.0780|11|
|8|clean|654528|.3684|15|
|8|impaired|1115712|.1487|22|

All nominal tick schedules report0 late ticks (>16.67ms behind). Highest pump duration2.1205ms in these six runs. Another preserved trial shows differing tails and lock counts; main allocations are identical. Scheduling/network ordering are nondeterministic, so this is not proof of physical WAN deadlines or confidence intervals. Clean coalescing observed; impairment intentionally retains ordinary queue behavior.

## Dedicated world steps, separate scope

Nine existing asset-backed synthetic server runs:2/4/8 players ×3 trials, MP3 PROVING GROUND, seed431,300warmup+900measured steps per trial. All trials preserved, including outliers; stepMilliseconds retain chronological order. Work proves90/180/320 shots for2/4/8 players; damage events0/1/84 and damage points0/6/606. Timed pickup claim workload is0, so this does not validate #472–474 claims. Allocation is current-thread sim.Step only (Feed/admission excluded); GC scope is process-wide including Feed. No transport or native render present.

| Players | Mean step range ms | p99 step range ms | B/step | >16.67ms step counts across trials |
|---:|---:|---:|---:|---|
|2|.1056–.2548|.3766–3.0401|1463.71|0,0,1|
|4|.4391–.5440|4.2529–6.9232|1640.52|2,3,1|
|8|.3127–.8276|1.4980–13.1002|2568.20|2,5,0|

Shared workstation build/test activity and process GC make tail attribution uncertain. These overruns are retained, not explained away or converted into a performance acceptance claim. No native FPS, Android frame pacing, physical client/server impairment, or Windows/Linux hardware acceptance: NOT_RUN. No justification to change60Hz, owner movement, combat authority, packet layout, critical delivery or server child isolation.

## Upstream dependency ledger (read-only, current heads)

#472 `8563b672a4f4cfa0c7d34c071113f1d81e5bf011`, base `codex/net21-turret-outcomes`; #473 `a4f4371a441f700b6b9c8d8a37b54e95ea92acea`, base `codex/net21-authority-grant-ledger`; #474 `31cdd16d80624e1a5c1fbdafafbed48336b5a86f`, base `codex/net21-pickup-claims`. Queried2026-10-13: each OPEN, MERGEABLE/CLEAN against its stated dependent base, all26 current-head checks SUCCESS, zero failures. Full job names/URLs/head SHAs retained in `upstream-pr-status.json`. This is not a mergeability claim against main or against the overnight integrated branch. They are unchanged and not retargeted/duplicated by this main-based profiling slice.

#497/#499/#501 are separate Phase4 ownership/integration slices; this baseline profile does not incorporate them. Phase8 warning cleanup owns CA2014/IL3000 changes in NetTransport,NetMaster,NetCampaignEvidence,NetBombVisibilityProbe; this PR avoids those runtime files.

## Reproduction and verification

Use Release builds. Run `dotnet run --project tools/nettest -c Release -- --transport-profile-json /tmp/transport.json`, `--network-benchmark-json`, `--replication-decode-benchmark`, `--transport-stress`, and `--server-performance` with the preserved JSON scenario inputs. The new content-free socket delivery/queue gate is in the existing network CI job; no timing/allocation threshold is enforced on hosted runner hardware. Build passes with existing warnings. Existing `--architecture`, `--protocol18` and `--netcode-performance` gates pass: owner position/full snapshot/byte fixture; doors/force-fields/moving collision/fractional rewind/exception restoration and0rewind allocations; deterministic sampling/ledger-reference/direct-decode equivalence/telemetry; revision fencing,0decode allocations and1172B fast-datagram budget. Report is a profiling gate outcome, not full Phase5 physical/platform acceptance.

Hosted CI is pending when this report is authored; root monitors current-head results. No merges performed.
