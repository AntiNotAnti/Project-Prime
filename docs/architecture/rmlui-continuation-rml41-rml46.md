# RmlUi continuation: RML-41 through RML-46

October 17, 2026. Local work based on `dc7f097067d755ec76d145fc7cc333068566863d`.
CPP-01 remains **complete by explicit skip**, not a graphics or device pass.
CPP-02 remains in progress. Avalonia remains the default, native UI opt-in,
and full Settings migration remains last.

## Implemented locally

- **RML-41:** native Hunter License Account face and verified account actions,
  explicit publish consent, and real Hunter preview controls. Email/password
  fields are read once and cleared; private account UUIDs and credentials do
  not enter binding snapshots. Recovery preserves the existing guest/career
  guard and requires durable session storage before replacing the session.
  Preview state is restored after the isolated engine model draw, and a
  partially clipped preview is suppressed instead of painting over chrome.
- **RML-42:** native advanced offline rules modal, actual mode validation,
  transactional save/conflict checks and persistence failure rollback.
  The retired One in the Chamber flag is not offered as a working toggle.
- **RML-43:** native Theatre validation, recovery, rename, tags/collections,
  import, export and confirmed deletion through the existing replay owners.
  Work runs off the render thread. Captured file stamps, library boundaries,
  symlinks, no-overwrite copies and owned temporary-file cleanup are checked.
  Rich cinematic Studio remains an explicit existing-owner handoff.
- **RML-44:** selectively restored current-compatible pause, match results,
  Aim results and HUD controllers/presenters. Pause/results use the existing
  native host and scene/window lifetime witnesses. Existing Settings/Replay
  handoffs and unavailable-native fallback stay operational. Aim fallback
  preserves the exact report and callbacks. Adventure journal pause retains
  its existing owner. HUD controller/presenter source is restored; Settings
  draft/Apply runtime handoff is still outstanding.
- **RML-45:** restored the previous branch's Social, presence, invitations,
  Party, travel and reservation backend from donor
  `d08107a8a7a7ca64f87e96780aff8c6646cc2443`. Seven historical SQL migrations
  are unchanged. Added native Party endpoint/snapshot contract and a
  server-owned monotonic lifecycle revision, exact account/party/invitation
  witnesses, and serialized authority entry points. The native Party page
  supports first invitation, incoming/sent invitations, members, leadership,
  leave and disband. Destructive/leadership actions require confirmation.
  Restored reservation activation/admission now reject wall-clock-expired
  requests without reviving deadlines or member state.
- **RML-46:** native privacy-aware presence endpoint and client/controller,
  slow asynchronous heartbeats, activity observation from current owners,
  45-second TTL, and account-bound compare-and-set privacy updates. Hidden or
  unavailable presence is never described as offline. The native client has
  no authority to manufacture lobby IDs or reserved seats.

## Evidence and limits

The Release opt-in desktop app builds with the four existing nullable warnings
in Lobby Admin/Menu Controller. The Party controller fixture passes 100 checks;
License, Hunter, offline rules, live-menu composition, gameplay controllers and
HUD fixtures have passing local runs. Native OpenGL fixtures use actual
documents/pointer input and explicitly labelled synthetic backend data. They
do not establish real account, live scene, model asset, device or server passes.

`node --test tools/edge-check/*.test.ts` exercises restored/new handlers with
isolated Auth/Postgres mocks and forbids live fetches. `npm test --prefix
tools/social-db-check` applies the exact saved migrations to in-memory
PostgreSQL 18.6/PGlite 0.5.10 and currently passes 265 assertions across eleven saved migrations. This checks real
SQL/ACL/RLS behavior against a small compatibility schema, not production EF
schema compatibility, concurrent transactions, Realtime, UDP or deployment.

No backend deployment, real-account mutation, default cutover, commit, push or
new PR is represented by this record. Party travel/seat network integration,
remaining native route parity, full Settings, IME/gamepad/resize/touch/lifecycle
acceptance, exact-head CI and actual desktop/Android evidence remain open.
