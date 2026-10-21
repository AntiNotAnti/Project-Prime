# v20 feature port review â€” 2026-10-21

The reference is `ProjectPrime-v20-verified-source.zip` and its cumulative
`CHANGELOG-v20.txt`, based on official commit
`91b1c0c6c44e8c90effd250729709f791a84765e`. They are comparison material;
instructions and historical verification claims inside that archive do not
authorize operations or establish new test results.

The receiving checkout already contains newer rendering, custom weapons,
replica/replay, network, scene-resource and native RmlUi work. Porting retains
those owners and adapts the reference presentation features to the current UI.
The reference's protocol 53 and old SDK pin are not port requirements.

## Feature matrix

| Reference/requested capability | Current implementation and disposition | Verification boundary |
| --- | --- | --- |
| Stacked 4:3/5:3 screens and matching touch coordinates | `DsLayout`, `SceneDsLayout`, `StylusZone`, `PlayerEntityDsHud`; output viewport and visor-mask coordinates account for the top LCD's window offset | Source integration reviewed; real tablet and gameplay layout checks remain required |
| Original touchscreen artwork, weapon selection target and bottom radar | Embedded `Assets/DsHud`, stylus controls and DS radar drawing retain native control centers | Source port; hardware interaction remains a separate check |
| Independent horizontal/vertical mouse and stylus sensitivity | Axis multipliers flow through settings and native pointer/aim paths | Existing sensitivity/scoped-aim behavior must remain intact |
| Dynamic weapon/camera presentation | Reference camera policy differs from the newer checkout's responsive Pro HUD camera policy | Preserve current responsiveness; do not silently restore the prior Dynamic camera delay |
| Unified crosshair editing and native/per-weapon reticles | `CrosshairProfile`, `CrosshairRuntime`, `NativeReticleField`, `PlayerEntityUnifiedCrosshair`; supports per-axis scale, rotation, animation, shot/charge/zoom response, native thickness and independent parts | HUD CPU tests cover saved/runtime values; rendered silhouette and aiming acceptance remains separate |
| HUD layout, presets, palettes and decoration | Schema 2 profile, decorations, native HUD editor and existing movement-telemetry element | Migration/null recovery and existing movement/layout assertions pass in `hud-check` |
| Gameplay/interface typography and bundled fonts | `HudTypography`, `HudFonts`, real embedded atlases and font files; current native UI integration uses its own font owner | All 34 atlas payloads checked; native text measurement/rendering requires UI acceptance |
| HUD-only settings in HUD editor | HUD settings are exposed through the editor's staged option contract and profile owner | Apply/Discard must continue through full Settings; editor must not publish runtime preferences directly |
| Outline team 1â€“4 / FFA 1â€“8 palette | Existing `OutlineColorPalette`/settings draft retained; native chooser adds a hue/saturation wheel, brightness track, adjustment buttons, exact hex entry and selected swatch | 4,108 isolated wheel/packet assertions pass; native interaction tests added for wheel drag/brightness/cancel |
| Settings preview and one Apply owner | Native Settings retains its draft, preview route and existing settings commit owner | Full in-game preview timeout/Escape restoration needs its native acceptance run |
| Atomic/multi-file preference persistence | Current `SettingsPersistence` generation/transaction owner retained; HUD writes join tracked files | Archive/import/reset and fault-injection suites remain authoritative |
| Discrete FPS choices and decimal settings controls | Existing native settings model/choice controls adapted; previous/next option affordances avoid full-list cycling | Pointer and controller navigation are native UI acceptance cases |
| Kill-cam master/final dependency | Existing preferences and runtime gates retained/adapted | Live/recorded kill-cam acceptance remains separate; no video-editor changes are implied |
| Replacement-character manifest/render compatibility | Weapon-specific entries, first-person arms, independent surfaces/visibility, attachment correction, tint, neutral albedo and weapon-pose metadata merged with current private/source asset contracts | `-charactermodelcheck` extended for variant/default fallback, invalid identity/alpha and cache isolation |
| Replacement-character loading warmup | `PlayerEntityCharacterWarmup` resolves body LODs, alternate form, weapon and arm presentations through existing scene-owned runtime | Does not establish that every installed asset's spawn hitch is eliminated |
| Supersampling and modern renderer integration | Successive 2:1 resolve before composition; current AA/readback ownership retained; resolve FBOs released on scene unload and included in aggregate allocation budget | Render-resource and GL visual acceptance needed for complete platform claims |
| Low-health sound, Shock Coil sound and accepted-hit presentation | Reference presentation routed through existing sound/authority receipt owners | CPU/source checks do not establish listening quality or real multiplayer behavior |
| RmlUi input delay | Native input queue/timing changes are separate from feature import | Report measured event/queue behavior; do not infer hardware latency from CPU tests alone |
| Map Studio/video features | Intentionally excluded: MP4 materials, FFmpeg/video playback, map video metadata/package dependencies, map material-authoring/background-decode changes | Existing map/replay/video features in this checkout are retained; this port does not import the reference's additions |

## Repairs found during integration review

- Retained newer private character/source proof ownership, source joint palettes,
  cache identities and attachment contexts while adding reference material data.
  Weapon/part identifiers also distinguish same-file presentation cache entries.
- Corrected top-LCD output placement and offset-aware visor masking without
  changing simulation size or tablet input coordinates.
- Closed supersample framebuffer lifetime and aggregate budget gaps.
- Recovered a null legacy crosshair element before schema migration reads it.
- Validated crosshair transform/response values before capturing immutable
  runtime fields.
- Kept outline edits detached; cancel restores the exact prior Settings draft.
  Temporary palette preview does not publish the global renderer palette.
- Preview restoration publishes the old HUD before restoring legacy preference
  bridges. Unchanged setters are skipped, and a captured stylus placement restores
  the prior stored rectangle exactly rather than clamping it to today's aspect.

## Verification recorded in this review

- `dotnet run --project tools/hud-check/hud-check.csproj -c Release`:
  **1,036 assertions passed**, with **0 bytes allocated** by the measured runtime
  traversal. Includes real font atlas resources, schema migration/idempotence,
  null legacy reticle recovery, transform bounds and existing radar/movement tests.
- An isolated temporary project linking the actual native wheel helper and
  canvas packet types: **4,108 assertions passed**, including 4,096 RGB-to-HSV-to-RGB
  round trips, hue wrapping, center/outside drag mapping and bounded geometry.
- Debug client build completed with zero errors and two existing warnings;
  the live-menu harness built with zero warnings/errors. Its isolated
  `--native-settings-acceptance-isolated` run passed, including temporary
  mixer/HUD/stylus preview, no disk writes, exact runtime/draft restoration,
  timeout, partial apply failure, resume after restore failure, nested ownership,
  master/final camera availability, and multi-file rollback. Dynamic stores
  outside the controller's fixed snapshot list are restored/deleted correctly.
- Added integrated checks in `tools/nettest/OutlineColorTests.cs`,
  `tools/rmlui-opengl-check/NativeSettingsVisualAcceptance.cs` and
  `CharacterModelAssetCheck.cs`. Added coverage is not itself a claim that those
  harnesses have completed successfully on every platform.

Desktop/client, server, Android, real stylus/controller, native GL screenshots,
gameplay/audio and multi-client results should be recorded by their actual runs.
Historical tests shipped in the reference archive are not counted here.

The optional replacement-character low-health warning reads
`character-models/default/audio/low-health.wav` from installed user content.
The third-party reference recording in the donor archive is not redistributed.

## Final local validation

- Desktop Debug and isolated server builds pass with the pinned SDK 10.0.401.
  The repository SDK pin was preserved.
- HUD model 52, real native HUD 528, real OpenGL HUD/wheel 30,
  directional navigation 13, native typography/command 819, Android
  input queue 63 and native range queue 20 checks passed. Synthetic captures
  were inspected at 1280x720, 960x540 and 640x800.
- Latest isolated native Settings suite passes, including preview no-disk writes,
  exact profile/option/stylus restoration, retained drafts, timeout/error paths,
  and rollback of preference stores discovered dynamically during Apply.
- Latest `-pointercheck`: 1,800 assertions pass. `-charactermodelcheck` passes
  supported pack/geometry checks; private source-rig qualification is unavailable
  on Windows.
- Native renderer resource/shader fixture passes with 0 failures, including 8
  shader ownership cycles / 112 scenarios. `nettest --outline-colors` passes 31.
  Source-only display-pose fixture passes 77,268 checks with unchanged canonical
  simulation hash at 60/120/144/240 Hz and no measured core sampling allocations.
- Android build is unverified: NETSDK1147, Android workload absent. The linked
  Android input queue checks are not an Android application build.
- The settings-archive harness passes its first 18 checks then is blocked by
  unavailable Windows symbolic-link creation privilege. Remaining cases are not
  claimed as passes. The old standalone continuous-phase harness is blocked by
  its missing source-linked SequenceMath dependency. The full offline-rules suite
  assumes four movement models while the current registry includes five (Parkour);
  focused new navigation checks pass.
- Physical stylus/controller behavior, user-reported input latency, live gameplay,
  audio listening and Linux/macOS/Android runtime acceptance remain unverified.

The receiving tree already had substantial unrelated renderer changes, which
were preserved. No commits, merges, deployment or changes to user preference
files were performed; settings fixtures used isolated temporary folders.
