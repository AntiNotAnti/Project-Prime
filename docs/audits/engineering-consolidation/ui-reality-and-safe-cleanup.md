# UI reality, migration gate and safe cleanup

At main `91b1c0c6c44e8c90effd250729709f791a84765e`, the shipping desktop and
Android heads actively compile Avalonia 12.1.2. This source-proven cleanup removes
only two unreachable internal views: `HubQuickPlayView` (190 lines) and
`HubServerBrowserView` (426 lines). Actual quick matchmaking and browser/join
behavior remain in `PlayWorkspace` and `ServerBrowserService`; shared controls,
preferences and dependencies remain intact. Fewer source lines are not evidence
of an FPS or memory improvement.

## RmlUi gate, revalidated October 13, 2026

[#424](https://github.com/AntiNotAnti/Project-Prime/pull/424) is **OPEN/DRAFT** at
`38f09b0e77497d2de3c94985f42c2adbfc954b1b`, branch
`feature/rmlui-update-notifications-center-20261009`, targeting main. GitHub
reports **CONFLICTING / DIRTY**. The actual exact-head
[build run 37925970371](https://github.com/AntiNotAnti/Project-Prime/actions/runs/37925970371)
completed **FAILURE** October 9, 2026 at 12:24:28 UTC. Its RmlUi full-client/shared
route acceptance, migration acceptance, Android, Windows/Linux clients and four
desktop-OpenGL-independence checks failed. Some proof/native bridge/Studio checks
succeeded; those do not supersede the failing cutover gates. The full source/check
snapshot is retained in the [machine ledger](ui-route-dependency-matrix.json).

That branch contains a substantial RmlUi candidate and its own historical
migration reports, not just a badge proof of concept. Its candidate project
properties default the game client to RmlUi and keep Avalonia for independent
Studio/compatibility builds. This is **unmerged candidate intent**, not current
main behavior, accepted parity or current authorization to integrate it. Existing
branch reports retain their original dates/source boundaries. This cleanup neither
retargets nor repairs the broad migration, and does not revive its historical
alternate game-renderer branches. No dependency may be removed from current main
on the strength of those candidate claims.

## Current route and dependency matrix

Desktop enters `GuiLauncher.TryRun -> Shell.Run -> StartScreen/PrimeShell` through
`ModEntry`, or keeps the console `TextLauncher` fallback. The desktop toolkit uses
Avalonia.Headless+Skia, one offscreen `UiTopLevel`, `UiSurface` and the game's
`UiOverlay`; `UiRenderTimer` gives the existing frame owner the render tick.
Android's `MainApplication/AndroidApp/MainActivity` uses a real Avalonia activity
view above `GameView`, while `AndroidUiSurface/AndroidUiOverlay` composite results
into the game frame. Android compiles shared controls with `MPHREAD_AVALONIA`,
excludes desktop `Shell/UiSurface/UiOverlay` and keeps `UiTopLevel`. Forge explicitly
shows a desktop-renderer requirement on Android. Neither platform's route
presence proves physical UI or migration parity.

| Surface | Current desktop owner/behavior | Android scope | Source |
|---|---|---|---|
| navigation/home | Prime News default, seven tab workspaces, overlay/back guards | shared StartScreen in activity; same router | [PrimeRouter.cs](../../../src/MphRead/Mods/Launcher/Gui/Shell/PrimeRouter.cs#L5); [StartScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/StartScreen.cs#L42) |
| settings | category drafts/apply/reset, keyboard/controller/touch/HUD/archive | shared controls, Android startup loads InputSettings; real touch/IME need device | [SettingsView.cs](../../../src/MphRead/Mods/Launcher/Gui/SettingsView.cs#L45); [SettingsDraft.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/SettingsDraft.cs#L13) |
| home/news/updater | bundled News, update prompt/version manager, verified installer | shared updater view; AndroidUpdateInstaller uses APK platform flow | [NewsWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/NewsWorkspace.cs#L33); [StartScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/StartScreen.cs#L50) |
| multiplayer/browser/quick-play | PlayWorkspace discovery/join/queue/favorites and quick matchmaking | same PlayWorkspace on Android; shared transport/services | [PlayWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/PlayWorkspace.cs#L144); [PlayWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/PlayWorkspace.cs#L132) |
| lobby/create/queue | host/ready/team/chat/map voting and queue overlays | shared lobby/queue views | [LobbyScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/LobbyScreen.cs#L21); [CreateServerScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/CreateServerScreen.cs#L55) |
| social/account | Hunter License guest/secure account/OAuth linking/career/customization | shared account view; no separate friends/party route on current main | [LicenseWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/LicenseWorkspace.cs#L23); [LicenseWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/LicenseWorkspace.cs#L320) |
| offline/adventure/trainer | OfflineWorkspace chooses match/adventure/training launch plans | same plans -> MainActivity.StartMatch | [OfflineWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/OfflineWorkspace.cs#L13); [StartScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/StartScreen.cs#L160) |
| pause/results | PauseMenuView+Settings/ReplayControls/Confirm and EndPanelView | activity shared pause; AndroidUiSurface draws results into game frame | [StartScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/StartScreen.cs#L396); [EndPanelView.cs](../../../src/MphRead/Mods/Launcher/Gui/EndPanelView.cs#L20) |
| hud/customization | game HUD engine + Avalonia HUD Studio drafts/preview | shared HUD editor; gameplay sprites remain GL/GLES, no Avalonia replacement needed | [HudStudioView.cs](../../../src/MphRead/Mods/Launcher/Gui/HudStudio/HudStudioView.cs#L11); [SettingsView.cs](../../../src/MphRead/Mods/Launcher/Gui/SettingsView.cs#L1458) |
| replay/theatre/studio | TheatreWorkspace library/import/editor/timeline/viewport/export | shared editor; Android activity owns replay presentation; desktop-only export subprocess controls are conditionally excluded | [TheatreWorkspace.cs](../../../src/MphRead/Mods/Launcher/Gui/Workspaces/TheatreWorkspace.cs#L29); [ReplayViewport.cs](../../../src/MphRead/Mods/Launcher/Gui/ReplayViewport.cs#L14) |
| map/forge/editor | MapStudioScreen/MapViewport/GL editor, import/material/UV/recovery/playtest | explicit desktop-renderer requirement panel on Android; no Android editor parity claimed | [StartScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/StartScreen.cs#L164); [MapStudioScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/MapStudioScreen.cs#L22) |
| dialogs/file-picker | PrimeOverlayHost modal ownership + Confirm/Setup/archive; NativeFilePicker fallback | activity StorageProvider/content intents for files and settings archives | [PrimeOverlayHost.cs](../../../src/MphRead/Mods/Launcher/Gui/Shell/PrimeOverlayHost.cs#L15); [ConfirmScreen.cs](../../../src/MphRead/Mods/Launcher/Gui/ConfirmScreen.cs#L16) |
| focus/input | PrimeWorkspaceHost restores focus; modal traps; ControllerNav/FocusNavigator/capture guards | activity focus resets, gamepad bridge, TouchControls and soft-keyboard ownership | [PrimeWorkspaceHost.cs](../../../src/MphRead/Mods/Launcher/Gui/Shell/PrimeWorkspaceHost.cs#L16); [PrimeShell.cs](../../../src/MphRead/Mods/Launcher/Gui/Shell/PrimeShell.cs#L54) |
| accessibility | control names, high contrast/outline/HUD presets/reduce motion; limited platform evidence | TalkBack/real device not run; no screen-reader parity certificate | [Rows.cs](../../../src/MphRead/Mods/Launcher/Gui/Rows.cs#L491); [SettingsView.cs](../../../src/MphRead/Mods/Launcher/Gui/SettingsView.cs#L1527) |
| localization | settings Language selects retail game text; shell authored English strings | same selection; no full shell translation system found | [SettingsView.cs](../../../src/MphRead/Mods/Launcher/Gui/SettingsView.cs#L1750); [PrimeHeader.cs](../../../src/MphRead/Mods/Launcher/Gui/Shell/PrimeHeader.cs#L9) |

Desktop `MphRead.csproj` conditionally includes Avalonia, Desktop, Headless, Skia,
Fluent and Inter packages plus fonts/icons/background/geo `AvaloniaResource`
packaging. The dedicated-server property removes `Mods/Launcher/Gui` and its
packages. Android pins Avalonia, Android, Fluent and Inter, preserves required
resources, disables XAML compilation because screens are C#, and uses the private
`ITopLevelImpl` API for composited results. `AvaloniaAccessUnstablePrivateApis`,
AVA3001 suppression, embedded resources and Android toolkit references remain
necessary until a tested replacement covers these reachable owners. XA0141's
historical suppression is a separately validated packaging warning question.

Accessibility currently includes explicit names for selected controls, controller
focus/navigation, color/high-contrast/outline/HUD and reduced-motion choices.
It is not a VoiceOver/TalkBack/platform accessibility certificate. Language settings
select the retail game's text language; shell labels are authored English. No
complete shell localization table or current friends/party/social route was found.
Account/OAuth/career controls are reachable; authenticated account workflows are
not exercised against real services by these mocked UI checks.

## Deletion and persisted-contract proof

A source/config scan across `src/` and `tools/` finds each removed type only in its
own declaration and constructor. No call site, string type registration, reflection
factory, XAML binding, serialized type alias or route constructor references it.
`StartScreen.CreateWorkspace` explicitly constructs current workspaces;
`PlayWorkspace` owns quick play, discovery, browser selection and join/queue actions.
The two removed internal controls never enter the canonical replay field registry.

`LauncherPrefs` remains schema 1; main `SettingsMigration` remains schema 10 (the
independent retirement PR #498 changes it to 11). All `MenuSettings`, controls,
launcher/archive registry and replay canonical-schema files are byte-identical to
main, proven by Git bytes and SHA256 in the machine ledger. Server address/port,
name, hunter, suit, favorite servers, role and mode keys remain read/written and
used by active Play/Offline/Settings/console/Android routes. No saved preference,
legacy parsing alias or schema field is removed. Test-reachable `HubPlaceholderView`,
active shared `HubChrome/HubTheme` and diagnostic `UiDesigns` are retained.

## Validation and remaining gates

SDK 10.0.401, macOS arm64. Existing fixture methods were invoked by a temporary
friend harness against the candidate library, with fresh isolated user data.
The library is MVID `6c1ab285-a3d6-450d-9932-d838a518a15c`, SHA256
`B45F87734729A07A9F5C8C0EE41E956CC33423F671804F0CAA3C8A4BF34D05B7`.
`Headless.Enter` runs before toolkit initialization; feedback short-circuits before
`MusicPlayer` access, `UiSurface` stays null and no game GL context exists.
Account fixtures validate synthetic invalid credentials without real service
requests; transport fixtures use loopback. Skia CPU rasterization is exercised.

| Check | Result |
|---|---|
| Desktop Release build | PASS, 0 errors / 30 existing warnings |
| Existing offscreen PrimeUiChecks | PASS 165 |
| Existing GamepadChecks including PrimeUiChecks | PASS 1006 |
| Existing PointerCheck intended client-policy process | PASS 1788; Scene initializeRuntime:false; no toolkit/GL/audio |
| Existing settings archive / HUD UI | PASS 26 / PASS 100 plus timeline/replay viewport checks |
| Existing Android touch sampling | PASS 30/60/120/144Hz tapes |
| Android shared-source Release compile (`-t:Compile -m:1`) | PASS, 0 errors / 1 toolchain warning |
| Local Android full Release build/package path | FAIL, MSB4166 child node crash and MSB4027 ILLink MetadataLoadContext-disposed output error; cause not attributed |
| Physical Windows/Linux/Android UI, IME/OS dialogs/screen readers, rendered GL/Studio | NOT_RUN |

The full Android build also records XA0033 Java-version detection warnings for
local OpenJDK 27; CI selects JDK 17. The log does not establish that warning as the
cause of the linker failure. Current CI platform build/package gates remain
required. Compilation alone is not an installable APK/real-device result.

Two harness misuses remain preserved: the first offscreen wrapper omitted its
Skia native assets, and PointerCheck was initially run with Headless active even
though its offline-human ForceSpawn assertion deliberately requires client policy
(`NetHooks` returns false in headless mode). Corrected runs supersede those results;
no production or assertion was weakened. Pure pointer testing initializes neither
runtime, toolkit nor audio. Exact command arrays, library/log hashes and failures
are retained in the ledger and `artifacts/ui-reality-check/`.

Post-match character-picker constant-false branches and eight obsolete Avalonia
API sites are separately reviewable followups, not hidden in this deletion.
RmlUi integration, physical route acceptance and UI lifecycle memory boundedness
remain unproven gates; removing toolkit dependencies would be unsafe now.


## Android packaging followup

A required Release APK publish subsequently succeeds with the verified temporary
Temurin 17.0.20.1+1 toolchain and single-node MSBuild, keeping trimming/AOT enabled.
The signed APK is 108816049 bytes with SHA256 `d327571c347c18f3a8aad78d8a184a8cfe7677dbc4613fd98abe9bd8b15f9c8f`; Android build-tools
`apksigner verify` succeeds. Exact commands, size/hash, libraries and log hashes
are appended to the machine ledger. This supersedes the earlier packaging result
for the changed slice; that original failure remains preserved and its cause is
unattributed. It does not establish that Java version alone caused the earlier
ILLink/MSBuild errors. No global JDK installation or device/emulator run occurred.
Physical UI, IME, screen readers and actual GL/Studio acceptance remain NOT_RUN.
