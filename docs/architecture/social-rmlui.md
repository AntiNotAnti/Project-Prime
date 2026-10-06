# RmlUi social surface

Slice 3 renders the Slice 1 relationship model and Slice 2 presence feed inside
the RmlUi home proof. It does not introduce a second social authority: every
mutation still goes through `SocialClient`, and online state still comes from
`SocialPresenceClient`.

## Home integration

The top account cluster now exposes a SOCIAL action with an incoming-request
badge. The existing right-side session card becomes a compact social glance:

- local Hunter/session identity remains at the top;
- up to three online friends are shown below it;
- each friend shows the privacy-reduced activity and arena label already
  supplied by Slice 2;
- OPEN SOCIAL expands the full drawer.

No Join Friend button is exposed. The network `RoomKey` is map identity, not a
globally unique lobby locator, so Slice 3 never turns it into an address.

## Drawer

The drawer contains three primary tabs:

- FRIENDS: persistent friends from Slice 1, including offline friends;
- PLAYERS: the privacy-filtered online directory from Slice 2;
- REQUESTS: incoming and outgoing friendship requests.

MANAGE BLOCKS opens the existing blocked-player relationship list without
promoting it to a fourth primary tab.

Rows merge persistent relationship data with the current presence snapshot.
Friends therefore remain visible while offline and gain live activity when they
are present. Online rows inherit relationship state so the same Hunter displays
FRIEND, INCOMING, OUTGOING or PLAYER consistently across tabs.

The filter field searches the loaded name, Prime ID, activity and visible arena
text. When the Players tab receives a syntactically valid full Prime ID, the
client also performs the exact Slice 1 lookup, allowing an offline Hunter to be
found without adding global display-name enumeration.

## Context actions

Selecting a row opens a bounded context sheet. Available actions are derived
from the authoritative relationship type:

- PLAYER: Add Friend, Block
- FRIEND: Remove Friend, Block
- INCOMING: Accept, Decline, Block
- OUTGOING: Cancel Request, Block
- BLOCKED: Unblock

The native RmlUi model only emits an action plus Prime ID. Managed code validates
the Prime ID shape and calls the existing SocialClient API. Successful mutations
replace the local social snapshot and invalidate Slice 2 presence immediately.

## Native bridge

The RmlUi bridge now registers a `SocialRow` struct and
`std::vector<SocialRow>` with the RmlUi 6.3 data-model system. The managed
host updates two dynamic arrays:

- `social_rows` for the active drawer view
- `home_friends` for the compact home card

The C ABI is deliberately narrow: clear, append drawer row, append home friend,
commit, and Back. Tokens, database IDs and service credentials never cross the
native presentation boundary.

Escape/controller Back closes the context sheet first, then the drawer, before
falling through to the existing shell navigation.

## Async behavior

Opening the drawer starts a background `SocialClient.LoadAsync`. While open,
the persistent social snapshot refreshes every 15 seconds. Presence is sampled
from Slice 2's already-cached read model and does not create another heartbeat.

Friend mutations, exact Prime-ID lookup and refreshes are all polled from the
RmlUi owner thread. Worker tasks never call the native RmlUi API directly.

## Deterministic UI acceptance

`-rmluisocial` is a screenshot-only fixture flag used with
`-rmluipocshot`. It seeds representative online/offline friends and opens the
drawer without contacting Supabase. The Linux RmlUi CI gate now captures a
1920x1080 social-drawer frame in addition to the existing multi-resolution home
captures.

This fixture is presentation data only and is never used by an interactive
client.

## Deferred to Slice 4

Slice 3 intentionally leaves these controls absent:

- game invite send/receive
- Join Friend
- invite accept/decline
- authenticated lobby resolver
- realtime invite notification transport
- party migration

Those features need a durable lobby/invite identity, not a UI shortcut around
the server browser.
