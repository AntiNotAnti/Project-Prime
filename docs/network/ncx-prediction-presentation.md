# NCX-06 prediction and confirmation presentation

The missing Mac-only NCX-06 source was not available in GitHub or this checkout.
The user authorized rebuilding it on 2026-10-20. This implementation starts from
main `10297d8cdde3d9944a634aea16e36c8c5001b7e8` and integrates the deliberate
main pin `66795c48790c774b17e5773e98fcd200b3642d32` (protocol79), including its
shader portability and opt-in body rendering changes. Historical handoff
results and unavailable captures do not qualify this reconstruction.

## Behavior

The reproduced race was canonical99 → predicted79 → snapshot79 → projected59
→ exact ACK79. `PredictionHealthProjection` now computes bounded possible
overlap before subtracting outstanding remote prediction damage. It freezes
canonical body/form health and their independent event cursors, launch attacker
generation and raw weapon identity when the prediction is created.

Each of the four unique compact snapshot events supplies its compatible damage
amount at most once across the 24 pending predictions. An event must be newer
than that prediction's frozen cursor and no newer than the actual health sample.
Complete unrelated attacker/generation/weapon history supplies no overlap.
Missing or conflicting history is explicitly ambiguous: possible overlap is
bounded by the otherwise unexplained net health decrease since each frozen
canonical anchor, allocating newer, narrower missing intervals first. Healing
can hide a paid event's net decrease; such ambiguous samples can still need a
later presentation correction. Compact history cannot prove which shot paid.

Only existing exact terminal outcomes retire the named prediction or outgoing
receipt. Accepted event exclusions have separate bounded128 retention, surviving
feedback consumption, prediction eviction and late validated outcomes. They
reset with the victim lifecycle and do not expire merely because an old ACK
arrived120 frames ago: new predictions can still be based on stale snapshots.
An accepted event cannot subsidize another pending prediction, including when
its compact history entry is absent. This is presentation arithmetic, not a
second payment ledger.

Canonical zero remains decisive and positive projection clamps to one. Self
damage/death, continuous healing, native payment, remote death, score, kill feed
and killcam retain their existing owners. Weavel body/form samples retain their
independent cursors; body damage is not reused as turret damage. Opponent HUD
meters already use canonical `NetHudHealth`; these tests must not be described
as independent framebuffer proof of eliminated opponent-HUD flicker.

## Reproduction and qualification

`tools/nettest --prediction-health-projection` exercises the bounded pure helper;
`--combat-reconciliation` drives production `NoteHit`/`ApplyVerdicts`, including
the failing snapshot-before-ACK reproduction, consumable feedback, eviction,
late acceptance, stale snapshots, independent form/body samples and healing.
The original red output remains in private qualification artifacts.

The existing native-render-check harness accepts steady requested elapsed
schedules60/120/144/240/360/500 and bounded `jitter144`. Its `--ncx06-matrix`
runner declares thirteen arms (seven Samus schedules, six other hunters at500).
The production fixed60Hz accumulator still owns every step. Existing replay
hash schemas are unchanged; separate exact combat, source input/shot pose and
scene/global RNG witnesses are compared across schedules and unchanged draws.
These requested schedules are not achieved FPS, physical panel scanout, mouse
latency or performance measurements. Synthetic controller isolation is restored
after the fixture and does not qualify physical input devices.

Windows qualification uses private physical game-asset copies and fresh native
bridge inputs. Byte-qualified fixtures/auditor use LF on every checkout; portable
Python tests preserve native path representations and mock POSIX process APIs
explicitly. Tests requiring unavailable Windows symlink privilege are skipped
with a reason. The opt-in drain publisher replaces existing status files
atomically; the reader must retain same-handle freshness and bounded sharing32
recovery without weakening drain/queue predicates.

The core timeline, receipt-conservation and paid-receipt auditors explicitly
accept timeline schemas1 and2. Schema2 adds optional pose/provenance metadata;
these auditors retain the same core identity/clock checks and do not qualify
those extensions. Future or untyped schema values remain incomplete. A prior
real UDP attempt retained a failing audit because its producer emitted schema2
while the old auditor required schema1; its successful process exits and drain
never upgraded that failed campaign.

The explicit `-nativeprojectiononly` mode on `-viewmodelacceptancecheck` requires
real cartridge weapon mesh packets at selected FOV60/78/120, a fixed canonical
tick and unchanged gun projection, while checking the actual world scaling
policy. It needs no replacement pack. Native pose also supports explicit
`-pose-fov`; omitted values preserve existing behavior. Paired isolated-focus
controls require observed synthetic loss/gain; the legacy path is deliberately
ineligible. A first Windows pilot exposed CRLF in the embedded shader literal
breaking its existing LF source-shape boundary; pinning `Shaders.cs` to LF fixes
checkout portability without changing logical shader code. The failed pilot is
retained and does not count as rendered acceptance.

Acceptance is INCOMPLETE until the final source-qualified baseline is published.
No NCX-07 visual threshold, broad resilience, cross-platform release or Fruity
superiority is claimed. Previously unavailable historical raw artifacts remain
unavailable. No feature defaults, wire fields, movement policies or replay hash
contracts changed. Existing `-nohitprediction` remains the diagnostic control;
rollback uses the matching source/package and the original failure reproduction.
