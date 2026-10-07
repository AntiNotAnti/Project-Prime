# Authoritative party slot reservations

Slice 7 closes the capacity race left intentionally open by Slice 6.

Party travel still uses the social service for identity, coordination and
notifications, but **seat ownership now lives on the dedicated server**. A social
reservation request cannot by itself occupy a player slot.

## Protocol 43

Slice 7 introduces three queue-transport control packets:

- `PartyReserveClaim`
- `PartyReserveState`
- `PartyReserveAccept`

Because a protocol-42 server would silently ignore these otherwise-valid packet
kinds, the live network protocol advances to **43**. Historical protocol-42
replay compatibility remains separate and unchanged.

The reservation control packets use the existing bounded queue connection rather
than opening a second gameplay transport.

## Atomic capacity reservation

“Atomic” describes **capacity allocation**, not simultaneous socket connection.

For one reservation request the dedicated server either finds every required seat
at once or allocates none. The allocator excludes:

- connected human players;
- bots;
- existing waitlist seat offers;
- existing party reservations.

The resulting player-UUID → slot mapping is fixed for the reservation lifetime.
Members still connect individually, but their assigned seats cannot be taken by
ordinary direct joins or waitlist promotions.

The reservation lifetime is 30 seconds by default and is bounded to 10–60
seconds.

## Two reservation modes

### Party Quick Play

The leader is not connected to the destination yet, so the reservation request
uses `include_leader=true`.

Quick Play first discovers a persistent protocol-43 lobby with enough **effective
free capacity** for the full party. The leader then:

1. creates an authenticated social reservation request for the target
   `AuthorityEpoch`;
2. opens the server’s queue transport;
3. proves their Hunter License with the existing short-lived `pp1.` career
   ticket;
4. causes the server to validate the immutable reservation roster;
5. causes the server to allocate every required seat together;
6. receives their own reserved-seat admission;
7. only then enters the normal gameplay join path.

Thus the leader does not consume the first seat before the rest of the party’s
capacity is protected.

### Leader already in a lobby

When a party leader is already present in a verified persistent lobby,
`include_leader=false`.

The backend excludes party members already verified on that authority. A pending
reservation request is kept available for absent members, but **no server seats
are occupied merely because that request exists**.

The first follower who explicitly chooses Follow Party proves their identity to
the server. At that point the dedicated server allocates the entire missing
roster together for the short reservation window.

## Identity and authorization

A reservation UUID is not a bearer token.

Every claimant sends:

- reservation request UUID;
- current network client ID;
- the existing short-lived signed Hunter License career ticket.

The target dedicated server forwards that proof through the
`social-party-reservation-server` Edge Function. The Edge function:

1. authenticates the dedicated server with its opaque career reporter key;
2. HMAC-verifies the `pp1.` ticket;
3. verifies that the ticket UUID is bound to the supplied client ID;
4. asks PostgreSQL whether that exact Hunter License UUID belongs to the
   reservation’s immutable member snapshot;
5. verifies the exact target `AuthorityEpoch`.

The dedicated server never trusts a player UUID supplied directly by the client.

Immediately before consuming a reserved seat, the server performs the validation
again. Party roster/leadership changes cancel pending/reserved requests.

## Server activation

After validating the request, the dedicated server selects all slots in one
allocation operation and generates a server reservation UUID.

The server then activates that exact assignment in PostgreSQL through the
server-authenticated relay. Activation validates:

- exact reservation request;
- exact reporter/server;
- exact authority epoch;
- exact member count;
- no duplicate member UUIDs;
- no duplicate slots;
- every assigned UUID belongs to the reservation roster;
- all slots are within the eight-player capacity.

Only after backend activation succeeds does the server publish
`PartyReserveState.Reserved` to claimants.

## Seat consumption

A member responds with `PartyReserveAccept` carrying:

- request UUID;
- server reservation UUID;
- authority epoch.

The server revalidates the member immediately before admission and permits that
connection to consume **only the slot assigned to that Hunter License UUID**.

Admission reuses the already-authenticated queue transport and feeds the assigned
slot through the normal `HandleHello` / `Welcome` path. No parallel gameplay
admission implementation exists.

After normal admission succeeds, the member is marked admitted in the backend.
When every member has been admitted, the server releases its in-memory
reservation group.

## Shared waitlist capacity

The normal waitlist and party reservations use one authoritative slot mask.

A party-reserved seat:

- cannot be taken by a direct join;
- cannot receive a normal waitlist offer;
- can be consumed only by the reservation member assigned that slot.

Likewise, an active waitlist offer is unavailable to the party allocator.

This prevents two independent reservation systems from promising the same seat.

## Effective capacity discovery

Protocol 43 extends `ServerStatusPacket` with one bounded
`ReservedSlots` byte after the existing waitlist tail.

It counts currently unoccupied seats held by:

- normal waitlist offers;
- party reservations.

Server browsers and Quick Play therefore calculate:

`effective free = MaxPlayers - connected Players - ReservedSlots`

instead of advertising reserved seats as open.

Older status tails safely decode `ReservedSlots = 0`; mixed live v42/v43 peers
are refused by the protocol boundary.

## Social database model

Slice 7 adds:

- `prime.social_party_reservations`
- `prime.social_party_reservation_members`

A request stores an immutable snapshot of the exact party members who require
seats. The member table later receives the server-assigned slot and member state.

Reservation state includes pending, reserved, completed and terminal
cancel/expiry/rejection states.

Party member or leader changes cancel live reservations through database
triggers. Private Realtime invalidations refresh the affected party clients.

All new tables have RLS enabled and direct `anon` / `authenticated` grants
revoked. Reservation helper functions are not executable by those roles.

## Server relay

`social-party-reservation-server` is an intentional server-authenticated Edge
Function and therefore uses the same explicit non-JWT relay model as authoritative
career reporting and social lobby membership.

The client never receives the server reporter credential or Supabase service
role.

## Failure behavior

Reservations fail closed.

Examples:

- insufficient seats → no group allocation;
- wrong protocol → normal protocol refusal;
- wrong authority epoch → validation rejected;
- claimant not in immutable roster → rejected;
- stale server process / lost allocator state → reservation expired rather than
  reconstructed from database;
- party roster changes → reservation cancelled;
- server leaves lobby → local reservations cancelled;
- timeout → seats released;
- backend activation failure → local reservation released;
- member cannot arrive during the reservation window → that unconsumed seat is
  released at expiry.

## Validation

Slice 7 adds or extends checks for:

- generated reservation packet IDs and malformed/truncated wire data;
- exact uint64 authority fencing;
- shared waitlist + party reservation slot masks;
- direct joins unable to steal reserved seats;
- party allocation unable to steal waitlist offers;
- signed claimant identity round trips;
- server-authenticated reservation Edge operations;
- exact immutable roster validation;
- reservation activation/admission/cancellation;
- migration execution in disposable PostgreSQL;
- full social acceptance including atomic party reservations;
- protocol-43 current-version contracts;
- status-packet reserved-capacity round trip.

The dedicated-server compile gate and Windows dedicated-server publish already
exercise the authority-side code path.

## Deliberately not included

Slice 7 does not make all party sockets connect at one exact instant. UDP
connections still arrive independently.

It also does not:

- reserve spectator slots;
- keep seats indefinitely for disconnected party members;
- auto-follow without member consent;
- allow reservations during an in-progress match;
- bypass normal Hunter identity, lobby hydration or game launch behavior.

Those constraints keep the reservation system a narrow admission primitive
rather than a second session architecture.
