# CW-04q — authority acceptance closeout

This slice adds acceptance fixtures to the live authority developed in CW-04a–p.
It does not enable custom weapons in ordinary lobbies. Readiness and combat still
require separate developer flags; bots remain gated until CW-12.

`nettest --custom-acceptance-udp DATA` runs the actual dedicated simulation with
ordinary endpoint-owned clients. Structurally valid actions with a perpendicular
aim or an out-of-tolerance muzzle receive terminal source denial, with no resource
transition or public launch. Retrying the corrected ray under the same identity
cannot promote it; a fresh contiguous action can succeed. Primary controls then
exercise an early bolt-cycle Fire, reload during that cycle, Fire during reload,
source-driven completion, and the next paid Fire. Both lobby-token owner and
non-owner use the same server path, including a run with client-only 160 ms RTT,
10% loss and seed 431. The architecture hosts a dedicated child and joins it as
an ordinary client; no production legacy `NetRole.Host` combat path exists.

`nettest --custom-replica-reconnect-udp DATA` reconnects an initial spectator with
the same client ID through a new UDP socket. The freed slot receives a new
transport connection, generation and bootstrap. Old exact baseline and event ACKs
are replayed over that new authenticated connection: neither opens/releases the
new mailbox. The replacement receives current resources at Life0 without old
facts or an invented in-flight projectile. The source's unacknowledged private
payment/results remain independently retained. After genuine native inputs
advance the bolt deadline, a second real Fire delivers only current-recipient
launch/contact facts, even when both old and new mailboxes use event token1.

`custom-weapons-check --client-projection-scene DATA` captures actual authority
`BroadcastSnapshot` bytes after real M7 collision/payment. An explicitly declared
test role handoff passes those bytes through the native client snapshot decoder,
lifecycle and live player bridge on real asset-backed local and puppet actors.
Body200→135 and lethal head150→0 converge using the shared damage counter with
empty native history and neutral native attribution. Duplicate/reordered snapshots
cannot repeat health, death or score effects. This is not a second authenticated
client socket or proof of fast/slow lane arrival-order behavior.

Fixture loading ACKs, actor placement and initial health are explicit seams. The
real UDP cases use genuine input/source capture, not forged source certificates.
Heartbeat helpers send normal native movement inputs to both actors while waiting;
they neither set health nor advance authority clocks directly. The control helper
allocates its next source frame above both the authority world and the latest
consumed source, including frames sent by those heartbeat helpers.

CW-04's implemented authority boundary can now feed CW-05's request producer and
controls. Public input/HUD/device cadence remains CW-05; package negotiation,
custom bot admission and replay reconstruction remain CW-12. Physical devices,
rendered acceptance, WAN measurements and latency percentiles remain release gates.
Native-only historical packet shapes and gameplay remain separate.
