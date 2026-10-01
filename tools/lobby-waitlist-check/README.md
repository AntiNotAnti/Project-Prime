# Lobby waitlist policy checks

Run the content-free core and loopback checks:

```
dotnet run --project tools/lobby-waitlist-check -c Release
```

`LobbyWaitlist` is the owner-thread policy core used by the production queue
admission path. The standalone tests below isolate its policy using synthetic
packets; production bootstrap and server tests are described in
[the live admission documentation](../../docs/network/lobby-waitlist.md).

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

## Remaining client work

The live admission layer now provides bounded queue-only transport bootstrap,
production protocol-35 packets, server reservations, and same-socket normal
admission handoff. Launcher UI remains a separate follow-up. Resume currently
requires the same endpoint and connection incarnation. SpectatorMode consumes a
normal player slot; there is no independent spectator admission pool.
