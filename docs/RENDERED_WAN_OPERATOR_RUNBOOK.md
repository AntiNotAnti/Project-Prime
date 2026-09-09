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
- Use a new or empty output directory. The operator starts exactly one ephemeral
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
