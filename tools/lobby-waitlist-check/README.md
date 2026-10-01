# Lobby waitlist policy foundation — live admission pending

Run the content-free core and loopback checks:

```
dotnet run --project tools/lobby-waitlist-check -c Release
```

`LobbyWaitlist` is an inactive server-side foundation. It does not expose a queue
on existing servers, add live packet types, change protocol 34, or assign player
or spectator roles. All methods execute on the constructing lobby owner thread.
No background worker is introduced. The standalone tests compile the actual core
source and require no game content, graphics context or dedicated simulation.

Implemented policy:

- FIFO insertion order, default 64 entries, configurable hard maximum 256.
- Distinct reserved seat masks; authoritative humans AND bots count as occupied.
- 15-second configurable offers, accept/decline/expiry and immediate next offers.
- Match/authority/offer/queue identity fences and strict endpoint plus connection
  incarnation ownership. Nonces and queue IDs are never authentication secrets.
- JIP-disabled NEXT MATCH state, epoch transitions revoke stale offers.
- Brief configurable disconnect retention and resume only for the same established
  endpoint/connection incarnation. Repeated disconnects cannot extend the grace.
- Admission callback must perform ordinary validation and atomically occupy the
  seat. False/throw preserves the offer; reentrancy is rejected. Successful admission
  immediately marks that slot occupied in the core before making further offers.
- Bounded aggregate metrics: joined/left, created/accepted/declined/expired offers,
  disconnect expiry, mean accepted wait, high-water queue length. No player names.

Call `Update` with monotonic server time and authoritative occupancy/lifecycle at
every control tick. `CanDirectJoin` must be checked by every future normal admission
path. Snapshot reads do not advance time. The core deliberately cannot validate
an endpoint's transport credentials: only the transport/server may construct an
established identity, and no network packet may supply it directly.

The six **synthetic** generated queue fixtures have test-only packet IDs and
protocol metadata. They validate byte contracts without allocating production IDs
or reserving a future live protocol version. Loopback tests send actual UDP
datagrams between sockets with fixture-established identities, checking FIFO,
malformed packets, duplicate requests, identity attacks and reserved-seat acceptance.
They do **not** exercise production NetTransport, DedicatedServer or NetLobbyTest.

## Required live integration before exposing this feature

1. Add an authenticated pre-admission transport bootstrap. Current `NetTransport`
   only creates connection state when sending/accepting a reliable 17-byte Welcome
   containing a real player slot. It rejects queue traffic before that point.
   Do not simply allow unsequenced queue commands: that would remove established
   endpoint/connection identity fencing. Queue-only welcome/challenge state must
   carry bounded, server-owned connection identity without claiming a player slot.
2. Bound bootstrap attempts, pending queue-only connections, retries, bytes and
   expiry; integrate reliable channel classification and receive budgets. Existing
   connection storage is bounded at 64 and cannot silently expand to 256 queues.
3. Define fresh production packet IDs/protocol at integration time, strict semantic
   ID validation, queue state/offer delivery and request dedup/retransmission.
   Preserve protocol-34 replay decoding when the live version changes.
4. Construct this core on the dedicated lobby control owner. Feed humans/bots,
   capacity, match/epoch/JIP and disconnect state. All normal joins and bot additions
   must respect reservations. Acceptance must call existing team, lifecycle,
   custom-map identity and load/readiness validation; consume the reservation only
   after successful admission. Shutdown clears queue state.
5. Implement client queue-only lifecycle, discovery capability/count, queue UI and
   accepted-seat handoff. Current SpectatorMode is presentation state rather than
   a separate server spectator admission pool; define that role lifecycle before
   promising Spectate + Queue. Rebinding/resume across new endpoints requires an
   established authenticated resume contract, not a guessed client/display name.
6. Extend production NetLobbyTest with actual queued bootstrap, full-server
   reservation theft attempts, simultaneous offers, expiry/retries, transitions,
   bots, JIP, spectators, disconnect/resume, shutdown and malformed datagrams.

These are completion gates, not optional polish. This foundation must remain
inactive until secure production admission and client handling land together.
