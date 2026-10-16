# RML-05: Native lobby Hunter selection and server-authoritative rule drafts

Following exclusive RML-04 native Home composition, this slice connects the
RmlUi lobby's existing Hunter/Suit buttons and 16-rule sheet to current
authoritative networking. It does not introduce another NetSession or match
configuration parser.

## Implemented

- Local Hunter cycling uses HunterRules.Pool, including Low Tier restrictions.
- Suit cycling keeps the current 0–3 scheme; both changes identify to the
  existing server and persist LauncherPrefs. They never assign remote players.
- Rules open from the current server session and bind a private immutable
  MatchDefinition/flags draft fenced to MatchId, AuthorityEpoch and revision.
- All sixteen existing native toggles, built-in arena cycling, catalog-driven
  game mode and matchup cycling, time/score parsing are implemented.
- The sheet applies only via existing LobbyCommandType.UpdateMatch after
  LobbyRules.ValidateDefinition. Nonowners and stale revisions cannot submit.
- The UI waits for an authoritative newer SessionState containing the exact
  accepted MatchDefinition and flags before retiring the pending edit.
- Incompatible options and remaining lobby administration use the existing
  protected legacy workspace. Custom map packages are not synthesised.
- The native Home's actual bitmap exclusivity and Avalonia recovery continue
  from RML-04. No default UI cutover or Android migration.

## Validation

tools/rmlui-live-menu-check now asserts native rule toggle reversibility,
the canonical modifier conflict constraints, format and limit parsing,
no optimistic lobby edits, revision fences, owner permissions, and server
acknowledgement. The default client, server, opt-in client and pinned native
GL2 software-frame acceptance remain CI gates.

## Deferred

Native lobby creation hosting, advanced custom teams, bot administration,
dynamic custom map editor, networked multi-client device acceptance,
Community/License/Settings/Studio and Android-specific lifecycle/IME.
