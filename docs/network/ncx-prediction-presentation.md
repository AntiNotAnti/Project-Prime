# NCX-06 prediction and confirmation presentation

The missing Mac-only NCX-06 source and captures were unavailable. The user
authorized rebuilding it on Windows. The initial reconstruction integrates pinned main
`73f5ebf62b111de4321183296c51d11166e7e397` at protocol82. Production qualification
uses `beac2cbc596e41d6a58596ce89ee52f0ea8f0bbe`; separately linked test/auditor
source is `63ee6152c24e983ecf848e9b8fe7c33a9a861234`. Historical handoff results
do not qualify this reconstruction.

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

Replay playback retains the actual file or clip source protocol even when a
frozen clip has no metadata. Linear and frozen playback consequently apply the
same accepted-form filter, preventing an unaccepted raw morph from changing a
reconstructed Weavel world.

Modern killcam startup requires a fully fenced marker or a unique exact,
already accepted `PlayerKilled` fact. Match, authority epoch, victim slot,
generation/life and attacker slot/generation must agree. The accepted launch
life is not inferred from nearby ticks, a different event ID or the attacker's
current respawn. Conflicting or missing proof stays closed; raw zero-life
markers remain unchanged. The final-camera identity wait starts on its first
eligible request, after the existing five-second game-over camera, and expires
after thirty frames. Personal-camera deadlines, request/reset boundaries and
legacy unqualified playback remain explicit. An active camera is never
retargeted by a later fact.

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

## Bounded Windows qualification

The [source-qualified manifest](../../tools/nettest/baselines/ncx-prediction-presentation.json)
records exact components, commands, private evidence hashes and retained outcomes.
The tested engine SHA-256 is
`64a9b31fb4896f8ed77f4033e66c9ae1ed221ea6433ac090771aa162d1be811d`.
The six-file tools correction changes four C# fixtures and two Python files;
only `nettest.dll`, its executable and symbols differ in the copied test runtime.
Production and dependencies remain byte-identical.

| Gate | Closed result |
| --- | --- |
| Headless | 210/210 across Off, Shadow and Paid; 52 pure plus 18 asset routes per mode |
| Python | 782 discovered, 776 executed passes, six explicit skips |
| Linked display values/clock | 225176 source-only checks |
| Native rendering | 13/13 arms; bounded focus/FOV controls; 904 decoded PNGs, 221 depth readbacks, 34 gun/charge and 204 body controls |
| Private cache | Six admitted maps, 30 physical output/manifest joins; two warm and two immutable startups |
| Canonical/replay | 47 same-Windows scenarios match pinned main; 18 current replay commands pass |
| Actual recorded killcam | Four unchanged protocol79 and two protocol82 recordings, 301 exact frames each with accepted-life and source-protocol proofs |
| UDP components | All 13 processes exit0; drain and receipt conservation pass; 247 exact paid samples have p95 authority-tick delta0 |
| Separate Weavel audit | Strict corrected base/form audit passes: three complete cycles, 746 independent anchors, 38 prediction joins and no health disagreements |

Samus covers requested60/120/144/240/360/500 and bounded jitter144. The six other
hunters cover500 only. SelectedFOV60/78/120 preserves authored gunFOV78. The
native phase preserves all6532 frozen source files, all three complete runtimes
and private assets. The strict startup pair preserves all3663 private files by
SHA/mtime. The headless clone matches that source at matrix end; no headless
before/after-mtime claim follows from this end comparison.

The original195/207 result retains twelve stale-fixture failures. The corrected
210-route component is separately bound. The old generic79 current-live harness
also remains FAIL because it bypasses archive packet conversion; current82
generic killcam passes2355 checks and the actual79 archives pass strict playback.
Both raw golden runs pass44/47, retaining the same `movement.platform`,
`weapon.missile.splash` and `alt.spire` failures. Their controller exit1 and
`allRequestedCommandsPass=false` remain unchanged; goldens were not regenerated.

Both original UDP campaign summaries remain FAIL. The corrected offline audit
uses860 physical copies of immutable inputs. Weavel base/form passes separately;
the eight-client derived audit still fails the peer7 post-drain graceful-close
deadline. No in-game timeout was observed, and the unresolved close failure
remains an NCX-09 obligation. Timeline observation gaps, unavailable legal-hit
denominators and unsupported capability observations remain incomplete or
unavailable. The paid-sample result measures authority ticks after sufficient
evidence, not input-to-feedback or end-to-end latency.

Historical qualification03–06 outcomes remain linked in the manifest, including
the old96-path canonical drift and original form audit INCOMPLETE with a separate
exact-terminal proof. Canonical source remained unchanged during the fresh07
startup interval. Historical Mac source/captures remain unavailable.

Later main `3aa42f32a3b450a76fd52a0d68a2a99bb5b1a81d` has a clean prospective
merge and reviewed disjoint hooks; its cannon/package changes are outside this
frozen runtime qualification. This slice changes no movement policy, feature
defaults, native payment ownership, wire fields or replay hash schemas. No
NCX-07 visual threshold, whole replay compatibility, physical-input/OS-focus,
restored-framebuffer, achievedFPS/scanout, performance, cross-platform release or
Fruity-superiority claim is made. Rollback retains the matching source/package
and original failure reproduction; `-nohitprediction` remains a diagnostic control.

## Protocol83 integration supplement

Before merge, main advanced to `8f5c63ec88b20078b5f3c5bfdb4987adab5031df`.
It was integrated at `6690300e`; protocol83 adds an exact archive hash to host
allocation requests while retaining protocol82 gameplay and checkpoint14 layouts.
The strict slow-lane auditor explicitly accepts83 at77 bytes and rejects84.
The checkpoint fixture explicitly asserts83. Prediction, health projection and
form reconciliation production sources remain byte-identical to `beac2cbc`.

Fresh combined source `321a5839` builds successfully and passes210/210 routes
across Off/Shadow/Paid. The final Python component passes777 with six skips out
of783 discovered. A Samus144 native smoke passes with53 independently decoded
PNGs and unchanged runtime/assets; both actual82 recordings pass301 exact
killcam frames under83. A separate source review preserves the custom-mode
reset and host allocation hooks. The first new Python positive fixture failure
is retained; completing its mandatory authority fields changes no parser.
The first strict recording pair also retains a failed runtime-closure wrapper:
normal map-catalog output used the portable runtime directory. A second pair
sets an explicit private user-data profile and preserves all359 runtime files.

This supplement has its own source/runtime/evidence hashes. It does not expand
the frozen82 full native matrix, canonical parity or real UDP qualification to
the combined83 package. Original campaign/golden failures remain unchanged.
