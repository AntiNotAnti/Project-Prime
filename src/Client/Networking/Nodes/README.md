# Persistent Node control

The graphical launcher's online entry opens compatible Backend Nodes. Hunter
License sign-in supplies a short-lived account Node admission; the account bearer
token is never sent to the Node. A configured but signed-out launcher can browse
the anonymous directory and request a short-lived guest admission using its
trimmed launcher name. Signed-in admission failures are surfaced directly and
never silently retried as guest. Guest display names label guest sessions only;
they do not represent authenticated account ownership. Node listings match the
local protocol, build display and content hash. Legacy direct servers remain an
explicit rollback entry.

One `NodeControlClient` retains WSS while UDP gameplay opens and closes. Control
frames and command queues are bounded, state is published as immutable snapshots,
and stale lobby revisions cannot replace newer state. A disconnected control
session can resume with its in-memory rotating token. Tokens are not persisted.

The lobby provides creation, join, map/mode configuration, ready, start, return
and rematch. Worker handoff connects with the Node-issued nonce, ticket and wire
match ID. A failed UDP join keeps the lobby visible; Retry requests a fresh
reservation ticket. Matching terminal events close the render window on its own
thread; the launcher stops UDP and reopens the same Node lobby. New handoff events
do not erase the return signal for a still-running previous match.

`ClientWorkerVerticalTests` exercises actual TLS WSS, a real Worker child and UDP
without a renderer, including natural completion and rematch. Unit tests cover
sequence/identity checks, stale snapshots, listener exceptions and bounded
send/disposal behavior. Rendered desktop/Android end-to-end behavior remains a
separate validation gate.
