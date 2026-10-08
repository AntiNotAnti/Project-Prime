# RmlUi migration status

This is the game-client migration checklist. It does not certify release parity.
The full migration is unfinished. RmlUi remains a development opt-in in the
audited baseline, and the game client and standalone Studio still contain
Avalonia UI.

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

The social stack remains open, and none of its inspected current heads has a
fully successful check set. Preserve and rebase this work in dependency order;
do not duplicate its social services or obsolete RmlUi views.

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

## Current foundation worktree assessment

The following is a separate assessment of work in progress on top of the pinned
source baseline. It does not alter the immutable inventory and does not certify
release parity. Results apply to the mutable October 7 working tree and need
revalidation at the eventual review head.

| Foundation area | Current status | Implemented boundary / observed evidence |
| --- | --- | --- |
| RML-01 managed host/action/input foundation | Partial | Managed `RmlUiHost`, centralized typed ABI v1, immutable revisioned binding snapshots, owner-thread/cancellation guards and multi-document lifetime. Real native core and P/Invoke integration passed; page decomposition/platform parity remains open. |
| RML-02 shared application routing | Partial | Core `LauncherRouter`, page/modal cancellation and snapshot lifetimes, route aliases/deep links, owner-thread/modal priority/focus history; existing `PrimeRouter` adapts the same application router. `tools/launcher-router-check` reports 75 passing contracts. |
| RML-03 input/focus foundation | Partial | Native DOM input covers 1x/1.25x/1.5x/2x, actual GLFW fullscreen/resize, release-outside/focus loss, Unicode and clipboard. Complete physical host/controller/touch/IME/accessibility acceptance remains open. |
| RML-04 shared lobby lifecycle | Partial | Core `LobbySessionController`/`NetLobbySessionBackend` with immutable snapshots, injectable authority and exclusive pump ownership; legacy presenter and direct native Shell integration share the service. Real server/scene/rollback/recovery acceptance remains open. |
| RML-15 modern GPU compositor | Partial | Native draw-list ABI 1 and engine-owned geometry/texture/scissor/transform/stencil compositor. Real Metal and Vulkan/MoltenVK pixel/lifetime/recovery checks passed on one M4 Pro. Unsupported layers/filters/custom shaders, other platforms and full document/performance parity remain open. |
| RML-16 desktop build/packaging foundation | Partial | RID-specific bridge/asset/license manifests, source/dependency/hash/export validation and Windows/macOS/Linux build paths. macOS arm64/x64 GL2 and neutral compiles passed. Windows/Linux runtime/clean-install/signing/CI acceptance remains open. |
| RML-17 Android build/payload foundation | Partial | NDK arm64-v8a/x86_64 neutral builds with 16 KB alignment and signed opt-in APK payload builds passed. `MphReadRmlUiNativeAssets=true` packages assets only; Android UI, input and lifecycle still use Avalonia. Physical runtime/AOT acceptance remains open. |

Local validation observed before final integration/rebase:

| Check | Observed result | Diagnostic evidence |
| --- | --- | --- |
| Release game-client build, `MphReadRmlUi=true` | Passed; 134 warnings, zero errors | `/tmp/prime-rmlui-final-client-build.log` |
| Native GL2 DOM/input/layout | Passed; 270 assertions, measured GLFW window/framebuffer mapping, fractional/Retina DPI, actual fullscreen/resize, release-outside/focus loss | `/tmp/prime-rmlui-route-check.log`; captures under `/tmp/prime-rmlui-native-final/` |
| Real native core/runtime | Passed; 2,931 assertions, 100 real modal cycles and 100 native reinitializations; ABI/Unicode/clipboard/lifetime/draw-list capture | `/tmp/prime-native-core-check.log` |
| Managed host with real native bridge | Passed; actual P/Invoke handshake, DOM intent, Unicode binding/fields/clipboard, focus restoration, cancellation/reinit/device-loss | `/tmp/prime-rmlui-host-native-check.log` and core log |
| Shared router | Passed; 75 contracts | Implementation owner's recorded stdout; final branch-head log still required |
| Shared lobby | Passed; 43 contracts and 50 fake backend create/leave cycles in the final isolated source harness, including command-error reset cases; full integrated rerun pending | Implementation owner's final source-harness stdout; `/tmp/prime-rmlui-lobby-check.log` preserves the earlier 38-contract binary run |
| Existing `-menubackdropcheck` | Passed; 65 content-free chamber/style contracts | Implementation owner's earlier local run; final integrated rerun pending |
| Actual Metal compositor/recovery | Passed; 20 assertions on Apple M4 Pro with `--recovery`, pixel output, resize/zero-size suspension, device reconstruction, shutdown/reentry; no validation errors | `/tmp/prime-rmlui-metal-final-check.log` |
| Actual Vulkan/MoltenVK compositor/recovery | Passed; 20 assertions on Apple M4 Pro with the same recovery/lifetime cases; no validation errors | `/tmp/prime-rmlui-vulkan-final-check.log` |
| macOS native compile matrix | Passed arm64/x64 with GL2 and neutral adapters | `/tmp/prime-rmlui-gl2-build.log`, `/tmp/prime-rmlui-neutral-build.log`, `/tmp/prime-rmlui-osx-x64-gl2-build.log`, `/tmp/prime-rmlui-osx-x64-neutral-build.log` |
| Android NDK bridge | Passed arm64-v8a/x86_64 neutral compile and 16 KB payload validation | `/tmp/prime-rmlui-android-arm64-build.log`, `/tmp/prime-rmlui-android-x64-build.log` |
| Android signed APKs | Opt-in native-assets APKs built for `android-arm64` and `android-x64`; AOT disabled with `RunAOTCompilation=false` | `/tmp/prime-rmlui-android-package.log`, `/tmp/prime-rmlui-android-x64-package.log`; RID APKs in `src/MphRead.Android/bin/Release/net10.0-android36.0/` |

The `/tmp` evidence paths are local diagnostics and are not committed outputs
or portable release artifacts. Earlier `CS0214` tool failures were repaired and
superseded by the successful native checks above. An attempted combined Android
RID invocation failed with `NETSDK1083`; successful APK evidence is for the two
separate RID builds. No result above is a passing current-head CI or post-rebase
check. Final integrated Release/default/server/screenshot checks remain pending
at this assessment; the frozen source inventory still refers to `6a24a39d` and
the intended integration base is remote `59a184255b24d922c3ad6e6c2e74435193208017`.

Reproduce the relevant foundation checks from the repository root with a working
.NET 10 SDK, CMake/dependencies and the platform prerequisites documented in
[native build instructions](../../tools/rmlui/README.md). These are rerun commands,
not assertions that they have passed on a different machine or branch head:

```sh
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadRmlUi=true
dotnet run --project tools/launcher-router-check -c Release
dotnet run --project tools/lobby-controller-check -c Release

# Real backend-neutral RmlUi core and managed/native host.
bash tools/rmlui/build-native.sh osx-arm64 draw-list
dotnet run --project tools/rmlui-core-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib
dotnet run --project tools/rmlui-runtime-check -c Release -- --native \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  src/MphRead/Mods/Launcher/RmlUi/Assets

# Rebuild GL2: adapters share the RID artifact path.
bash tools/rmlui/build-native.sh osx-arm64 gl2
dotnet run --project tools/rmlui-route-check -c Release -- \
  artifacts/rmlui-native/osx-arm64/libProjectPrime.RmlUi.Native.dylib \
  --capture /tmp/prime-rmlui-native-rerun
dotnet run --project tools/rmlui-compositor-check -c Release -- metal --recovery
dotnet run --project tools/rmlui-compositor-check -c Release -- vulkan --recovery

bash tools/rmlui/build-native.sh osx-x64 gl2
bash tools/rmlui/build-native.sh osx-x64 draw-list

# ANDROID_NDK_ROOT must point to the installed pinned NDK.
bash tools/rmlui/build-native.sh android-arm64 draw-list
bash tools/rmlui/build-native.sh android-x64 draw-list
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -r android-arm64 \
  -p:MphReadRmlUiNativeAssets=true -p:RunAOTCompilation=false \
  -o publish/rmlui-android-arm64
python3 tools/rmlui/verify-runtime.py --apk \
  publish/rmlui-android-arm64/com.projectprime.game-Signed.apk android-arm64
dotnet publish src/MphRead.Android/MphRead.Android.csproj -c Release -r android-x64 \
  -p:MphReadRmlUiNativeAssets=true -p:RunAOTCompilation=false \
  -o publish/rmlui-android-x64
python3 tools/rmlui/verify-runtime.py --apk \
  publish/rmlui-android-x64/com.projectprime.game-Signed.apk android-x64
```

Foundation contract/compile checks cannot establish physical input, real
multiplayer/lobby return, Unicode/IME, modern GPU/device loss or packaged
production parity outside the named observed tests/environments. The native input
harness proves measured GLFW/DOM routing, while the full game host, hardware
controller, touch and IME matrix remains open. The real compositor tests establish
specified macOS GPU/recovery cases; Windows/Linux/Android and complete native
document/gameplay acceptance remain open. There is no default/canary rollout or
client/Studio Avalonia removal.

## Evidence still required

- [ ] Foundation contracts: exactly-once network pumping/start handoff, server rejection, session-isolated rule drafts and one pointer conversion.
- [ ] Native host: real DOM/actions, version/capability compatibility, shutdown/reinitialize and stale-document cancellation.
- [ ] Full launcher and running-match overlay: actual window input, gameplay transition/return and dedicated/local server cleanup.
- [ ] Desktop matrix: OpenGL, DX12, Vulkan and selected macOS modern backend, packaging and device recovery.
- [ ] Android: packaged ABI plus physical input/IME/safe-area/background/resume and surface recovery.
- [ ] Legacy/RmlUi golden screenshots and hit traces at 720p/900p/1080p/1440p/4K, 1x/fractional/Retina, form factors and fullscreen.
- [ ] Same-machine Avalonia/RmlUi cold launch, idle CPU/redraw, p95 UI frame cost and CPU/GPU resource measurements.
- [ ] 100 page cycles, 50 lobby create/leave cycles and 20 match-return loops, or an explicitly justified revised acceptance target.

The frozen documentation-only audit claims no screenshot/performance/device
results. The separate local foundation assessment records the limited native,
GPU and lifecycle observations above; it does not accept the full golden,
performance, real-online, physical-device or release matrix. Proposed targets
remain requirements until actual artifacts and commands are recorded against
the tested integration revision. Cancellation and skipped/stale CI never count
as a passing acceptance result.
