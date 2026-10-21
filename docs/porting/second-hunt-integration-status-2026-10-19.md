# Second Hunt integration status

This ledger follows the user's request to implement the 2026-10-19 plan on
Project Prime, test on Windows, and merge completed local
slices without waiting for review or CI. The plan is the implementation
specification; its quoted suggested prompt is not a separate authority.
The full plan is **not complete**. Remaining engineering is distinguished
from device, source, deployment and performance acceptance below.

Work is isolated from the original checkout's unrelated changes in the
attached `second-hunt-integration` and `character-runtime-integration` worktrees.
The current Windows body slice uses the attached `imported-body-runtime` worktree;
earlier character/import work uses `character-runtime-integration`. Explicit native-fallback
Trooper mechanics use the attached `trooper-wall-cling` worktree. No protected game payloads,
private screenshots, live backend credentials or user saves are committed.
The original seven Hunter values, dedicated combat/world authority,
owner-reported movement and historical replay readers remain the contracts.
The user subsequently requested this Windows machine for testing going forward;
further Android testing remains deferred. Protocol numbers in frozen evidence documents describe those exact builds.
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
| [925](https://github.com/AntiNotAnti/Project-Prime/pull/925) | Offline Trooper Arsenal private three-bolt Blaster, shared UA, native collision/damage/marks and reciprocal native-bot kill; combined desktop/dedicated/GL/menu checks; further Android testing skipped | `939765d825e0a7a8fd2ba19098e114a18cc9c560` |
| [940](https://github.com/AntiNotAnti/Project-Prime/pull/940) | Opt-in normal/Overcharged source bodies in actual offline Samus matches, Hunters loadout draft, Windows physical source identity, full39-joint poses and explicit native fallbacks; real Windows gameplay parity and launcher/import lifecycle | `ff522877ec16e065ec7e0782928f5f2448ff5b70` |
| [944](https://github.com/AntiNotAnti/Project-Prime/pull/944) | Normal imported first-person Power Beam cannon, exact body/cut material generation, native muzzle/effect alignment and authored ViewModel projection; fractional late-aim and Parkour-state native fallback checks | `67fc370548c7fec4b59327e37208bedd1f07fcac` |
| [945](https://github.com/AntiNotAnti/Project-Prime/pull/945) | Complete raw Prime Rev2 Trooper material/TXTR authoring in existing transactions; exact actual v3 upgrade/cancellation and all forty prior files preserved | `648c847d6182d0e6ddc0fd9f1f2739e2209a8231` |
| [948](https://github.com/AntiNotAnti/Project-Prime/pull/948) | Four exact generated Trooper source-program body viewers on the full35-joint rig,113 strict clips, Power C1 and post-slot1; Windows actual all-six switching/retirement and old-file migration preservation | `123df20e3737f672048309310f18c8c7f9428cd9` |
| [950](https://github.com/AntiNotAnti/Project-Prime/pull/950) | Four real local offline Weavel source bodies, grounded full35-joint poses and Hunters persistence; Windows actual native gameplay parity, import/launch lifecycle and protocol82 grapple presentation fallback | `73f5ebf62b111de4321183296c51d11166e7e397` |
| [952](https://github.com/AntiNotAnti/Project-Prime/pull/952) | Four own fixed Trooper cannons and source-program viewers, full35 rig/206-triangle cuts, native Power Beam/muzzle/gameplay parity, v6 migration and combined main951 Windows acceptance | `65d30788c1af5064041dd3f709b8803e77151641` |
| [953](https://github.com/AntiNotAnti/Project-Prime/pull/953) | Independent Overcharged fixed cannon,793-triangle shell cut/full39 rig/cannon-only alpha variant; actual Windows native parity/viewer/launch, v6 migration and Trooper regressions | `5eae078120972911e006696dcb39f65558a1b191` |
| [955](https://github.com/AntiNotAnti/Project-Prime/pull/955) | Normal source ball/full two-joint ready pose/own raw material following native rolling and bombs; v7 migration, Windows actual pause/respawn/retirement/native parity and combined main954 launch/Trooper regressions | `b8b0cd84a4da5a28bad8380af623b53660aef1ef` |
| [961](https://github.com/AntiNotAnti/Project-Prime/pull/961) | Independent Overcharged shell/vent/vein/line/heart ball, original full39-joint bake/8170 triangles, exact48-step heart clock and private material variants; Windows actual native parity/launch, v8 migration and combined main957/959/960 acceptance | `967a6f7a2d71413d9fe488fef1486d0f5fa3f29b` |
| [963](https://github.com/AntiNotAnti/Project-Prime/pull/963) | Normal/Overcharged blue native ball-trail tint with unchanged18-slot history/geometry/timing; Windows actual gameplay/trail/RNG trace parity, native fallback and launcher checks; axial screenshots remain visibility-limited | `734b7544e3679ac9d6e0e6a47aa80c156a04de9b` |
| [965](https://github.com/AntiNotAnti/Project-Prime/pull/965) | Current-picture own boost136 blue tint at native sampled brightness; transient Scene/packet ownership, reference/pool/guard isolation and combined main964 Windows gameplay/effect/trail parity; GL color state without visible-pixel claim | `11c94e1342129784d0d20cbb08801cdcbd02693a` |
| [966](https://github.com/AntiNotAnti/Project-Prime/pull/966) | Independent Alpha/Gamma Rev2 source drone v1 bundles, full33-rig/raw materials/strict banks, unchanged52 Trooper components and edition-aware group import; Windows CPU/transaction qualification, no imported drone renderer/gameplay activation | `5927946086ea4dd0a97b0a935608c487bfc5cda7` |
| [967](https://github.com/AntiNotAnti/Project-Prime/pull/967) | Explicit solo imported Alpha/Gamma Ready owners/full33 rig/two GX groups, private pinned-root loop, Hunters persistence, native attack/status fallback; Windows actual176324 checks/1418-step native trace parity, shader/clock/lifecycle and inspected/deleted captures | `76a8b84d1e6b5333981049e1a2b3818ca2ee4c76` |
| [968](https://github.com/AntiNotAnti/Project-Prime/pull/968) | Source drone unfold/fire/fold driven by native phase and held Ready return; Windows244385 runtime checks, unchanged1418-step gameplay and separate420-step recovery trace, full rig/selected GPU uniforms and inspected/deleted captures | `72ff875c2443d8527f7bafd1a3c8f52ab68512e2` |
| [969](https://github.com/AntiNotAnti/Project-Prime/pull/969) | Source laser29 driven by native next frame with unchanged native beam/gameplay; Windows364571 checks, exact all-four-pattern3083-step native trace, independent134-phase/held-fade gold and26 inspected/deleted views | `4169e311e9869cc3ff659cf5748b94098a4c418e` |

The commit prefixes above are identities, not a claim that unrelated current
main changes were covered by an earlier frozen build. Each evidence document
records its exact source and test scope. Merges follow local verification;
this ledger makes no green-CI or external-review claim.

## Recent merged slices

The [Scout Drone directional flight slice](second-hunt-scout-drone-movement-2026-10-21.md)
adds six full-rig source loops from completed native movement, eight-step choice
holds, source free-phase clocks and held-current transitions. A real camera
handoff gap found during captures is fixed with complete native fallback through
native camera progress0–39. Final Windows640884 runtime checks retain all four
Native/Alpha/Gamma gameplay traces, including a new2793-step directional/moving
attack sequence. All18 captures were inspected/deleted; the final settled Down
pairs pass exact same-camera comparison with HUD detail limits. Source hit,
combat/arsenal admission and broader character engineering remain unfinished.

The [Scout Drone laser runtime slice](second-hunt-scout-drone-laser-2026-10-21.md)
adds the pinned source laser body pose across native windup/sweep/recovery while
retaining the native cached beam and all gameplay. Windows364571 runtime checks
pass with unchanged1418-step/420-step traces and a new3083-step trace across all
four patterns, native cooldown, cloak recovery and near-wall collision. All26
captured views were inspected and individually deleted; no beam/fit change was
needed for the selected views. Source movement/hit and broader character work
remain unfinished.

The [Scout Drone burst runtime slice](second-hunt-scout-drone-burst-2026-10-21.md)
adds source unfold/fire/fold driven by native counters, Snap entries and held
Ready return fades to the same solo Alpha/Gamma owners. Windows qualification
passes244385 checks with unchanged1418-step native trace and a separate420-step
quick-release/cloak/recovery trace. Both complete source material groups and all
33 joints are checked; four captures were inspected and individually deleted.
Native laser/status/morph fallback remains. Source laser, movement and hit
animation and broader character engineering remain unfinished.

The [Scout Drone Ready runtime slice](second-hunt-scout-drone-runtime-2026-10-21.md)
adds separate Alpha/Gamma source owners to explicit solo practice, full33-joint
geometry, both source material groups and an independently checked private Ready
loop. Native burst/laser/status/morph presentation remains the complete fallback;
native movement, planted body, beams, laser and RNG trace match across all three
appearances. Actual Windows runtime176324 checks, native Hunters persistence and
retirement checks pass. Source attack animation and broader character engineering
remain unfinished.

The [Scout Drone source authoring slice](second-hunt-scout-drone-authoring-2026-10-21.md)
adds independent Alpha/Gamma v1 bundles from pinned Rev2 Metroid3/AudioGrp,
full33-joint geometry, raw materials and strict banks. All52 existing Trooper
v6 components and recipe identities remain unchanged. Edition-aware group
requests preserve original Prime's four bodies while Rev2 includes six sources.
That authoring slice qualifies source transport/transactions only. The later
Ready runtime slice above adds Windows GX drawing and solo presentation without
changing the existing private practice gameplay owner.

The [source ball boost slice](second-hunt-source-ball-boost-2026-10-21.md)
adds blue hue at native brightness to each source ball's own boost136 effect
only in the admitted picture. Native particle/material state and alpha remain
unchanged; pause/guards/retirement clear tint, and native furl30 remains native.
Windows actual expanded native/source gameplay and canonical effect traces
match; actual mesh GL color state is checked, with no visible-pixel claim.
Normal core, source transitions/furl, animated guns, drones, abilities and
multiplayer remain engineering.

The [source ball trail slice](second-hunt-source-ball-trail-2026-10-21.md)
adds the shared blue tint to both Echoes forms only when a source ball is
submitted in the current draw. Native18-slot history, fade, texture, geometry
and timing stay unchanged; full native fallback retains yellow. Actual Windows
native/normal/Final gameplay and active trail/RNG traces match across1,415
steps. Normal core, other particle tint, animated guns, drones, abilities and
multiplayer remain engineering.

The [Overcharged composite ball slice](second-hunt-final-ball-runtime-2026-10-21.md)
adds its own shell, vents, red vessels, blue lines and heart:8170 whole triangles
derived from the complete39-joint original fixed pose into a static two-joint
ball rig. Source material variants remain private and separate from the cannon.
The heart follows an exact48-step cycle under the existing60Hz owner; rolling,
position, collision and bombs stay native. Final bundle8 preserves22 prior
components and all normal output. Windows original-source, actual match,
launch and source viewer checks pass. Full source gun animation, normal's
procedural core/trail, imported abilities/drones and multiplayer remain work.

The [normal source ball slice](second-hunt-normal-ball-runtime-2026-10-21.md)
adds the complete2220-triangle/two-joint raw ball and its own source material to
local offline normal Dark Samus. Its fixed ready pose follows native rolling,
position, collision and bombs. Normal bundle7/body5/ball4 preserves22 prior
components; Overcharged retains native ball presentation. Windows original-source,
actual native gameplay parity, pause/respawn/retirement and transactional upgrade
checks pass. Normal's procedural core/trail, Final's composite vein/heart ball,
source gun animation, drones, abilities and multiplayer remain engineering.

The [Overcharged fixed cannon slice](second-hunt-final-cannon-runtime-2026-10-21.md)
adds its own793-triangle shell cut on the complete39-joint rig, original
material groups3/4/5/7, and cannon-only Alpha-pass multiplier0.55. Its receipt
binds the distinct Final geometry and source generation; equal ANIM/basis
does not alias normal's capability. Final bundle7/body6 preserves the raw
body/ball/material files and normal/Trooper recipes. Windows actual native
gameplay parity, muzzle/viewmodel alignment and Trooper regressions pass.
Animated source guns, other weapons, remaining source balls and imported abilities,
drones and multiplayer remain engineering.

The [Trooper fixed cannon slice](second-hunt-trooper-cannon-runtime-2026-10-21.md)
adds each appearance's own206-triangle source cannon, full35-joint fixed pose
and original three material programs to local offline native Power Beam.
Rev2 bundle6/body5 adds own cannon receipts and ViewModel entries while all44
old non-manifest v5 files remain identical. The normal/Overcharged cannon
capabilities remain distinct. Actual Windows native gameplay parity, source
cut/material/lease refusals, old-generation migration and native fallback
checks pass; see the evidence document for combined main951 binary scope.
Animated guns, other weapons, source balls, imported abilities/drones and
multiplayer integration remain engineering.

The [Trooper offline body runtime](second-hunt-trooper-offline-body-runtime-2026-10-21.md)
merged in PR950 at `73f5ebf62b111de4321183296c51d11166e7e397` and
extends Hunters with four local Weavel body appearances, complete35-joint
source poses and existing Scene source programs. Windows actual match checks
pass68,143 protocol82 assertions with all four1,011-step native gameplay traces identical;
77 actual launcher lifecycle checks,27,303 independent CPU pose checks and
native Apply/reload/Discard UI acceptance pass. At that freeze native gun/ball and unsupported
states remain explicit fallbacks. The subsequent fixed cannon slice is above. Cosmetic selection does not enable Trooper
practice abilities; imported guns, drones, full abilities and multiplayer
integration remain engineering. See the evidence document for exact freezes,
numerical bounds, earlier raw failures and platform limits.

The [Trooper source-program viewer](second-hunt-trooper-source-program-preview-2026-10-21.md)
merged in PR948 at `123df20e3737f672048309310f18c8c7f9428cd9` and extends
the same importer/loader/Scene owners to all four exact generated bodies,
complete original material programs and113 strict clips each. Windows owned-source
checks pass184,312 CPU and1,176 GPU assertions, including actual v3/v4 migration,
all-six same-Scene switching in two contexts and exact GPU retirement. The same
final core passes the77,124-assertion actual cannon regression. This is a viewer
slice; its later offline body extension is recorded above. Source first-person
weapons and extra gameplay integration remain engineering. Protocol81 is inherited from pinned main PR946;
the character slice adds no wire/replay fields. Android testing stays deferred.

The [Trooper material authoring slice](second-hunt-trooper-material-authoring-2026-10-20.md)
merged in PR945 at `648c847d6182d0e6ddc0fd9f1f2739e2209a8231` and
adds complete raw material/TXTR archives to all four Prime Rev 2 appearances
through the existing transaction. The actual Windows migration probe passes
182,729 assertions and preserves all forty preexisting component files exactly;
both Echoes material archives remain byte-identical. This is authoring transport:
Trooper rendering and imported actors remain work. Further Android testing is deferred.

The [normal first-person cannon](second-hunt-normal-cannon-runtime-2026-10-20.md)
merged in PR944 at `67fc370548c7fec4b59327e37208bedd1f07fcac`. The merged tree
exactly matches qualified head `f8ea46dad`; production freeze `0eacfd135`
passed desktop/dedicated Release, 77,124 actual cannon assertions (including
675-step three-way native gameplay parity and 990 fractional pictures), 54
real launcher lifecycle assertions and relevant native UI/GPU/rig/replay/
network/custom-weapon regressions. Protocol80 is inherited from concurrent
Parkour main; this slice adds no wire/replay fields. Other weapons, source
gun animation, Overcharged cannon, ball and Trooper integration remain work.

The [offline body runtime](second-hunt-offline-body-runtime-2026-10-20.md)
merged in PR940 at `ff522877ec16e065ec7e0782928f5f2448ff5b70`.
The merged tree exactly matches qualified head `fdcf35ace`; only evidence prose
changed after combined production/test freeze `043e12fd7`. Windows desktop and
dedicated builds,16,480 actual-match assertions with exact native gameplay parity,
54 actual launcher lifecycle assertions, source/rig/GPU/native-Hunters/importer,
36,965 custom-weapon contracts and relevant replay/network/timing checks pass.
This is a local offline body slice with native gun/ball/unsupported-state
presentation, not completed characters or the full plan. Android remains deferred.

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
That frozen evidence qualifies a macOS private viewer. The later
[offline body runtime](second-hunt-offline-body-runtime-2026-10-20.md) adds normal
and Overcharged source bodies to actual local Samus matches, selected through
Hunters and verified on Windows x64, with complete native fallback for unsupported
presentation. The later Trooper body cosmetics and normal source cannon are
recorded above. Android source drawing, remaining source guns/ball and full
character selection/network/replay integration remain unfinished. Normal clock
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
damage/kill unqualified in that earlier freeze. The subsequent explicit
[Arsenal practice](second-hunt-pirate-blaster-arsenal-practice-2026-10-20.md) adds
private three-bolt Blaster/shared UA, native damage and240-step marks. Its exact
desktop Scene/GL/menu and dedicated checks pass, including reciprocal actual native-bot damage
and a Blaster kill with untouched native AI. The later Trooper body cosmetics
do not combine with these practice abilities. Animated Trooper guns,
ability presentation, online/replay and full character integration
remain unfinished. Android package checks had finished before the user requested
skipping further Android testing. Later main PR921/923 integration received desktop-only requalification; its Android dependency/runtime acceptance remains unqualified.

## Plan coverage and remaining work

| Phase | Implemented coverage | Engineering still required | Acceptance still required |
| --- | --- | --- | --- |
| SH-00 | Frozen11-commit/641-path audit,21 source witnesses, ownership and protocol/replay ledger | Maintain ledger for later slices | No inherited upstream acceptance claim |
| SH-01 | Auto/Touch/AimOnly/Off; pad identity/handoff; Direct default; Classic opt-in hold/smoothing/reset | No known missing code in the shipped input slices | Physical Android touch/controller switching, combat and latency/feel |
| SH-02 | Direct weapon-wheel selection opt-in, shared sprite geometry, canonical weapon checks and input ownership | Device-found defects if any | Actual selectable/highlighted Android game and no combat/menu/replay regression |
| SH-03 | Immutable local generations; bounded receipts/hashes; atomic publication and retained old assets; cancellation, leases, batch partial-success reporting; existing Check/Update selected/all/Remove flow; complete Dark Samus body/ball generations with component versions/status and repair reuse; Echoes full original ANIM banks, raw CINF and deterministic source GLBs/provenance share one transaction; Rev2 Trooper recipe6/body5 adds own fixed cannon receipts and ViewModel entries while retaining complete142-leaf ANIM/CINF/graph/source-GLB/material authoring and adds exact generated Weavel body manifests, three-program/seven-TXTR source previews and113 strict clips under the explicit35-bone/root3/parent2 contract; normal recipe6/body5 retains a fixed cannon recipe and Body/Cannon diagnostic viewer, while Final recipe7/body6 adds its own strict body/cannon manifest,793-triangle trimmed source cut and Alpha-pass shell variant; both bodies now require complete raw material/TXTR archives (16 programs,20 unique textures) in the same transaction; bounded Android selected-document access, captured app-writable root and view/Activity/provider ownership with actual emulator import/recovery (see [access evidence](second-hunt-android-source-access-2026-10-20.md)) | Remaining skin/gun/material inventory beyond qualified Alpha/Gamma drone source bundles; coalesced waiter semantics | Physical/cloud/SD and revoked/low-space storage cases, broader Activity/process-death/crash acceptance, cross-platform component parity, Android runtime preview and full game/Studio import interaction (further Android testing currently skipped by user) |
| SH-04 | Six local collision previews and private/source identity fences; actual source geometry audit; bounded TXTR decoder and six private texture banks verified against all390 owned entries; immutable material snapshots and shared upload infrastructure; bounded material parser and paired source-coordinate transport through final emission/cache/model ownership; all six owned arena UV streams verified | Complete-arena import recipe; full-precision dual-UV shared-renderer translation, TEV/base-lightmap semantics and complete material tiers; per-arena turret/kinetic/world mechanics; bots and authoritative multiplayer feasibility | All six actual local playtests/physics; supported GPU/Android fidelity; explicit server-content gate before online support; bank infrastructure does not qualify complete arenas |
| SH-05 | Existing per-seat private visual/source-rig owners preserved; six source authoring appearances including distinct Overcharged body and related body/ball bundles; full Echoes and Rev2 Trooper ANIM/CINF authoring imports plus independent Alpha/Gamma33-joint source drone bundles (authoring only), deterministic source geometry, all four Rev2 Trooper animated source-program viewers (12 programs,23 unique textures,113 strict clips each), real offline Weavel body cosmetics with full35-joint source poses and grounded transitions, and normal/Overcharged animated bodies with complete16-program source-material preview plus normal fixed source-cannon diagnostic preview; opt-in Hunters selection and real offline Samus body runtime with full39-joint source poses/materials, Windows physical identity and complete native fallback; normal, Overcharged and four Trooper fixed first-person Power Beam source cannons with exact cuts/materials/native muzzle alignment; normal raw ball and independent Overcharged shell/vent/vein/heart ball following native rolling/collision/bombs, with shared blue native trail and current-picture boost tint; complete raw normal/Overcharged body material/TXTR authoring (16 programs,20 unique textures) with actual Android upgrade/repeat/removal (see [body scope](second-hunt-generated-character-preview-2026-10-20.md) and [cannon scope](second-hunt-generated-cannon-preview-2026-10-20.md)) | Remaining source GX parts and broader supported-platform rendering beyond the Windows offline body slice; complete source gun animation and other weapons; normal procedural core and supported source transitions/furl, remaining alternate forms and supported transitions; Complete imported Blaster/affinity and drone presentation/geometry beyond qualified private offline native-fallback Arsenal/primary/laser/native-bot practice; wall-cling online/replay and imported presentation beyond the explicit native-Weavel offline practice modes; capability/selection/replay/authority integration | Mixed-seat actual media/GPU, complete transitions and combat/replay/respawn; no playable-extra-character claim yet |
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
