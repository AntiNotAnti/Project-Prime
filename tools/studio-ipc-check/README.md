# Studio IPC foundation check

Run `dotnet run --project tools/studio-ipc-check -c Release` with .NET 10.

This executable uses actual current-user named pipes and an independent child
process. It checks mutual authentication, explicit protocol-version rejection,
frame bounds and fragmented reads, malformed/null/unknown JSON, duplicate request
IDs, cancellation, disconnected peers, handshake timeout, clean shutdown,
cross-process forwarding, a stale-descriptor startup race, and crash/reconnect
with a fresh local capability.
On Unix it verifies mode 0700 directories and mode 0600 descriptors.
It also exercises the production game owner queue entry: cancellation removes
pending work promptly, while an executing action retains its staging resources
until its actual completion is observed.
An actual directory alias forwards to the same primary on Unix; Windows casing
and paired macOS bundle identity use the same BCL helper as installation leases.
An independent delayed game child replaces an unavailable descriptor whose old
PID remains alive, proving reconnect waits for a new authenticated endpoint.

The shared protocol project references only the .NET base class library. Studio
IPC version 1 is independent of the gameplay network protocol and carries small
control messages. A challenge proof crosses the pipe; the descriptor capability
does not. It is never a Hunter License or Community account credential.

The Studio endpoint handles document opening and ping. The separate Game role
uses the same shared transport for package installation, external playtesting,
host configuration, diagnostics, and narrow Community-ticket brokerage. Those
handlers are checked by `game-studio-broker-check` and the desktop lifecycle gate;
this wire checker does not claim native gameplay coverage. Unsupported messages
continue to fail explicitly.

Request IDs retain at most 512 completed/pending entries, and the listener admits
at most eight connected peers. Exact retries receive their remembered result;
conflicting payloads with the same ID are rejected. Callers should retry an
uncertain response with its original request ID. Callback owners must honor the
provided cancellation token before adopting documents or performing side effects.
Shutdown cancels callbacks, retains their actual tasks for draining, and bounds
the network shutdown wait; cancellation cannot revoke a document already opened.

The durable `instance.lock` file is never unlinked. The owning file handle is
closed by the OS after a crash, so a new primary can replace the stale descriptor
and rotate its random endpoint and capability. Unix named-pipe names are kept
short for macOS's 104-byte domain-socket path limit.
