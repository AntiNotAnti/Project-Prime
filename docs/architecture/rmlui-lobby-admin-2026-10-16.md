# RML-23: native authoritative Lobby Admin

## Historical parity without a second network controller

The October 9 branch included a Lobby Admin presenter tied to the old
LobbySessionController. The active client instead runs the authoritative
NetSession lobby. Reusing the old presenter unmodified would create a second
session path and stale slot actions.

RML-23 restores the existing pages/lobby/admin.rml UI and connects it through
a presentation-neutral, owner-thread RmlUiLobbyAdminController. The current
NetSession/SessionStatePacket/roster remain the *only* authority.

## Supported actions

- Live eight-slot roster with Hunter/suit, bot/ready, ping, selected player
  and team state.
- Kick, owner transfer, lobby closure and bot removal with explicit
  confirmation before sending any command.
- Server-side add/configure/remove bot, difficulty, Hunter and suit.
- Player team selection, automatic team, damage-reduction handicap and team
  lock editing through the current lobby command API.
- Current lobby chat and local failed-map retry. Chat is bounded to
  96 UTF-8 wire bytes and uses NetChat.Send.
- Start Match reuses the existing owner-only StartMatch action.
- The UI disables unauthorized/invalid actions and displays server pending
  state, rather than optimistically changing the roster.

Every command captures MatchId, AuthorityEpoch, SessionRevision, target
slot and target generation. The service rechecks the snapshot immediately
before SendLobbyCommand. Confirmation is invalidated on session revision,
slot replacement or authority change. Only the server may accept or reject
the command.

## Presentation and lifecycle

The old RML/RCSS assets are retained and bound to the new state model.
The document manager stacks Admin over Lobby (or Rules), enforces active
document sequence admission and restores focus on Back/close. Back cancels
a pending confirmation before it closes the Admin page. The typed chat
input is read from the real Admin document, then cleared only on submission.

No parallel NetSession pump, global rendering stack or new network protocol
has been introduced.

## Acceptance

The no-game-assets live-menu fixture exercises roster binding, permission
gates, confirmation/cancellation, targeted generation, replacement slot,
command-pending refusal, bot and chat submission, and authority retirement.
Run the full CI including desktop software OpenGL and network regressions.

## Deliberately deferred

The historical preview thumbnails/3D Hunter cosmetics, remaining native
workspaces and Android surface/touch/IME work are separate migration slices.

Full Settings will be migrated **last**, after those non-Settings features
are completed and validated.
