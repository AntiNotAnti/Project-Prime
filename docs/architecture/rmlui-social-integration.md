# Native Social integration

The Social service stack from pull requests #365, #366, #368, #369, #370, #371
and #373 is applied in dependency order onto the RmlUi foundation. The old
monolithic prototype renderer and native string-message Social bridge are not
imported: `SocialController` and the independent Social document use the existing
authenticated clients, presence, private realtime invalidations, parties,
verified lobby directory, travel consent and server reservations.

The combined live network protocol is 44. Mainline 43's Samus Morph Ball touch
semantics and all historical replay packet widths remain intact. The older
reservation branch independently used 43 for new queue control packet kinds;
44 prevents those incompatible peers from silently sharing a session. Reserved
capacity is excluded from normal discovery selection and shares the server's
seat mask with the existing waitlist. A party admission adopts the exact queue
transport and Welcome instead of opening a new socket.

## Process and page ownership

The graphical process calls `SocialRuntime.Start()` once, then calls
`Suspend()`, `Resume()` and `Stop()` for platform lifecycle. Page retirement
unsubscribes its backend and cancels its own requests. It does not stop the
process's presence, party travel or invite observer. `NotifySessionChanged()`
requests prompt re-observation after a session transition.

The page creates a `SocialController`, calls `Refresh()` on open and `Pump()`
from the UI owner thread. Workers publish immutable copies only through Pump.
The snapshot supplies paginated, searchable rows and exact available commands.
Destructive actions retain their original lifetime, data revision, row key,
party identity, travel identity/revision and reservation identity through
confirmation. Worker invalidation is consumed before dispatch and stale state
requires the user to review the current target. An already-sent mutation cannot
be unsent by Cancel. Refresh, lookup and verified join preparation can be
cancelled; late admission handles are disposed rather than leaked.

Native SocialAction 230 uses bounded action arguments 0 through 52. Search and
Prime ID lookup read their dedicated document fields explicitly; credential or
proof values never enter native action payloads or Social snapshots. Auth and
transport failures have safe, actionable status messages, without raw response
bodies or session tokens.

## Joining and whole-party Quick Play

The root polls `TryTakeJoin(out SocialJoinRequest? request)`. The request carries
the verified host, port, server, room and authority epoch. Transfer its optional
reserved admission exactly once with `TakeAdmission()` and call the existing
`NetLaunch.Connect(..., partyAdmission: admission)`. After connecting, require
the live `NetSession.AuthorityEpoch` to match `request.AuthorityEpoch`; a replaced
server must not be accepted because it reused an address. Dispose any unconsumed
request/admission on cancellation, stale route lifetime or connect failure.

Native Quick Play reads `SocialMatchmaking.CurrentParty`. For a leader with
multiple members it discovers a compatible lobby with at least MemberCount
effective free seats, then calls `PrepareLeaderReservationAsync(entry, token)`
before joining. Pass the returned Admission to the same NetLaunch path. The
existing server validates authenticated party membership, reserves all requested
seats atomically, and refuses insufficient capacity. Followers consent explicitly
to published travel; they never race normal join admission for reserved seats.
After successful leader Quick Play, call `NoteQuickPlayTravel()`.

## Local acceptance

`tools/social-controller-check` uses fake service boundaries and makes no
account, friend, invite, party or presence requests. It covers stale confirmation,
leadership/travel replacement, cancellation and retirement, late admission
release, one-time transfer, thread ownership, privacy choices, full Prime ID
validation, pagination and explicit failure feedback.

The integrated existing gates additionally exercise all actual Edge handlers,
frozen dependency locks, actual SQL migrations and service-only/RLS grants in
disposable PGlite, protocol fixtures, queue budgets and real loopback waitlist
admission. These checks do not prove production relay JWT configuration or
cross-process reservation concurrency; the existing local relay and multi-client
deployment gates remain required before rollout. No production state is mutated
by automated acceptance.
