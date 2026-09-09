# Lobby waitlists

Server.Node owns a bounded FIFO waitlist inside each lobby. `LobbyManager` is
the only writer: waitlist state is serialized by the existing lobby authority
lock and uses the same injected `TimeProvider` as lobby lifecycle commands.
There is no queue thread or global mutable waitlist.

## Admission and identity

`lobby.queue.join`, `lobby.queue.leave`, `lobby.queue.accept`, and
`lobby.queue.decline` are authenticated, 30/sec control-rate limited, and
revision-aware. A queue position is assigned by a server sequence; client
timestamps, display names, and request order are never ordering keys. Each entry
carries the server-owned `Standard` priority class (the `Normal` alias is kept
for policy vocabulary); clients cannot supply a priority or timestamp. Ordering
is priority class followed by the server sequence. Identity
deduplication uses the tagged `HumanIdentityKey` (registered account or guest
session), so a guest UUID cannot alias an account UUID.

An observer may remain a lobby member while queued. If observer capacity is
full, a user can be queued without becoming a member. A queue cannot own a
lobby; when the final actual member leaves, queue entries are cancelled and the
lobby is removed. Both the tagged human identity and its Node session bind each
entry and reservation. A human identity or session can queue in only one lobby,
cannot hold two reservations, and cannot claim another session's offer.

## Offers and match boundaries

The default bounded offer window is 15 seconds (configurable from 10 to 20
seconds with `Node:WaitlistOfferSeconds`). `Node:MaximumWaitlistPerLobby`
defaults to 64 and is hard-bounded to 1024. Multiple available seats are
offered to FIFO heads at once. Offer IDs are opaque and single-use; stale
revisions, IDs, expired offers, and offers from a frozen phase fail closed.

An accepted offer converts an observer to a player or admits a queued-only
identity. Requested team is advisory; the existing team allocator remains
authoritative. Bot count reduces the available human seats. An outstanding
offer also reserves its capacity against ordinary `lobby.join` requests, so a
late direct join cannot steal an offered seat.

`ImmediateSeat`, `NextMatchSeat`, and `ObserverUntilNextMatch` are policy values.
An immutable `MatchSpec` is never changed during `InMatch`: a configured
immediate policy is resolved to a next-match boundary. Survival modes use the
next-match policy. FIFO is the only accepted Duel queue policy. `WinnerStays`,
`LoserStays`, and `Manual` remain protocol vocabulary but fail closed at lobby
creation until authoritative match results can drive a real ordering policy;
they do not add a new Duel gameplay mode.

Disconnected sessions retain membership, queue entries, and offers during the
existing 45-second resume grace. Only final `NodeSessionManager` pruning calls
`LobbyManager.Disconnect`, which cancels the queue entry and then advances the
remaining heads.

## Snapshot and metrics

Lobby snapshots and public list entries expose bounded player, observer, and
waitlist counts plus an ordered display-name summary. Only the requesting
session receives its own offer ID and expiry metadata. Snapshot notifications
are delivered to both lobby members and queued-only sessions, and resume
restores the queued lobby view.

`LobbyManager.WaitlistMetrics` exposes bounded domain metrics for size, accepted,
expired, and declined offers plus aggregate wait time. No external metrics
framework is required.
