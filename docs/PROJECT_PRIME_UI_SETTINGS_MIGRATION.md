# Project Prime UI and settings migration contract

Status: current-tree architecture record. This document describes the
settings and lobby-rule implementation present in the working tree; it is not
a release or live-client acceptance report.

The UI overhaul is a presentation and ownership change around the existing
client/runtime boundaries. The authoritative multiplayer path remains the
Node-owned lobby, the immutable `MatchSpec`, and the Worker-owned match. A
local settings file can configure presentation and input, but it cannot
configure an admitted online match.

Source of truth for this record:

* [SettingRegistry](../src/Client/Launcher/Settings/SettingRegistry.cs),
  [SettingDescriptor](../src/Client/Launcher/Settings/SettingDescriptor.cs),
  [SettingExclusion](../src/Client/Launcher/Settings/SettingExclusion.cs), and
  [SettingRowIds](../src/Client.Core/Launcher/Settings/SettingRowIds.cs)
* [ClientSettings](../src/Client/Settings/ClientSettings.cs),
  [MenuSettings](../src/Renderer/Configuration/MenuSettings.cs),
  [GameSettings](../src/Client/Runtime/GameSettings.cs), and
  [FeaturesSettings](../src/Client/Settings/FeatureSettings.cs)
* [InputSettings](../src/Client.Core/Runtime/InputSettings.cs),
  [PadBindings](../src/Client/Input/PadBindings.cs), and
  [TouchSettings](../src/Client/Input/TouchSettings.cs)
* [ClientSceneServices](../src/Client/Networking/ClientSceneServices.cs),
  [LocalLookFrame](../src/Game/Runtime/LocalLookFrame.cs), and
  [PlayerAimAssist](../src/Game/Gameplay/Hunters/PlayerAimAssist.cs)
* [LauncherPrefs](../src/Client/Launcher/Portable/LauncherPrefs.cs) and
  [SettingsView](../src/Client/Launcher/Gui/SettingsView.cs)
* [NodeContracts](../src/Server.Shared/NodeContracts.cs),
  [NodeControlCodec](../src/Server.Shared/NodeControlCodec.cs), and
  [LobbyManager](../src/Server.Node/Lobbies/LobbyManager.cs)

## Ownership model

`SettingScope` is deliberately a normal enum rather than flags. Each setting
has one logical owner:

| Scope | Owner and examples |
| --- | --- |
| `ClientPreference` | Local display, audio, input, HUD, and network choices. |
| `AccountPreference` | Backend-owned profile values such as authenticated display name and favorite Hunter. |
| `MatchHostRule` | Node/lobby-owned map, mode, capacity, and match rules. |
| `PlatformPreference` | Platform-specific input such as Android touch and stylus controls. |
| `Diagnostics` | Local debug logging and input-balance diagnostics. |
| `ServerOperator` | Backend/Node policy and Worker capacity; never a normal player row. |
| `Internal` | Runtime/session state and compatibility plumbing. |

The registry is metadata, not a UI framework. A descriptor has a stable ID,
row ID, category, scope, control kind, platform mask, optional dependency,
range/choices, and persistence identity. It intentionally does not capture a
`MenuSettings` instance or hold lifecycle delegates. `SettingRegistry.Validate`
rejects duplicate descriptor IDs, duplicate row IDs, duplicate persistence
owners, invalid ranges/choices/dependencies, unexplained unavailable rows, and
lifecycle delegates. `ValidateRenderedRows` lets the focused UI test prove that
the concrete view rendered every descriptor applicable to a platform.

`SettingExclusion` is the explicit owner for a persisted value that should not
be a normal player control. `SettingAlias` records a legacy parser key and its
canonical descriptor target. A property is therefore either a registered UI
owner, an explicit exclusion, or a documented parser alias; it must not remain
an unowned persisted value.

`SettingsView` currently exposes these pages: Gameplay, Controls, Graphics,
Audio, System, Network, Accessibility, and About. A row is registered through
the stable ID assigned by `SettingRowIds`; Android-only rows are platform-gated
and desktop-only capabilities are shown as unavailable/disabled where the
shared view can explain that state. Host rules are not part of these pages.

## Persistence boundaries and lifecycle

```text
Savedata/settings.json  -> ClientSettings -> MenuSettings + FeaturesSettings
directory/controls.txt  -> InputSettings -> ClientPlayerBindings/Pad/Touch
directory/launcher.txt  -> LauncherPrefs -> shell, update, content, region
Backend/PostgreSQL      -> AccountSession/ProfileEndpoints -> account identity
Node memory             -> LobbyManager -> LobbyRulesOptions -> MatchSpec
```

`ClientSettings.LoadSettings` deserializes the `Savedata/settings.json`
envelope, applies the nested `Features` dictionary, and returns
`MenuSettings`. `CommitSettings` writes the current `FeaturesSettings.Commit`
dictionary and the complete `MenuSettings` object. `GameSettings.Apply` maps
the client-side values into audio, language, HUD, radar, and render state.
`SettingsView.Commit` applies the same values immediately where supported,
writes `controls.txt` and `launcher.txt`, then commits `settings.json`.

`InputSettings` and `LauncherPrefs` use the directory selected by
`LauncherPrefs.Directory`. Desktop normally points it beside the executable;
the Android head points it at app data before loading so the package directory
is not used for writes. Their `Load`/`Save` methods treat preference I/O as
non-fatal and the in-memory defaults remain usable if a file cannot be read or
written.

The table shorthand below is:

* `J` = `ClientSettings.LoadSettings`; `C` = `SettingsView.Commit` and
  `ClientSettings.CommitSettings`.
* `I` = `InputSettings.LoadLines`; `S` = `InputSettings.GetSaveLines`.
* `L` = `LauncherPrefs.Load`; `W` = `LauncherPrefs.GetSaveLines`.
* A descriptor ID is the owner in `SettingRegistry`; `—` means an explicit
  exclusion or an informational/action row with no persisted value.

## `settings.json` inventory

### `MenuSettings` client preferences

These are the public `MenuSettings` properties that remain valid client-facing
values. Defaults are the literal defaults in `MenuSettings`; nullable quality
keys are intentionally absent (`null`) in files written before the quality
schema was introduced and then resolve from the selected preset.

| Property/key | Scope and default | Load/save and runtime consumer | Descriptor, UI, platform |
| --- | --- | --- | --- |
| `Language` | Client; `English` | J/C; `Scene.Language` | `audio.language`, Audio/Language, all |
| `FeedbackVolume` | Client; `0.7` | J/C; `Combat.FeedbackAudio.Volume` | `audio.feedback-volume`, Audio/Volume, all |
| `SfxVolume` | Client; `0.35` | J/C; `Sfx.Volume` | `audio.sfx-volume`, Audio/Volume, all |
| `MusicVolume` | Client; `0.50` | J/C; `Music.SetUserVolume` | `audio.music-volume`, Audio/Volume, all |
| `ResolutionScale` | Client; `100` | J/C; `RenderOptions.ResolutionScale` | `graphics.render-scale`, Graphics/Performance, all |
| `Lighting` | Client; `on` | J/C; `RenderOptions.Lighting` | `graphics.lighting`, Graphics/Quality, all |
| `Fog` | Client; `on` | J/C; `RenderOptions.Fog` | `graphics.fog`, Graphics/Quality, all |
| `GraphicsPreset` | Client; `original` | J/C; `RenderOptions.ResolveQuality` | `graphics.quality`, Graphics/Quality, all |
| `TextureFilteringPreset` | Client; `null` (preset fallback) | J/C; `RenderOptions.ResolveQuality` | `graphics.texture-filtering`, Graphics/Quality, all |
| `Anisotropy` | Client; `null` (preset fallback) | J/C; `RenderOptions.ResolveQuality` | `graphics.texture-detail`, Graphics/Quality, all |
| `Msaa` | Client; `null` (preset fallback) | J/C; `RenderOptions.ResolveQuality` | `graphics.edge-smoothing`, Graphics/Quality, all |
| `Bloom` | Client; `null` (preset fallback) | J/C; `RenderOptions.ResolveQuality` | `graphics.bloom`, Graphics/Quality, all |
| `DynamicVisualLights` | Client; `null` (preset fallback) | J/C; `RenderOptions.ResolveQuality` | `graphics.dynamic-lighting`, Graphics/Quality, all |
| `AdvancedNetwork` | Client; `off` | J/C; network-health diagnostic display | `network.diagnostics`, Network/Diagnostics, all |
| `HitMarkers` | Client; `Visual` | J/C; `Combat.CombatFeedbackSettings.HitMarkers` | `hud.hit-markers`, Graphics/HUD, all |
| `HitMarkerTiming` | Client; `Confirmed` | J/C; `Combat.CombatFeedbackSettings.Timing` | `hud.hit-marker-timing`, Graphics/HUD, all |
| `HeadshotCue` | Client; `on` | J/C; combat feedback presentation | `hud.headshot-cue`, Graphics/HUD, all |
| `KillConfirmation` | Client; `on` | J/C; combat feedback presentation | `hud.kill-confirmation`, Graphics/HUD, all |
| `RadarStyle` | Client; `Enhanced` | J/C; `RadarSettings.Style` | `hud.radar.style`, Graphics/Radar, all |
| `RadarOrientation` | Client; `heading` | J/C; `RadarSettings.Orientation` | `hud.radar.orientation`, Graphics/Radar, all |
| `RadarPosition` | Client; `TopRight` | J/C; `RadarSettings.Anchor` | `hud.radar.anchor`, Graphics/Radar, all |
| `RadarScale` | Client; `1.0` | J/C; `RadarSettings.Scale`, clamped to `.65..1.5` | `hud.radar.scale`, Graphics/Radar, all |
| `RadarOffsetX` | Client; `0` | J/C; `RadarSettings.OffsetX`, clamped to `-256..256` | `hud.radar.offset-x`, Graphics/Radar, all |
| `RadarOffsetY` | Client; `0` | J/C; `RadarSettings.OffsetY`, clamped to `-192..192` | `hud.radar.offset-y`, Graphics/Radar, all |
| `ShowFps` | Client; `off` | J/C; `RenderOptions.ShowFps` | `graphics.fps-counter`, Graphics/Performance, all |
| `FrameRateCap` | Client; `display` | J/C; `FrameTiming.FrameRateCap` | `graphics.fps-limit`, Graphics/Performance, all |
| `CelShading` | Client; `off` | J/C; `RenderOptions.CelShading` | `graphics.cel-shading`, Graphics/Quality, all |

The UI also persists the nested `Features` dictionary. Its current committed
keys are deliberately limited to the following five values:

| Feature key | Scope and default | Load/save and runtime consumer | Descriptor, UI, platform |
| --- | --- | --- | --- |
| `ReticleOpacity` | Client; `1` | `FeaturesSettings.Load`/`Commit`; HUD reticle | `hud.reticle-opacity`, Graphics/HUD, all |
| `ProHud` | Client; `false` | `FeaturesSettings.Load`/`Commit`; `Features.ProHud` | `hud.pro`, Graphics/HUD, all |
| `ProHudFixedWeapon` | Client; `true` | `FeaturesSettings.Load`/`Commit`; `Features.ResolveFixedWeapon` | `hud.pro-weapon`, Graphics/HUD, all; depends on `hud.pro` |
| `CrosshairStyle` | Client; `Cross` | `FeaturesSettings.Load`/`Commit`; `Crosshair.Style` | `hud.crosshair-style`, Graphics/HUD, all; depends on `hud.pro` |
| `CrosshairSize` | Client; `Medium` | `FeaturesSettings.Load`/`Commit`; `Crosshair.Size` | `hud.crosshair-size`, Graphics/HUD, all; depends on `hud.pro` |

`CelBands` and `CelEdge` are still public JSON members for compatibility, but
`GameSettings.Apply` sets them to the fixed renderer values `8` and `0.5`
regardless of an old file. The registry excludes them; they are not player
controls. `Features.HudOpacity`, helmet/visor opacity, and the partial legacy
HUD switches are not in the committed `Features` dictionary and have no
normal settings descriptor.

### Explicit `settings.json` exclusions

The legacy global match-rule controls are removed from the normal SettingsView.
Host-rule controls belong to the Host/Edit Match surface and are sent through
the Node lobby path described below. The old `MenuSettings` members remain
read-compatible for console/offline and upgrade paths, but their presence in a
JSON file is not evidence that they are supported player preferences.

The following public `MenuSettings` properties remain deserializable so old
files do not fail, but do not have a normal SettingsView owner:

| Exclusion IDs and keys | Reason and current consumer |
| --- | --- |
| `legacy.match.point-goal` / `PointGoal`; `legacy.match.time-limit` / `TimeLimit`; `legacy.match.damage-level` / `DamageLevel`; `legacy.match.friendly-fire` / `FriendlyFire`; `legacy.match.hunter-radar` / `HunterRadar`; `legacy.match.affinity-weapons` / `AffinityWeapons` | Match rules are host/Node-owned online. The values are read only by the compatibility `GameSettings.ApplyMatchRules` path when no authoritative admission rules exist. |
| `legacy.match.time-goal` / `TimeGoal`; `legacy.match.auto-reset` / `AutoReset` | Legacy compatibility members. `TimeGoal` can be consumed by the non-admitted compatibility path; no current client consumer was found for `AutoReset`. Neither is a normal local UI setting. |
| `legacy.match.team-play` / `TeamPlay` | Team structure is derived from the selected match mode; there is no independent local switch. |
| `legacy.menu.room-key` / `RoomKey`; `legacy.menu.mode` / `Mode`; `legacy.menu.players` / `Player1`; `legacy.menu.player2` / `Player2`; `legacy.menu.player3` / `Player3`; `legacy.menu.player4` / `Player4`; `legacy.menu.models` / `Models` | Legacy console/offline launch/session state, not a persistent player preference in the graphical SettingsView. |
| `legacy.menu.mph-version` / `MphVersion`; `legacy.menu.fh-version` / `FhVersion` | Content compatibility identity, owned by content discovery and paths. |
| `legacy.menu.cel-bands` / `CelBands`; `legacy.menu.cel-edge` / `CelEdge` | Fixed renderer design values; old values cannot override the current design. |
| `legacy.graphics.texture-filtering` / `TextureFiltering` | Boolean migration key. `TextureFilteringPreset` is canonical; `RenderOptions.ResolveTextureFilteringPreset` consults this key only when the new key is absent. |

An admitted online match does not call `GameSettings.ApplyMatchRules`; the
accepted `MatchRules` from the Node/Worker handoff are applied instead. This
is the correctness boundary that prevents an old local file from overriding
authoritative lobby configuration. Offline and compatibility launch paths
retain the old read path until a separately approved migration removes it.

## `controls.txt` inventory

`InputSettings` owns `controls.txt`, loaded by `InputSettings.LoadLines` and
written by `InputSettings.GetSaveLines`. The canonical static settings are:

| Property/key | Scope and default | Load/save and runtime consumer | Descriptor, UI, platform |
| --- | --- | --- | --- |
| `MouseSensitivity` / `sensitivity` | Client; `1` | I/S; mouse aim multiplier | `controls.mouse-sensitivity`, Controls/Mouse, all |
| `InvertMouseY` / `invert_y` | Client; `false` | I/S; mouse aim | `controls.mouse-invert-y`, Controls/Mouse, all |
| `InvertMouseX` / `invert_x` | Client; `false` | I/S; mouse aim | `controls.mouse-invert-x`, Controls/Mouse, all |
| `ScrollAllWeapons` / `scroll_all_weapons` | Client; `true` | I/S; player weapon-cycle path | `controls.scroll-all-weapons`, Controls/Mouse, all |
| `ChatKey` / `chat_key` | Client; `T` | I/S; shell consumes it before gameplay input | `controls.chat-key`, Controls/Keys, all |
| `ControllerPreset` / `controller_preset` | Client; `Classic` | I/S; `PadBindings` preset | `controls.controller-preset`, Controls/Gamepad, all |
| `GamepadHorizontalSensitivity` / `gamepad_horizontal_sensitivity` | Client; `1` | I/S; gamepad look | `controls.controller-horizontal-sensitivity`, Controls/Gamepad, all |
| `GamepadVerticalSensitivity` / `gamepad_vertical_sensitivity` | Client; `1` | I/S; gamepad look | `controls.controller-vertical-sensitivity`, Controls/Gamepad, all |
| `GamepadInvertY` / `gamepad_invert_y` | Client; `false` | I/S; gamepad look | `controls.controller-invert-y`, Controls/Gamepad, all |
| `GamepadHapticsEnabled` / `gamepad_haptics_enabled` | Client; `true` | I/S; haptic presentation | `controls.controller.haptics`, Controls/Gamepad, all |
| `GamepadMoveDeadZone` / `gamepad_move_deadzone` | Client; `.15` | I/S; left-stick input | `controls.controller.move-dead-zone`, Controls/Gamepad advanced, all |
| `GamepadLookDeadZone` / `gamepad_look_deadzone` | Client; `.10` | I/S; right-stick input | `controls.controller.look-dead-zone`, Controls/Gamepad advanced, all |
| `GamepadOuterDeadZone` / `gamepad_outer_deadzone` | Client; `.02` | I/S; outer-stick input | `controls.controller.outer-dead-zone`, Controls/Gamepad advanced, all |
| `GamepadMoveActivateThreshold` / `gamepad_move_activate` | Client; `.25` | I/S; movement threshold | `controls.controller.move-activate`, Controls/Gamepad advanced, all |
| `GamepadMoveReleaseThreshold` / `gamepad_move_release` | Client; `.18` | I/S; movement threshold | `controls.controller.move-release`, Controls/Gamepad advanced, all |
| `GamepadLookExponent` / `gamepad_look_exponent` | Client; `1.60` | I/S; look response | `controls.controller.response-exponent`, Controls/Gamepad advanced, all |
| `GamepadYawRate` / `gamepad_yaw_rate` | Client; `300` | I/S; look response | `controls.controller.yaw-rate`, Controls/Gamepad advanced, all |
| `GamepadPitchRate` / `gamepad_pitch_rate` | Client; `240` | I/S; look response | `controls.controller.pitch-rate`, Controls/Gamepad advanced, all |
| `GamepadOuterBoostEnabled` / `gamepad_outer_boost_enabled` | Client; `true` | I/S; outer-ring response | `controls.controller.outer-boost`, Controls/Gamepad advanced, all |
| `GamepadOuterBoostStart` / `gamepad_outer_boost_start` | Client; `.95` | I/S; outer-ring response | `controls.controller.outer-boost-start`, Controls/Gamepad advanced, all |
| `GamepadOuterYawBoost` / `gamepad_outer_yaw_boost` | Client; `150` | I/S; outer-ring response | `controls.controller.outer-yaw-boost`, Controls/Gamepad advanced, all |
| `GamepadOuterPitchBoost` / `gamepad_outer_pitch_boost` | Client; `80` | I/S; outer-ring response | `controls.controller.outer-pitch-boost`, Controls/Gamepad advanced, all |
| `GamepadBoostDelaySeconds` / `gamepad_boost_delay` | Client; `.18` seconds | I/S; outer-ring timing | `controls.controller.boost-delay`, Controls/Gamepad advanced, all |
| `GamepadBoostRampSeconds` / `gamepad_boost_ramp` | Client; `.12` seconds | I/S; outer-ring timing | `controls.controller.boost-ramp`, Controls/Gamepad advanced, all |
| `GamepadTriggerPressThreshold` / `gamepad_trigger_press` | Client; `.20` | I/S; trigger edge | `controls.controller.trigger-press`, Controls/Gamepad advanced, all |
| `GamepadTriggerReleaseThreshold` / `gamepad_trigger_release` | Client; `.12` | I/S; trigger edge | `controls.controller.trigger-release`, Controls/Gamepad advanced, all |
| `GamepadZoomMultiplier` / `gamepad_zoom_multiplier` | Client; `1` | I/S; zoom look rate | `controls.controller.zoom`, Controls/Gamepad, all |
| `GamepadGyroEnabled` / `gamepad_gyro_enabled` | Client; `false` | I/S; SDL gyro when supported | `controls.controller.gyro`, Controls/Gamepad advanced, desktop; disabled/unavailable on Android |
| `GamepadGyroSensitivity` / `gamepad_gyro_sensitivity` | Client; `1` | I/S; SDL gyro | `controls.controller.gyro-sensitivity`, Controls/Gamepad advanced, desktop |
| `GamepadGyroInvertX` / `gamepad_gyro_invert_x` | Client; `false` | I/S; SDL gyro | `controls.controller.gyro-invert-x`, Controls/Gamepad advanced, desktop |
| `GamepadGyroInvertY` / `gamepad_gyro_invert_y` | Client; `false` | I/S; SDL gyro | `controls.controller.gyro-invert-y`, Controls/Gamepad advanced, desktop |
| `InputBalanceTelemetryEnabled` / `input_balance_telemetry` | Diagnostics; `false` | I/S; local input-balance measurements | `controls.controller.telemetry`, Controls/Diagnostics, all |
| `StylusAimingEnabled` / `stylus_aiming` | Platform; `true` | I/S; Android stylus input | `controls.stylus.aiming`, Controls/Stylus, Android |
| `StylusSensitivity` / `stylus_sensitivity` | Platform; `1` | I/S; Android stylus input | `controls.stylus.sensitivity`, Controls/Stylus, Android |
| `StylusInvertY` / `stylus_invert_y` | Platform; `false` | I/S; Android stylus input | `controls.stylus.invert-y`, Controls/Stylus, Android |
| `StylusPrimaryAction` / `stylus_primary` | Platform; `Fire` | I/S; Android stylus bindings | `controls.stylus.primary`, Controls/Stylus, Android |
| `StylusSecondaryAction` / `stylus_secondary` | Platform; `Zoom` | I/S; Android stylus bindings | `controls.stylus.secondary`, Controls/Stylus, Android |
| `StylusClassicGestures` / `stylus_classic_gestures` | Platform; `true` | I/S; Android stylus gestures | `controls.stylus.classic-gestures`, Controls/Stylus, Android |
| `StylusDoubleTapJump` / `stylus_double_tap_jump` | Platform; `true` | I/S; Android touch/stylus gesture | `controls.stylus.double-tap-jump`, Controls/Stylus, Android |
| `StylusFlickBoost` / `stylus_flick_boost` | Platform; `true` | I/S; Android touch/stylus gesture | `controls.stylus.flick-boost`, Controls/Stylus, Android |
| `StylusPressureToFire` / `stylus_pressure_to_fire` | Platform; `false` | I/S; Android pressure input | `controls.stylus.pressure-to-fire`, Controls/Stylus advanced, Android |
| `StylusPressureThreshold` / `stylus_pressure_threshold` | Platform; `.35` | I/S; Android pressure input | `controls.stylus.pressure-threshold`, Controls/Stylus advanced, Android |
| `BottomScreenMode` / `bottom_screen_mode` | Platform; `Off` | I/S; scene-owned native six-affinity selector popup | `controls.stylus.bottom-screen-mode`, Controls/Stylus, desktop SDL pen + Android touch/stylus |

The bottom-screen implementation is a client-only in-renderer 4:3 panel. The
verified slice is the six-affinity weapon selector: it reuses the native HUD
selector assets and sector math, routes pointer input through the current scene,
and submits a `WeaponSelectionIntent` that is revalidated by the existing
authority path. Full native lower-screen radar/background art and unverified
hotspots remain staged work; no gameplay mapping is inferred for them.

Controller aim assist is currently an internal always-on behavior at its default
strength. It has no settings descriptor or UI row, and `controls.txt` no longer
writes its retired keys. Existing `gamepad_aim_assist`,
`gamepad_aim_assist_enabled`, and `gamepad_aim_assist_strength` lines are accepted
and ignored during migration so an older file cannot change the policy.

Touch layout values share `controls.txt` but are owned by `TouchSettings`:

| Property/key family | Scope/default | Load/save and runtime consumer | Descriptor, UI, platform |
| --- | --- | --- | --- |
| `ButtonsVisible` / `touch_buttons` | Platform; `true` | `TouchSettings.ReadSetting`/`WriteSettings`; Android touch overlay master visibility | `controls.touch-buttons`, Controls/On-screen buttons, Android |
| `IsEnabled(Shoot)` / `touch_shoot` | Platform; `true` | TouchSettings; Fire | `controls.touch.Shoot`, Controls/On-screen buttons, Android |
| `IsEnabled(Jump)` / `touch_jump` | Platform; `true` | TouchSettings; Jump | `controls.touch.Jump`, Controls/On-screen buttons, Android |
| `IsEnabled(Morph)` / `touch_morph` | Platform; `true` | TouchSettings; Morph | `controls.touch.Morph`, Controls/On-screen buttons, Android |
| `IsEnabled(SpectatorView)` / `touch_spectatorview` | Platform; `true` | TouchSettings; Spectator View | `controls.touch.SpectatorView`, Controls/On-screen buttons, Android |
| `IsEnabled(Missile)` / `touch_missile` | Platform; `true` | TouchSettings; Missile | `controls.touch.Missile`, Controls/On-screen buttons, Android |
| `IsEnabled(WeaponMenu)` / `touch_weaponmenu` | Platform; `true` | TouchSettings; Weapon wheel | `controls.touch.WeaponMenu`, Controls/On-screen buttons, Android |
| `IsEnabled(Zoom)` / `touch_zoom` | Platform; `true` | TouchSettings; Zoom | `controls.touch.Zoom`, Controls/On-screen buttons, Android |
| `IsEnabled(Pause)` / `touch_pause` | Platform; `true` | TouchSettings; Menu | `controls.touch.Pause`, Controls/On-screen buttons, Android |
| `IsEnabled(Scoreboard)` / `touch_scoreboard` | Platform; `true` | TouchSettings; Scoreboard | `controls.touch.Scoreboard`, Controls/On-screen buttons, Android |
| `IsEnabled(Chat)` / `touch_chat` | Platform; `true` | TouchSettings; Chat | `controls.touch.Chat`, Controls/On-screen buttons, Android |

### Dynamic binding inventory

The registry and view deliberately derive binding rows from the runtime-owned
collections rather than maintaining a second hand-written list:

* Every `Keybind` property in `InputSettings.Bindings` gets a
  `controls.key.<PropertyName>` descriptor and a row with the same stable row
  ID. The default comes from `ClientPlayerBindings.CreateDefault`; the two
  independent utility bindings are `RecapHistory = F6` and `QuickSwap = Q`.
  The current property set is:

  ```text
  MoveLeft=A, MoveRight=D, MoveUp=W, MoveDown=S
  RolltLeft=A, RollRight=D, RollUp=W, RollDown=S
  AimLeft=Left, AimRight=Right, AimUp=Up, AimDown=Down
  Shoot=Mouse left, Zoom=Mouse right, Jump=Space, Morph=C, Boost=Space
  AltAttack=Mouse left, NextWeapon=ScrollDown, PrevWeapon=ScrollUp
  WeaponMenu=Mouse middle, RecapHistory=F6, QuickSwap=Q
  PowerBeam=1, Missile=2, VoltDriver=3, Battlehammer=4, Imperialist=5
  Judicator=6, Magmaul=7, ShockCoil=8, OmegaCannon=9
  AffinitySlot=unbound, Pause=Tab, HudOverlay=LeftShift
  ```

  `InputSettings.GetSaveLines` writes each as `<PropertyName>=Key:...`,
  `Mouse:...`, `ScrollUp`, or `ScrollDown`. A newly added runtime Keybind is
  automatically included in the persistence scan and registry build; the
  concrete SettingsView row and coverage test are still required before it is
  considered complete.

* Every `PadBindings.Actions` entry gets a
  `controls.pad.<Action>` descriptor and `pad_<Action>` persistence key.
  Defaults and display labels are:

  | Action | Default |
  | --- | --- |
  | `Shoot` | `RightTrigger` (Fire / alt attack) |
  | `Zoom` | `LeftTrigger` |
  | `Jump` | `A` (Jump / boost) |
  | `Morph` | `B` |
  | `Scoreboard` | `Back` |
  | `NextWeapon` | `RightBumper \| DpadRight` |
  | `PrevWeapon` | `LeftBumper \| DpadLeft` |
  | `Missile` | `DpadUp` |
  | `PowerBeam` | `DpadDown` |
  | `Menu` | `Start` |
  | `WeaponWheel` | `X` |
  | `QuickSwap` | `Y` |

* Every `TouchSettings.Order` entry gets the `controls.touch.<Control>`
  descriptor listed above. This keeps the Android list discoverable while the
  master switch is off; individual values remain persisted underneath it.

The controls file also contains `input_schema=4`. It is a parser/schema marker,
not a player preference, and is explicitly excluded from the registry.

## `launcher.txt` inventory

`LauncherPrefs` owns `launcher.txt` and exposes its values to the shell, update
coordinator, content-pack discovery, and Node selection. The canonical writer
is `GetSaveLines`:

| Property/key | Scope and default | Load/save and runtime consumer | Descriptor, UI, platform |
| --- | --- | --- | --- |
| `BackendAddress` / `backend_address` | Internal/shell; configured backend origin | L/W; `AccountSessions`/Gateway | Excluded `launcher.backend-address`; advanced shell service setting, all |
| `LastRole` / `last_role` | Internal; `0` | L/W; legacy launch state | Excluded `launcher.last-role`, all |
| `PlayerName` / `player_name` | Client/guest; `Player` | L/W; guest admission and local profile form | `gameplay.player-name`, Gameplay/Profile, all |
| `LastHunter` / `hunter` | Client/profile default; `Samus` | L/W; local launch and profile default | `gameplay.hunter`, Gameplay/Profile, all |
| `Bots` / `bots` | Internal/offline launch; `3` | L/W; offline launch plan | Excluded `launcher.bots`, all |
| `BotLevel` / `bot_level` | Internal/offline launch; `1` | L/W; offline bot setup | Excluded `launcher.bot-level`, all |
| `LastKind` / `last_kind` | Internal; `0` | L/W; launch-state recall | Excluded `launcher.last-kind`, all |
| `UpdatePolicy` / `update_policy` | Client; `Automatic` | L/W; updater | `system.updates`, System/Updates, all |
| `PreferredRegion` / `preferred_region` | Client; `Automatic` | L/W; `PlayController` Node selection | `network.preferred-region`, Network/Region, all; dynamic choices |
| `DebugLogs` / `debug_logs` | Diagnostics; `false` | L/W; `DebugLog` attachment | `system.debug-logging`, System/Diagnostics, all |
| `ReducedMotion` / `reduced_motion` | Client; `false` | L/W; Prime shell motion resources | `accessibility.reduced-motion`, Accessibility/Motion, all |
| `AnnouncerPack` / `announcer_pack` | Client; built-in (`null`) | L/W; presentation content resolver | `audio.announcer-pack`, Audio/Packs, all; dynamic installed choices |
| `MusicPack` / `music_pack` | Client; built-in (`null`) | L/W; presentation content resolver | `audio.music-pack`, Audio/Packs, all; dynamic installed choices |
| `WindowMode` / `window_mode` | Client; `Windowed` | L/W; `WindowMode.Startup` | `graphics.window-mode`, Graphics/Window, all; Android displays managed/disabled |

`GameFiles` and `ShareLogs` are informational/action rows. They have stable
registry row IDs but no persisted setting key. `BackendAddress`, role/bot
launch values, and `last_kind` remain in the file for shell/compatibility
operation and are excluded from ordinary player settings.

The old `auto_update` key is read only when `update_policy` is absent. It maps
`true` to `NotifyOnly` and `false` to `Off`, then `LauncherPrefs.Save` writes
the canonical `update_policy` form. The registry records this as the
`auto_update` alias of `system.updates`.

Preferred-region choices are not a hard-coded list. The launcher always offers
`Automatic`, adds valid region IDs observed from the current bounded directory
refresh, and retains a valid persisted ID even when that ID is not currently
advertised. Invalid, empty, control-containing, or overlong IDs are ignored;
the observed set is capped at 128. Settings keeps each canonical ID separate
from its friendly presentation label: known IDs use stable labels and an
unrecognized ID is shown as `Unknown (id)` without changing the persisted ID.

## Account/backend state is not local settings state

Authenticated profile values are owned by the Backend and PostgreSQL:
`PlayerProfile.DisplayName` and `PlayerProfile.FavoriteHunter` are updated by
`ProfileEndpoints` and read through the account license. The authenticated
display name is the identity used for Node admission. `LauncherPrefs.PlayerName`
is a local guest/display-name preference and is never overwritten when a
signed-in identity is restored; it is not a second authenticated identity
authority. `SettingsView` receives an immutable identity presentation snapshot
so authenticated Settings shows the account name and Hunter Profile action,
while guest/signed-out Settings shows only the scoped local guest name.

Guest admission is explicit. A failed sign-in or restore does not silently
select guest mode. A guest display name is not an account identity, an
authorization key, a reconnect owner, or a uniqueness key. The Node carries a
tagged account or guest identity in its session/lobby contracts.

The lobby's selected Hunter is also distinct from the local favorite/default:
`LobbySelectHunter` updates authoritative lobby membership and readiness, while
`gameplay.hunter` only supplies the local profile/launch default. No local
descriptor edits lobby state directly.

## Authoritative lobby rules

`LobbyRulesOptions` is the one canonical host-rule representation. Its nullable
fields mean “use the mode's authoritative default,” not “accept an arbitrary
client value.” `LobbyManager.Lobby.HostRules` owns the normalized value. The
Node normalizes the legacy `LobbyConfigure.TimeLimitSeconds` and `PointGoal`
fields into it, rejects conflicting legacy/structured values and
mode-inapplicable fields, and validates ranges before changing map, readiness,
waitlist, or revision state.

The vertical path is:

```text
HostMatchDraft / Edit Match
    -> PlayController.ConfigureLobbyAsync
    -> v3 LobbyConfigure
    -> Node normalization and validation
    -> LobbySnapshot.HostRules + legacy projections
    -> MatchSpec freeze
    -> Worker MatchInstance
```

`HostMatchDraft` and `LobbyRuleApplicability` are usability layers only. The
server remains authoritative. Mode-specific labels are derived from the mode:
score goal for Battle-like modes, starting lives for Survival, objective time
for Defender/Prime Hunter, and Octolith reset only where applicable. Team play
is derived from mode; it is not an independent global setting.

Every match start, rematch, and supported continuation derives `MatchSpec.Rules`
from the same canonical `HostRules`. `LobbySnapshot` retains the legacy
`TimeLimitSeconds` and `PointGoal` projections for old-shaped consumers. When
the canonical `Rules` object is present, codec validation requires those
projections to agree; an absent `Rules` object is the permitted old-shaped
snapshot form. Public list entries carry only bounded map/mode/rule metadata,
not an entire lobby snapshot.

## Control protocol v3 and mixed-version policy

The reliable Node control envelope is now version 3:

* `NodeControlCodec.Version` is exactly `3`; `Write` emits version 3.
* `Read` requires an object with exactly `version`, `type`, `requestId`, and
  `payload`, rejects duplicate JSON properties, and rejects any envelope whose
  version is not 3 before decoding its payload.
* Source-generated JSON uses `UnmappedMemberHandling.Disallow`, so unknown
  command or nested rule fields fail closed rather than being silently dropped.
* `LobbyConfigure.Rules` is additive within v3. A v3 payload that omits
  `Rules` remains readable and is normalized from the legacy optional fields.
  An advanced field must be inside the structured object and must be known.
* A v1 envelope is not down-converted or opportunistically accepted. It fails
  with `Unsupported control envelope version` before payload interpretation.
  Therefore a v1 Node/client pair and a v3 Node/client pair are not a supported
  mixed deployment; upgrade the control endpoint as one compatibility unit.

The canonical rule object is still checked when snapshots are written and
read. Invalid map/mode/range/applicability data, oversized list metadata, and
conflicting legacy projections fail closed. This is separate from gameplay
UDP protocol compatibility; the v3 statement applies to the Node control
envelope only.

## Migration policy and current follow-up risks

The intended steady-state policy for a renamed value is:

```text
read old alias -> normalize into canonical value -> write canonical key
```

Current implementation status is intentionally recorded rather than implied:

| Area | Current behavior | Follow-up |
| --- | --- | --- |
| `launcher.txt` `auto_update` | Read-only alias; canonical `update_policy` wins when both exist, and a legacy-only load saves the new key. | Keep the alias read until the supported migration window ends. |
| `controls.txt` dead zone/look/preset/gyro/haptics aliases | Modern keys win regardless of file order. `InputSettings.GetSaveLines` writes the canonical keys and additionally keeps `gamepad_deadzone` and `gamepad_look` compatibility lines. `gamepad_invert_y` is the current canonical controller-invert spelling, not a separate duplicate. | Decide, in a separate compatibility change, when old writers can stop emitting the extra dead-zone/look lines. Do not remove reads first. |
| `controls.txt` additional parser spellings | Reader accepts threshold/response/yaw/pitch/boost/trigger/sensitivity/zoom and related older spellings. Retired aim-assist keys are consumed but ignored. | Keep parser aliases covered by migration tests; registry aliases should be extended if a spelling becomes a supported documented input. |
| `settings.json` `TextureFiltering` | Canonical `TextureFilteringPreset` wins; old boolean is consulted only when canonical is absent. The current SettingsView still writes the old boolean for older builds. | Stop emitting the old field only after the mixed-reader support window is explicitly closed. |
| `settings.json` obsolete match fields | They remain public `MenuSettings` properties and are serialized by the general JSON serializer. The registry excludes them, and admitted online matches ignore them in favor of Node rules. | If the fields are eventually removed from writes, retain tolerant reads first and preserve offline/console compatibility deliberately. |
| `CelBands`/`CelEdge` | Old values deserialize but are overridden by fixed renderer values; the SettingsView writes fixed compatibility values. | Keep the fixed-value exclusion until a separate renderer design changes it. |

### Unsupported or retired values

Unsupported values follow the existing tolerant-read/authoritative-owner
pattern rather than becoming hidden controls:

* Legacy global match rules may be deserialized and used only by the
  non-admitted compatibility path. An admitted online match takes rules from
  the Node snapshot/Worker handoff, so a stale local value cannot override the
  host.
* `CelBands` and `CelEdge` are accepted for old-file compatibility but are
  replaced by the renderer's fixed values. The old `TextureFiltering` boolean
  is only a fallback when `TextureFilteringPreset` is absent.
* Launcher endpoint, role, bot, and schema fields remain operational state or
  parser markers. They are explicitly excluded from the player-facing
  registry and must not be promoted to settings merely because they persist.

No migration may let an old file silently reintroduce a retired player-facing
control or override an authoritative online rule. Removing a read requires a
separate compatibility decision and evidence from the supported upgrade
window; it must not be inferred from the absence of a current UI row.

## Evidence boundary and validation gates

Use these evidence labels in future work and review:

| Evidence category | What it establishes | What it does not establish |
| --- | --- | --- |
| Source/static | Ownership, call paths, persistence identities, codec rules, and exclusions in the current tree. | That the native UI renders correctly or that a deployed service accepts the flow. |
| Focused tests | Registry coverage/uniqueness, setting reset/round-trip seams, lobby-rule normalization, canonical snapshot projections, and control-codec rejection/compatibility cases. | Physical controller ergonomics, Android input delivery, GPU rendering, or internet behavior. |
| Build/infrastructure | Compilation, package boundaries, generated source contracts, local server/package smoke where actually run. | A release-quality deployed topology or sustained production capacity. |
| External live validation | Real desktop/native window, real controller, Android/touch device, deployed Backend + Node + Worker, repeated handoff/results/lobby flow. | Nothing beyond the exact binary, device, deployment, and version tested. |

The focused source/tests associated with this contract are:

* `tests/Tests/Client/SettingsRegistryTests.cs` and
  `tests/Tests/Client/SettingsResetTests.cs`
* `tests/Tests/Client/ControllerCapabilityTests.cs` and the capability/identity
  cases in `tests/Tests/Client/UiCaptureFixtureTests.cs` and
  `tests/Tests/Client/PrimeShellControllerTests.cs`
* `tests/Server.Shared.Tests/LobbyRulesContractTests.cs` and
  `tests/Server.Node.Tests/LobbyRulesTests.cs`
* `tests/Server.Node.Tests/ControlCodecTests.cs`

Passing a source/build/focused test is not a live GUI, Windows/native,
controller, Android, deployed database, or load/soak result. The external
gates remain: rendered SettingsView at representative desktop/tablet/phone
sizes; keyboard/controller/touch operation; Node v3 control against the
deployed service; host-rule-to-Worker vertical behavior; reconnect and repeated
round continuation; and Android safe-area/soft-keyboard behavior. Each gate
must be reported with its exact build, device/topology, and result.

## Maintenance checklist

When adding a persisted player-facing value:

1. Identify its single scope and runtime consumer.
2. Choose exactly one file/key and a stable descriptor/row ID.
3. Add a descriptor or a specific `SettingExclusion` with a reason.
4. Add an alias only when there is a real older key, and make precedence
   explicit.
5. Add load/save and invalid-value coverage, including the relevant platform.
6. Verify online rules remain in the lobby/Node path and never in global local
   Settings.
7. Record source, focused-test, infrastructure, and live evidence separately.

This keeps the settings surface discoverable without turning the registry into
an implicit authority layer or allowing persistence and UI coverage to drift.
