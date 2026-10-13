# Half-turret nullable flow correction

Base: `91b1c0c6c44e8c90effd250729709f791a84765e` (pinned main).
Branch: `codex/consolidation-turret-invariants`. No runtime dependencies on the
other consolidation PRs; cumulative validation should include this source delta.
SDK: `/Users/jarrett/.dotnet/dotnet` 10.0.401, runtime 10.0.12, macOS arm64.

`ScenePlayerRegistry.Create` initializes the owned turret for every created
player. Normal lifecycle never clears that field: turret death clears health and
the active flag but retains the object. Before player creation the field can be
null; replay field restoration can also technically install null links. These
changes therefore capture the object at each use rather than suppressing warnings
or claiming all invalid construction states are impossible.

- Damage split captures the owned turret with an explicit invariant exception.
  The existing branch, odd-damage rounding, player health floor, bot accounting,
  payment and damage order are preserved. All turret mutations use that local.
- Rescued burn captures the turret with an explicit invariant exception at the
  burn dereference. It deliberately adds no active/health guard: a hit that kills
  the turret still follows the existing burn-after-death behavior. Missing turret
  ownership fails clearly instead of quietly paying body damage or burning body.
- Lag history uses the existing Hunter/flag/positive-health conditions with one
  property-pattern capture. The existing initial false active flag remains;
  matching records set it true and copy that same turret's position.

No nullable suppressions, schemas, protocol, replay contracts, rules, scheduling,
new tests, audio/GL device use or settings changes were introduced.

## Validation

Release client/product compilation passes with **27 remaining warnings**; the
nettest build adds one existing duplicate-using warning (**28 total**). Release
server compilation passes with **19 remaining warnings**. All three targeted
CS8602 diagnostics are absent. Pinned main still has an unrelated ArtifactEntity
CS8602; this PR does not claim a warning-free build. Mechanics incremental build
passes with zero warnings/errors.

The first client/server build attempt exposed a local-variable name collision
with an existing pattern variable; it was corrected before final validation.
Those failed logs and one interrupted duplicate nettest build are retained.

Existing test suite results (all use isolated user data; legal AMHE1 assets only
where required; no native audio or GL device):

| Check | Result |
|---|---|
| Combat reconciliation | PASS, 50 assertions |
| Shadow lag timing/geometry | PASS, 2,352 profiles |
| Replay form history | PASS, 43 assertions |
| Form protocol | PASS, 31 assertions |
| Turret outcome codec | PASS, 245 assertions |
| Native turret outcome | PASS, 25 assertions |
| Asset form lifecycle | PASS, 59 assertions |
| Combat arbitration | PASS, 94 assertions |
| Continuous combat | PASS, 667 assertions |
| Quiescent turret / blocked escape | PASS, 655 / 1,032 checks |
| Seven hunters, Vanilla/Balanced/Enhanced mechanics | PASS, 406 assertions |
| Replay format | PASS, 3,034 checks |
| Turret event suite | FAIL, same pinned-baseline zero-source-life admission assertion |
| Full combat scene | FAIL, same pinned-baseline dead-held-fire Magmaul assertion |

The two failures were repeated with the unchanged freshly built nettest harness
(`tools/nettest` sources match pinned main) and its dependencies, substituting
only the immutable baseline product DLL:
`engineering-baseline/tools/native-render-check/bin/Release/net10.0/ProjectPrime.dll`,
SHA256 `22c7eb0fb0e59c7e40fa656c4bda1c20a58c42b5628e3fdd5f47fb79fb6ff0b2`.
No baseline product was rebuilt or modified. Turret events fail in
`RetiredTurretClaims` at `Offer(zeroSourceLife: true)` with “exact authored offer
enters authority native turret queue”; full combat fails at
`DeadHeldFireDoesNotSpawnGhostShot/Magmaul`, health zero and fired count 0→0.
Both remain separate pre-existing fixture/source investigations, not passing
suite claims. The assertions were not weakened.

Raw artifacts remain local at `/tmp/consolidation-turret-invariants`, including
`checks-receipt.json`, `baseline-receipt.json`, individual logs, and
`validation-receipt.json` with source/DLL/log hashes and exact commands. Build
logs use `/tmp/consolidation-turret-invariants-*-build.log`. The build commands
use `--artifacts-path` with separate client/server paths, `-c Release -m:1
-nr:false`; the final product/nettest builds also use `--no-restore`.
`PROJECT_PRIME_USER_DATA` points to that artifact directory's `userdata` folder,
which contains only a copy of the original paths.txt for asset selection.
Physical platforms, rendered output and malformed replay ownership acceptance
are outside this small correction's validation scope.
