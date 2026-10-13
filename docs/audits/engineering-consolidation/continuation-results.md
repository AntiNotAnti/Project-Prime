# Engineering consolidation continuation: scoped results and remaining acceptance gates

**Publication snapshot. Scoped local-core review is complete; full acceptance is false.**
Full consolidation acceptance remains **false**. Driver-origin repeated-load
boundedness is an open P0, physical and full media/platform gates remain, and the
final aggregate/individual hosted CI remains pending at the publication snapshot.
Compiled source, binary identities, scoped local checks and staged Android release-native
package receipts have now been supplied.

The refreshed source baseline remains
`91b1c0c6c44e8c90effd250729709f791a84765e`. Environment receipts use SDK
10.0.401/runtime 10.0.12 on macOS 27 ARM64/Apple M4 Pro unless a report explicitly
names another platform/toolchain. This continuation adds later results to the
October 12 plan and October 13 first campaign. The original baseline, architecture,
integration reports and raw failure receipts remain unchanged; their hashes are
pinned in [continuation-phase-dispositions.json](continuation-phase-dispositions.json).
Old statements about missing high-LOH roots, Music loader ownership and later
historical fixtures describe their recorded snapshot. The updates below provide
the later evidence without relabeling those original results.

No PR has been merged. No repository protection, signing, release or account
settings were changed. Fixed 60 Hz simulation, OpenGL/GLES-only game rendering,
protocol 53, server combat authority, validated owner movement, existing queue
policies, generated replay contracts, map/player-data identities and Balanced
Mode remain invariants. No production GC policy, experimental rendering default,
whole UI toolkit replacement or general NetSession rewrite is proposed here.

## Source identity, dependencies and review units

Every listed PR is based on the exact pinned main above except **#505**, whose
actual code base is #493 head
`328343fffdc47cad03e3fe70389ba462a9f4b6f3`. Review/integrate #493 before #505;
#505 is not an independent main diff. The ledger pins every PR head, base branch,
code-base SHA, constituent commit and report hash. The table identifies reviewed
proposal heads, not a claim of final current-head CI completion.

| PR | Concern | Exact proposal head | Code base |
| --- | --- | --- | --- |
| [#488](https://github.com/AntiNotAnti/Project-Prime/pull/488) | Protocol/replay documentation | `ba29091105a7a043644e11355ba1f15b5631b89e` | pinned main |
| [#489](https://github.com/AntiNotAnti/Project-Prime/pull/489) | Baseline and earlier integration | `f85c6c15480f89e27f4ee636ed23d4dbb2736bdb` | pinned main |
| [#490](https://github.com/AntiNotAnti/Project-Prime/pull/490) | Updater staging ownership | `ac914984b14df231f2c7b59351d6265d1566c621` | pinned main |
| [#491](https://github.com/AntiNotAnti/Project-Prime/pull/491) | Finder metadata | `4aa587a5eb1d40df78c9a6e32d671253df8c27d8` | pinned main |
| [#492](https://github.com/AntiNotAnti/Project-Prime/pull/492) | Shader ownership | `0273aa86c44772e728e069ebcd95e04f9c0b2590` | pinned main |
| [#493](https://github.com/AntiNotAnti/Project-Prime/pull/493) | Native attribution | `328343fffdc47cad03e3fe70389ba462a9f4b6f3` | pinned main |
| [#494](https://github.com/AntiNotAnti/Project-Prime/pull/494) | SFX ownership | `c8fa6a1f9689b9dd78acc24e36779309eaace378` | pinned main |
| [#495](https://github.com/AntiNotAnti/Project-Prime/pull/495) | Failed Music startup | `30ac0fc7efed0f5a1d78a9aa8a19e6c2f5205381` | pinned main |
| [#496](https://github.com/AntiNotAnti/Project-Prime/pull/496) | AI/HUD negative evidence | `f17b27a95089ff26fc4b8f77fab4bce900608bc5` | pinned main |
| [#497](https://github.com/AntiNotAnti/Project-Prime/pull/497) | Collision query ownership | `66376f8755a2c1cb302cbfd4d7a4dd6081a16c02` | pinned main |
| [#498](https://github.com/AntiNotAnti/Project-Prime/pull/498) | Live Enhanced retirement | `29987bc32d97c5b29a3a3585a6de2de756824bfc` | pinned main |
| [#499](https://github.com/AntiNotAnti/Project-Prime/pull/499) | Fixture repair | `81ef736f6f8c2c2b77bba72faaabbd6faefe0913` | pinned main |
| [#500](https://github.com/AntiNotAnti/Project-Prime/pull/500) | UI reality/unreachable views | `54dd962d5f9f547730f9317166640d3f59b709c1` | pinned main |
| [#501](https://github.com/AntiNotAnti/Project-Prime/pull/501) | Requested-scene setup | `ed6f2419dbb274762cfba80b538c18fe1a41f02f` | pinned main |
| [#502](https://github.com/AntiNotAnti/Project-Prime/pull/502) | Network/server profiling | `c75daffd142767259ec5f31206aa43b6881c9b19` | pinned main |
| [#503](https://github.com/AntiNotAnti/Project-Prime/pull/503) | Export queue admission | `46c4c0cb8bdc116ece6e7db7cfd3ca8f92c45af5` | pinned main |
| [#504](https://github.com/AntiNotAnti/Project-Prime/pull/504) | Music load/callback ownership | `0abff3ca30a463b9cce26f1f3f13f226bbc510d8` | pinned main |
| [#505](https://github.com/AntiNotAnti/Project-Prime/pull/505) | Context/LOH/replay controls | `80c3758cf1a422dbdadac85b66923b3a0126759c` | #493 `328343ff` |
| [#506](https://github.com/AntiNotAnti/Project-Prime/pull/506) | Android DTO trimmer roots | `8cf4c6eff2ea05eb72f732f941ddce62e10b2cfd` | pinned main |
| [#507](https://github.com/AntiNotAnti/Project-Prime/pull/507) | Null sidecar records | `2fbce7961223f131c24112fc001f058ef247e5a0` | pinned main |
| [#508](https://github.com/AntiNotAnti/Project-Prime/pull/508) | Diagnostic token disclosure | `62ba4c76916b47aea5c88efa2039e39c093b1d90` | pinned main |
| [#509](https://github.com/AntiNotAnti/Project-Prime/pull/509) | Supported UI APIs | `3c5968a36650407dbd6e36dcaf4a8d0a3341020a` | pinned main |
| [#510](https://github.com/AntiNotAnti/Project-Prime/pull/510) | Executed submission attribution | `28e2d24e9d000bba8da0d7e207f3d34ce909e4c5` | pinned main |
| [#511](https://github.com/AntiNotAnti/Project-Prime/pull/511) | Discarded encoder stderr | `5c0c84bbb2274e5db3adcda8b9fe7466b635dbc4` | pinned main |
| [#512](https://github.com/AntiNotAnti/Project-Prime/pull/512) | Bounded stack scratch/image identity | `aa966556e0eed73a434e5dd4faf40a57c7d69e0b` | pinned main |
| [#513](https://github.com/AntiNotAnti/Project-Prime/pull/513) | GC tradeoff characterization | `fec7e8dc0771cf3ce28d1213dec31f204365f914` | pinned main |
| [#514](https://github.com/AntiNotAnti/Project-Prime/pull/514) | Disabled results picker | `7b6b7b0d3d2e13294f16e41e1b65bb3c644aff2a` | pinned main |
| [#515](https://github.com/AntiNotAnti/Project-Prime/pull/515) | Theatre library/file-export producers | `18692b938e5e869b20afddff4878a366027d8db6` | pinned main |

Theatre producer [#515](https://github.com/AntiNotAnti/Project-Prime/pull/515),
head `18692b938e5e869b20afddff4878a366027d8db6`, is a separate main-based
Phase 6/9 review unit; it is published as an unmerged draft with CI pending. It has no code dependency on video-export admission #503. Sidecar #507,
UI #500/#509 and lifecycle fixes are integration/acceptance dependencies, not
required changes to compile that producer boundary.

Native evidence must also name its **measured codebase**. #493/#505 attribution,
#510 draw/visual controls and #513 GC timing used lifecycle validation bundle
`749aa12dc89d41dc83823eafe0929c13b1ebfb99`, incorporating #492/#494/#495,
with immutable client SHA256
`86f075e7c8dd7a06277b4050feedc796680b97a2f259615e2aa81e1faab37328`.
Those publication branches being main-based does not turn their measurements
into tests of the final overnight integration. #504 production loader source
was validated at `6224c17a7ede5b6fdd719add3bee67f7d05324c4`; its final native
fixture is `0762e18b36d4c6bcdf8b11eb9d3a2966c9ebf2fc`, and report head is
`0abff3ca30a463b9cce26f1f3f13f226bbc510d8`. #506's targeted actual-Android
link uses the separately identified #504 Android build, not a full APK from its
own descriptor branch.

The earlier rebuilt feature bundle
`87617f47dd67d4cae77931c43f9dce6af87b3fd4` passes 12 scoped checks. It predates
several continuation slices and cannot certify their final assembly. Final compiled source is `edcf0f1bc6be7cfb0734c42ebaa41ce8b8724ef2`. All 13
client/nettest/tool/native-harness builds and the independent dedicated-server
build pass: **14 builds total**. Client MVID is `88959546-d757-49cd-9826-dd496a2fec7c`; client/nettest
SHA256 are `6c222a9084af3ac2006db87406702a4e64fd31a3dce5d8e1892f06f88337a959` /
`03c5a35ff683bd0491c47b04d3e9a26269fdc7a54cf6c9e39f3cefe8d8696d1e`.
The same source passes 17 gameplay/network, nine device-free tools, eight render/UI
diagnostics, six guards/Python commands and two harness selftests. Immutable
archive fixtures remain unchanged. Actual native Music trials belong to focused
`0762e18b` source and were not rerun on this combined source. Receipt/log/command
hashes are pinned in the machine ledger; final aggregate/individual hosted CI remains pending. Staged Android inventory
passes within the explicit signing/device scope below.

The `codex/consolidation-feature-validation` validation branch was pushed and dispatched aggregate
build run [38372420976](https://github.com/AntiNotAnti/Project-Prime/actions/runs/38372420976)
on exact edcf0f1 source. Push run 38372423024 succeeds; that does not establish
aggregate build completion. No integration PR or merge was created. Independent
PR head checks and the aggregate source workflow must both be reported at their
actual heads. At the supplied **2026-10-13 09:06:37 UTC** publication snapshot,
**23 of 28 focused PRs are green, five pending, zero failed**; the full aggregate
run has **19 of 25 jobs successful**, Android in progress and five Mac jobs queued.
It remains pending. This is not final all-green acceptance. Stable PR checks
links are available from each PR table entry (`/checks`) and the aggregate run
above. Final green receipts remain machine-local/in PR bodies so reporting them
does not trigger a circular documentation-commit/CI cycle.

Final local counts include combat 204, Balanced 347, retirement 81, archive/live
46/20, advanced 130, game modes 2,198, replay 3,034, seven-Hunter archive 1,103,
scene ownership six, biped 51, waitlist 309, lobby 7,744 and transport 1,736 intents
with 128 exact-once controls. Lobby differs from the earlier 7,747 because its
existing 300 ms timed assertion loop is preserved, not weakened. Movement frames
are Samus/Kanden/Spire/Noxus/Trace/Sylux/Weavel **575/607/607/627/617/613/603**, all
zero hard rejections, with the fixture's unsupported mechanics still explicit.
All 20 selectable replay modes each preserve 1,801 gameplay/presentation hashes
and 793 projectile frames; animation restore counts vary by mode and are retained
in the raw receipt. These checks are scoped headless/synthetic observations.

Integration must preserve shared workflow insertions for shader, SFX, Music
startup/loader, replay queues/sidecars, encoder, diagnostics, Theatre and UI
checks. Preserve #497 query leases on #498 surviving archival callers; apply
#499 test-only fixture changes without restoring live Enhanced behavior. Keep
#495 failed-startup cleanup when integrating #504 Music ownership. Renderer
observers in #510/#513 and #492 ownership must coexist without changing normal
policy. Keep #508 redaction when integrating #512 image/diagnostic changes.
#501 must retain the separate upstream #472 power-up hook. No branch retargeting
or broad conflict-driven rewrite is justified.

Overlapping integration fragments retain all independent workflow steps,
regression-tool friend-assembly registrations and #495/#504 startup/load ownership
composition in their existing owners. No additional
implementation or behavioral compensation was introduced by those conflict
resolutions. The frozen compiled SHA and same-source regression receipts remain
the review boundary; focused PRs remain independently reviewable and unmerged.
The binary metadata receipt also pins nettest MVID
`625e5be7-a1cb-4856-9c4c-707283b817a9`, server MVID
`c7021259-5ec6-48f5-a73a-ad95615875a4`, server SHA256
`6d5db8b29503925750ab8d66827584ae64d98814d4a02d5309b4da133415f5bb`
and native harness identity. The frozen local-core integration manifest verifies ten receipt hashes: eight
core receipts covering 14 build exits and all 42 scoped local commands, plus two
Android receipts covering publish/signature/alignment/inventory results.

## Native ownership improvements and open P0

Shader negative control reproduced 10 failures in 14 scenarios. #492 passes
1,680 assertions across 14 scenarios/120 iterations, 120 exact recovery frames, other-owner binding checks
and actual program retirement; the lifecycle bundle has 240 exact across-build
RGBA/depth pairs. Zero owner fields alone are not used as native deletion proof.
#505 adds positive native recognition before unload and unrecognized display-list,
buffer, VAO, program, texture/FBO/RBO names after release, including all five
failure stages. Its bounded shader ledger has fixed observed canonical/world
source hashes; it does not prove identical driver pipeline keys or safe sharing.

#494 reduces acquired SFX buffers from 129 to the 65 actually used, fixes partial
acquisition and Android mixer cleanup, and serializes desktop close readiness.
750 lifecycle assertions plus existing allocation/routing checks and 120-cycle
fake/headless/linked-Android fixtures pass. Matched native audio trials attribute
surviving baseline OpenAL source vectors at **337.437 MiB to zero** in the
lifecycle bundle; all outstanding non-mmap groups containing OpenAL frames fall
**347.285 to 1.181 MiB**. The roughly 295 MiB post-second-GC RSS difference repeats
in forward/reverse audio trials with paired managed states. Deletion versus
serialization/library cleanup was not factorially isolated. These results do
not resolve whole-process graphics residency or physical audible acceptance.

#495 passes four startup failure/ownership checks and disposes 120 fake acquired
engines deterministically. #504 now addresses the separate stale publication,
partial cancellation and callback disposal races: 707 device-free assertions,
10,000 coalesced requests and 120 cycles pass. Two **actual macOS MiniAudio/NCSF
120-cycle trials** pass; the final trial has **3,539 assertions** and requires a
native callback plus advancing decoder stream for every graph, exact PCM/mask/
tempo parity, preserved shared output and complete owned graph/worker/engine/
device drain. SoundFlow 1.4.1 leaves an empty managed master mixer shell with
`Disposed=false`; that observed shell is not classified as live native output.
A permanently blocked decoder/read may retain one bounded worker/resources after
the two-second wait; shutdown avoids racing unsafe disposal. Native allocation
accounting, RSS gain, adjacent cue/custom-map/Android audio and physical
Windows/Linux/Android device-loss acceptance remain unproven.

#505 closes the formerly missing **captured high-LOH root** question within its
capture scope. LOH is **403,048,464 bytes** versus **403,047,288 bytes** of captured
rooted objects at least 85,000 bytes: a 1,176-byte gap, including measured
1,152-byte fragmentation plus 24 bytes. Shared thread-local byte/char array pools
root **397,935,048 bytes**; remaining captured large objects are diagnostic and
current-scene/static metadata arrays. Observed SFX/owner/bank/nonempty payload
weak references are dead. One labeled diagnostic-induced Gen2 is disclosed;
the .NET 10 lossy-buffering warning remains. This accounts for roots, not
allocation callsites/workload contribution, and supports no GC/pool policy change.

The **driver P0 remains open**. Direct malloc-prefix AGX fragment groups survive
120-cycle actual context disposal at **167.783630 MiB**. Another recreated
context with 180 more cycles raises them to **423.113419 MiB**, with
**422.972794 MiB** still outstanding after actual disposal: 600 loads overall.
Window/context and owned GL name release do not establish allocation-group
boundedness. Severe compression/paging makes falling RSS an invalid release
oracle here. Idle stability, unchanged shader source hashes and driver stack
origin do not classify a bounded cache or leak. The final renderer experiments below add negative evidence, not a boundedness
pass; no speculative cache, context-reset or collection patch is supported.

## Rendering and transition evidence: policies retained

#510 partitions executed immediate submissions and inclusive RenderItem setup/
geometry wall scopes without retaining Scene/RenderItem objects or instrumenting
the ordinary disabled path. Five descriptive pilots report median window counts
57 solo, 70.57 one-bot, 390.01 eight-player Off, 413.75 High and 416.81 Ultra.
High-shadow palette/particle/room geometry median window p95 is 0.941/0.664/0.371
ms. These scopes include synchronous driver waits, are inclusive and not additive;
GPU timers are UNVERIFIED. Call counts cannot justify reordering particles,
translucency, stencil or depth-sensitive passes.

Six scoped cross-build visual pairs match world/HUD/repeat PNGs, raw world depth
and available shadow depth across solo, one-bot and eight-player Off/Low/High/Ultra.
The one-bot outline occlusion witness has zero exposed outline pixels with near
depth and 2,154 with far depth in both builds. Capture readbacks are not timing
measurements. Ten unprofiled disabled-seam controls, five per arm in reversed
order, report median FPS **301.43 to 300.76**, and median per-run p99 **6.550 to
6.623 ms**. Agent builds were idle but the untouched user-owned game remained
running; these are ambient regression controls, not a quiet-host speedup claim.
Earlier two-runs-per-arm lifecycle ABBA **406.68 to 404.71 FPS** and p99
**7.3620 to 7.3893 ms** remain a separate, differently configured historical scope.

#513's 20 controls, five per arm, keep fixed 60 Hz simulation without stalls or
dropped steps. Median FPS/p99: keep 301.94/6.438 ms, skip-load 300.22/6.677,
skip-end 302.04/6.503, skip-both 300.29/6.511. Existing load/end collections take
median 15.19/18.88 ms; inclusive stages are 123.31/34.55 ms. Skipping end shortens
its stage to 14.99 ms but leaves a 213.2 MiB managed estimate; keeping the
collection reduces 212.7 to 96.0 MiB and RSS 493.4 to 452.1 MiB. There is no clear
frame-time benefit supporting policy removal. The normal client still executes
both collections and post-EndScene save ordering is preserved.

Original rebind probes retain the previous weak scene even after foreground
rebind; that bundle predates #497 and static collision candidate references are
a source-backed confounder. The latest foreground facades are also intentionally
available for post-teardown saves. The separate final confirmation runtime
`299e3c250f508606ccf23d81ee1c5e81e29a7531` includes #497 `c605f755`, one actual
next-world fixed step and a borrowed/disposed collision query. All four arms
still retain the old weak scene after next load/end; GC root/liveness remains
UNKNOWN. It does not prove a leak, retention chain or full-world bound. Preserve
both receipts and post-EndScene save owners; no further policy patch was made.

A default-off individual-model VBO prototype was also implemented and rejected
at its first exact solo pair: **7,380 world pixels**, **7,369 HUD pixels** and
**16,366 depth values** changed (world maximum channel delta 185; depth maximum
absolute delta 0.083406). All **904** promoted VBO/IBO names retired, but visual
correctness failed. Other arms and FPS benchmarking were NOT_RUN. Prototype
`d90c5488` was fully reverted by `d050f9b1`; private validation source `15baee21`,
failed captures and negative receipt remain recoverable. #510 contains no
submission optimization. #510/#513 final publication heads are pinned above.

Read-only shader reuse eligibility is also negative. Scene variants, the shared
RTT vertex shader and persistent pipeline/outline/editor/sideview owners already
reuse their resources; no equivalent per-frame compile work remains to redo.
A cross-scene cache lacks a proven context/share-group lease, complete sampler/
fog/cosmetic/camera/palette uniform isolation/invalidation, concurrent foreground/
editor/side/failure ownership and final-lease disposal. The nine observed
canonical/world hashes exclude pipeline/outline/UI and driver keys, so equal
observed GLSL does not establish safe pooling. No new cache is implemented.
Any later candidate needs exact cross-role color/depth parity, failure rollback,
final-lease/context-recreation checks, increasing-load native allocation-group
plateau and transition/frame-tail evidence before adding permanent complexity.

## Full-plan dispositions

| Phase | Scoped result / justified disposition | Remaining acceptance gate |
| --- | --- | --- |
| 0 baseline | #488/#489 source, owner/platform/dependency inventory; #512 warning/artifact/source evaluation | Final aggregate/individual CI receipt; complete representative workload and physical matrix; protection is recommendation only |
| 1 memory/lifecycle | #492/#494/#495/#504 proven ownership fixes; #493/#505 source/root attribution; #513 measured GC controls | Growing driver groups are open P0; unknown old-scene roots/liveness; physical audio/EGL/shared-output/device-loss; no production GC change |
| 2 obsolete/UI | #498 live retirement with archival reader boundary; #500 actual route matrix/two dead views; #491 metadata, #509 APIs, #514 dead picker | Recorded rendered seven-Hunter archive/ability/audio; physical UI parity; #424 unready migration; no compatibility-type deletion |
| 3 GL/GLES | #510 actual ownership/draw/visual evidence; existing 25 policies preserved; #513 no-policy-change result | No dominant-category optimization proven; GPU/quiet timing/other-GPU/Android and full visual parity; depth/procedural experiments stay opt-in |
| 4 ownership | #497 nested collision leases/corrected 8,192 reuse; #501 requested-scene setup; #499 test setup repair | Final combined scoped checks PASS; final CI; above-budget allocation limits; physical movement/visibility; broader seams separately gated |
| 5 network/server | #502 profiles existing direct decode and bounded delivery; #512 bounded scratch | Full application-level 2/4/8 peer impairment/JIP/reconnect/rotation/claims; upstream stacked work; no unproven iterator/session rewrite |
| 6 replay/Studio/editor | #503 export admission; #507 sidecar null rejection; #511 fixed stderr drain; #505 actual headless seek ownership; Theatre producer candidate | Real rendering/encoding/export audio/video/HD identity/cancel, UI/editor/playtest memory; remaining producer/process bounds |
| 7 AI/HUD/input | #496 actual two-map frequency/JIT/negative evidence; existing bot/input/pacing checks; #500/#509/#514 device-free UI checks | Other-runtime/AOT/full bot costs; physical latency/haptics/touch/thermal; preserve event-only logic and Android sleep/spin |
| 8 builds/CI | #512 warning/image identity, #506 roots, #509/#514 warnings; #500 signed APK; platform/packaging guards retained | Combined warnings and staged Android inventory verified; hosted CI; physical startup/page-size/signing/update; no size/native-pruning/assembly rewrite claim |
| 9 robustness | #490 rejected staging/cancel; #507 null sidecars; #508 token header; #511 stream drain; bounded export/Theatre admission | Broad malformed ingress/privacy/expansion budgets; published-package compatibility; timed large-entry cancel and physical upgrade |
| 10 readiness | Earlier combined 12-check source receipt and focused unmerged proposals documented | Final aggregate/individual hosted CI plus unresolved P0 and physical/full media/gameplay gates: acceptance false |

Evidence-deferred candidates have concrete reasons. #496's actual ARM64 FullOpts
navigation array allocates zero bytes over five 100,000-call rounds; two maps at
1/4/8 bots run 1,800 steps each with no step failures. Respawn selection is rare
and event-driven; HUD LINQ runs on award events. Other runtimes/AOT are not inferred.
#502's existing direct decode already avoids canonical copies and allocates zero;
delayed fault copies own cross-stack lifetime. Six production loopback socket
cases pass 2/4/8-peer delivery, exact-once controls, queue drain and 400 ms stall,
but nine server-step trials retain deadline overruns and had zero pickup-claim
workload. Empty Drain's measured 240 bytes/call is a hypothesis, not proof that a
manual enumerator's release/order/playback complexity is worthwhile. Broad
scene/NetSession/shared-assembly decomposition, workflow merging and Studio
runtime separation need measured ownership/startup benefits before implementation.

Upstream #472/#473/#474 remain stacked on their stated dependent feature bases,
with exact source heads recorded by #502; their clean status there does not mean
mergeability against main or this combined proposal. #424 readiness in #500 is
open draft `38f09b0e77497d2de3c94985f42c2adbfc954b1b`, CONFLICTING/DIRTY, with
failing full-client/migration/Android/platform checks from run 37925970371,
completed October 9 at 12:24:28 UTC. Active desktop/Android Avalonia 12.1.2 remains;
its 15-surface navigation/settings/home/lobby/join/account/social/pause/HUD/Studio/
editor/dialog/focus/input/accessibility/localization matrix is not a migration
parity pass. No complete tested replacement is selected for toolkit deletion.

## Compatibility, regression and negative evidence

Retirement preserves protocol 53, reserved historical bits, fixed 26-byte player
suffix, 24-byte zones, world-v9/replica-v10 canonical contracts and type/field
identities. World contract stays
`A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A`.
Named `TryReadArchived` relaxes only retired support policy; it still rejects
invalid room/time/mode/modifier combinations. Live settings schema 11 explicitly
normalizes Enhanced off without selecting Balanced; EnhancedDirectHit stays
independent. Historical code remains eligible only through replay-owned scenes.
Two frozen native-form timelines/21 private files pin 224 asset hashes, source
slot/generation/life/model identities, eight v9 seeks and continuations. Both
baseline and candidate pass 1,103 checks. These are synthetic headless archives,
not recorded human Enhanced ability, GL/HUD/audio or physical playback acceptance.

#497 preserves 618 exact ordering/float-bit rows, fixes nested face duplication
(two contacts to one) and simultaneous scene failures (1,000 of 2,000 to zero).
32,000 parallel queries pass; normal/warmed 4,225 and 8,192 candidate queries
allocate zero. The initial 2,048 retention cap allocated **43,387,200 bytes per
100 warmed 4,225-candidate queries** and was rejected. Current above-budget 8,193
queries complete, clear references and discard scratch; universal allocation
neutrality is not claimed. The pool retains at most eight workspaces and 65,536
candidate objects per lane. Concurrent mutation of one scene is unsupported.
#501's requested-scene setup eliminates four baseline ownership failures with six
candidate assertions, without a broad scene API rewrite.

#499 repairs only fixture defects: the earlier shooter pose at Y=32.341797 caused
fall-damage receipts with zero new shots; restored settled Spawn preserves flights
and damage assertions. Synthetic status sources receive real launch stamps and
captured owner Hunter metadata. The telemetry window starts before the explicit
seven spawns and now asserts each Hunter once. Combat 204/Balanced 347 pass on
pristine and integrated libraries and the earlier rebuilt combined source.
No production damage, status duration, physics or telemetry changed. The three
old DNS failures were INVALID_INVOCATION (unknown flags/missing argument), not
runtime regressions; original logs/manifests remain. Correct retirement/archive/
live/advanced commands and every final integration invocation must be used.

#503 bounds eight waiting video exports with active/start counted separately;
26 queue tests preserve FIFO/retry/cancel/metadata and fresh ownership. #507's
three null guards turn five uncaught NRE fixtures into 12 passing checks while
preserving valid siblings, IDs, order and version-1 sidecars. #511 drains discarded
stderr with a 2,048-character buffer: generated 16 MiB input allocates
**67,283,248 to 4,120 bytes**, with EOF/Unicode/IOException/reader ownership intact.
It does not certify stdout/progress accumulation or inherited-pipe termination.

Theatre #515 `18692b9` invokes actual installed callbacks without a Window, scene,
GL/audio device, encoder or account request. Original source is 1 PASS/6 FAIL;
candidate 7 PASS/0 FAIL. A burst of 128 favorite/file-export callbacks changes
128 queued scans/copies to one waiter; disposed queued scan admission goes to
zero and clears running/cancellation/request state. Latest selection/favorite
publication, exact one-copy bytes, controls, sentinel preservation, I/O failure/
retry and accepted export completion after disposal pass. Whole-workspace
WeakReference death is **INCONCLUSIVE** because an unused never-attached disposed
control also stays alive. Constructor storage policy, global semaphore callers,
thumbnail attach producers and physical Android storage-provider paths are not
certified by these fixtures.

Updater #490 changes original 67 PASS/8 FAIL to 76 passing checks for trusted
release extraction failure/cancellation cleanup. Large-entry timed cancellation,
archive expansion/download budgets and physical published upgrades are not proved.
#508 changes synthetic owner-token header tests from 2 PASS/22 FAIL to 24 passes
at disk/crash-ring sinks while preserving parser/nonsensitive argv. Other messages,
native stderr, dumps and growth policy remain outside that redaction proof.
Existing map package/PK3/OBJ limits and content hashes are reused; there is no
blanket security certification or arbitrary attacker-controlled updater claim.

## UI, builds and physical limits

#500 removes only two constructor/route/reflection/serialization-unreferenced
views; active PlayWorkspace and shared services/preferences stay. UI 165,
gamepad 1,006, client-policy pointer 1,788, HUD 100, settings archive 26 and
synthetic touch/pacing tapes pass. The earlier pointer run under Headless was
fixture misuse: ForceSpawn intentionally requires the client policy. It remains
preserved; no product/assertion weakening. #509's supported placeholder/PNG APIs
preserve the actual old/new placeholder and exact 92-byte PNG while removing
eight CS0618 sites. #514 removes the constant-false post-match picker while
retaining actual ballot/rematch/lobby-return and saved RespawnChoice semantics.
Baseline/candidate each pass 11 assertions. Empty and persistent-return PNGs
match exactly; offline **8,145 pixels/max channel delta 3** differ only within
unchanged process-randomized arena gradient interiors, with zero outside
changes. A one-channel pixel (0,0) probe is correctly rejected. This is scoped
outside-boundary equivalence, **not full offline PNG parity**; the first 15,637
pixel/max-4 mismatch remains recorded.

#512 independently reduces desktop/server warnings 30/22 to 22/11. #509 removes
eight obsolete desktop sites; #514 removes eleven unreachable desktop/eight server
sites. The actual final combined client and dedicated server each have **three existing
CS8602 warnings and zero errors**, from NetHitClaims, NetUnlagged and PlayerEntity.
The separate nettest CS0105 and source-linked diagnostic stub warning remain
visible in tool receipts. No blind nullability suppression was added.
No broad warning suppression or claim/history nullability change is hidden.
#506 fixes seven nested DTO XML root separators; isolated ILLink warnings fall
seven to zero, with seven DTO/16-property metadata+JSON proof. Normal existing
APK outputs already retained the DTOs; no authentication defect is asserted.

#500's isolated actual Release publish with verified Temurin 17 and single-node
MSBuild completes 117 AOT assemblies. Its signed APK is **108,816,049 bytes**,
SHA256 `d327571c347c18f3a8aad78d8a184a8cfe7677dbc4613fd98abe9bd8b15f9c8f`,
and apksigner verification passes. Preserve the prior MSB4166/MSB4027 failure;
no Java-cause inference is established. Later default-package inventory finds **both required libktx.so ABIs missing**
in this prior APK, a preexisting local-default coverage gap. Its compile/AOT and
signature passes remain valid; **full release inventory is not accepted**. The
final edcf0f1 default APK similarly compiles/signs in 124.665 seconds with 29
warnings/zero errors and is 108,906,161 bytes (SHA256
`6e4c3b60cd7729408f7e1e09f0c8d61c5301439a279576d1d0a8e0ec93bc145a`),
but lacks both KTX natives. Preserve both default trials. Existing pinned
KTX revision `6b3d8bf15788f604c6b91dd95fafabb6c59cd723` builds both arm64/x64
inputs without source/signing changes. A separate fresh release-staged publish
passes in **151.323 seconds**, with **29 warnings/zero errors**, **117 AOT
assemblies per ABI × two ABIs**, and **262 native entries**. Its debug-signed APK
is **119,314,232 bytes**, SHA256
`bcb8bd5ab297b98846b565045a6f66fcfb41e716183d60cfef5ed375d6a0f0d5`.
Both required KTX entry hashes match the pinned inputs. Apksigner v2/v3 and
zipalign 16K checks pass; both KTX ELF LOAD alignments are 0x4000. Source remains
clean and declared avares coverage passes. This validates the staged package
inventory, not distribution-keystore signing, physical installation or startup.
The larger staged package has required native content; no package-size gain is claimed. #504 has a separately reported normal Android Release/AOT pass;
the combined staged packaging receipt now passes; device launch remains independent.

Android source evaluation has 1,127 compile items/1,097 shared and no duplicate
identities; current GLES aliases, AOT/startup and platform UI differences remain.
Server output excludes Avalonia but an earlier inventory still has 1,580,488 bytes
of loose GLFW/miniaudio/STB reachable by compiled graphical/export/menu commands.
Default dedicated startup is headless; speculative pruning could break those
commands. Neither inventory nor deleted repository bytes imply install-size gain.
Platform packages/signing/update/rollback/licenses/retail-data guards are retained.

No device-free fixture establishes physical keyboard/gamepad/haptics/touch latency,
IME/file-picker/screen-reader parity, Windows/Linux/Android audible device-loss,
Android thermal/EGL/page-size acceptance, full Studio playback/HD arm cannon,
frame/audio export or physical updater installation/relaunch. Localized retail
text selection is not full shell translation; account/career routes are not a
completed friends/party migration. Full acceptance remains false even if all
hosted checks eventually pass.

## Maintainer gates and rollback

Local-core build, 42-command and staged Android package review is complete. The
frozen integration manifest SHA256 is
`7fcc2f834938f3069a7ad6aeec782768756247b2db0111374b30b62d2d55182f`;
binary metadata receipt SHA256 is
`bb427ed4f5844d9084d83156665276dce9c9935508bf1e0fbcd10c9762941559`;
final staged Android receipt SHA256 is
`2ef65f1c779c8892a75d3420ddaf7305e0a08bc7c5e2d450859432acfa5e4efa`.
Future hosted CI receipts are separate and do not mutate this local manifest.

1. Complete the exact reviewed publication-head and aggregate edcf0f1 hosted
   checks. Use stable PR checks/run links; queued jobs remain pending. A new
   documentation publication changes #489's head, so the recorded green #489
   snapshot does not certify its later publication. Record final green receipts
   locally/in PR bodies without a circular reporting-commit/CI loop.
2. Resolve driver P0 through increasing-load, owner/lifetime and actual context
   controls with mechanism-aware allocation-group comparisons and host/paging
   disclosure. Old-scene root/liveness remains UNKNOWN. Do not infer boundedness
   or leaks from RSS, source hashes or weak survival; no unsupported cache/GC patch.
3. Complete physical platform, rendered archived replay, shared-output/audio,
   UI/input/accessibility, real encoder/export/cancel/HD identity, application-level
   network gameplay and updater install/launch gates. Valid-content budgets need
   published-package/sidecar compatibility evidence before new rejection rules.
4. Further performance/seam work requires a demonstrated benefit while preserving
   60 Hz, authority, frame/depth ordering, parser layouts and explicit ownership.

Rollback is per focused PR/commit family and requires no automatic data conversion.
Remove #505 controls before #493 if retiring their shared diagnostic tooling;
restore #497 getter and every borrower together. Reverting #498 restores live
eligibility without changing archived fields, but settings already normalized to
off remain off until explicitly changed in a restored build. Restore only #500's
two views or #514's three result files for UI rollback; retain preference/schema
owners. Queue/Theatre state is in memory; accepted export destinations remain
caller-owned and must not be blindly deleted. Restore #504 loader/graph as one
unit while retaining #495 startup cleanup; rollback reintroduces the documented
race and has no format migration. Updater revert restores failed-stage defects;
cleanup touches only owned staging. Shader/SFX reverts restore reproduced native
ownership defects. #508 revert restores token disclosure. Diagnostic-only #496/
#502/#510/#513 can be removed without changing simulation or production GC policy.
Preserve independent CI steps and source overlaps on every integration or revert;
never use a destructive checkout reset over user work.

The accompanying JSON pins all proposal heads, measured source distinctions,
negative/limited evidence, gates and rollback instructions. Final hosted CI remains pending at the
publication snapshot; this report does not claim release or full consolidation
acceptance.
