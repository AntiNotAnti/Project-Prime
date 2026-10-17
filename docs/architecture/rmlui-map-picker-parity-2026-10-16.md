# RML-20: native multiplayer map picker

The October 9 backup shipped a six-card map selector with Vanilla/Community
categories. Its old presenter held a LobbySessionController and preview cache.
The current multiplayer path is NetSession plus IRmlUiMenuServices, so blindly
restoring that presenter would duplicate state and invalidate map package policy.

## Native flow

- The lobby map preview and Rules > Next Arena now open the existing authored
  pages/lobby/maps.rml modal. It stacks above the rules sheet through the
  shared page manager and is dismissed first by Back/Escape.
- Vanilla maps come from the current built-in catalog. Community maps come
  from locally registered definitions **with an installed immutable package**,
  not the public directory or an unverified remote map title. Six cards are
  visible per page; category changes reset the page.
- Selection resolves the exact room/package identity through
  NetworkMapIdentity.ForRoom, checks the current rules definition, then
  stages a local draft. The modal closes back to the rules sheet so owners
  must explicitly press Apply. The authoritative UpdateMatch flow still
  fences MatchId, authority epoch and revision and waits for server echo.
- Guests can browse the available maps but cannot stage changes. Empty
  Community lists display a clear installation message. Cancelling a map
  picker opened directly from the lobby also retires its implicit rules
  draft. Leaving the lobby or changing authority invalidates stale drafts.
- Asset previews currently retain the fallback label. Thumbnail acquisition
  and native GPU texture loading have not yet been reintroduced.

## Verification

The presentation-neutral no-assets live-menu acceptance now covers ordering,
deduplication, category paging, illegal indices, verified selection staging,
no optimistic UpdateMatch, cancellation and non-owner rejection. The map
modal's bindings use the same page manager and native typed-intent registry
as the existing Home/Play/Lobby/Rules pages.

## Outstanding presentation parity

The old branch still contains the SettingsPagePresenter and ten files
providing full settings workflows, and native Hunter Selection, Lobby Admin,
Splash and rich Lobby Bindings/Map Preview presenters. Priority follow-ups:

1. Restore full Settings using current typed SettingsController and backend.
2. Port hunter selection/cosmetics to current authoritative hub/lobby services.
3. Port lobby administration (bots, kick, teams, chat, host controls).
4. Restore preview assets and the deployment-chamber scene using the active
   renderer, not the retired WebGPU backend.
5. Complete Android launch surface, asset extraction, touch/IME,
   pause/resume/context-loss, and device acceptance from RML-19.
6. Replace remaining legacy route handoffs and verify default-UI cutover.

This PR does **not** claim full RmlUi feature parity or Android activation.
