# Second Hunt integration status

This ledger follows the user's request to implement the 2026-10-19 plan on
Project Prime, test on Windows with Mac fallback, and merge completed local
slices without waiting for review or CI. The plan is the implementation
specification; its quoted suggested prompt is not a separate authority.
The full plan is **not complete**. Remaining engineering is distinguished
from device, source, deployment and performance acceptance below.

Work is isolated from the original checkout's unrelated changes in the
attached `second-hunt-integration` and `character-runtime-integration` worktrees.
Character/import work uses `character-runtime-integration`; explicit native-fallback
Trooper mechanics use the attached `trooper-wall-cling` worktree. No protected game payloads,
private screenshots, live backend credentials or user saves are committed.
The original seven Hunter values, dedicated combat/world authority,
owner-reported movement and historical replay readers remain the contracts.
Protocol numbers in frozen evidence documents describe those exact builds.
Pause-map verification uses protocol 64 and body/ball verification uses 65;
the current observed source-access baseline includes main through PR863 /
protocol 73 at frozen `d098819dc`, with Android-only follow-up `78aa831ee`.
Those frozen identities do not qualify later main changes. Our slices add no wire change.

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
| [828](https://github.com/AntiNotAnti/Project-Prime/pull/828) | Bounded animation duration metadata and pure original ending timeline | `6366ce762a692a3b2bcf0bf61e3920c095f15b78` |
| [830](https://github.com/AntiNotAnti/Project-Prime/pull/830) | Optional fixed-step campaign timing and immutable ending facts | `1c8ea00a0b3ae779638ecba50a78b9696438e038` |
| [832](https://github.com/AntiNotAnti/Project-Prime/pull/832) | Bounded credits models, exact animation binding and CPU decoder ownership | `e24be8ae318627fa1c86f8fc2a96704ddd563759` |
| [833](https://github.com/AntiNotAnti/Project-Prime/pull/833) | Detached original backgrounds/font metrics and bounded CPU glyph/color layout | `3dd5a0b833e67e43d840997c6a30b475fad5e017` |
| [835](https://github.com/AntiNotAnti/Project-Prime/pull/835) | Private CPU widget animation and detached flat geometry | `32977c43e3de5cffa28532beb78fd029ac32259d` |
| [837](https://github.com/AntiNotAnti/Project-Prime/pull/837) | Bounded aggregate credits catalog, immutable result binding and CPU frame ownership | `2a1ce057936d086d33cb2318966bbc52615bec78` |
| [840](https://github.com/AntiNotAnti/Project-Prime/pull/840) | Optional bounded native canvas, explicit texture policy and retained draw-frame ownership | `6e2287e860fcb31b2231093a0c3807261ef8d820` |
| [842](https://github.com/AntiNotAnti/Project-Prime/pull/842) | Native Home rule controls, native/managed mode cache restoration and stable keyboard focus | `0ccee5caf6a43f4d3db41cf5b83843a4ecf533e6` |
| [850](https://github.com/AntiNotAnti/Project-Prime/pull/850) | Complete transactional Echoes ANIM/CINF banks and existing private source-rig playback handoff | `a9f4bbcd404c232f62a46bdc154afe3fdc12769a` |
| [856](https://github.com/AntiNotAnti/Project-Prime/pull/856) | Deterministic Echoes source GLBs/provenance and read-only normal animated diagnostic preview | `81a3462e7d16220b303d78f3af7ab2064cbc089c` |
| [859](https://github.com/AntiNotAnti/Project-Prime/pull/859) | Generated normal Echoes cannon recipe, fixed source cut and read-only Body/Cannon preview | `cafa432247d99025231a0130e51c1f8ba2408331` |
| [865](https://github.com/AntiNotAnti/Project-Prime/pull/865) | Bounded Android owned-document access, character import controls and actual emulator interruption/recovery | `c1c5f6641fa94bc92cbb731f734bbe446480ac25` |
| [873](https://github.com/AntiNotAnti/Project-Prime/pull/873) | Separately pinned Prime USA Rev 2 Trooper authoring imports and actual Android four-appearance group/reimport/removal | `eb852689072f2b35f1f70769759e11371a543879` |
| [878](https://github.com/AntiNotAnti/Project-Prime/pull/878) | Full Prime Rev 2 Trooper35-bone authoring rigs,142-leaf ANIM banks and generated source GLBs; actual Android v2-to-v3 upgrade/repeat/removal | `2ed2da6862ee63761e4cae84f7320bd0933fc63f` |
| [884](https://github.com/AntiNotAnti/Project-Prime/pull/884) | Complete raw normal/Overcharged body material and TXTR authoring, bounded immutable codec, actual Android upgrade/repeat/removal and Windows qualification | `8f3cd64bb7f2f3d6bc5463417606482f4a19f62b` |
| [903](https://github.com/AntiNotAnti/Project-Prime/pull/903) | Explicit offline native-Weavel wall-cling practice with actual owner/arena/native-UI and Android packaging checks | `db131b9fc8353cfc6315e2b3fb206a11f5ef372a` |
| [901](https://github.com/AntiNotAnti/Project-Prime/pull/901) | Complete normal/Overcharged private body source-program viewer, exact source clocks/ownership, Final manifest migration and actual Android SAF upgrade/repeat/removal | `4a8719384dc24e4d7af7d147f469f9a7f642ef77` |
| [911](https://github.com/AntiNotAnti/Project-Prime/pull/911) | Explicit offline Scout Drone primary/hover practice, native pool/body ownership, real Scene/OpenGL/menu checks and both Android Release packages | `7bc52d5b7e217a8a52c057bdf80738a0309b3f69` |
| [916](https://github.com/AntiNotAnti/Project-Prime/pull/916) | Offline native-fallback Scout Drone laser and distinct0–7 native-bot combat route; exact Scene/GL/UI and default/optional Android Release qualification | `67bface0f2f360f70283d849633d141fa247f675` |

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

The [body source-program viewer](second-hunt-character-source-program-preview-2026-10-20.md)
connects all seven normal and nine Overcharged programs to the existing Scene,
with private generated-profile admission, separate material time, complete
source texture upload, rollback/state restoration and Final cannon refusal.
It remains a macOS private viewer; playable actors, Android source drawing,
Imported Trooper actors, remaining abilities and selection/network/replay are
unfinished. Normal clock
transport is proved, but visible normal material animation is not established
in the owned views. Exact source/test identities and retained setup failures
are in that evidence document.

The [explicit offline wall-cling practice](second-hunt-trooper-wall-cling-practice-2026-10-20.md)
connects the dormant mechanics to an immutable local launch choice using the
actual native player/collision owners. Mac owner, plan, initialized arena and
native UI checks pass; default/optional Android packages compile, sign and pass
AOT/native-library verification. Windows final results remain unknown after SSH
failure. This uses native Weavel presentation and weapons. A subsequent explicit
[Scout Drone primary practice](second-hunt-scout-drone-primary-practice-2026-10-20.md)
adds hover, vulnerable native split body and timed twin bursts with native fallback
meshes/projectiles. Actual Scene and GL checks pass at the recorded combined
freeze. The subsequent explicit [laser and native bot combat practice](second-hunt-scout-drone-laser-combat-2026-10-20.md)
adds the donor laser schedule through native damage and a distinct0–7 native-bot
launcher route. Actual Scene/GL/menu checks and both Android Release packages
pass at its recorded freeze; native bot accepted fire is established, with bot
damage/kill still unqualified. Blaster, imported Trooper actors, online/replay and
physical Android gameplay remain.

## Plan coverage and remaining work

| Phase | Implemented coverage | Engineering still required | Acceptance still required |
| --- | --- | --- | --- |
| SH-00 | Frozen11-commit/641-path audit,21 source witnesses, ownership and protocol/replay ledger | Maintain ledger for later slices | No inherited upstream acceptance claim |
| SH-01 | Auto/Touch/AimOnly/Off; pad identity/handoff; Direct default; Classic opt-in hold/smoothing/reset | No known missing code in the shipped input slices | Physical Android touch/controller switching, combat and latency/feel |
| SH-02 | Direct weapon-wheel selection opt-in, shared sprite geometry, canonical weapon checks and input ownership | Device-found defects if any | Actual selectable/highlighted Android game and no combat/menu/replay regression |
| SH-03 | Immutable local generations; bounded receipts/hashes; atomic publication and retained old assets; cancellation, leases, batch partial-success reporting; existing Check/Update selected/all/Remove flow; complete Dark Samus body/ball generations with component versions/status and repair reuse; Echoes full original ANIM banks, raw CINF and deterministic source GLBs/provenance share one transaction; Rev2 Trooper recipe3/body2 requires full142-leaf ANIM/CINF/graph/source-GLB authoring under the explicit35-bone/root3/parent2 contract; normal recipe6/body5 retains a fixed cannon recipe and Body/Cannon diagnostic viewer, while Final recipe6/body5 adds its own strict generated body manifest and private source-program viewer; both bodies now require complete raw material/TXTR archives (16 programs,20 unique textures) in the same transaction; bounded Android selected-document access, captured app-writable root and view/Activity/provider ownership with actual emulator import/recovery (see [access evidence](second-hunt-android-source-access-2026-10-20.md)) | Skin/gun/material inventory and remaining source parts; coalesced waiter semantics | Physical/cloud/SD and revoked/low-space storage cases, broader Activity/process-death/crash acceptance, cross-platform component parity, Android runtime preview and full game/Studio import interaction |
| SH-04 | Six local collision previews and private/source identity fences; actual source geometry audit; bounded TXTR decoder and six private texture banks verified against all390 owned entries; immutable material snapshots and shared upload infrastructure; bounded material parser and paired source-coordinate transport through final emission/cache/model ownership; all six owned arena UV streams verified | Complete-arena import recipe; full-precision dual-UV shared-renderer translation, TEV/base-lightmap semantics and complete material tiers; per-arena turret/kinetic/world mechanics; bots and authoritative multiplayer feasibility | All six actual local playtests/physics; supported GPU/Android fidelity; explicit server-content gate before online support; bank infrastructure does not qualify complete arenas |
| SH-05 | Existing per-seat private visual/source-rig owners preserved; six source authoring appearances including distinct Overcharged body and related body/ball bundles; full Echoes and Rev2 Trooper ANIM/CINF authoring imports, deterministic source geometry, and normal/Overcharged animated bodies with complete16-program source-material preview plus normal fixed source-cannon diagnostic preview; complete raw normal/Overcharged body material/TXTR authoring (16 programs,20 unique textures) with actual Android upgrade/repeat/removal (see [body scope](second-hunt-generated-character-preview-2026-10-20.md) and [cannon scope](second-hunt-generated-cannon-preview-2026-10-20.md)) | Remaining source GX parts and supported-platform rendering beyond the qualified private macOS normal/Overcharged body viewer; complete first-person firing/muzzle presentation and playable animation state/transition parts; qualified Overcharged runtime cosmetic; Trooper blaster/affinity and complete drone presentation/geometry beyond explicit offline native-fallback primary/laser/native-bot practice; wall-cling online/replay and imported presentation beyond the explicit native-Weavel offline practice mode; capability/selection/replay/authority integration | Mixed-seat actual media/GPU, complete transitions and combat/replay/respawn; no playable-extra-character claim yet |
| SH-06 | Fenced create-lobby handoff, cancellation/retry, preference acceptance and serialized local start ownership | Directory-owned grouped search, mode/region/content query, pre-allocation ballot, telemetry and dedicated handoff | Real deployment/HMAC/account/server-child/load/reconnect; existing leader-admitted party reservations are not grouped search |
| SH-07 | Reproducible binary/hardware/settings/map/workload provenance, strict paired comparator and actual GL functional capture | Controlled experiments and any justified opt-in runtime improvement | Achieved pacing, variance, latency and error tolerances; current draw-CPU evidence is not FPS improvement |
| SH-08 | Existing campaign travel/endings/movie lifetime retained; optional merged native room detail; bounded original credits/results source and animation durations; pure 30 Hz ending timeline; bounded model/animation admission and exact-byte CPU decoding; detached native backgrounds/font metrics and bounded glyph layout/color and private CPU widget sampling; aggregate asset admission, immutable captured-result binding and bounded CPU frame composition; optional bounded native canvas geometry/texture transport, sampler policy and GPU/lease retirement; optional AMHE1/English desktop original credits/results activation with bounded cancelable source load, stacked-screen canvas, 30 Hz clock, source OK/application cancellation and existing audio owner; optional cumulative 60 Hz campaign time records and captured result facts | Original cockpit/ship presentation; Android original ending-page owner; widescreen/crossfade fidelity and any remaining original credits/results behavior; any unimplemented map behavior | Full story/travel/save paths, physical touch/controller, upper-door/connector marker travel and platform captures |
| SH-09 | Source/performance/material/provenance feasibility memo | Future research only; not a dependency of other phases | No Prime4 importer or playable-support claim |
| SH-10 | Repeated frozen Mac source-free/import/UDP/replay checks, Android compile/AOT builds, owned-media probes and scoped actual GL checks | Finish engineering above before full final qualification | Linux, physical Android/controller/touch, story/soak/deployment and controlled performance signoff |

Unavailable hardware does not explain away unfinished engineering. Source
rendering, grouped matchmaking, abilities and original frontend presentation
are still implementation work. Unverified runtime features remain OFF or
unregistered; commercial source content remains local/offline where required.

## Verification environment and evidence

Earlier Windows SSH authentication attempts failed, so those earlier slices
used the authorized Mac fallback. Later Windows SSH qualification succeeds on
DESKTOP-94724L4 with private SDK10.0.401; the source-program preview and its
combined-main snapshot each pass all seven Windows commands. Actual owned
source GL probes still use the qualified Mac boundary. Mac testing uses SDK10.0.401,
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

The optional [native canvas transport](second-hunt-native-canvas-2026-10-20.md)
passes39 frozen serial commands at `e7526ef32`, including776 actual GL checks,
173 native/managed draw-list transport checks, both Android native libraries
and default/optional signed Release/AOT packages. This enables the existing
renderer to carry flat source presentation; original credits activation and
lifecycle remain implementation work.
