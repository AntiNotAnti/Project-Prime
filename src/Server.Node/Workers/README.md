# Node-owned Workers

`AddNodeWorkerPool` registers the manager, scheduler, Node-only admission signer,
report ingestion when configured, and the hosted process lifecycle.
Workers are Node-owned child processes. They do not expose a user-facing local
hosting mode, and the client never launches a Worker directly. The client cutover
uses public lobbies on the Node; private or unlisted local hosting is retired and
no `--standalone` Worker option exists.
`Node:Workers:Processes` is an array of `WorkerLaunchOptions`:

- `FileName`: `dotnet` or an absolute Worker apphost executable.
- `Arguments`: for dotnet, the Worker DLL followed by content and lane options.
  Worker identity, pipe and artifact-directory arguments belong to the manager.
- `Content`: optional expected content version/hash/build/protocol. Hello must
  match it exactly. Scheduling always uses the authenticated Hello profile.
- `ArtifactDirectory`: absolute Node-configured root, passed as `--artifact-dir`.
- `Capacity`: maximum matches/players with initial active counts both zero.
- Startup, heartbeat and shutdown deadlines and bounded command/event capacities.

Use the Worker `--describe-content true --content-dir <absolute-path>
--content-version AMHE1` command to obtain the expected immutable content profile.
No game Scene is created in Node.

The manager creates a CurrentUserOnly duplex pipe with a random name. A separate
32-byte single-use startup token crosses redirected stdin only. Identity and token
must match the first Hello; subsequent Hello messages are terminal violations.
Only platform/runtime environment variables reach child processes. Node reporting,
directory and signing credentials are neither forwarded nor logged.

`WorkerScheduler.StartWorkerAsync` attaches the sole bounded IPC event consumer.
`PlaceAsync` validates a frozen spec and shares one creation task for identical
MatchId/spec retries. Different specs under the same ID are rejected. Selection
uses compatible content/build/protocol, readiness, health and reserved load, then
WorkerId for deterministic ties. Creation timeout cancels the owned match. Process
failure interrupts only its owned matches, with no transparent recreation.

Hosted shutdown closes lobby admission, drains match placement, waits for terminal
matches and report artifacts to become durably owned by the Node outbox, then sends
Shutdown. `Node:Workers:DrainTimeout` defaults to five minutes. Host shutdown budget
includes this deadline and child shutdown time. `ForceAfterDrainDeadline` explicitly
permits forced termination; otherwise drain failure is reported as unconfirmed,
not successful durable completion. Abrupt host teardown still interrupts children.
The reporting server UUID must equal the Node UUID.

Terminal placement and worker history is bounded. The coordinator calls
`ForgetMatch` only after consuming the terminal outcome/report, and `RetireAsync`
only for a completed worker before replacing it.

Tests:

```sh
dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter 'RequiresGameContent!=true'
GAME_DATA_DIRECTORY=/absolute/path/to/AMHE1 dotnet test tests/Server.Node.Tests/Server.Node.Tests.csproj -c Release --filter 'RequiresGameContent=true'
```

The first suite starts a small real child-process pipe harness. The second starts
two actual Worker processes and four bot matches, verifies measured lane progress,
capacity rejection and crash isolation, then cancels and drains the surviving
worker. This is functional lifecycle evidence, not capacity or live-client proof.
