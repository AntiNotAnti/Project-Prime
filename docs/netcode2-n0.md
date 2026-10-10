# Netcode 2.0 — N0 baseline and campaign harness

N0 follows the October 10 implementation plan against main
`468bbbf027148ce94901dfa8bf138d16a4498c03`. That commit descends from the restored
v0.1.47 baseline `0351ef6f707ab0369c95d8d4185869f6e02622f8`. No code from PRs
#410–#423 is incorporated. N0 adds evidence, test drivers and the bounded delivery fixes described in the
[follow-up validation](netcode2-n0-reliability-fix.md); protocol remains
**41**, with unchanged live packet and replay encodings, combat authority,
movement authority, renderer, and settings UI.

The first Mac/Windows campaign is **blocked**. See the
[validation and defect ledger](netcode2-n0-validation.md) for preserved failures,
assembly identities, measured results and unresolved acceptance gates.

## What is measured

`PRIME_NET2_EVIDENCE=/absolute/path/to/participant.json` enables a process-owned,
bounded diagnostic capture. It is unset in normal play. The capture records:

- Distinct actual fire events offered in local intent carriers and accepted in
  remote intent carriers, plus successful logical fire commits. Keys include
  authority epoch, match, shooter slot, generation, life and shot ID. Source and
  observation frames, weapon, charge, kind and continuous phase are metadata.
- Authoritative player health, turret health, kills, deaths and damage-event
  sequence every sixth authority frame. Comparison joins the complete player
  identity **and the same authority frame**, never wall time or predicted HP.
- Exact accepted/published semantic-event and award bytes, including their
  complete actor/target identities and the late-join baseline.
- Per-process time windows, alive-player frame denominators, actual offered and
  accepted intent-carrier counts, existing transport bytes/packets/queues and
  intent decisions, lifecycle counters, semantic frontiers, bot occupancy and
  spectator state.
- Per-map server step p50/p95/p99/p99.9, worst step, allocations, dropped steps,
  stalls and failures; process heap, total allocations and working set.

Counters describe their existing producer's reset scope; the windows preserve
observations before a map/session reset. Offered carriers mean calls to the
transport send path, not acknowledged packets. Accepted carriers mean intents
accepted by the simulation, not every UDP datagram received. CPU percentiles
come from the existing server histogram (0.01 ms bins, overflow reported using
the observed worst value). They include startup and transitions in each segment.

Each evidence stream and its dedup set is capped at 262,144 rows (1,048,576
when the soak flag is also set). An overflow is
reported and **fails** adjudication. No file writes occur in the simulation
hooks. The report is written once at shutdown; a crash or interrupted write is
missing/incomplete evidence. The opt-in test server can stop gracefully when
the runner creates `<evidence path>.stop`; this avoids platform-specific console
signals on Windows. This marker does not affect ordinary servers.

## Run on macOS or Windows

Build `tools/nettest` in Release with the repository's .NET SDK. Supply a
`paths.txt` containing absolute paths to an existing authorized asset extraction.
The runner hashes all referenced assets without copying them into its outputs.
It stages binaries and creates a separate user-data directory per process, uses
loopback UDP, disables master listing and automatic updates, and records commands,
flags, source identity, runtime, asset and assembly hashes. Use a **new output
directory for every attempt**; existing outputs are never overwritten.

```sh
dotnet build tools/nettest -c Release
python3 -m unittest discover -s tools/nettest -p test_net2_evidence.py
python3 tools/nettest/run-net2.py --build tools/nettest/bin/Release/net10.0 \
  --paths /absolute/path/to/paths.txt --source EXACT_SOURCE_SHA \
  --out artifacts/net2/lan-2-attempt1 --players 2 --seconds 90
```

On Windows use `python` and Windows paths. Repeat with `--players 4` and `8`.
Launch rendered runs from a terminal in the active Windows desktop session;
an SSH service session does not provide the primary display needed by OpenTK.
Headless independent-process campaigns can run through SSH.
Profiles are `lan`, `moderate` (100±20 ms, 1% loss), `poor` (250±40 ms, 2% loss),
and `extreme` (400±80 ms, 5% loss, 3% reorder, 1% duplicate), with recorded seeds.
These are simulated impairments on real UDP sockets and independent processes.

Three rendered views and replay recordings:

```sh
python3 tools/nettest/run-net2.py --build tools/nettest/bin/Release/net10.0 \
  --paths /absolute/path/to/paths.txt --source EXACT_SOURCE_SHA \
  --out artifacts/net2/three-view-attempt1 --players 3 --seconds 90 \
  --scenario missile --rendered --record
```

The existing rig assigns the first slot to shoot and the second to the target
role; the third process requests spectator mode for an independent observer view.
Rig runs report local evidence readiness, then require the offline cross-process
verdict. They do not run unrelated feature-tour jump/morph assertions. These are simulation shot
commits and captured rendered frames, **not a proven same-projectile visibility
metric**. `renderedProjectileAgreement` stays `NOT_RUN` until footage has been
independently paired and checked. A recording file alone is not a playback pass.

The sustained campaign requires 8 slots and 1,200–1,800 seconds:

```sh
python3 tools/nettest/run-net2.py --build tools/nettest/bin/Release/net10.0 \
  --paths /absolute/path/to/paths.txt --source EXACT_SOURCE_SHA \
  --out artifacts/net2/soak-attempt1 --players 8 --seconds 1200 --soak \
  --rooms "MP1 SANCTORUS,MP3 PROVING GROUND" --round-minutes 4
```

`PRIME_NET2_SOAK=1` is set only for this campaign. Within the existing scripted
input driver it cycles Shock Coil, Imperialist duels and Missiles on the server
clock. Seven initial participants allow an owner-command bot fill/removal, then
a late join at 30 seconds, a planned departure and fresh connection at 240
seconds, plus a spectator/rejoin interval. Coverage is observed, not assumed
from requested flags. Failed joins/commands, frozen gameplay and short-lived
workloads leave the corresponding gates incomplete. Exact spawn-trap and Shock
Coil kill/drain parity are separate unresolved gates even if these weapons fire.

## Strict result meanings

- **FAIL:** nonzero exit, timeout in any participant/log, unrequested reconnect,
  connection loss, semantic overrun/gap/conflict, invalid resurrection, queue
  drop, simulation failure, bounded-evidence overflow, duplicate logical shot,
  weapon/charge/kind mismatch, unexplained offered-shot loss, missing observer
  commits, or mismatched authority health/event samples.
- **INCOMPLETE:** missing/partial/schema-incompatible evidence, missing players,
  zero offered shots where firing was expected, no alive denominator, no common
  authority interval, or required soak coverage absent. An exit code of zero
  and a `RESULT: PASS` log line cannot override this.
- **PASS:** the measured campaign checks pass with their denominators present.
  This does not convert separate `NOT_RUN` coverage gates into passes.

Semantic completeness excludes the final 60 authority frames of each observed
match, and cross-view commit checks exclude 30 frames at each boundary. Those
bounds are exported. Owner-offered versus authority-accepted/committed accounting
does **not** silently forgive a final tail: until N3 supplies explicit expiry or
rejection proof, unexplained tails fail and are retained for investigation.
Normal rejection of stale packets is reported separately from accepting an
invalid life; rejections are not automatically treated as gameplay corruption.

## Source-to-target mapping and rollback

| Existing source | N0 observation/test addition |
| --- | --- |
| `NetSession.SendIntent` / `AcceptSlotIntent` | Offered/accepted canonical fire-event identities |
| `NetFireEvents.Commit` / `Prepare` | Successful logical commit and explicit continuous-phase skip in each live simulation |
| `ReplayCapture.AcceptedState` | Raw authoritative health/event sample with authority frame |
| `DedicatedServerSemantic` / `NetSessionSemantic` | Published/accepted exact semantic facts |
| `ReplayCapture.AfterSimulation` | Bounded time windows and persistent error observations |
| `ServerSim.Stop` / dedicated shutdown | Per-map step distributions and final process evidence |
| Existing `HitRig`, authority bot lifecycle, spectator controls | Explicitly opt-in sustained workload |
| `NetDatagram` / `NetTransport` | Bounded server-fact admission and pending close ACK retention |
| `tools/nettest/run-net2.py` / `net2_evidence.py` | Isolated independent processes and offline adjudication |

Leaving both `PRIME_NET2_*` environment variables unset disables the evidence
and test drivers. The production delivery repair is enabled independently;
revert `4e38efe2` to roll back that behavior, or revert the N0 slice as a whole. There is no wire migration or saved-profile migration. Preserve
all original attempts before any subsequent fixes. N1 and later dependent slices
remain blocked until the baseline defects and unresolved acceptance gates in the
evidence ledger are reviewed.
