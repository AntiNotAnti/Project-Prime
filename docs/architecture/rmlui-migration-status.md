# RmlUi migration status

This is the game-client migration checklist. It does not certify release parity.
The full release migration is unfinished. Native routes, shared authority, modern
composition and platform adapters now exist in the working tree. Shipping
default, physical/platform parity and conditional standalone Studio acceptance
remain separate gates. The immutable baseline below retains its historical
Avalonia/proof state.

## User acceptance scope

On October 7 the user took ownership of live testing and removed external
physical-device/screen-reader testing as a completion prerequisite. Local Mac
checks, hosted CI/software GPU and emulator automation remain the implementation
and cutover evidence. Unperformed physical/VoiceOver/TalkBack/vendor-device runs
must be reported honestly as coverage limits; they do not block shipping-policy
or client-removal implementation. The frozen plan inventory retains its original
requirements as historical scope, and this user decision governs current work.

## Pinned evidence

| Evidence | Revision / result |
| --- | --- |
| Source inspected for the immutable acceptance inventory | `6a24a39d9a37014f9ae4618da4af6361c1872203`, PR #387 merge |
| Supplied plan's source baseline | `f8308a92460cb7962762b6bf15c01341e8e3b845`, PR #388 merge; not in the inspected local ancestry |
| Remote `main` inspected October 7, 2026 | `59a184255b24d922c3ad6e6c2e74435193208017`, PR #375 merge |
| Remote build run at that revision | [37682562968](https://github.com/AntiNotAnti/Project-Prime/actions/runs/37682562968); queued/in progress when captured; not a green baseline |
| Remote semantic content-free result | Successful completed check at the remote SHA; this does not establish the native/platform matrix |
| Native macOS arm64/x64 gates | Queued at capture; no pass inferred |
| Prior superseded `main` build runs | Cancelled; no pass inferred |

The exact read-only remote assessment is frozen in
[rmlui-baseline-2026-10-07.json](rmlui-baseline-2026-10-07.json), including check
names, conclusions, timestamps and URLs. This file records a historical
observation, not a live status. Assess each new branch head separately before
review/merge; neither merged state nor a successful stale-cancellation workflow
is build acceptance.

[rmlui-acceptance-baseline-6a24a39d.json](rmlui-acceptance-baseline-6a24a39d.json)
is the immutable source inventory: 27 plan slices, 61 workflow obligations,
eight existing routes, 154 Avalonia source surfaces and 449 concrete
control/action expressions. It includes method/event/navigation identifiers,
settings controls and dynamic input-setting sources. Generated row expressions
retain source locations; they are evidence of legacy scope, not tests or
claims that every helper is a separate product workflow. Dynamically constructed
settings/bindings require semantic expansion during the parity audit.

The only accepted status values are `not-started`, `partial`, `functional` and
`parity-verified`. `partial` means some real behavior exists while required
scope/evidence remains. `functional` requires named environment evidence;
`parity-verified` requires legacy behavior and the required production matrix.
No baseline workflow is marked `parity-verified`. Preserve the frozen inventory
and add later revision-specific evidence instead of rewriting its history.

## Existing implementation and overlap

[PR #383](https://github.com/AntiNotAnti/Project-Prime/pull/383),
[#384](https://github.com/AntiNotAnti/Project-Prime/pull/384),
[#385](https://github.com/AntiNotAnti/Project-Prime/pull/385),
[#386](https://github.com/AntiNotAnti/Project-Prime/pull/386) and
[#387](https://github.com/AntiNotAnti/Project-Prime/pull/387) are merged and present
in the source baseline. They provide the live eight-player lobby/chamber,
formation, native multiplayer entry, authoritative rules subset and pointer
mapping/centering. [PR #388](https://github.com/AntiNotAnti/Project-Prime/pull/388)
is merged remotely but is outside the pinned source baseline. Merge status does
not prove native compile, direct input, full launcher/lobby integration or
platform parity.

At the frozen remote observation the social stack remained open, and none of
its inspected heads had a fully successful check set. The later working-tree
integration reuses that stack in dependency order; the historical check counts
below are not current-head CI acceptance.

| PR | Head | Base | Observed latest checks by name |
| --- | --- | --- | --- |
| [#365](https://github.com/AntiNotAnti/Project-Prime/pull/365) | `ae6ddcc1c17a298250077275bd605b2f9c23ce6b` | `main` | 4 success, 7 failure, 22 cancelled |
| [#366](https://github.com/AntiNotAnti/Project-Prime/pull/366) | `9180deb09ce152efe88ae7a0b19b8420dc5a380a` | `feature/social-foundation` | 11 success, 17 failure, 2 cancelled, 3 skipped |
| [#368](https://github.com/AntiNotAnti/Project-Prime/pull/368) | `38262e11dba3de58badf651f750b85f791609754` | `feature/social-presence` | 8 success, 11 failure, 11 cancelled, 3 skipped |
| [#369](https://github.com/AntiNotAnti/Project-Prime/pull/369) | `7f609440056f8ecced01ff1bf5786cdadb8384c6` | `feature/social-rmlui` | 5 success, 4 failure, 21 cancelled, 3 skipped |
| [#370](https://github.com/AntiNotAnti/Project-Prime/pull/370) | `4a322016399ebb090ba68e2bc1e2af37c996c6c9` | `feature/social-invites` | 4 success, 5 failure, 24 cancelled |
| [#371](https://github.com/AntiNotAnti/Project-Prime/pull/371) | `19d08c0d13a6052d1d83281a9174241969eff048` | `feature/social-party-polish` | 5 success, 19 failure, 6 cancelled, 3 skipped |
| [#373](https://github.com/AntiNotAnti/Project-Prime/pull/373) | `c2f1db6e287582adb8cd06e4d522b9fbacc959f2` | `feature/social-party-travel` | 12 success, 16 failure, 2 in progress, 3 skipped |

These counts select the most recently started check of each name from GitHub's
current-head rollup. Older successes do not replace a newer cancelled result.
The frozen audit retains links to each selected check. Failures include native,
RmlUi, input and engineering-contract gates; do not infer that they are caused
by social code without reading the corresponding logs and reproducing them.

## Ordered slices

The source-baseline status below is conservative. Foundation implementation in
the current worktree must acquire its own build/test evidence before these
acceptance gates can be checked. See
[runtime boundaries](rmlui-runtime-boundaries.md) for ownership and fallback
decisions.

| Slice | Baseline status | Remaining acceptance |
| --- | --- | --- |
| RML-00 Baseline/inventory/CI | Partial | Native macOS both architectures + Linux, direct input, green pinned release baseline; screenshots/traces/performance matrix |
| RML-01 Engine-owned runtime | Partial | Split host/native lifetime and documents, versioned typed actions, cancellation and 100 document cycles; no toolkit snapshots |
| RML-02 Router/components/themes | Not started | Shared shell, all route/modal/focus policy, responsive components and shared tokens; legacy navigation exists |
| RML-03 Input/focus/text/accessibility | Partial | Pointer mapping exists; full host DPI/capture/scroll, Unicode/clipboard/IME, controller, touch and accessibility evidence |
| RML-04 Presentation-neutral lobby | Not started | Shared controller/pump and adapters, no hidden views, real create/play/return/recovery tests |
| RML-05 Lobby administration | Partial | Rules/native entry subset exists; full owner/bot/team/chat/rotation/matchmaking and 2/4/8 clients |
| RML-06 Hunter/cosmetics/loadout/teams | Partial | Engine previews/basic selection exist; dedicated native selection/owned assets, acknowledgements and resource persistence |
| RML-07 Social stack integration | Not started | Rebase existing stack, current-head checks and real multi-account invite/travel/reservation tests |
| RML-08 Settings/setup/updater | Not started | Every recorded settings action, draft/persistence/recovery plus setup/update/version workflows |
| RML-09 Hunter License/account | Not started | Native profile/stats/history/achievements/customization/account parity with protected credentials |
| RML-10 Community | Not started | Native browse/install/publish/revise/remove, authorization/validation/progress/cancellation |
| RML-11 Offline/practice/Adventure | Not started | Native setup/saves/training and full offline/Adventure return cycles |
| RML-12 Replay/Theatre/Studio launch | Not started | Native library/playback/camera/fullscreen/focus and independent Studio launch/error flows |
| RML-13 In-game menus/overlays | Not started | Pause/settings/vote/spectator/results and running-match composition/input at required refresh rates |
| RML-14 Creation-tool client entry | Not started | Native Map/HUD entry or accepted Studio deep links; preserve all save/edit/publish operations |
| RML-15 Modern GPU compositor | Not started | Backend-independent core + engine compositor, required clipping/blending/lifetime and real DX12/Vulkan/macOS recovery |
| RML-16 Desktop bridge/packaging | Not started | Existing macOS/Linux proof builds are not production packages; Windows/RID/signing/clean-install acceptance |
| RML-17 Android bridge/input/lifecycle | Not started | NDK packages, safe-area/touch/IME/surface ownership and physical-device create/play/resume/recovery |
| RML-18 Full parity/performance | Not started | Golden matrix, 2/4/8 online/server cycles, content/input/failure/security/accessibility and measured performance |
| RML-19 Shipping default/rollout | Not started | Validated targets ship without proof flags; canary diagnostics and tested deterministic rollback |
| RML-20 Client Avalonia removal | Not started | Only after RML-18/19: clean all-target client builds without UI assemblies and dedicated server UI-free |

Client scope completion requires all RML-00 through RML-20 gates. Studio is a
separate application. Product-wide zero Avalonia additionally requires:

- [ ] STUDIO-RML-00 inventory/extract document/workspace authority and golden workflows.
- [ ] STUDIO-RML-01 multi-document/window/dock/file-dialog/focus host.
- [ ] STUDIO-RML-02 full Replay Studio workspace and export parity.
- [ ] STUDIO-RML-03 full Map Studio workspace, viewport/edit/build/publish parity.
- [ ] STUDIO-RML-04 desktop packaging/device/lifetime/export validation.
- [ ] STUDIO-RML-05 independently accepted Studio default and Avalonia removal.

## Current source and local validation assessment

This assessment describes the mutable October 7 working tree on observed HEAD
`e9f4d3bd7429820cc1e24ae2e3f024815aecf8f1`; uncommitted source and later integration
changes require validation at the eventual review commit. It does not rewrite
the frozen inventory or declare release parity. The
[current workflow assessment](rmlui-current-workflow-assessment.json) maps all
61 baseline obligations to 75 existing controller/backend/presenter/native-host
source files, records their source hashes and retains named remaining gates.
Each workflow is `partial` until its complete required automated integration/platform evidence is
accepted under the user scope above. Source coverage is distinct from a demonstrated success/failure flow.

| Slice | Current source / named evidence | Acceptance still open |
| --- | --- | --- |
| RML-00 | Frozen source/remote inventories and complete 61-workflow mapping; local feature, native and renderer checks | Immutable green review-head CI and complete baseline golden/performance matrix |
| RML-01 | Owner-thread host, ABI v1 typed intents, revisioned documents/bindings, cancellation, debug reload, neutral renderer; real native registry/core now 5,148 assertions, including 100 modal cycles and 100 reinitializations | Complete production lifecycle and all-target integration |
| RML-02 | Shared router 75 contracts; page manager, composed shell/components, priority/focus lifetimes; real News catalog/filter/select/detail/Discord failure workflows | Full legacy navigation/dirty/session guard and gamepad acceptance |
| RML-03 | Actual fractional/Retina pointer/fullscreen/resize checks; Windows/Cocoa/Linux text adapters, real Android InputConnection; immutable semantic graph and actual Cocoa/Android providers | Actual Windows UIA/Linux AT-SPI automated provider checks and complete route localization; physical coverage user-owned |
| RML-04/05 | Shared lobby controller/backend and native Shell handoff without hidden lobby screens; lobby/Hunter 92 contracts, native Admin/Hunter 67, queue contracts 72 and real server queue regressions 9 | Rendered full-host 2/4/8 clients, custom-map/late-join/recovery and required match-return cycles |
| RML-06 | Toolkit-neutral Hunter/owned cosmetic selection with real engine preview authority and native actions | Full asset/acknowledgement/preview/resource persistence automation; physical coverage user-owned |
| RML-07 | Reused Social stack; controller 149, native 343, Edge 56, all 11 frozen Deno handlers and disposable SQL authority gates; actual lobby/queue protocol checks | Authorized multi-account staging invite/party/travel, real platform and rendered gameplay acceptance |
| RML-08 | Explicit typed settings schema with existing persistence/rollback authority; settings controller 23, real backend 29, native 40; setup controller 27 and real engine/native/monitor 34 | Successful user ROM extraction, real install/restart/rollback, automated picker/input matrix; live hardware testing user-owned |
| RML-09 | Native License/account/stats/history/customization via existing client; controller 43 and native compact/Retina layout/input 60 with zero production account commands | Automated account recovery/link/verification/service failures; live secure entry user-owned |
| RML-10 | Real Community service boundary; browse/search/sort/revisions/install/import/host/favorite/report/creator/conflict/cancel workflows; 129 controller + 488 real native assertions | Authorized staging mutations, real package/library/scene cycles and platform pickers |
| RML-11 | Offline 50 controller + 555 native; Adventure 31 + 132; complete mode/modifier/bot/practice/training and save/new/continue boundaries | Actual gameplay/save overwrite/return and map/practice/platform parity |
| RML-12 | Theatre library/playback/viewport 36 + 206; independent Studio entry 136 neutral + 152 native (288 combined) | Real replay simulation/export/camera return and paired Studio process/picker/IPC |
| RML-13 | In-game/pause/vote/spectator/results native presenters, existing engine/HUD composition; 46 contracts + 155 native assertions | Running-match input/rendering and high-refresh/gameplay transition acceptance |
| RML-14 | Native HUD editor 33 + 376 and Studio entry reuse existing authoring authority | Full edit/save/publish and external Studio acceptance; conditional Studio workspace migration remains separate |
| RML-15 | Neutral draw-list ABI and engine-owned scissor/transform/stencil/texture compositor; actual Metal 22 and Vulkan/MoltenVK 22 chamber/recovery checks; portable authored theme reports no unsupported draw features | DX12/Linux/Android automated GPU and full gameplay/color-space/performance matrix; arbitrary layers/filters/custom shaders remain unsupported; live hardware coverage user-owned |
| RML-16 | Strict RID source/export/hash/dependency manifests; macOS arm64/x64 GL2/neutral compiles and Windows/Linux build/CI paths | Actual Windows/Linux runtime, clean install/signing/current-head CI and automated input |
| RML-17 | `MphReadRmlUiAndroid=true` selects native SurfaceView/Core presenters with owner-thread input/lifecycle and provider; actual emulator ES3 126, InputConnection 13 and accessibility 16; signed arm64/x64 APK/NDK payloads | Automated safe-area/IME/background/GPU recovery and production AOT; physical devices user-owned |
| RML-18 | Named native geometry/pointer/lifecycle/GPU suites and integrated Metal route captures exist | Complete legacy-vs-native golden/hit traces, same-machine performance and required real gameplay loops |
| RML-19 | Explicit opt-in runtime and legacy rollback paths retained | Accepted platform default/canary diagnostics and deterministic rollback rollout |
| RML-20 | Native-only client source boundary/authority extraction is under final architecture validation; dedicated server boundary excludes UI bridge/render/font payloads | Accepted all-target UI-assembly-free client builds after RML-18/19; shipping dependency cutover |

Current local evidence is intentionally specific. An injected fake backend,
real DOM, real network protocol and real GPU each establish different boundaries.
None alone accepts a complete production workflow.

| Local check | Observed result / boundary | Diagnostic evidence |
| --- | --- | --- |
| Opt-in Release game build | 135 warnings, zero errors; all advanced routes before the newest News/policy/queue changes | `/tmp/prime-rmlui-queue-gallery-build.log`; later final integration run required |
| Native core/action registry | 5,148 actual native assertions; all 12 News actions, strict queue/action ranges, modal density and lifetime tests | Native owner's latest frozen source checkpoint; earlier queue 5,112 `/tmp/prime-native-core-queue-check.log`; earlier core `/tmp/prime-native-core-check.log` |
| Native GL2 input/layout | 270, measured GLFW window/framebuffer mapping, fractional/Retina DPI, real fullscreen/resize/release-outside/focus loss | `/tmp/prime-rmlui-route-check.log`; `/tmp/prime-rmlui-native-final/` |
| Optional native scheduling / retained draw | Host 51 actual native scheduling assertions and 28 fake/fallback contracts; copied draw-list 12 assertions including 100 retained frames without allocation and real mutation/lifetime invalidation | Host/renderer owner's source harnesses; `/tmp/prime-rmlui-drawlist-cache-check.log`; production timing remains a separate gate |
| Shared router/lobby/Hunter | Router 75; combined lobby/Hunter 92; native Admin/Hunter 67; earlier fake backend 50 create/leave cycles | `/tmp/prime-rmlui-lobby-final-contracts.log`; `/tmp/prime-native-lobby-layout-check.log`; router owner's stdout |
| Native-only shared Shell routing | 343 real DOM/presenter/IPC assertions, including dirty Apply/Discard, busy/required Setup guards and deferred Studio cancellation; reflective cache/broker harness, no RenderWindow or production account operations | `/tmp/prime-rmlui-shell-routing-native-final.log`; `tools/rmlui-shell-routing-check`; matching transitional final run remains pending |
| Multiplayer queues | 72 injected contracts, 9 actual DedicatedServer/queued NetSession/cancel-before-Welcome regressions; actual waitlist 310 | `/tmp/prime-rmlui-multiplayer-queue-contracts.log`; `/tmp/prime-rmlui-multiplayer-live-queue-final.log`; `/tmp/prime-social-queue-cancel-waitlist.log` |
| Existing Social authority | 149 controller, 343 real native, 56 Edge; frozen Deno 11 handlers and disposable PGlite SQL gates | `/tmp/prime-social-owned-edge-check.log`; `/tmp/prime-social-owned-deno.log`; `/tmp/prime-social-owned-sql-check.log`; source/native tool logs |
| Real native DOM/UDP lobby lifecycle | 879 assertions: 2/4/8 real UDP clients and shipped DOM actions, 50 fresh join/leave cycles and 20 successive matches on the same controllers/connections with real server intermission, fresh match identities, once-only load requests and explicit pump yield/resume; empty bootstrap scene fixture | `/tmp/prime-rmlui-lobby-live-check.log`; `/tmp/prime-rmlui-lobby-live-build.log`; rendered GPU/multiwindow/physical gameplay acceptance remains open |
| Real lobby/custom-map protocol | Lobby 8,162; custom-map readiness pass; waitlist policy/loopback 335 | `/tmp/prime-social-lobby-ready.log`; `/tmp/prime-social-custom-map-ready.log`; owner's waitlist logs; does not establish rendered multiplayer parity |
| News | 28 contracts + 348 real native at 720p/1080p/compact/2x; exact bundled catalog shared with legacy; browser boundary fake, no actual external opening | `/tmp/prime-news-controller-check.log`; `/tmp/prime-news-native-check.log` |
| Settings | Controller 23, authoritative backend 29, native page 40, including glyph modal, input ownership, video rollback and dirty navigation | `/tmp/prime-rmlui-settings-core-check.log`; `/tmp/prime-rmlui-settings-backend-final-check.log`; `/tmp/prime-rmlui-settings-native-final-check.log` |
| Setup | Controller 27, real engine/native/monitor 34; no successful user ROM extraction or update install/restart performed | `/tmp/prime-rmlui-setup-core-check.log`; `/tmp/prime-rmlui-setup-engine-final-check.log` |
| License | Controller 43; 60 native compact/2x account field/outer-wheel/Back checks; logical dp text and 44dp targets; zero production account commands | `/tmp/prime-rmlui-license-final-contracts.log`; `/tmp/prime-license-native-check.log` |
| Community | 129 fake-service controller + 488 actual native; exact Unicode maxlength and explicit UTF-8 read budgets, conflict/revision/host/cancel boundaries | `/tmp/prime-community-controller-check.log`; `/tmp/prime-community-native-check.log` |
| Offline | 50 fake launch/persistence contracts + 555 native across eight viewport/density cases, real pointer/wheel and positive full-panel/control geometry | `/tmp/prime-offline-native-check.log`; `tools/offline-controller-check` |
| Adventure / Theatre / HUD / Studio | Respectively 31+132, 36+206, 33+376, 136+152 controller/native assertions (Studio 288 combined); existing engine/document/process authority | Checked-in matching tools and implementation-owner local logs; full scene/export/process acceptance remains open |
| Shared theme/localized chrome | 610 native at seven viewports/aspects; six chrome languages, header/footer containment, real Hunter/Social pointer actions, short-stage wheel,48dp touch targets and actual normal/high-contrast route highlights; no unsupported draw features | `/tmp/prime-accessibility-check.log`; `tools/rmlui-accessibility-check` |
| Native accessibility service | 150 actual semantic assertions; no editable values, secure metadata, private action/revision/modal/generation guards, Unicode SetText and real typed press | `/tmp/prime-accessibility-check.log`; new native status proves 200 idle captures with zero native semantic reads, JSON decodes or managed allocation per viewport; actual model/resize/pointer/command/caret-deadline invalidation passes; production timing rerun pending |
| Cocoa accessibility | 218 actual AppKit/GLFW/native assertions, including 100 attach/detach, real names/roles/screen bounds, protected values, queued actions and retirement | `/tmp/prime-cocoa-accessibility-check.log`; physical VoiceOver is user-owned live coverage |
| Desktop text input | Cocoa 222 actual AppKit/GLFW/native; Linux 219 native-host plus fake-bus lifecycle checks | Owner's Cocoa/Linux tool logs and [platform text input](rmlui-platform-text-input.md); actual Linux D-Bus transport/physical engines pending |
| Windows/Linux semantic provider contracts | 150 real native action/metadata/revision/modal/lifetime contracts plus actual GLib protocol XML/Cache wire checks; real Windows COM HWND and Linux GDBus AT-SPI providers exist; external OS fixture modes pending hosted CI | Implementation owner source/native stdout; `tools/rmlui-platform-accessibility-check`; physical NVDA/Narrator/Orca is user-owned coverage |
| Modern GPU | Metal 22 and Vulkan/MoltenVK 22 on M4 Pro, chamber rendering, resize/zero-size/resource/device recovery and shutdown/reentry | `/tmp/prime-rmlui-metal-chamber-check.log`; `/tmp/prime-rmlui-vulkan-chamber-check.log` |
| Android emulator | ES3 pixel/state 126 with 100 resource cycles; InputConnection 13; accessibility 16 with real virtual nodes, bounds, protected metadata, focus, Unicode SetText, typed Back and retirement | `artifacts/rmlui-android-proof/rmlui-android-accessibility-check.txt`; actual no-Avalonia CHECK APK/emulator; preserves strict owner/revision gates |
| Actual modern GPU lifetime | 100 real page replacements across ten RML documents, 333 successful presents per backend, stable geometry/buffer/atlas/stencil/surface counts, reconstruction and teardown | [Metal lifetime evidence](rmlui-evidence/gpu-lifetime-metal.json), [Vulkan lifetime evidence](rmlui-evidence/gpu-lifetime-vulkan.json); engine counts, not driver VRAM measurement |
| Initial same-machine production timing | Three alternating Metal runs per mode, 2560×1440, 60fps, 5s warmup/20s sample with IME/accessibility enabled: native median UI p95 1.2456ms vs Avalonia 0.2371ms and CPU 31.08% vs 15.86% of one core; native allocations/cold presentation improved | [Initial comparison](rmlui-evidence/performance-metal-initial/comparison.json); measured CPU/UI regression is under investigation; full-process CPU includes scene/background services |
| Native-only Android managed boundary | Signed arm64/x64 Debug APKs contain 226 managed assemblies and Release APKs 93, all with zero Avalonia assemblies; payload hashes, no-Avalonia restore and cartridge guard recorded | [Android native client evidence](rmlui-evidence/android-native-client.json); AOT disabled for these fixtures |
| Native/platform packages | Strict current native source manifests; macOS arm64/x64 GL2/neutral, Android arm64-v8a/x86_64 neutral/16KB and signed opt-in APK builds | `/tmp/prime-rmlui-final-allpages-native.log`; platform owner RID logs; Windows/Linux runtime and production AOT unperformed locally |
| Integrated route captures | Nine actual Metal route runs exited zero and captures reviewed; License Retina readability repair followed its capture | `/tmp/prime-rmlui-final-route-gallery/`; these are limited integrated observations, not accepted legacy goldens |

The `/tmp` paths are ephemeral local diagnostics, not committed release outputs.
Every result must be rerun or attached at an immutable review head. Historical
cancelled, skipped or stale checks never count as a passing gate. The final build,
server/default/native-only architecture, complete route capture and platform
CI matrix remain owned by the integration run; the newest source changes are
not inferred to pass that matrix from an earlier binary.

Reproduce source and real native checks from the repository root with .NET 10
and the platform prerequisites in [native build instructions](../../tools/rmlui/README.md).
The [review slice proposal](rmlui-review-slice-proposal.json) assigns the current
diff to one-PR-per-slice candidates and flags shared/late dependencies. It
excludes parent-owned all-route Shell integration until RML-18; it is a review
proposal, not independently compiling commit acceptance. Its regeneration tool
is `tools/rmlui-assessment-check/propose-slices.py`.

The checked-in tool READMEs describe required data, injected boundaries and
native flags. These commands are rerun instructions, not claims of a pass on a
different revision or machine:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadRmlUi=true
dotnet run --project tools/launcher-router-check -c Release
dotnet run --project tools/lobby-controller-check -c Release
dotnet run --project tools/news-controller-check -c Release
dotnet run --project tools/offline-controller-check -c Release
dotnet run --project tools/adventure-controller-check -c Release
dotnet run --project tools/community-controller-check -c Release

bash tools/rmlui/build-native.sh osx-arm64 draw-list
dotnet run --project tools/rmlui-core-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
dotnet run --project tools/news-controller-check -c Release -p:MphReadRmlUi=true -- --native \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
dotnet run --project tools/rmlui-accessibility-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
dotnet run --project tools/rmlui-cocoa-accessibility-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib

bash tools/rmlui/build-native.sh osx-arm64 gl2
dotnet run --project tools/rmlui-route-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  --capture /tmp/prime-rmlui-native-rerun
dotnet run --project tools/rmlui-compositor-check -c Release -- metal --recovery
dotnet run --project tools/rmlui-compositor-check -c Release -- vulkan --recovery

bash tools/rmlui/build-native.sh osx-x64 gl2
bash tools/rmlui/build-native.sh osx-x64 draw-list
bash tools/rmlui/build-native.sh android-arm64 draw-list
bash tools/rmlui/build-native.sh android-x64 draw-list
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 \
  -p:MphReadRmlUiAndroid=true -p:MphReadRmlUiAndroidCheck=true \
  -p:RunAOTCompilation=false -p:AndroidPackageFormat=apk \
  -o publish/rmlui-android-runtime
python3 tools/rmlui/verify-runtime.py --apk \
  publish/rmlui-android-runtime/com.projectprime.game-Signed.apk android-arm64
```

Repeat the Android publish with `android-x64`. `MphReadRmlUiNativeAssets=true`
packages the bridge/assets only; `MphReadRmlUiAndroid=true` additionally selects
the native runtime. Validation Activity extras `rmlui-ime-check=true` and
`rmlui-a11y-check=true` exercise real Android framework fixtures. These fixtures
do not submit production account forms. Production AOT remains an explicit gate.

## Evidence still required

- [ ] Immutable green review-head client/default/native-only/server and required platform CI; clean install/signing/rollback packages.
- [ ] Native full-host 2/4/8 clients, server rejection/restart/packet-loss, custom-map/prewarm, late join/spectator and required real gameplay return loops.
- [ ] Legacy/native golden screenshots and hit traces at 720p/900p/1080p/1440p/4K, fractional/Retina DPI, form factors and fullscreen on the required backends.
- [ ] Same-machine Avalonia/native cold launch, idle CPU/redraw, p95 UI frame cost and CPU/GPU resource measurements under actual gameplay.
- [ ] Automated Windows/Linux accessibility providers and complete six-language route-body localization. External physical input/vendor IME/VoiceOver/TalkBack is user-owned live coverage.
- [ ] Authorized staging account/social/community service success/failure/cancel; real user ROM/update installation, package/replay/save/Studio process workflows.
- [ ] Android emulator safe-area/keyboard/background/resume/surface/device recovery and production AOT; physical vendor-device coverage is user-owned.
- [ ] Accepted shipping default/canary and client dependency removal after parity; conditional Studio track if product-wide zero Avalonia is required.

Real native counts, actual GPU/resource cycles, Android framework input and route
captures above establish bounded observations. They do not accept the full golden,
multi-account, performance or release matrix. Required automated gates stay
open until concrete results are recorded at the tested integration revision.
External physical-device/screen-reader live tests remain user-owned coverage and
are not implementation-completion blockers.
