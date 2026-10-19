# Project Prime RmlUi historical parity audit
Date: 2026-10-17
Source of truth: `backup/main-2026-10-09` versus current `main` and RML-29..32 stacked candidates.

**Current status, October 19 / RML99: accepted and delivered for the tested Mac RmlUi restoration scope.** [RML99 replay parity](../implementation/rmlui-replay-parity-rml99-2026-10-19.md) records current Main/Studio, following-B, detached-export, layout and packaged/extracted acceptance and the delivered archive. It supersedes the earlier RML98-in-progress status. [RML98 restoration](../implementation/rmlui-regression-restoration-rml98-2026-10-19.md) and [RML97 acceptance](../implementation/rmlui-migration-completion-rml97-2026-10-18.md) retain their separate frozen evidence. Tables, roadmap items and unresolved wording below are historical source-audit records, not current qualification claims; use RML99 for the latest accepted scope and limits. No broad hardware/performance, remote-service or cross-platform acceptance is implied. Map Studio remains intentionally Coming soon.

Continuation on October 17: main `dc7f0970` includes RML-33..38, including the
previously open #698..700 and the subsequent #701.
The implementation sequence below is the original audit roadmap; its slice
numbers are not the final merged PR numbering. Current work continues as
[RML-39 career navigation](rmlui-license-career-rml39.md) and
[RML-40 privacy opt-out](rmlui-license-privacy-rml40.md). See the [execution tracker](../implementation/three-fork-audit-progress-2026-10-17.md)
for the user's CPP-01 skip decision and remaining CPP-02 acceptance.

Direct user steering allows independent implementation tasks to run in parallel;
RmlUi is not a hard blocker. Any new or changed product UI must use RmlUi.
The historical roadmap below is source material, not a higher-priority instruction.

User scope revision, October 18: full campaign playthrough and ending/credits
validation are excluded from the current plan and release gates. Focused
campaign UI, startup, save, import and audio lifecycle checks remain; current
runtime validation is Mac-only.

October18 continuation: [RML79–80 restoration](../implementation/rmlui-parity-restoration-rml79-rml80-2026-10-18.md) records actual original-feature gaps and bounded acceptance. Weapon preview, dirty-navigation feedback, Home labels, filtered selection, two Smart Views and leased game invitations/friend joins are restored. Actual native Social checks exposed two page-startup binding defects, now fixed. Live backend qualification remains open. Older table wording about complete Social/persistent Home Party data and rich Theatre/editor handoffs is historical; consult the continuation for current feature gaps and exact evidence identities.

## October 18 scope revision: Prime Studio split

The user disabled Map Studio for this phase. Its product destination is a disabled
**Coming Soon** card; retained map editor, import, build and publish owners are
future implementation sources, not outstanding migration deliverables. The native
hub must not read the map catalog or acquire an editor/document owner.

Prime Studio is a separate application with the existing Replay Editor. Main
Project Prime exposes basic Theatre viewing; authoring belongs to Prime Studio.
Header roles and typed route/backend capabilities must enforce this boundary.
The latest qualified routing is recorded in RML-95 below: the game header opens
an internal chooser; its explicit workspace cards launch the separate app.

A source audit identified remaining reachable Avalonia paths: full Settings from
native Quick Settings and in-game Settings; HUD editing through SettingsView;
setup failure recovery through SetupScreen; native pause/results/campaign failure
recovery; explicit version management; and constrained Create Lobby fallback.
The automatic update offer is native in RML-96; release browsing/switching and
installation progress are still separate remaining presentation work.
Replay recovery now preserves the main-viewer/Studio-editor role guard and fatal
native-host failure closes instead of reverting to old UI. Map authoring source
remaining on disk is dormant; its placeholder acquires no project owner. Full Settings and HUD remain
the last planned migrations. This paragraph supersedes the historical Native
Studio row and roadmap wherever they still describe active map-authoring work.

## Definition of parity

A page is **not complete** merely because `.rml` and `.rcss` compile.
It must have all of: exact route action and Back/focus ownership; real
authoritative data; correct permissions and asynchronous cancellation;
original foreground/background visual composition; desktop frame acceptance;
Android-specific input/lifecycle where applicable; and a supported fallback
when a backend is not migrated.

## Verified structural gaps and current implementation

| Area | Historical design | Current implementation / issue | Next acceptance |
| --- | --- | --- | --- |
| Home 3D Hunter | Actual engine mesh, suit, animated deployment chamber, accent and atmosphere | Actual cartridge-backed native GL2 Home/picker/lobby fixtures pass at 640×360, 1280×720 and 1920×1080, including all seven Hunters and four suits. Short-window picker composition, footer/focus and eight-pad extension pass 8,143 checks | Physical devices, focus/resize/re-entry, fallback, reduce motion; menu proof does not certify full-world rendering |
| Lobby 3D formation | 8 pads, exact network roster, local front, distinct animated models | RML-29 restores per-slot previews, deterministic server roster and pad/label anchor table | Solo through eight clients, bots, churn, stale generation, team colors, 60/120/240Hz |
| Lobby data/actions | Ready count, teams, chat, match time/score, real map preview, owner actions | RML-30 / #693 binds authoritative chat, rules and team/roster fields. Local continuation adds bounded asynchronous PNG-to-TGA thumbnails through the existing native image binding | Owner/non-owner, team lock, chat length, map package, locale, DPI and no flicker |
| Standalone Hunter selection | Interactive real mesh/suit, cosmetics and preview camera | RML-31 / #694 restores isolated engine viewport using selected draft. Local RML-41 restores Biped/Alt, rotation, zoom, death, loop and reset controls | Biped/alt/death, cosmetics, all Hunters, fallback and GL state restoration |
| Home feature rail | Populated news dispatch and live session/social panel | RML-32 / #695 restores bundled News and real session state. Local RML-45/46 connects verified Friends/Party/presence; no invented metrics | News content, navigation, no dead buttons; separate authenticated Social backend |
| Native Social/Party | Friends, invitations, online players, presence, party ownership, privacy | Local RML-45/46 restores historical backend and connects native Party, privacy-aware presence and account/revision witnesses; travel/seat UDP integration remains open | Live account identity, invites, Party lifecycle, presence/privacy, offline retry, authorization |
| Community | Discovery, install/download, creator publish, revisions and cleanup | RML-37/38 verified install and owned archive/restore continue. Native revision history, historical installation and confirmed Publish/Unlist/Draft/Delete/Restore pass 430 synthetic native checks at each of three sizes. New-package publishing and durable account-bound retries are being migrated | Live backend verification, exact package hashes, durable transfer, no UI stalls, safe scene ownership |
| Hunter License | Customization, career/stats/history, achievements, account and privacy | RML-39 adds native career Stats, eight-row recent History, derived Achievements and Benchmark; RML-40 adds account-bound private opt-out with server reread. Local RML-41 connects Account actions, cosmetic selectors, explicit publish confirmation and existing Hunter preview owners | Live two-account/privacy acceptance, screenshot parity, cosmetic entitlement/recovery and device input |
| Theatre/Replay | Full library, filtering, playback, clips, recovery, Studio editing | Native file work, transport, bookmarks, range marks, materialized saving/cancellation and virtual clips use existing owners. Camera/lens/keyframe editing passes native fixtures at three sizes; actual view/projection capture is fenced by successful presentation. Rich cinematic/export workflows still have existing-editor handoffs | Historic protocol, map hash, validated file stamp, export fidelity, editor session ownership; synthetic UI checks do not prove full-world camera rendering |
| Offline/Aim Lab | Match rules, bots, drills, training configuration | Local RML-42 adds validated transactional advanced rules; native specialized drill configuration uses existing trainer definitions | Challenge presets, saved inputs, bounds, simulated match acceptance |
| Adventure | Three saves, campaign menus/mission flow, effects | Three-slot read/overwrite/resume, discovered-only mission map/scan logbook, genuine ending receipt/credits and five-destination gunship cockpit/star map are native through existing campaign owners | Focused Mac startup/scene entry, save management, menu/input, import and audio lifecycle checks; detached desktop ship/journal/ending fixtures pass. Full campaign/ending validation is removed from scope; Android runtime is deferred. |
| Native Studio | Creator/Map/Replay launch, workspace lifecycle, data handoff | Native project/template/recovery browsing, create/open/duplicate and confirmed saved-project deletion use the retained editor owner. A native editable viewport/inspector is being implemented through that same document/history/autosave owner | Native editor save/preview, advanced editing and data handoff; retained editor regression passes 486 checks |
| In-game/pause/results/HUD | The original RML shell and composition contracts | Local RML-44/48/49 wires native scene-bound pause/results/Aim/replay transport/journal; HUD source is restored but full Settings draft handoff remains open | Real pause, rematch, vote, replay, HUD and device input tests |
| Android | RML resources, draw-list ABI, GLES presentation, touch/IME, lifecycle | Earlier opt-in APK passes packaging/alignment/signature checks using NDK28.2 but predates current pages. Native input/resize, verified APK extraction and explicit opt-in Pause/Journal pass source fixtures and the actual Android experiment build. Scene-free native local launcher is being implemented | Current-source native APK, device boot, touch, IME, background/foreground, thermal/frame pacing; package-disabled compilation is host/API evidence only |
| Full Settings | Category-based reversible graphics/audio/input/network controls | **Deliberately deferred.** Existing working Settings remains the owner until every other page is migrated | FINAL slice: video rollback, failure/recovery, user profiles, safe persistence |

## Recommended review/implementation order

1. **RML-29..32**: complete Home/Lobby/Hunter 3D and currently authored P0
   controls. Merge only after CI, keep opt-in and screenshot against the
   October 9 build. Do not infer visual parity from source presence.
2. **RML-33**: full Home Social and Lobby Social backed by real current
   account service, not placeholder counts. Port the service explicitly if
   missing, with privacy gates.
3. **RML-34**: authenticated Community install, durable transfer progress,
   local map identity, and creator workflows.
4. **RML-35**: Hunter License/account/career/customization, all interactive tabs,
   current model-preview materials and proper error state.
5. **RML-36**: Replay/Studio and Adventure rich-screen parity, and editor
   process ownership / transfer index.
6. **RML-37**: full in-game/pause/results/HUD/back/focus contract.
7. **RML-38**: Android surface/touch/IME/resume and desktop backend visual
   acceptance, native fallback and incremental performance benchmarks.
8. **FINAL**: full Settings migration, then explicit default UI cutover.

## Invariants

- No invented game/roster/network authority or optimistic server admission.
- Preserve immutable installed Community map package identity.
- Keep historical replay readability and studio external-process ownership.
- No renderer recreation per page or per frame; keep one scene-free side
  Scene for menu Hunter preview and one GL context.
- All mutable drafts are versioned and cancellable. A hidden/retired modal
  cannot deliver stale commands.
- Do not merge any source merely because a textual `check-pages.py`
  inspection passes. Capture visual evidence across source and target
  dimensions, verify frame timing and GPU resource stability.
- Full Settings **last**.

## October 18 — exclusive RmlUi completion

The user now prioritizes completing RmlUi exclusively. Separate networking, presentation-performance, character, arena and accuracy work is paused. Actual replay-world runs remain in scope only to exercise native UI ownership, input, editing and return flows. Full Settings remains the final UI slice and must follow the current working SettingsView layout; see [the layout requirements](../architecture/rmlui-settings-layout-requirements-2026-10-18.md) for the requested usability changes.

RML81 completes all 14 original Theatre smart views and duration sorting (80 index assertions and 29 real sidecar/projection assertions), and the contextual Social commands pass 293 native checks at each of 640×360, 960×600 and 1280×720 with synthetic authorities. One actual production Shell/DemoPlayback run passes all 134 steps at 960×600, including held camera resize/reentry, native timeline editing, seek/rebuild, source-sidecar ABA rejection and normal teardown. The 1,979-frame durable source replay, configured setup and all 3,858 game files remain unchanged. This is bounded Mac UI acceptance, not physical-input, networking or general performance qualification. Evidence: `artifacts/rmlui-parity-presentation-rml81-20261018/`. RML82 Community transfers and Hunter comparison/default staging are implemented but undergoing corrected compilation and runtime acceptance.


### October 18 RML-83 lobby closeout

See [RML-83 lobby restoration](../implementation/rmlui-lobby-restoration-rml83-2026-10-18.md) for exact implementation, preserved failures, scope and final Mac ARM delivery. The requested roster spacing, real map preview, empty-slot bot controls and original MPH menu trial are qualified at three actual desktop densities/sizes. Transfer detail accessibility also passes three sizes. Full RmlUi parity is still open; full Settings stays last and must follow the user's existing working SettingsView layout. A broader replay UI run closed during the paused timeline hold and remains incomplete; no full new replay acceptance is inferred from the bounded lobby results.


### October 18 RML-84 platform restoration and Home party seats

The latest user request supersedes the RML-83 art/color trial: restore the reference chamber/platform and blue/cyan UI, preserve map/bot repairs, and add three Home party platforms plus four Current Session seats. [RML-84 evidence and delivery](../implementation/rmlui-platform-layout-rml84-2026-10-18.md) records 699 native UI checks at three Mac sizes, five passing CPU suites and signed/extracted ARM package acceptance. The existing account-fenced Party controller is shared with Home; periodic reads retain same-account labels while pending and clear on failure or account change. Party member names/presence are authoritative; remote models await an appearance contract. Full RmlUi parity remains open; full Settings is still the final slice.

### October 18 RML-85/86 Community library and reporting

[RML-85 library](../implementation/rmlui-community-library-rml85-2026-10-18.md)
restores active Installed/Favorites, leased favorite changes, exact links,
published previews and metadata through existing owners. [RML-86 reporting](../implementation/rmlui-community-report-rml86-2026-10-18.md)
adds the native account-bound report modal and corrects accessible detail labels.
Five CPU suites, RGB preview checks and 788 native checks pass; a signed/extracted
Mac ARM build is delivered. Qualification is synthetic for account mutations;
live backend and physical-input acceptance remain open. Local package import is
the next native continuation. Full Settings stays last.


### October 18 RML-87 local package import and Retina layout

[RML-87](../implementation/rmlui-community-import-rml87-2026-10-18.md) removes the legacy Community import handoff. Native review/confirmation uses the retained installer and verified private snapshot. Five CPU suites, 24 actual-installer checks and 1,239 native checks pass; all 48 Retina captures are reviewed. Explicit layout fixes resolve input/action and row/paging overlaps. Full migration remains in progress; Settings stays last.


### RML-94 split qualification result

The requested separate native Prime Studio app and Main Theatre viewer split are
implemented and delivered in `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml94-20261018.zip`.
The hub disables Map Studio with Coming soon and grants no map editor/publishing
capability. Main creates no replay authoring backends and its editing commands/
shortcuts are refused; Studio retains the current native editor. Main library and
playback hand off logical paths through the restored bounded authenticated local
process protocol. Replay recovery no longer opens the old editor in either native
role. Fatal native-host failure logs and closes instead of activating legacy UI.

Eight CPU suites, 18 instance checks, three native header/hub cases, playback
capability GL checks and 93 actual replay-window steps pass. Both sealed ARM apps
pass packaged window/resize and extracted startup/inventory checks. Root visually
reviewed the Studio header text in its native screenshot because plain brand DIVs
are intentionally absent from the accessibility projection. No physical-input,
remote-service or full migration completion is claimed. Full Settings/HUD and the
other recovery paths listed above remain open; disabled Map Studio is future scope.


### RML-95 routing correction and native startup

The latest user request supersedes the RML-94 header behavior. PRIME STUDIO opens
its two-card chooser inside the game; only explicit OPEN MAP STUDIO / OPEN REPLAY
STUDIO commands launch the separate app. Map is still a Coming soon placeholder;
Replay opens the current editor library. No map owner or main-app replay authoring
capability is acquired.

Home restores Friends below four Current Session seats, two real bundled news
headlines, and independently scrolling compact utility columns. Hunters repaints
the procedural Home chamber before native UI and its isolated real mesh. Startup
now uses native RmlUi with the unchanged authored artwork and one-shot Press Start;
updates cannot cover the pending gate. The old startup bitmap is not constructed
when native UI is required. Updates/version prompts themselves are not yet native.

Client/server and nine tools compile cleanly; eight CPU suites and 33 authenticated
instance checks pass. Three native layout/input cases pass 1,427 checks; 113 actual
production Shell/replay steps and 3,174 real-model stage checks pass. Both signed
Mac ARM apps pass packaged render/resize and extracted startup/inventory checks.
The actual GL runs inject the process-launch boundary; CPU checks separately cover
real authenticated workspace forwarding. Physical input/live service/performance
and full migration acceptance remain open. See the RML-95 entry in the main tracker
for delivered ZIP identity, preserved failures and exact evidence paths. Full
Settings and HUD remain last; recovery/version routes are still tracked above.

### RML-96 centered Home, reliable tabs and native update offer

Home now measures the two native side rails and centers the Hunter viewport and
all four platforms within their gap. One uniformly scaled layout drives both the
model and chamber shader. The far platform occupies a reserved band above the
model viewport, including Trace's horns. Lobby formation is unchanged. Actual
cartridge-backed seven-Hunter/four-suit checks pass at 640×360, 960×600 and
1512×818 client sizes; root visually reviewed Trace at the latter two sizes.

PLAY explicitly returns Home after a native page, rather than resuming a retained
Theatre destination. Repeating the active native tab keeps its exact page owner.
The header highlight follows the foreground page, keyboard Left/Right includes
Theatre, and dirty Quick Settings visibly requires Apply or Discard. Actual Main
window runs exercise all 25 directed tab pairs at both 640×360 and 960×600, using
native keyboard events and production pointer mapping. Community discovery is
an empty read-only fixture; these runs do not certify remote services or physical
input. Three Main/Studio replay return runs pass 557 steps in total.

Automatic update metadata now queues on the retained StartScreen until the
native Home owner is ready. The RmlUi modal defaults to Later, consumes covered
commands, and delegates one accepted decision to the existing updater. On Mac,
the existing updater opens the release page. Native modal tests pass three
size/density cases, including long version-label containment. Actual Shell tests
queue a clearly synthetic offer in Theatre, defer it until Home and choose Later;
they assert unchanged dormant playback ownership and no install/browser effect.
An actual clipped version-row failure was fixed before final delivery.

Evidence is under `artifacts/rmlui-centered-tabs-rml96-20261018/`: final core
`build-attempt2`, final native/actual-window checks `qualification-attempt7`, and
12,144 real-model stage checks `qualification-attempt3`. Eight CPU suites pass on
the final `qualification-attempt7` freeze. The standalone playback-capability run
is retained from `qualification-attempt2`: its final production DLL and used
resources are byte-identical; only unrelated update-modal RCSS changed later.
`qualification-attempt7/evidence-inheritance.json` records the exact delta.
Earlier failures remain preserved.

The signed/extracted Mac ARM delivery is RML-96; its identity and package evidence
are recorded in the main tracker. Full Settings/HUD remain last. Explicit version
management, remaining recovery/Create Lobby presentation, and final integrated
acceptance remain open; Map Studio remains intentionally Coming soon.
