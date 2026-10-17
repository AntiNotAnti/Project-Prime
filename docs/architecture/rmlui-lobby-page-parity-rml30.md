# RML-30: recover native Lobby bindings and controls

## Root cause

The original authored RmlUi Lobby includes centre chat input, server match
time/goal, map-preparation state and roster team selectors. The restored
RmlUiMenuController projected player names only, then sent main Lobby chat
to the separate Admin modal controller. The chat input had no proper document
owner and could not submit.

The Lobby has a local-front display order to match the eight Hunter 3D pads,
but player selection treated the display index as a network slot. Clicking
the front player could target a different network occupant.

## Changes

- Reuse one server-fenced local-first roster order for visible labels,
  Admin opening, team cycling and the 3D Hunter formation.
- Bind actual NetSession match time, goal, map status, countdown state and
  NetChat history to the existing authored RML controls.
- Wire the centre chat input to its owning Lobby page and use the current
  NetChat.Send network path. Admin input remains independent. Refuse invalid
  or oversized UTF-8 messages and only clear accepted requests.
- Map visual player selection back to exact server slot and generation.
  Validate MatchId, authority epoch, revision, owner/team lock and pending
  state before submitting the existing team-change command.
- Disable absent/unverified players rather than sending optimistic commands.
- An actual map thumbnail is still outstanding: show a truthful preview
  availability status rather than a fabricated image URI.

## Acceptance

The asset-free live-menu test covers real Lobby match and chat bindings,
local-first selection and exact network slot/generation, owner/team locks,
empty nameplates and chat byte limits. Full desktop, Android and multiplayer
CI acceptance remains mandatory before merge.

## Remaining non-Settings parity

Home social/news/party rail needs live services, map preview image pipeline,
native Social interactions, full Community creator/download functionality,
3D cosmetic previews and richer Theatre/campaign routes all need follow-up.

**Full Settings remains the final migration phase.**
