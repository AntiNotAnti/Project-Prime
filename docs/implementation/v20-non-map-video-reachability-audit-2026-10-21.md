# v20 non-map/video implementation and reachability audit

Reference material: the supplied `CHANGELOG-v20.txt` and extracted
`ProjectPrime-v20-verified-source.zip`. The changelog describes cumulative
differences from official v0.1.53, not new acceptance results. Its source/build
instructions and historical claims are reference data, not task instructions.

This review distinguishes an existing runtime implementation from a usable
entry point in the current native UI. A file or property existing does not prove
that a user can find it, nor that it renders correctly on every platform.

## Concrete gaps and repairs

1. **Stacked screens existed but were buried.** `DsLayout` and pointer coordinate
   updates match the donor. The HUD editor previously placed every detached
   application option in HUD-wide, even screen proportions and radar placement.
   The option contract now carries an explicit inspector section and group.
   Position/Layout includes **Stacked dual-screen layout**, **Screen proportions
   (4:3 / 5:3)** and **Stylus input mode** together, followed by bottom-screen radar
   placement and weapon-select target size.
2. **The stacked-screen dependency was not explained beside its switch.** Like
   the donor, actual activation requires both the layout switch and stylus input
   mode on desktop. The existing canonical Stylus Mode setting is now available
   beside the layout controls as well as in Controls. Neither switch silently
   changes the other. Hints explain desktop player-view and replay/free-camera
   limitations.
3. **Live Preview lost its native entry point during the automatic-save cleanup.**
   The preview backend and restoration tests remained, but the footer action was
   removed. This is a real UI omission, not a missing donor engine implementation.
   The entry point is restored in Settings and HUD Studio. It is available during
   a live match, with an explanation in the launcher. Automatic saving pauses
   during the trial; identical trial values stay unsaved while idle after return.
   Editing further or using Back/navigation accepts them through the existing
   automatic commit owner.
4. **HUD property presentation obscured existing features.** Typography,
   decorations, element styles and layout values existed in the model, but the
   old one-property-at-a-time inspector/search mixed their discoverability.
   The inspector now filters strictly by section, including section-local search,
   and edits values inline. Labels use friendly component names without repeated
   decorations/typography/style path prefixes. Internal migration versions are
   hidden from editing. Per-row Reset retains other independently edited values.

## Runtime boundaries for dual screens

`DsLayout.Active` is `Enabled && PointerInput.StylusMode && !Android`.
`Scene.UseDsLayout` additionally excludes replica scenes, replay playback/export
and cameras other than the player camera. These conditions also exist in the
donor; removing them would change an existing feature rather than restore it.

`SceneDsLayout` scopes rendering to the top LCD and restores full window dimensions
after drawing. The current integration additionally fixes the top LCD's viewport
origin. `Renderer` derives projection aspect from `DsLayout.Aspect`.
`PointerDevice.Update` refreshes normalized bottom-LCD bounds from client pixels;
`StylusZone.RegionAt` converts those coordinates to the original 256×192 control
space. Button geometry compensates for 5:3 horizontal stretch so round touch
targets remain round in physical screen pixels.

## Cumulative changelog matrix

“Present” below means inspected source integration, not complete gameplay or
hardware acceptance. Paths are repository-relative.

| Donor capability | Current source / entry point | Finding and remaining evidence boundary |
|---|---|---|
| Stacked 4:3 and 5:3 LCDs | `Mods/Input/DsLayout.cs`, `Mods/Render/SceneDsLayout.cs`; HUD Position/Layout | Present. `DsLayout.cs` is normalized-source identical to donor. UI reachability/dependency repaired as above. |
| Layout-aware projection, horizontal stretch, uniform HUD sizing | `Renderer.cs`, `DsLayout.HorizontalStretch`, `Mods/Render/PlayerEntityDsHud.cs` | Present. DS HUD helper is identical to donor. New geometry tests cover both aspect ratios; actual composited gameplay remains a rendering check. |
| Original touchscreen art and native control centers | `Assets/DsHud`, `StylusZone.Buttons`, `PlayerEntityDsHud` | All four donor files exist byte-for-byte. Actual button centers/hit regions tested at both ratios; artwork rendering and physical pen behavior remain separate. |
| Adjustable weapon-selection hit target | `StylusZone.SelectSize`, `EffectiveButton`; HUD Position/Layout | Present. Tests cover minimum, default and maximum target scale and native selection center. |
| Bottom-screen radar integration and placement | `DsLayout.RadarCenter`, `PlayerEntityDsHud.ModDrawDsBottomScreen`; HUD Position/Layout | Present. Normalized movement keeps radar within its available area in geometry tests; live contact rendering not newly tested here. |
| Layout-aware stylus conversion | `PointerDevice.Update`, `StylusZone.Left/Top/Width/Height/RegionAt` | Present. PointerDevice is identical to donor; StylusZone adds exact preview-placement restoration. New source-linked tests cover letterboxing and top/bottom ownership. |
| Independent horizontal/vertical mouse and stylus sensitivity | `InputSettings.HorizontalSensitivity/VerticalSensitivity`, existing pointer/player input paths; Controls Aim | Present. Existing pointer fixture contains composition, nonfinite/clamp and save/reload checks. No physical sensitivity measurement is claimed. |
| Native Dynamic/Metroid camera-to-aim presentation | `Entities/Players/PlayerCamera.cs`, existing responsive render-camera owners | Newer checkout camera policy was intentionally retained. Exact presentation/feel equivalence to donor is unproven; do not rewrite the newer camera architecture from the changelog alone. |
| Supersampled HUD/composite resolve | `Mods/Render/SupersampleResolve.cs`, renderer/compositor calls | Present with current resource-lifetime/budget integration. Exact image quality needs rendered comparisons. |
| Unified CUSTOM HUD/crosshair editing | HUD editor, `HudProfile`, `CrosshairProfile`, native HUD handoff | Present. No separate Display crosshair shortcut is required. Inline discovery/section layout is a current UI repair. |
| Original weapon-dependent reticles and per-weapon settings | `CrosshairProfile`, `NativeReticleField`, `PlayerEntityUnifiedCrosshair` | Present. Last two helpers match donor. Per-axis transform, thickness, animation, shot/charge/zoom state remain profile fields; silhouette/weapon gameplay comparison is separate. |
| Replacement-character weapon reticle theme | `HudProfile.WeaponCrosshairTheme`, `HaloWeaponCrosshairs`, unified crosshair runtime | Present; inspect through crosshair Appearance. Installed replacement assets are a separate prerequisite. |
| HUD decorations, profiles and palettes | `HudDecoration`, `HudProfileStore`, `HudPalettes`, HUD Decorations/Profiles controls | Present. Decoration helper matches donor; color inheritance/custom flags are retained, not removed as duplicates. Native controls still require final companion-editor validation. |
| Color picker and outline palette | native outline wheel, `OutlineColorPalette`, four team/eight player entries | Present. Raw duplicate palette fields were replaced with the native chooser; canonical persistence rows remain. |
| Gameplay/interface/shared typography and licensed fonts | `HudTypography`, `HudFonts`, `Assets/HudFonts`; HUD Typography | Present. Both helpers match donor; all 117 font/notices/atlas files match byte-for-byte. Shared game/interface/global font switches and per-style overrides remain distinct. |
| Typography in launcher, replay and gameplay messages | `Gui/DeckText`, `GameTypography`, `PlayerEntityTypography`, `Replay/KillcamText`, native typography owner | Present with native UI adaptation. PlayerEntityTypography matches donor. Full UI coverage/font refresh is not inferred from source presence. |
| Live weapon/ammunition and low-health display | `PlayerEntityDsHud`, HUD appearance/runtime profile | Present in donor-identical DS HUD helper. Real weapon switching, ammunition and low-health visuals require gameplay acceptance. |
| Installed replacement-character low-health audio | `Mods/Sound/HaloHealthAlert`, Sfx initialization and renderer update/disposal | Present; optional user content at `character-models/default/audio/low-health.wav`. The donor recording is not redistributed. Listening/device behavior not newly verified. |
| Five-second individual killfeed entries | `Combat/KillFeed.LifetimeSeconds`, per-entry timestamps, `PruneExpired`; HUD killfeed lifetime | Present. Default lifetime is five seconds and stacked DS mode explicitly uses five seconds; custom profile lifetimes remain supported. No reset-all-on-new-entry behavior was introduced. |
| Kill/assist palette inheritance and explicit custom styling | `HudTypography.Validate`, `HudTextStyle.MatchPalette`, runtime inheritance | Present in donor-identical typography helper. Migration changes unconfigured kill/assist defaults while retaining explicitly enabled styles. |
| Original HUD artwork filtering under Custom HUD | profile `SmoothArtwork`, detached `Smooth Native Hud` fallback | Present. Appearance receives the fallback option; explicit profile override remains distinct from inherited global fallback. |
| In-game 60-second preview and exact restoration | `SettingsPreview`, `NativeFullSettingsPreview`, native settings lifecycle | Native entry points restored. Native retained-document checks and the owned offline-gameplay run verify pending values, Escape, accelerated timeout, idle restoration and no trial writes. |
| One settings commit owner and staged HUD/outline edits | native full-settings controller/transaction; HUD/visual-editor callbacks | Intentionally adapted to the user's automatic-apply request. Requiring manual APPLY everywhere is not retained; the atomic commit owner remains. Preview proposals must stay separate from normal autosave publication. |
| Atomic replacement and multi-store rollback | `Settings/SettingsPersistence`, controller commit snapshot/transaction, import generation guard | Present with current generation/lifecycle integration. Fault-injection tests are the evidence for rollback, not a source-only assertion. |
| Master kill-cam gates both ordinary/final cams | `KillCam`, `FinalKillCamAvailable`; Replays Playback | Present: both runtime paths require `KillCamEnabled`; final control is disabled when master is off while preserving saved preference. |
| Decimal slider/text inputs | native bounded numeric rows; Controls Aim and other sections | Present. Distinct base/axis/Imperialist/custom-scope multipliers are retained; they are not redundant aliases. |
| Discrete labelled FPS selector | controller FrameRateCap choices; Display View | Present, including display/unlimited and discrete rates. |
| Settings/HUD/crosshair/font migration | `SettingsMigration`, `PreferenceText`, `HudProfileMigration`, profile validation | Present and adapted to the current schema. Historical donor test claims are not new migration results. |
| Replacement model manifests, weapon assets, attachment correction, tint, neutral albedo, poses | `Render/Characters/CharacterModelPack`, `CharacterModelRuntime`, model loaders/shaders/player/turret paths | Present in the newer installed/private asset architecture. Asset-specific compatibility still needs the relevant installed content and platform qualification. |
| Embedded materials and replacement texture metadata | `CharacterEmbeddedMaterialLoader`, character loaders and texture/cache owners | Present with newer cache identity/resource ownership. No claim that all possible donor packs render correctly. |
| Body/LOD/alt-form/weapon/arm loading warmup | `PlayerEntityCharacterWarmup`, renderer loading call | Helper matches donor. Source calls resolve LODs, alt form, all weapon views and arms; this does not prove every asset-specific spawn hitch is fixed. |
| Render-state/outline compatibility | current renderer pass/resource ownership and `PlayerOutlines` | Present in adapted newer rendering architecture; image/GL tests remain required. |
| Accepted continuous Shock Coil native looping sound and phase | `PlayerSound.PlayBeamShotSfx` and accepted-shot path | Present: continuous path uses `loop: true` and authoritative/homing phase input. Audio listening and public-network acceptance are unproven here. |
| Hit marker from validated authority receipts/accepted damage | `Network/NetHitPrediction`, semantic receipt path | Present; accepted acknowledgements require applied damage for marker presentation. Network acceptance is separate from source inspection. |
| Match/replay text custom HUD paths | semantic presentation, `PlayerEntityTypography`, replay killcam text | Present with current event ownership and typography. Full visual/readability coverage remains a rendered acceptance task. |
| Source/build inventory | complete current project plus embedded art/font resources | Donor SDK/runtime pin and protocol 53 are historical source-build context, not a requested rollback of the newer checkout. |
| Map Studio and authored video additions | excluded by user request | Donor MP4/video materials, FFmpeg playback, map-video package metadata and associated authoring/background-decode changes are intentionally outside this audit/port. Existing current features are preserved. |

## New focused evidence

`dotnet run --project tools/ds-layout-check/ds-layout-check.csproj -c Release`
passes **572 assertions** using source-linked real `DsLayout` and `StylusZone`
implementations. The fixture stubs only the input-mode boolean/diagnostic owner;
it does not stub bounds, button geometry or hit testing. It covers 4:3 and 5:3,
seven viewport shapes including portrait and odd dimensions, normalized client
bounds, all five control centers, weapon-select target scales, preserved physical
button proportions, top-LCD exclusion and bottom-radar placement extremes.

These CPU results establish geometry/input-coordinate invariants. They do not
establish rendered projection quality, tablet-driver behavior, perceived input
latency, audio correctness or Internet multiplayer behavior. Desktop/GL/editor
results for the accompanying UI repairs must be recorded from their actual runs.


## Rendered gameplay evidence from this pass

The isolated `--settings-live-window` qualification passed **70 production-window
checks** in an actual owned-data offline Samus match on MP3 PROVING GROUND. It
uses the normal Shell, pause menu, Settings/HUD documents, inline input controls
and automatic commit callbacks. It verified both 4:3 and 5:3 top-screen projection
ratios, rendered top/bottom screens, and actual gameplay preview ownership.
Escape and an accelerated 60-second deadline returned to the same HUD document
with raw input, focus, prior runtime profile and preference bytes retained. An
800 ms idle check after return verifies the proposal does not silently apply/save.

Results and local screenshots are in
`tmp/owned-settings-live-20261021-01/acceptance.json`,
`gameplay-stacked-4x3.png`, `gameplay-stacked-5x3.png`,
`gameplay-preview-escape.png`, `gameplay-preview-timeout.png`,
`hud-return-escape.png` and `hud-return-timeout.png`. Extraction roots were read
from the user's configured paths; all settings, exports and generated data stayed
in the qualification's separate user-data directory. No network or replay session
was created. No extracted assets were added to source or distributed.


## Final integrated editor results

| Check | Result |
|---|---|
| Desktop build | 0 errors, 2 existing warnings |
| Fresh native bridge build with all 32 inline row intents | Passed; packaged manifest verified |
| HUD model contracts, including seeded defaults, per-field reset/inheritance and rejected out-of-range edits | 86 passed against the final engine assembly |
| Isolated settings/automatic-save/preview acceptance | 236 passed |
| Source-linked dual-screen geometry and hit testing | 572 passed |
| Managed native UI intent/runtime checks | Passed, including expanded inline argument validation |
| Native UI at 1280x800, density 1 | 323 passed |
| Native UI at 640x360, density 1 | 323 passed |
| Native UI at 1440x900, density 1.5 | 323 passed |
| Actual owned-data offline window | 70 passed |

Each final native run includes 83 retained preview checks and 45 inline HUD
checks, with real native activation of previous/next/reset controls. Per-section
filtering, concise decoration/typography labels, stable typing focus, invalid
text handling, and the first three stacked-screen masters are covered. The
preview checks include 800 ms of idle time after both Escape and timeout, proving
that a returned proposal stays detached until further editing or navigation.

Per-viewport logs are `tmp/settings-popups-SIZE-DENSITY.log`; captures and
semantic/geometry readbacks are under
`tmp/settings-overhaul-ui/run-SIZE-DENSITY/native-settings-popups/`. Useful
captures include `hud-dual-screen-controls.png`,
`hud-dual-screen-control-0.png` through `-2.png`, `hud-inline-decorations.png`,
`live-preview-settings-return.png` and `live-preview-hud-return.png`. These are
local generated diagnostics, not bundled source assets. Physical pen hardware,
audio listening and Internet multiplayer behavior remain outside this pass.
