# RmlUi Home + live lobby presentation correction

This pass fixes the mismatch between the authored deployment-chamber design and the
actual window-sized client presentation. It builds on the merged Living Chamber
and Home Side Rails rather than introducing a second renderer or a second social service.

## Home

- Remove **Deployment Link**, which only repeated the active Hunter and activity
  already displayed by the selector/caption.
- Move the bundled Community Dispatch / News teaser into the upper-left rail.
- Keep the actual Friends / Party rail visible at normal 720p desktop heights.
  The old `@media (max-height: 850dp)` rule incorrectly concealed it.
- The rail remains useful when no friends are online: counts, a truthful
  connecting/offline/no-friends state, and the verified Social route stay visible.
- Live joinable friends, requests, party and invitation indicators continue to
  derive from the existing Social read model. Home never bypasses Social's
  permission checks or transfers admission tokens.
- The friends/request directory now warms and refreshes asynchronously from the
  process-level Social service (75-second cadence). It is cancelled on stop,
  skipped by the existing account-safe capture/runtime policies, and does not
  block render/network/gameplay threads.
- Narrow screens prioritise the activity selector and Hunter rather than
  stacking side cards over them; small-height layouts compact rail bodies.
  Only exceptionally short viewports drop the peripheral cards.

## Chamber appearance and activity routing

- Raise the visibility of the already-authored reactor halo, per-Hunter color
  spill, floor rings, wall trim, light shafts, ambient scanner bars and dust.
- Preserve neutral Hunter model materials and chroma: this changes environment
  light, not equipped suits or gameplay lighting.
- Activity moods are distinct for Quick Play, Lobby Browser, Offline Battle,
  Aim Lab, Adventure and the actual Lobby, with Community and Studio technical
  moods for those content routes.
- Returning from News/Social/Studio/Community to Home restores the previously
  selected activity mood instead of leaving another tab's ambience behind.
- Reduce Menu Motion still freezes the effects' timebase.

## Actual live lobby

- Reuse the real eight-player V/chevron formation and the authoritative
  `LauncherLobbyFormation` pad geometry.
- Draw the atmospheric side-bay stage pass in Lobby too. Previously the
  `DrawUnderHunter` lobby early return prevented that entire effect layer.
- Feed the selected Hunter identity for **each occupied presentation slot**
  into its own floor/rim/halo light, rather than painting every pad in the
  local Hunter's color. Never draw lighting for unoccupied slots.
- The top-center compact squad strip shows authoritative occupied/ready/local
  statuses for all eight slots and live phase/roster/ready text.
- A concise match-preparation readout sits inside the existing Next Match
  panel. Player roster, map preview, rules, Teams, chat and nameplate actions
  are preserved and styled for legibility/focus/ready state.
- An **Invite Friends / Party** button uses the already-authorized Social
  page, including its active-lobby join and invite guards. There is no
  direct/unverified join or protocol change.

## Acceptance

1. 1280x720, 1920x1080, 2560x1440@2x and compact 960x600:
   real-native RmlUi source DOM stays in viewport; featured/social controls
   remain accessible at 720p and collapse on narrow viewports.
2. An empty Social account still shows an actionable, honest status. A
   joinable friend's row routes through Social, not a raw join.
3. A lobby with 2, 4 or 8 players has aligned real-model platforms/nameplates,
   occupancy/ready status, rule actions, map preview, roster and chat.
4. Distinct Hunter accents are visible under the same chamber geometry, and
   each occupied lobby slot has its own selected Hunter color.
5. Changes of tab, activity, or Hunter do not stutter, hide the center model,
   damage mouse hit testing, or repaint gameplay materials. Reduce Motion
   freezes environmental animations.
6. Build, native page checks, live lobby controller checks, shader/layout checks,
   and platform compilation must pass on the PR before merging.

The feature branch is the implementation. This document is design intent and
test targets, not proof of physical-device acceptance.
