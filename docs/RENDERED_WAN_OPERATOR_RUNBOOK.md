# Rendered WAN operator runbook

This developer harness collects a candidate evidence package from two physical
Internet endpoints. It uses the production Node control session, lobby handoff,
external Worker process, direct Worker UDP transport, renderer, and same-session
`match.rejoin` path. It never declares rendered-WAN, QZ1, or QZ5 acceptance. A
human must inspect every capture and separately establish that traffic crossed
the intended Internet path.

The split harness accepts ordinary multiplayer rooms only. Compiled developer
fixtures, public diagnostics, UDP relays/tunnels, and protocol changes are
deliberately unsupported.

The separate same-host rendered harness has a deterministic fidelity probe:

```bash
dotnet run --project tools/nettest/nettest.csproj -c Release -- \
  --rendered-wan-validation /absolute/path/to/AMHE1 /new/evidence/path \
  --scenario headshot --fixture unit1-rm1-dynamic \
  --mode players --rtt 100 --jitter 15 --loss 1 --seconds 12
```

It validates scenario choreography, Imperialist root-shot correlation, report
identity, and failure classification. It remains a loopback developer fixture,
is always reported with `renderedWanProof=false`, and is not a substitute for
the split real-WAN matrix below.

## Evidence classes and acceptance boundaries

Every artifact and conclusion must carry one of these labels. The split
operator and client reports are candidate evidence; their flags remain false
until the independent path and human gates are complete.

| Label | Establishes | Does not establish |
| --- | --- | --- |
| `SOURCE` | Current call paths, ownership, constants, and documented exclusions | Runtime, rendering, device, or Internet behavior |
| `UNIT` | Focused assertions over one code seam | A native window, physical input, or WAN path |
| `HEADLESS` | Deterministic simulation/harness output without a native display | GPU output, device lifecycle, human feel, or geographic path |
| `RENDERED` | A native host produced a frame/capture on the named host | Physical Android, high-refresh, or real-WAN behavior |
| `DEVICE` | The exact APK, Android hardware, display, peripherals, and lifecycle run | Another device, deployment, or WAN geography |
| `WAN` | Two independent endpoints, verified public path, Node admission, direct Worker UDP, and recorded transport/authority metrics | Visual quality without human inspection |
| `HUMAN` | Reviewer's conclusions for the exact captures and run | Unrecorded protocol, authority, or transport facts |

`--rendered-wan-validation` and `--rendered-wan-snapshot-matrix-plan` are
headless/process-local or same-host rendered evidence, even when their
impairment values resemble a WAN profile. They cannot fill a `WAN` cell.
Likewise, a real-WAN report is not a `HUMAN` visual pass until every capture is
reviewed. Keep the labels separate in the run manifest.

## Server/operator prerequisites

- Reserve one fixed TCP port for the Node's TLS listener and one fixed UDP port
  for the Worker. Port `0` is rejected.
- Configure the firewall/NAT to forward those exact ports to the server. The
  Node URI and Worker IPv4 supplied on the command line must be the externally
  reachable advertised endpoints.
- Supply a currently valid TLS PFX and a separate password file. A certificate
  chaining to the client machine's normal trust roots is preferred. On Unix,
  both files must be user-only (`chmod 600`).
- The Worker advertised endpoint must be a canonical IPv4 literal. Hostnames,
  wildcard addresses, and a loopback advertisement for a non-loopback bind are
  rejected.
- Use an output path that does not exist. The harness exclusively reserves it
  and refuses even an existing empty directory so stale or delayed artifacts
  cannot be attributed to a new run. The operator starts exactly one ephemeral
  Node and one manager-owned external Worker with one lane and one-match capacity.

Example (replace every value with the real lab endpoints and files):

```bash
dotnet run --project tools/nettest/nettest.csproj -c Release -- \
  --rendered-wan-operator /srv/prime/AMHE1 /srv/prime/evidence/server \
  --node-bind 0.0.0.0 --node-port 27443 \
  --node-uri wss://wan-lab.example:27443/v1/control \
  --tls-pfx /srv/prime/tls/node.pfx \
  --tls-password-file /srv/prime/tls/node-password \
  --worker-bind 0.0.0.0 --worker-host 203.0.113.10 --worker-port 27020 \
  --room "MP1 SANCTORUS" --mode players --seconds 15 \
  --reconnect-at 7 --wait-seconds 180
```

The operator writes:

- `descriptor.json`: public, secret-free run, endpoint, content, build,
  protocol, and exact certificate/SPKI hash bindings.
- `secret.json`: a single short-lived, one-use admission for one fixed subject.
  On Unix the harness writes it with mode `0600`.
- `server-report.json`: secret-free Node/Worker/match/wire/subject/seat/time and
  host-side debug-command evidence.

The console never prints the admission ticket. Do not paste `secret.json` into
chat, issue trackers, CI logs, or shell tracing.

## Transfer and remote client

Transfer `descriptor.json` (including its exact certificate/SPKI pins) over an
independently authenticated channel, and transfer `secret.json` over a separate
private encrypted channel. Preserve `secret.json` as user-only on the client:

```bash
chmod 600 /secure/inbox/secret.json
```

The admission expires no more than 120 seconds after issuance and is consumed
once. Start the client promptly on a second physical endpoint:

```bash
dotnet run --project tools/nettest/nettest.csproj -c Release -- \
  --rendered-wan-client /opt/prime/AMHE1 \
  /secure/inbox/descriptor.json /secure/inbox/secret.json \
  /opt/prime/evidence/client --width 640 --height 360
```

Normal platform certificate validation is the default. A private lab CA should
normally be installed in the client's trust store. Only when that is impossible,
the lab may opt into the descriptor's exact certificate **and** SPKI hashes:

```bash
# Exact out-of-band pin only; this is not a generic certificate bypass.
... --allow-lab-pin true
```

The client revalidates the descriptor, admission identity/expiry, local content,
build, protocol, ordinary-room constraint, and exact Worker endpoint before it
joins. It then uses the same live Node control session for `match.rejoin` and
connects directly to the Worker over UDP.

## Real geographic WAN matrix

Run every cell independently with a fresh output directory, run ID, and
short-lived admission. `US`, `Europe`, and `Japan/APAC` identify the verified
public endpoint geography recorded by the operator; an endpoint label or IP
spelling by itself is not geography or path proof. Record redacted metro/ASN,
UTC start/end, Node/Worker region, and the independent route/path evidence in
the private run record.

The matrix cell notation is `effective RTT target / jitter target / loss target /
client-to-server:server-to-client nominal contribution`. Jitter is the target
p95 absolute deviation, and loss is the requested total impairment envelope;
the real run must record measured loss separately in each direction. The
asymmetry values are a repeatable profile for planning, not an acceptance
substitute for measured one-way data.

| Geographic path | `50` cell | `100` cell | `150` cell | `200` cell | `250` cell | `300+` cell |
| --- | --- | --- | --- | --- | --- | --- |
| US | `50 ms / ±2 ms / 0% / 50:50` | `100 ms / ±5 ms / 1% / 45:55` | `150 ms / ±8 ms / 2% / 40:60` | `200 ms / ±12 ms / 3% / 40:60` | `250 ms / ±20 ms / 3% / 35:65` | `≥300 ms / ±30 ms / 5% / 35:65` |
| Europe | `50 ms / ±2 ms / 0% / 50:50` | `100 ms / ±5 ms / 1% / 45:55` | `150 ms / ±8 ms / 2% / 40:60` | `200 ms / ±12 ms / 3% / 40:60` | `250 ms / ±20 ms / 3% / 35:65` | `≥300 ms / ±30 ms / 5% / 35:65` |
| Japan/APAC | `50 ms / ±2 ms / 0% / 50:50` | `100 ms / ±5 ms / 1% / 45:55` | `150 ms / ±8 ms / 2% / 40:60` | `200 ms / ±12 ms / 3% / 40:60` | `250 ms / ±20 ms / 3% / 35:65` | `≥300 ms / ±30 ms / 5% / 35:65` |

For each cell, retain the target and the measured values; do not replace an
actual asymmetric path with the nominal profile. At minimum record:

| Record | Required fields |
| --- | --- |
| Path identity | `region`, redacted endpoint/provider/metro, `real-wan-independent-path`, direct Node/Worker endpoint, route evidence, and whether VPN/relay/tunnel was absent |
| Timing | `rtt_p50_ms`, `rtt_p95_ms`, `jitter_client_to_server_p95_ms`, `jitter_server_to_client_p95_ms`, and the configured/observed target cell |
| Delivery | `loss_client_to_server_pct`, `loss_server_to_client_pct`, packets sent/received/dropped per endpoint, and any reordering/duplication observation |
| Asymmetry | Measured one-way client-to-server and server-to-client p50/p95 contributions and the resulting ratio; do not infer it from RTT alone |
| Lifecycle | Node/control session, Worker handoff, match identity, `match.rejoin`, same seat, reconnect timing, and terminal state |
| Review | Per-capture file/hash, reviewer, date, defects, and `HUMAN` disposition |

The `--rtt`, `--jitter`, and `--loss` options on the same-host validation
command describe process-local impairment and have no real asymmetric-path
semantics. They may help prepare a scenario but cannot be recorded as the
geographic cell's measured WAN values.

### Exact hit-registration metrics per cell

Attach the secret-free report schema and the before/after reconnect snapshots
to every cell. Do not reduce hit registration to a single hit count. The
following are the exact existing metric groups to retain (zero is a valid
value):

| Domain | Exact metrics |
| --- | --- |
| Client/path | `MeasuredRttMs`, `MeasuredJitterMs`, `PacketsSent`, `PacketsReceived`, `PacketsDropped` where present in the two-client report; also retain the external per-direction timing/loss fields above |
| Discrete feedback | `Predicted`, `Confirmed`, `Denied`, `AuthoritativeUnpredicted`, `DuplicatePrevented`, `Pending`, `ConfirmationRate`, and `DenialRate` |
| Continuous feedback | `ContinuousPredicted`, `ContinuousConfirmed`, `ContinuousDenied`, `ContinuousAuthoritativeUnpredicted`, `ContinuousPending`, and `ContinuousConfirmationRate` |
| Headshot/kill feedback | `AuthoritativeHeadshotCues`, `PredictedHeadshots`, `ConfirmedHeadshots`, `HeadshotsDowngraded`, `HeadshotsPromoted`, `HeadshotsDenied`, `AuthoritativeHeadshotsUnpredicted`, `KillPromotions`, `HeadshotAgreementRate`, `HeadshotDowngradeRate`, and `HeadshotPromotionRate` |
| Feedback timing | `MeanShotToPredictionFrames`, `MeanShotToConfirmationFrames`, `MeanPredictionLeadFrames`, and `TimingSamples` |
| Authority rewind | `RequestedRewindTicks`, `ValidatedRewindTicks`, `MaximumRequestedRewindTicks`, `MaximumValidatedRewindTicks`, `ClampPositionError`, and `ClampVerticalError`; treat `ValidatedRewindTicks < RequestedRewindTicks` as the raw clamp indication |
| Presented collision | `PresentedPoseSamples`, `PresentedPoseErrorMean`, `PresentedPoseErrorPercentiles` (`P50/P95/P99/P999/Max`), `PresentedPoseErrorMax`, `PresentedVerticalSamples`, `PresentedVerticalErrorMean`, `PresentedVerticalErrorPercentiles`, `PresentedVerticalErrorMax`, `SpeculativeHitSamples`, `SpeculativeHitPresentedPoseDistanceMean`, `SpeculativeHitPresentedPoseDistancePercentiles`, `SpeculativeHitPresentedPoseDistanceMax`, `ShotTickMismatchSamples`, `PresentedTickVsShotTickMismatchMean`, `PresentedTickVsShotTickMismatchPercentiles`, `PresentedTickVsShotTickMismatchMax`, `SimulationTickMismatchSamples`, `PresentedTickVsSimulationTickMismatchMean`, `PresentedTickVsSimulationTickMismatchPercentiles`, `PresentedTickVsSimulationTickMismatchMax`, `PresentedPoseUnavailable`, and `Aligned/Minor/Material/Severe` buckets |
| Interpolation | `InterpolatedSamples`, `ExtrapolatedSamples`, `SnapshotUnderrunSamples`, `HeldSamples`, `PresentedFrames`, `InterpolatedFrames`, `UnderrunFrames`, `ExtrapolatedFrames`, `HeldFrames`, `MaximumExtrapolationTicks`, `DelayTicks`, and `TargetDelayTicks` |

The two-client headshot schema additionally requires the scenario counters
`TriggerAttempts`, `LocalRootShots`, `AuthoritativeRootShots`,
`CorrelatedRootShots`, `PredictedContacts`, `PredictedHeadshots`,
`AuthoritativeHits`, `AuthoritativeHeadshots`, `ConfirmedHeadshots`,
`DowngradedHeadshots`, `PromotedHeadshots`, `DeniedHeadshots`, and
`HeadshotAgreementRate`. Keep discrete and continuous confirmation rates
separate. A cell is incomplete when shot identity/correlation, headshot
evidence, authority rewind, or presentation data is missing.

## Offline merge and review

After both processes finish, collect the public descriptor, secret-free reports,
and the client's `captures/` directory. Keep the secret out of the evidence
package. Operators may independently record file hashes with:

```bash
shasum -a 256 descriptor.json server-report.json client-report.json captures/*.png
```

Create the bounded candidate manifest:

```bash
dotnet run --project tools/nettest/nettest.csproj -c Release -- \
  --rendered-wan-merge descriptor.json server-report.json client-report.json \
  captures merged-candidate.json
```

Merge fails on any run/Node/incarnation/Worker/match/wire/subject/seat/endpoint/
content/build/protocol/mode/room mismatch, invalid time overlap, failed runtime or
reconnect state, missing capture, or capture hash mismatch. Even a successful
manifest retains:

```text
renderedWanProof=false
qz1Accepted=false
qz5Accepted=false
requiresHumanVisualReview=true
```

The server requires at least eight applied host-debug refreshes. The planned
UDP reconnect may produce at most eight `debug` rejections while the exact seat
is briefly disconnected; zero rejections requires a null rejection code, and a
nonzero count requires the exact `debug` code. Any other code or a ninth miss
fails the report and merge.

Loopback packages are labeled `same-host-split-smoke-non-wan`. A non-loopback
package is labeled `non-loopback-path-candidate-unverified`; endpoint spelling
alone cannot prove two physical endpoints or an Internet path.

Inspect every PNG for false walls, door/field/platform presentation defects,
projectile duplication or disappearance, broken interpolation, and reconnect
discontinuities. Automation evidence and human visual conclusions must be
reported separately.

## Policy guardrails and deferred voting

- `LagCompensationPolicy.MaxRewindTicks` remains `15` (250 ms at the fixed
  60 Hz simulation). Do not raise it from loopback, emulator, headless, or
  same-host rendered observations. Revisit the cap only if the real geographic
  matrix records material clamping and the smallest evidence-backed alternative
  is selected.
- The existing Node-owned Results ballot remains unchanged. This runbook may
  exercise Results display and offered option selection, but it does not alter
  ballot options, revisions, authority, or the reliable wire contract described
  in `docs/G5_VOTING.md`.
- In-match voting is explicitly **DEFERRED**. Do not implement or evaluate a
  second vote surface until repeated live
  `match -> Results -> ballot -> continuation -> next match` cycles have shown
  stable result retention, handoff, reconnect, and next-match startup (use the
  existing 20-cycle live policy when that gate is run). A source, unit,
  headless, emulator, or single rendered run cannot advance this gate.

## STOP conditions

Stop the run and preserve its failure reports when any of the following occurs:

- the descriptor or admission is expired, reused, malformed, or identity-mismatched;
- normal TLS validation or the explicitly enabled exact dual pin fails;
- advertised Node/Worker endpoints do not equal the intended firewall/NAT mapping;
- a wildcard, loopback mismatch, Worker hostname, port `0`, fixture, or non-multiplayer room is requested;
- Node/Worker/content/build/protocol/match/wire/seat bindings disagree;
- fewer than eight host-debug refreshes apply; reconnect-gap counters are
  inconsistent; more than eight refreshes are rejected; or any rejection is
  not the exact `debug` code allowed for the briefly disconnected seat;
- no exact admitted-subject seat can be resolved;
- direct UDP join, rendering, capture, or same-control-session reconnect fails;
- reports overlap incorrectly, contain failure state, or any report/capture hash differs;
- the endpoints are actually the same physical host, or a VPN, relay, tunnel, or
  other path prevents an honest two-endpoint Internet-path claim;
- completion would require a protocol change, public admin/debug endpoint,
  certificate-validation bypass, gameplay-authority change, or automated SSH/deployment.

A same-host split smoke can validate orchestration only. Label it
`same-host/non-WAN`; it is never Internet-path evidence.
