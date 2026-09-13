# Multiplayer — Node directory, lobbies, and hosting

Status: current architecture summary, last reviewed 2026-09-12. The retired
UDP master-server, in-client `DedicatedServer`, `HostRequest`, and client
`-hostgame` design are historical and are not supported hosting paths.

## Current discovery path

The Backend owns the public Node directory. A Server Node registers its stable
identity/incarnation, public control URI, region, supported protocol/build/content,
and population. Heartbeats refresh liveness; stale registrations disappear.
The client account session reads bounded, revisioned directory pages and
validates their shape before presenting them.

```text
Client AccountSession
  -> Backend HTTPS Node directory
  -> selected persistent Server Node control connection
  -> lobby list/create/join/ready/chat
  -> Node starts or assigns managed Worker
  -> Node sends authenticated match handoff
  -> client gameplay UDP goes directly to Worker
```

Relevant owners:

| Owner | Path |
|---|---|
| Backend directory and admission | `src/Backend/Nodes/` |
| Node registration/heartbeat | `src/Server.Node/Directory/NodeDirectoryReporter.cs` |
| Portable client directory query | `src/Client.Core/Accounts/AccountSession.Nodes.cs` |
| Reliable Node control client | `src/Client.Core/Networking/Nodes/NodeControlClient.cs` |
| Shared browser/lobby presentation | `src/Client.Presentation/Launcher/Gui/NodeBrowserView.cs` and `Launcher/Shell/PlayController.cs` |

## Current lobby and match ownership

The Server Node owns public lobbies, membership, seats, rules, ready state,
chat, match transition, Worker placement, and recovery. Creating or joining a
lobby does not start an authoritative simulation inside the client. When the
lobby starts, the Node owns the transition and supplies a ticketed Worker
handoff. The Worker owns one `MatchInstance` and direct gameplay UDP; reliable
lobby/control traffic remains on the Node connection.

Quick play selects a compatible public lobby or asks the Node to create one.
Manual browsing reads the Node's bounded lobby pages. Client presentation may
optimistically update only UI state; Node snapshots and revisions remain the
authority.

## Target invariants

- The client never launches a Worker or hosts authoritative gameplay in its
  own process.
- The Backend directory never owns lobby or match state; it reports live Nodes
  and issues bounded admission.
- The Node connection persists across lobby and match transitions.
- Gameplay is not tunneled through Backend or Node control traffic.
- Directory/Backend latency cannot block a Worker's fixed 60 Hz loop.
- Guest and signed-in admission remain explicit; display names are not identity
  or reconnect ownership.

## Temporary and evidence boundaries

Client.Core owns directory/control contracts while Client.Presentation owns the
browser and lobby views. This split is current, not a source-link fallback.
Deployed directory availability, public TLS/firewall behavior, geographic WAN,
and capacity remain separate release gates. The native macOS content-free
renderer smoke does not validate this network path. See
`docs/CURRENT_ARCHITECTURE.md`, `docs/CURRENT_PROTOCOL.md`, and
`.claude/KNOWN-GAPS.md`.
