# Second Hunt integration status

This ledger follows the user's request to implement the 2026-10-19 plan on
Project Prime, test on Windows with Mac fallback, and merge completed local
slices without waiting for review or CI. The plan is the implementation
specification; its quoted suggested prompt is not a separate authority.
The full plan is **not complete**. Remaining engineering is distinguished
from device, source, deployment and performance acceptance below.

Work is isolated from the original checkout's unrelated changes in the
attached `second-hunt-integration` worktree. No protected game payloads,
private screenshots, live backend credentials or user saves are committed.
The original seven Hunter values, dedicated combat/world authority,
owner-reported movement and historical replay readers remain the contracts.
Protocol numbers in frozen evidence documents describe those exact builds.
Pause-map verification uses protocol 64 and body/ball verification uses 65;
current main advanced to 67 through concurrent custom-weapon work in PR819.
Our slices add no wire change.

## Completed and merged slices

| PR | Change | Merge commit |
| --- | --- | --- |
| [790](https://github.com/AntiNotAnti/Project-Prime/pull/790) | SH-00 frozen upstream/path/source ownership audit and SH-09 feasibility | `36a87be6b` |
| [793](https://github.com/AntiNotAnti/Project-Prime/pull/793) | Touch modes, controller handoff and opt-in direct weapon selection | `c58974850` |
| [800](https://github.com/AntiNotAnti/Project-Prime/pull/800) | Paired performance evidence/provenance harness and comparator | `8413cd643` |
| [802](https://github.com/AntiNotAnti/Project-Prime/pull/802) | Immutable Echoes import generations, receipts, cancellation and private-source fences | `9a6faaa589bf3b82a1ce01664f5883fa5bb4349d` |
| [804](https://github.com/AntiNotAnti/Project-Prime/pull/804) | Opt-in Classic stylus response and existing settings owners | `2e96dd89d` |
| [805](https://github.com/AntiNotAnti/Project-Prime/pull/805) | Correct historical replay fixture byte widths | `a5da2d5b5` |
| [808](https://github.com/AntiNotAnti/Project-Prime/pull/808) | Complete private character authoring bundles and Check/Update/Remove lifecycle | `4e1779ba153edb5552a02c467283ebc076b58ef7` |
| [810](https://github.com/AntiNotAnti/Project-Prime/pull/810) | Distinct Overcharged body authoring source and proven zero-tail decoding | `c0087ae61417a47841ff2078d78d8c37ed2316be` |
| [809](https://github.com/AntiNotAnti/Project-Prime/pull/809) | Cancellable, fenced created-lobby client handoff and deferred preferences | `e976574c66926ccd9bf338fe5fd45e6028a133d0` |
| [812](https://github.com/AntiNotAnti/Project-Prime/pull/812) | Optional native campaign map detail, bounded source conversion and exploration privacy | `38dc5b104dd8048c7662597c8b7ced9ee01a70ac` |
| [815](https://github.com/AntiNotAnti/Project-Prime/pull/815) | Complete Dark Samus body/ball authoring generations, part/component status and repair reuse | `a36a110e9ad2b025e4679e18e8d3949266c06f86` |
| [817](https://github.com/AntiNotAnti/Project-Prime/pull/817) | Bounded source CMPR/RGB5A3 conversion with explicit pixel contract and complete owned texture oracle | `d7141505c8f22cb59f07e5ccba70d21b9eb6fd18` |
| [818](https://github.com/AntiNotAnti/Project-Prime/pull/818) | Private source texture banks, immutable model material snapshots and shared material upload ownership | `e239340af921695274d3eb208dc2162484654860` |
| [821](https://github.com/AntiNotAnti/Project-Prime/pull/821) | Bounded material reader and opt-in full-precision source-coordinate reader | `bdf69e2e54306c0e40b4818217016e9a25427b73` |
| [824](https://github.com/AntiNotAnti/Project-Prime/pull/824) | Private model-bound paired-UV companion, final compiler emission and immutable runtime adoption | `da443dc9e86c7bb51402ce0024797379497f1d10` |
| [826](https://github.com/AntiNotAnti/Project-Prime/pull/826) | Bounded detached original credits/results source document | `d948432362e5cfb969ed7573945d14fe22e40e5d` |

The commit prefixes above are identities, not a claim that unrelated current
main changes were covered by an earlier frozen build. Each evidence document
records its exact source and test scope. Merges follow local verification;
this ledger makes no green-CI or external-review claim.

## Recent merged slices

[PR812](https://github.com/AntiNotAnti/Project-Prime/pull/812) merged optional
collision detail inside the existing campaign pause map, with native NAV
fallback, source bounds, cancellation, teardown retirement and live
visited-room checks for geometry and annotations. The preference defaults
OFF. The same PR records the actual Echoes material/UV rendering gap without
claiming that source-material rendering was implemented.

Frozen production `6724e5a6950c87a3e8ecd49e6e7793ecc6648cfc` passed desktop/dedicated Release and Android Release/AOT/signedAPK. Test-only harness `981cace4e959f41ace22a502fc32bf8651d35795` passed1682 actual native-map GL checks against that exact DLL; all production files match byte-for-byte. Relevant source hashes are unchanged and all54 exploratory/superseded/final private map captures were deleted after inspection. Later concurrent protocol65/custom-weapon changes in merged main are outside that frozen production proof. Full details and retained fixture corrections are in the feature evidence document.

PR815 merged complete body/ball authoring generations and component status. Its first frozen owned-source run found repair-generation reuse and Python root-sentinel defects; both are fixed in `a0c24687a`, which passed24 serial commands plus48 Python tests, Desktop/dedicated Release, Android Release/AOT/signedAPK, full owned-source repair/reuse/cancel/remove and all12 previous body-file hashes. The original source hash is unchanged and all derived media was deleted. This is authoring acceptance, with runtime gameplay/material/morph gates still unqualified. The final documentation commit does not change production/test bytes. Full details are in the body/ball evidence document.

[PR818](https://github.com/AntiNotAnti/Project-Prime/pull/818) merged private texture-bank infrastructure and immutable model material snapshots. Frozen source `e1bbd3c2043b28170880c1595882d0f87182e0d3` passed all17 serial checks, including desktop/dedicated Release and Android Release/AOT/signedAPK verification. Source-free results include137 bank,128 material-boundary,191 TXTR,486 map-editor and58 material-tool checks. All390 owned texture entries match the independent pixel oracle and round-trip through six in-memory banks. Actual synthetic Mac GL checks verify independent bindings, exact RGBA readback, queued decoding, captured generation, quality/toggle ownership and teardown through the existing Scene. This qualifies infrastructure, with no complete-arena import or rendering claim. Full results and limits are in the [source texture-bank evidence](second-hunt-source-texture-banks-2026-10-19.md).

[PR824](https://github.com/AntiNotAnti/Project-Prime/pull/824) merged the private
source-coordinate companion. Frozen source `e0bc67d70` passed all 25 serial
commands, desktop/dedicated Release, signed Android Release/AOT, 2,126 new
transport checks and existing material-GPU/editor/replay/source-rig checks.
All six owned sources retain 68,470 triangles and 162,207 candidate UV corners;
per-material UV bits and native-packed-position/UV multisets match exactly.
The independent PR821 compiler witness produces identical legacy five outputs,
manifest and recipe/cache identity. Source hashes remain unchanged and derived
payloads are deleted. A narrow render-only double-normal calculation retains
four finite tiny source triangles without relaxing collision/ordinary geometry
rules. The qualified production/test files match merged main exactly; later
concurrent CW-05b is outside the frozen whole-binary proof. This implements
transport, with no source shader/arena/mechanics activation. Details are in the
[source geometry evidence](second-hunt-source-geometry-companion-2026-10-19.md).

## Plan coverage and remaining work

| Phase | Implemented coverage | Engineering still required | Acceptance still required |
| --- | --- | --- | --- |
| SH-00 | Frozen11-commit/641-path audit,21 source witnesses, ownership and protocol/replay ledger | Maintain ledger for later slices | No inherited upstream acceptance claim |
| SH-01 | Auto/Touch/AimOnly/Off; pad identity/handoff; Direct default; Classic opt-in hold/smoothing/reset | No known missing code in the shipped input slices | Physical Android touch/controller switching, combat and latency/feel |
| SH-02 | Direct weapon-wheel selection opt-in, shared sprite geometry, canonical weapon checks and input ownership | Device-found defects if any | Actual selectable/highlighted Android game and no combat/menu/replay regression |
| SH-03 | Immutable local generations; bounded receipts/hashes; atomic publication and retained old assets; cancellation, leases, batch partial-success reporting; existing Check/Update selected/all/Remove flow; complete Dark Samus body/ball v2 with component versions/status and repair reuse | Skin/gun/material inventory and remaining source parts; Android URI lifecycle; coalesced waiter semantics | Physical storage URI, crash/storage failure and full game/Studio import interaction |
| SH-04 | Six local collision previews and private/source identity fences; actual source geometry audit; bounded TXTR decoder and six private texture banks verified against all390 owned entries; immutable material snapshots and shared upload infrastructure; bounded material parser and paired source-coordinate transport through final emission/cache/model ownership; all six owned arena UV streams verified | Complete-arena import recipe; full-precision dual-UV shared-renderer translation, TEV/base-lightmap semantics and complete material tiers; per-arena turret/kinetic/world mechanics; bots and authoritative multiplayer feasibility | All six actual local playtests/physics; supported GPU/Android fidelity; explicit server-content gate before online support; bank infrastructure does not qualify complete arenas |
| SH-05 | Existing per-seat private visual/source-rig owners preserved; six source authoring appearances including distinct Overcharged body and related body/ball bundles | HD/cannon/runtime animation parts; qualified Overcharged runtime cosmetic; Trooper wall-cling/drone/blaster; capability/selection/replay/authority integration | Mixed-seat actual media/GPU, complete transitions and combat/replay/respawn; no playable-extra-character claim yet |
| SH-06 | Fenced create-lobby handoff, cancellation/retry, preference acceptance and serialized local start ownership | Directory-owned grouped search, mode/region/content query, pre-allocation ballot, telemetry and dedicated handoff | Real deployment/HMAC/account/server-child/load/reconnect; existing leader-admitted party reservations are not grouped search |
| SH-07 | Reproducible binary/hardware/settings/map/workload provenance, strict paired comparator and actual GL functional capture | Controlled experiments and any justified opt-in runtime improvement | Achieved pacing, variance, latency and error tolerances; current draw-CPU evidence is not FPS improvement |
| SH-08 | Existing campaign travel/endings/movie lifetime retained; optional merged native room detail; bounded original credits/results source and animation durations; pure 30 Hz ending timeline | Original cockpit/ship presentation; original animated credits/results and required records; any unimplemented map behavior | Full story/travel/save paths, physical touch/controller, upper-door/connector marker travel and platform captures |
| SH-09 | Source/performance/material/provenance feasibility memo | Future research only; not a dependency of other phases | No Prime4 importer or playable-support claim |
| SH-10 | Repeated frozen Mac source-free/import/UDP/replay checks, Android compile/AOT builds, owned-media probes and scoped actual GL checks | Finish engineering above before full final qualification | Windows access, Linux, physical Android/controller/touch, story/soak/deployment and controlled performance signoff |

Unavailable hardware does not explain away unfinished engineering. Source
rendering, grouped matchmaking, abilities and original frontend presentation
are still implementation work. Unverified runtime features remain OFF or
unregistered; commercial source content remains local/offline where required.

## Verification environment and evidence

Windows SSH authentication was attempted and failed, including the existing
connection path. Testing used the user-authorized Mac fallback with SDK10.0.401,
Apple M4 Pro/OpenGL2.1 Metal91.7 for scoped GL checks, and private verified
Temurin21 plus Android SDK36 for Release/AOT builds. No physical Android device
was available. A compile does not prove physical input, GPU or game acceptance.

Owned Echoes RVZ SHA256 remains
`ba8996256d005749dd6b7ff29ea30e5998451a3741ec87494a6217b01b703529`.
Actual source probes verify pinned archives, keep source read-only, isolate
derived output, and remove media after checking it. The normal Dark Samus body
files and generation remained bit-identical through the Overcharged slice.

Detailed records live in the adjacent audit, touch, import, character-bundle,
Overcharged, created-lobby and pause-map evidence documents. Local ignored
`artifacts/sh-integration` receipts retain hashes, counts and logs, never
protected payloads. The material gap memo records347 actual materials,
390 texture entries and109303 UV corners; that is research evidence for
correct implementation scope, not a rendered-material completion result.

PR817 qualified the exact protocol66 source at `32dffe174` with Desktop/dedicated Release, Android Release/AOT/signedAPK,191 synthetic TXTR checks and all390 owned textures against an independent per-pixel oracle. PR818's merged bank/material infrastructure preserves this explicit pixel contract; neither decoder nor bank evidence activates complete Echoes source-world rendering.
