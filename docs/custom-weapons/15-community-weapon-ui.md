# CW-15 — player and creator Community UI

The active native Community page now opens a Weapons page. It uses the existing
configured Community endpoint, Hunter License identity owner and durable shared
transfer queue. A separate `weapons:*` intent family preserves existing map intent
indices. Public/Mine search, eight project cards per page, exact revision navigation,
creator/permission details, all four package identities, notes and install status are
visible before download acceptance.

Accept Exact Download queues only the selected immutable archive. Published and
unlisted revisions can be downloaded anonymously; drafts require the verified
original creator. Download verification and local installation are separate actions.
Install reparses the exact completed archive and uses the atomic weapon store. Four
recent weapon jobs expose state/progress/errors, stable selection and pause/cancel/
retry controls. Private jobs are displayed and resumed only for the original account;
account changes cancel stale UI work and discard stale completions. Closing the page
retires UI requests while admitted durable transfer jobs retain their receipts.

The lobby's Review Required Weapon / Accept Download action resolves the active
package offer's content ID and exact archive SHA through the configured service.
It never selects a newer revision as a substitute. The required local store is frozen
to the connection's preparation store. The page permits only the exact required
archive for that acceptance/install flow. After explicit installation, only matching
failed preparation for the current connection/manifest may retry; ordinary server
offers, validation, loaded and readiness acknowledgements still govern joining.
Back declines the download and leaves the package requirement unsatisfied.
The deployment modal and countdown remain hidden while exact weapon preparation
is incomplete, so they cannot swallow the review action during Starting. The session
still reports Starting to lock lobby edits. A header review action also covers late
joins outside the ordinary lobby page. These are presentation changes; gameplay
freeze, preparation validation and match-loading barriers remain authoritative.

Creators publish a saved `.ppweaponproject` containing a license attestation for its
exact archive. The UI verifies the account and current project owner/expected parent,
freezes the archive into private staging, then admits the job through the shared queue's
own account gate. It does not nest the identity gate while the queue verifies it.
Revision notes and published/unlisted/draft visibility are explicit. A separate owner
visibility action publishes or withdraws selected exact bytes; archive/restore manages
the project, and authenticated reports target a selected revision. None can override
moderator hiding. The service's publication opt-in remains authoritative.

Weapon availability never grants inventory. Players use Weapon Studio's separate
pickup-test game or collect an authored pickup in an exact-package custom match.
There is no editor inventory/equip shortcut or silent download/install on joining.

## Evidence and limits

Local compilation receipts are under `artifacts/custom-weapons/CW-15/compile/`.
Release desktop compilation, including the deployment/review presentation fix,
exited 0 with 0 errors and 2 existing warnings in 96.90 seconds on integrated
main `c00164fd8e537a7f1640ba4c150555d7a005a2f5` plus this slice. The receipt retains
log and managed binary SHA-256 values.
**NOT RUN per user instruction:** rendered browsing/publishing/install flow, account
switching/expiry, cancellation/retry/race/fault scenarios, missing/corrupt packages,
reconnect/readiness, hot unload/reinstall, controller/accessibility and map/UI
regressions. Android build/device checks remain deferred.

This MVP uses bounded text/JSON fields and the native UI. It has no asset conversion,
ratings/collections editor or mobile creator UI. Portable replay import still requires
explicit installation of its validated retained package through the existing local
package/Studio flow; its missing-install refusal gives the exact retained file path.
These scope limits and remaining qualification are tracked in [CW-16](16-release-acceptance.md).
