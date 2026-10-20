# RmlUi UX audit — 2026-10-19

## Scope and evidence

Source audit of tracked `origin/main` RML, RCSS, shell composition and page management. Findings below distinguish source-demonstrable constraints from visual risks requiring runtime qualification. All paths in the inventory are relative to `src/MphRead/Mods/Launcher/RmlUi/Pages/`. There are 86 authored page documents, plus the shared app-shell/dialog templates and the legacy `Assets/prime_home.rml` asset. Inventory includes retained compatibility documents; the existence of a document does not prove that every production route currently mounts it.

`RmlUiMenuPages.cs` explicitly mounts Home, Play browser, Lobby, rules, maps, hunter identity, admin, offline setup/arena/training/rules, adventure saves/confirmation, theatre library and party queue. Other native presenters own remaining routes. `RmlUiPageManager.cs` already rejects delayed/duplicate intents and dispatch from background documents, caps modal nesting, restores focus, and cancels work with document lifetime. No measured rendering or scheduling performance regression was established by this source audit; avoid changing ownership or async protocols on speculation.

## Historical comparison

The shared Components and Home authored assets have no diff between `origin/feature/rmlui-primary-ui` and `origin/main`. The native runtime comparison adds hunter beam/beam-color actions and extends lobby/offline rule intent ranges; those restorations should remain intact. Historical work is useful as a reference, but wholesale replacement with that branch would lose current intent coverage. `origin/poc/rmlui-home` is an earlier prototype and should not replace the composed runtime.

## Findings and bounded repairs

- **P1 — narrow-shell route access:** `Components/app-shell.rcss` hides `#profile` below 540dp. `Components/app-shell.rml` places the `license:open` action on this control, while its footer only supplies Quit. Preserve an explicit compact Profile/License route and a valid focus chain. The consolidated shell preserves a compact Profile action and uses two header rows below 640dp.
- **P2 — clipped touch Settings categories:** `Pages/settings/native-full.rcss` gives compact category strips a fixed 34dp height and hides vertical overflow; `Themes/prime.rcss` gives touch buttons a 48dp minimum. The compact subpage strip also caps height at 27dp. Touch-mode strips accommodate 48dp controls. Short touch windows use a scrollable vertical category rail and one form-panel scroller to keep whole fields reachable.
- **P2 — unbounded social confirmation:** `Pages/social/social.rcss` centers `#social_confirmation` without a height bound or scrolling. Expanded text and short windows can put actions outside the viewport. Repair caps it at 92vh and enables vertical scrolling.
- **P2 — unbounded Quick Settings:** `Pages/settings/quick.rcss` previously flowed an unconstrained panel inside the shell's clipped stage. Short windows could hide Apply/Discard/Back. The panel is anchored inside the stage; its content scrolls while Apply/Discard/All Settings/Back remain in a fixed action bar.
- **P2 — versions content wider than its card:** `Pages/updates/versions.rcss` used viewport widths for descendants inside a card with percentage width and fixed padding. At narrow widths, child text, rows and cells can exceed the padded content area. The final repair uses a shared explicit viewport width budget (88vw/640dp card, 80vw/600dp children) because RmlUi 6.3 resolves percentage-only intrinsic modal text too narrowly. The budget leaves room for padding at the tested 480dp minimum.
- **P2 — compact Home rail legibility risk:** `Pages/home/home.rcss` reduces utility rails to 28vw/26vw while preserving rich copy and several controls. At narrow widths these become very small reading and interaction areas. Runtime checks should inspect actual text wrapping, party controls and activity drawer behavior, including large-text/touch preferences. Utility rails now use bounded readable proportions while keeping a clear center at ordinary landscape widths.
- **P3 — accumulated cascade complexity:** shell and Home styles contain multiple sequential revisions of the same breakpoints and selectors. Consolidation can improve maintainability, but only with screenshot/bounds coverage for normal, narrow, short, large-text and touch modes. Preserve page actions and application-owned tab selection.

The profile issue is a route-availability finding. The remaining layout findings require rendered qualification at the viewport and accessibility combinations in which they occur. No claim of complete visual verification is made by source inspection alone.

## Complete authored-page inventory

| Area | Documents |
| --- | --- |
| Adventure | `adventure/confirm.rml`, `ending.rml`, `saves.rml`, `ship.rml` |
| Common | `common/quit.rml` |
| Community | `community/browser.rml`, `flow.rml`, `native-discover.rml`, `native-import.rml`, `native-publish.rml`, `native-report.rml`, `native-revisions.rml`, `native-transfers.rml` |
| Home | `home/home.rml` |
| HUD | `hud/editor.rml`, `native-unsaved.rml` |
| Hunters | `hunters/identity.rml`, `local-picker.rml`, `native-picker.rml`, `page.rml`, `selection.rml`, `strip.rml` |
| In game | `ingame/aim-results.rml`, `confirm.rml`, `journal.rml`, `pause.rml`, `results.rml` |
| License | `license/license.rml`, `native-overview.rml` |
| Lobby | `lobby/admin.rml`, `lobby.rml`, `maps.rml`, `rules.rml` |
| News | `news/detail.rml`, `news.rml` |
| Offline | `offline/arena.rml`, `rules.rml`, `setup.rml`, `training.rml` |
| Play | `play/browser.rml`, `queue.rml` |
| Settings | `settings/capture.rml`, `conflict.rml`, `glyphs.rml`, `native-full.rml`, `native-visual-editor.rml`, `quick.rml`, `reset.rml`, `settings.rml`, `setup.rml`, `unsaved.rml`, `video.rml` |
| Setup | `setup/native-game-files-confirm.rml`, `native-game-files.rml`, `rom.rml`, `setup.rml`, `update.rml` |
| Social | `social/confirm.rml`, `native-party.rml`, `social.rml` |
| Start | `start/splash.rml` |
| Startup | `startup/startup.rml` |
| Studio | `studio/entry.rml`, `native-editor.rml`, `native-entry.rml`, `project-change.rml` |
| Theatre | `theatre/bookmark-remove.rml`, `bookmarks.rml`, `camera-confirm.rml`, `camera-tools-confirm.rml`, `camera-tools.rml`, `camera.rml`, `confirm-delete.rml`, `editor-confirm.rml`, `editor.rml`, `export-confirm.rml`, `export.rml`, `library.rml`, `playback.rml`, `ranges.rml`, `spatial.rml`, `studio-tools.rml`, `studio-workspace.rml`, `timeline.rml` |
| Updates | `updates/available.rml`, `versions.rml` |

## Screen-group review and implementation decisions

All 86 documents in the inventory were examined for authored structure, stylesheet ownership, action bindings and duplicate element IDs. There are no duplicate IDs within any individual page document. Shared template IDs are composed independently per document. Source review is distinct from the executed native measurements recorded below.

| Screen group | Qualitative source finding and decision |
| --- | --- |
| Adventure (4) | Save selection and destination actions stay with their controller. Text previously used viewport widths inside padded panels; use parent-relative widths and let the confirmation itself scroll. Ship/ending pages retain their existing page owners and bounded flow. |
| Common (1) | Quit uses the existing update-card modal surface and two explicit actions. No action or modal ownership change is needed. |
| Community (8) | Discovery's eight entries, exact revision metadata and install actions are the main reading burden. Compact collection rows and earlier primary actions improve access. Import/report/publish/revisions/transfers already use bounded modal/scroller layouts; preserve exact package/service authority. Browser/flow compatibility documents remain present. |
| Home (1) | Both utility rails have independent scrolling and the center remains engine-owned. Replace tiny fractional-width compact rails with readable proportions; retain drawer, party and news actions. Shell Profile remains reachable on narrow windows. |
| HUD (2) | Editor and unsaved confirmation retain their existing preview/inspector ownership and actions. No targeted change is justified by this audit; generated control coverage belongs to the editor harness. |
| Hunters (6) | The active standalone picker is `native-picker.rml`; `page.rml` is a separate retained route. Selection/cosmetic options become one scroller and Equip/Back move into a separate action group. All existing action strings and IDs are preserved. Lobby identity, local picker, selection and strip documents remain independent. Short touch layouts need particular care when pinned action height consumes the options region. |
| In game (5) | Pause, journal, match/aim results and confirmations already use their own match/aim panels and explicit actions. Preserve gameplay overlay ownership; no gameplay or vote behavior is changed by this presentation work. |
| License (2) | Native overview uses a single spread scroller with a pinned footer. Its account/history/stat/catalog faces remain controlled by presenter visibility. The retained license route adopts bounded content scrolling. No new loadout controls are introduced into License. |
| Lobby (4) | Roster rows must resist flex shrinking and keep team/player controls readable. Rules, map picker and admin remain modal-owned. Preserve the screen-space model anchors, authoritative ready/start gating and map selection behavior. |
| News (2) | Listing/detail actions and article flow remain intact. No additional page-local change is justified by the examined structure. |
| Offline (4) | Setup, arena search, rules and training retain their bounded columns/dialogs and explicit draft/apply/cancel controls. No action or route change is needed. |
| Play (2) | Browser and connect panels should consume available stage height; the server list owns scrolling. Queue remains a distinct modal route. Create/connect actions and networking state remain unchanged. |
| Settings (11) | The full native form remains the active main Settings route. Its category/subpage strips must account for the 48dp touch minimum. Quick Settings needs viewport bounds and compact spacing. Capture/conflict/reset/unsaved/video/visual editor and retained setup/glyph surfaces keep their existing draft and confirmation actions. |
| Setup (5) | Game-file setup and its confirmation retain their installation/import boundaries. ROM/setup/update compatibility pages remain authored and linked. No new installation behavior or background operation is introduced. |
| Social (3) | Main list/detail content uses one page scroller and wrapping actions; long confirmation content needs its own bounded scroll surface. Party and confirmation documents keep their existing authority and Back behavior. |
| Start (1) | Splash retains Continue/Quit actions and its narrow/short typography rules. No additional presentation change is justified by source structure. |
| Startup (1) | Startup remains a separate first-run/loading surface with its existing action and art ownership. No action change is needed. |
| Studio (4) | Chooser, editor and project-change dialogs retain their specialized workspace controls and scrolling. Header role projection, including the two Studio destinations, stays application-owned. Do not replace editor structures with the game picker layout. |
| Theatre (18) | Library, playback, editor, ranges, bookmarks, camera/spatial/tools/export/timeline/workspace and confirmations retain their specialized shared styles and large generated action sets. No speculative virtualization or owner rewrite is justified by the source audit. Existing timeline/workspace tests remain the appropriate behavioral qualification. |
| Updates (2) | Available/Versions retain selection/cancel/confirm ownership. Version rows and supporting text must fit the padded card, and native initial-layout behavior must be checked when replacing viewport widths with percentages. |

Markup review confirms the action-string multisets are identical before/after for both changed documents, `hunters/native-picker.rml` and `community/native-discover.rml`. Their changes alter grouping/order, not controller commands. Shell selectors still give application-selected `.nav.active` precedence over page-local default tab colors. The high-contrast active rule includes both `#screen` and `#primary_nav`, so its active treatment outranks blanket high-contrast button rules. Touch scrollbar selectors include a preference class and therefore outrank shared tag-only scrollbar sizing.

The engine Hunter preview uses the measured `native_hunter_preview` rectangle and rejects rectangles outside the main picker panel, stage or framebuffer. Making a preview ancestor scrollable requires retaining those containment assumptions: scrolling controls independently is preferable to moving the engine rectangle underneath other UI. This is a code-review constraint, not a claim that CPU draw-list fixtures render the game model.

## Implemented design and interaction changes

- Consolidated the accumulated shell overrides into one geometry definition and responsive rules. Normal chrome uses a 64dp header and 44dp footer; compact windows retain every game destination and Profile in two rows. Spacing follows 4/8/12/16/24/32dp, with 12dp mouse scrollbars and the existing 24dp touch preference.
- At 1280×720, the shared stage grows from 587dp to 611dp (24dp, 4.1%). At 1920×1080 it grows from 919dp to 971dp (52dp, 5.7%). Compact two-row navigation intentionally spends height to preserve readable routes.
- Home uses compact activity/news/session panels and bounded rails. The center remains reserved for the existing engine-owned Hunter composition.
- Community renders eight paginated results in two columns on wide screens, moves paging above the collection and install/host actions above metadata, and wraps secondary actions into compact columns. Existing asynchronous loading and exact-package authority remain intact.
- Hunter customization uses compact cosmetic groups and one draft scroller. Equip/discard/license/back stay outside that scroller on ordinary windows. Insufficient-height touch windows scroll the whole draft column so all 48dp controls remain reachable. Preview controls scroll separately in touch mode, keeping the native model rectangle stationary.
- Lobby retains readable roster minimums instead of crushing eight player controls with flex shrink. Short rosters scroll intentionally. Server Browser uses available stage height instead of a 260dp cap, with independent bounded directory/connect panels.
- License uses compact statistic panels and one spread scroller; persistent footer actions remain visible. Social/Party use compact rows, consistent wrapping commands and bounded confirmation dialogs. Campaign text sizes against its panels; confirmation descriptions no longer add a nested scroller.
- Settings changes are limited to existing presentation: touch containment and fixed Quick Preferences actions. Existing category/subpage/filter/draft behavior remains unchanged; further migration is separate work.

No backend, game logic, native bridge, renderer, wire protocol, service ownership, document rebuilding strategy or settings migration was changed. Existing revision gates, per-document binding caches, scheduling, selection/scroll preservation and modal focus ownership already address the main runtime risks. There was no evidence justifying speculative virtualization or an ownership rewrite.

## Validation

Detailed executed results and durable evidence follow below. Raw native reports, all captures and Windows logs remain in the task's `artifacts/rmlui-ux-20261019/` directory. Diagnostic `before/after` and early `*-final` outputs are superseded by the final receipts identified below.

Windows x64 `DESKTOP-94724L4` was the primary test host. The native GL2 bridge was rebuilt from exact tracked source with pinned RmlUi 6.3/FreeType, then used for both asset sets. Baseline assets identify `2d18ac057`; subsequent rebases through `d51605175` introduced no UI/native bridge changes. The final report and benchmark match all **148** authored source-file hashes, including the final Quick Settings markup. See [the committed validation receipt](rmlui-ux-2026-10-19/validation.json) for library/tool/source hashes, dimensions, summaries, timings and capture hashes.

| Validation | Result |
| --- | --- |
| Full authored inventory lifecycle | 86 documents × 12 framebuffer/density cases: every initialize/open/update/render/close succeeded |
| Focus, action reachability, content width, touch and large-text fixture checks | **2,187 checks; baseline 33 failures → candidate 0** |
| Actual native GL offscreen captures | 72 PNGs per asset set: 8 populated fixtures × 9 resolutions; RGBA8/stencil framebuffer targets at exact requested dimensions |
| Actual Windows interactive GL | Lobby 1280×720 and 800×600; License 1280×720; Studio workspace 1280×720: all exit 0 |
| Included interactive coverage | Native pointer/Enter/controller dispatch, disabled/start authority, chat focus/text, license tabs/account/history, Home rails, version selection/cancel, Settings categories and workspace ownership |
| Windows managed suites | Runtime, Hunter, Community, License, Quick Settings and Social passed; Quick Settings rerun after its final action-bar change |
| Actual native scheduling | 51 scheduling assertions passed, including dirty/idle/timer/document replacement behavior |
| Mac build | `dotnet build tools/rmlui-opengl-check -c Release`: zero warnings/errors |
| Mac managed runtime | Host lifecycle, ABI, scheduling, DPI, Unicode, clipboard, range/IME seams and repeated initialization passed |
| Assets and actions | 148 markup/styles resolve local links; original element IDs and action-string counts retained; `git diff --check` passed |
| Hosted CI | Not awaited; the user explicitly requested immediate merge after completion |

Density-1 physical targets are **1280×720, 1920×1080, 2560×1440, 3840×2160, 1280×800 (16:10), 2560×1080 (ultrawide), 800×600, 640×360 and 480×320**. Density-2 physical targets are 1280×720, 2560×1440 and 3840×2160, corresponding to logical 640×360, 1280×720 and 1920×1080. Touch fixtures run in every case; large-text fixtures run at logical 640×360 and 1280×720. The smallest case is a stress test; short touch controls retain scrolling when their full 48dp targets cannot fit simultaneously.

All 144 native captures were reviewed in overview sheets, with full-size review of the changed Community, Hunter, License, Social, Quick/Full Settings, Versions and compact Profile layouts. Captures use synthetic service data. Blank Hunter/portrait regions in the standalone UI fixtures are reserved for engine/image content; these fixtures do not render cartridge models. The existing interactive Home/License/workspace harnesses separately exercised real native presentation with their own controlled service fixtures.

## Before and after

The following are actual native OpenGL renders into offscreen targets, with the same fixture data, native library and harness for each comparison. They are not browser approximations. More resolutions and all raw reports remain under the local artifact directory named above.

| Surface | Before | After | Visible change |
| --- | --- | --- | --- |
| Community, 1280×720 | [Before](rmlui-ux-2026-10-19/community-before.png) | [After](rmlui-ux-2026-10-19/community-after.png) | Two-column results, earlier paging/install/host, compact secondary actions |
| Hunter, 1280×720 | [Before](rmlui-ux-2026-10-19/hunters-before.png) | [After](rmlui-ux-2026-10-19/hunters-after.png) | Compact cosmetic groups and persistent equip/back commands; model rectangle retained |
| License, 1280×720 | [Before](rmlui-ux-2026-10-19/license-before.png) | [After](rmlui-ux-2026-10-19/license-after.png) | Wider useful statistics area and one spread scroller |
| Quick Settings, 640×360 | [Before](rmlui-ux-2026-10-19/quick-settings-before.png) | [After](rmlui-ux-2026-10-19/quick-settings-after.png) | Previously unreachable actions become a fixed bottom action bar |
| Compact Profile, 480×320 | [Before](rmlui-ux-2026-10-19/compact-profile-before.png) | [After](rmlui-ux-2026-10-19/compact-profile-after.png) | Profile and all destinations remain available in two short header rows |
| Versions, 480×320 | [Before](rmlui-ux-2026-10-19/versions-before.png) | [After](rmlui-ux-2026-10-19/versions-after.png) | Explicit native-compatible width budget contains long release names and actions |

## Performance measurements

`tools/rmlui-layout-check/benchmark.py` compares the same two asset sets in **ABBA order**, warming every fixture once per arm and recording 20 cycles per arm: **640 samples, zero errors**. Each variant has 40 samples per page. The test measures native CPU document composition, synthetic publication, layout/draw-list capture and immediate idle calls at 1280×720, density 1. It does not measure GPU time or game FPS. Full medians/p95 and draw counts are in the committed receipt.

| Screen | Median open ms, before → after | Median initial update/render ms, before → after |
| --- | --- | --- |
| Home | 1.654 → 1.554 | 1.102 → 1.060 |
| Hunter | 0.962 → 0.853 | 0.644 → 0.662 |
| Community | 0.974 → 0.978 | 0.758 → 0.898 |
| License | 1.121 → 1.549 | 0.659 → 0.658 |
| Social | 1.137 → 1.102 | 0.834 → 0.804 |
| Quick Settings | 0.569 → 0.545 | 0.284 → 0.291 |
| Full Settings | 10.155 → 9.977 | 4.181 → 4.199 |
| Versions | 0.398 → 0.389 | 0.199 → 0.192 |

Results are mixed and small in absolute time. Home composition improves by 6.1% and initial layout/capture by 3.8%. Community's larger visible collection increases initial capture by 0.140ms; its draw commands increase from 94 to 105. License composition costs an additional 0.428ms while its initial update/render is unchanged. These one-time costs are disclosed rather than presented as universal performance gains. Median idle calls remain approximately **0.6µs** across the fixtures, and existing native scheduling regressions pass. No runtime allocation, texture-churn, GPU, sustained frame-time or Android performance improvement is claimed.

## Remaining qualification and improvements

- **P1 platform qualification:** physical Android touch/gamepad, device safe areas, actual cartridge Hunter meshes at every new viewport, IME delivery and service-backed online workflows still need dedicated device sessions. Native synthetic touch classes and controller dispatch do not substitute for those checks.
- **P2 visual breadth:** every document passed native lifecycle testing, but detailed layout/focus assertions and before/after captures cover eight representative populated fixtures. Very long localized strings, every generated editor row, all modal states and all live service error combinations are not exhaustively proven.
- **P2 performance:** the License open-cost increase and Community initial-capture increase are measured above. Profile these further if transition latency is observed in real use. Full Settings still has a large retained authored form; migration/virtualization remains a separate project and was not expanded here.
- **P3 maintainability:** the shell cascade is consolidated. Several specialized page styles still contain historical overrides; additional cleanup should retain the new native geometry/interaction coverage.
- Hosted CI/review completion was deliberately not a merge prerequisite. This report records local and Windows results, not a claim that future hosted jobs passed.
