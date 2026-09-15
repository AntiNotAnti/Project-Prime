using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Settings;

/// <summary>
/// Stable row identifiers shared by descriptors and the concrete settings
/// controls. Dynamic keyboard/pad/touch rows append their persisted action key.
/// </summary>
public static class SettingRowIds
{
    public const string WindowMode = "graphics.window-mode";
    public const string RenderScale = "graphics.render-scale";
    public const string FieldOfView = "graphics.field-of-view";
    public const string FpsLimit = "graphics.fps-limit";
    public const string GraphicsPreset = "graphics.quality";
    public const string TextureFiltering = "graphics.texture-filtering";
    public const string Anisotropy = "graphics.texture-detail";
    public const string Msaa = "graphics.edge-smoothing";
    public const string Bloom = "graphics.bloom";
    public const string DynamicLighting = "graphics.dynamic-lighting";
    public const string Lighting = "graphics.lighting";
    public const string Fog = "graphics.fog";
    public const string FpsCounter = "graphics.fps-counter";
    public const string TexturePack = "graphics.texture-pack";
    public const string VisualStyle = "graphics.visual-style";

    public const string ProHud = "hud.pro";
    public const string ProHudWeapon = "hud.pro-weapon";
    public const string ReticleOpacity = "hud.reticle-opacity";
    public const string ReticleScale = "hud.reticle-scale";
    public const string CrosshairSize = "hud.crosshair-size";
    public const string CrosshairStyle = "hud.crosshair-style";
    public const string HitMarkers = "hud.hit-markers";
    public const string HitMarkerTiming = "hud.hit-marker-timing";
    public const string HeadshotCue = "hud.headshot-cue";
    public const string KillConfirmation = "hud.kill-confirmation";
    public const string Killcam = "hud.killcam";
    public const string RadarStyle = "hud.radar.style";
    public const string RadarOrientation = "hud.radar.orientation";
    public const string RadarAnchor = "hud.radar.anchor";
    public const string RadarScale = "hud.radar.scale";
    public const string RadarOffsetX = "hud.radar.offset-x";
    public const string RadarOffsetY = "hud.radar.offset-y";
    public const string RadarRange = "hud.radar.range";
    public const string RadarOpacity = "hud.radar.opacity";
    public const string RadarElevation = "hud.radar.elevation";
    public const string RadarPreset = "hud.radar.preset";
    public const string RadarColors = "hud.radar.colors";
    public const string RadarMarkerScale = "hud.radar.marker-scale";
    public const string RadarMarkerOpacity = "hud.radar.marker-opacity";
    public const string RadarMarkerOutline = "hud.radar.marker-outline";
    public const string RadarEdgeArrows = "hud.radar.edge-arrows";
    public const string RadarLabels = "hud.radar.labels";
    public const string RadarObjectiveEmphasis = "hud.radar.objective-emphasis";
    public const string RadarEnemies = "hud.radar.enemies";
    public const string RadarTeammates = "hud.radar.teammates";
    public const string RadarObjectives = "hud.radar.objectives";
    public const string RadarFlags = "hud.radar.flags";
    public const string RadarBases = "hud.radar.bases";
    public const string RadarNodes = "hud.radar.nodes";
    public const string RadarDefenders = "hud.radar.defenders";
    public const string RadarResources = "hud.radar.resources";
    public const string RadarWeapons = "hud.radar.weapons";
    public const string RadarAmmo = "hud.radar.ammo";
    public const string RadarHealth = "hud.radar.health";
    public const string RadarPowerups = "hud.radar.powerups";
    public const string RadarFloors = "hud.radar.floors";
    public const string RadarElevationThreshold = "hud.radar.elevation-threshold";
    public const string RadarZoom = "hud.radar.zoom";
    public const string RadarAutoMinimum = "hud.radar.auto-minimum";
    public const string RadarAutoMaximum = "hud.radar.auto-maximum";
    public const string RadarZoomSmoothing = "hud.radar.zoom-smoothing";
    public const string RadarMapFill = "hud.radar.map-fill";
    public const string RadarMapOutlines = "hud.radar.map-outlines";
    public const string RadarFloorBrightness = "hud.radar.floor-brightness";
    public const string RadarAdjacentOpacity = "hud.radar.adjacent-opacity";
    public const string RadarBackgroundDim = "hud.radar.background-dim";
    public const string RadarBackgroundBlur = "hud.radar.background-blur";
    public const string RadarGrid = "hud.radar.grid";
    public const string RadarRings = "hud.radar.rings";
    public const string RadarCompass = "hud.radar.compass";
    public const string RadarPersistence = "hud.radar.persistence";
    public const string RadarPulse = "hud.radar.pulse";
    public const string RadarEdgeScale = "hud.radar.edge-scale";
    public const string RadarPriority = "hud.radar.priority";

    public const string FeedbackVolume = "audio.feedback-volume";
    public const string SfxVolume = "audio.sfx-volume";
    public const string MusicVolume = "audio.music-volume";
    public const string AnnouncerPack = "audio.announcer-pack";
    public const string MusicPack = "audio.music-pack";
    public const string Language = "audio.language";

    public const string MouseSensitivity = "controls.mouse-sensitivity";
    public const string DynamicCrosshairTravel = "controls.dynamic-crosshair.travel";
    public const string DynamicCrosshairSensitivity = "controls.dynamic-crosshair.sensitivity";
    public const string DynamicCrosshairTurnSpeed = "controls.dynamic-crosshair.turn-speed";
    public const string MouseInvertY = "controls.mouse-invert-y";
    public const string MouseInvertX = "controls.mouse-invert-x";
    public const string ScrollAllWeapons = "controls.scroll-all-weapons";
    public const string MorphBallMouseFlickBoost = "controls.morph-ball.mouse-flick-boost";
    public const string MorphBallStickFlickBoost = "controls.morph-ball.stick-flick-boost";
    public const string MorphBallSwipeBoost = "controls.morph-ball.swipe-boost";
    public const string ChatKey = "controls.chat-key";
    public const string ControllerPreset = "controls.controller.preset";
    public const string ControllerHorizontalSensitivity = "controls.controller.horizontal-sensitivity";
    public const string ControllerVerticalSensitivity = "controls.controller.vertical-sensitivity";
    public const string ControllerInvertY = "controls.controller.invert-y";
    public const string ControllerHaptics = "controls.controller.haptics";
    public const string ControllerHapticsStrength = "controls.controller.haptics-strength";
    public const string ControllerMoveDeadZone = "controls.controller.move-dead-zone";
    public const string ControllerLookDeadZone = "controls.controller.look-dead-zone";
    public const string ControllerOuterDeadZone = "controls.controller.outer-dead-zone";
    public const string ControllerMoveActivate = "controls.controller.move-activate";
    public const string ControllerMoveRelease = "controls.controller.move-release";
    public const string ControllerExponent = "controls.controller.response-exponent";
    public const string ControllerResponseCurve = "controls.controller.response-curve";
    public const string ControllerTurnAcceleration = "controls.controller.turn-acceleration";
    public const string ControllerYawRate = "controls.controller.yaw-rate";
    public const string ControllerPitchRate = "controls.controller.pitch-rate";
    public const string ControllerOuterBoost = "controls.controller.outer-boost";
    public const string ControllerOuterBoostStart = "controls.controller.outer-boost-start";
    public const string ControllerOuterYawBoost = "controls.controller.outer-yaw-boost";
    public const string ControllerOuterPitchBoost = "controls.controller.outer-pitch-boost";
    public const string ControllerBoostDelay = "controls.controller.boost-delay";
    public const string ControllerBoostRamp = "controls.controller.boost-ramp";
    public const string ControllerTriggerPress = "controls.controller.trigger-press";
    public const string ControllerTriggerRelease = "controls.controller.trigger-release";
    public const string ControllerZoom = "controls.controller.zoom";
    public const string ControllerZoomVertical = "controls.controller.zoom-vertical";
    public const string ControllerAutoCalibration = "controls.controller.auto-calibration";
    public const string ControllerStickAimMode = "controls.controller.stick-aim-mode";
    public const string ControllerGyro = "controls.controller.gyro";
    public const string ControllerGyroActivation = "controls.controller.gyro-activation";
    public const string ControllerGyroSensitivity = "controls.controller.gyro-sensitivity";
    public const string ControllerGyroInvertX = "controls.controller.gyro-invert-x";
    public const string ControllerGyroInvertY = "controls.controller.gyro-invert-y";
    public const string ControllerTelemetry = "controls.controller.telemetry";
    public const string TouchButtons = "controls.touch.buttons";
    public const string StylusAiming = "controls.stylus.aiming";
    public const string StylusSensitivity = "controls.stylus.sensitivity";
    public const string StylusInvertY = "controls.stylus.invert-y";
    public const string StylusPrimary = "controls.stylus.primary";
    public const string StylusSecondary = "controls.stylus.secondary";
    public const string StylusClassicGestures = "controls.stylus.classic-gestures";
    public const string StylusDoubleTapJump = "controls.stylus.double-tap-jump";
    public const string StylusFlickBoost = "controls.stylus.flick-boost";
    public const string StylusPressureToFire = "controls.stylus.pressure-to-fire";
    public const string StylusPressureThreshold = "controls.stylus.pressure-threshold";
    public const string BottomScreenMode = "controls.stylus.bottom-screen-mode";
    public const string BottomScreenActivation = "controls.stylus.bottom-screen-activation";
    public const string BottomScreenCursorSensitivity = "controls.stylus.bottom-screen-cursor-sensitivity";
    public const string BottomScreenCursorStartX = "controls.stylus.bottom-screen-cursor-start-x";
    public const string BottomScreenCursorStartY = "controls.stylus.bottom-screen-cursor-start-y";
    public const string BottomScreenPowerBeamX = "controls.stylus.bottom-screen-power-beam-x";
    public const string BottomScreenPowerBeamY = "controls.stylus.bottom-screen-power-beam-y";
    public const string BottomScreenMissileX = "controls.stylus.bottom-screen-missile-x";
    public const string BottomScreenMissileY = "controls.stylus.bottom-screen-missile-y";
    public const string BottomScreenNextWeaponX = "controls.stylus.bottom-screen-next-weapon-x";
    public const string BottomScreenNextWeaponY = "controls.stylus.bottom-screen-next-weapon-y";
    public const string BottomScreenWeaponSelectX = "controls.stylus.bottom-screen-weapon-select-x";
    public const string BottomScreenWeaponSelectY = "controls.stylus.bottom-screen-weapon-select-y";
    public const string BottomScreenAltFormX = "controls.stylus.bottom-screen-alt-form-x";
    public const string BottomScreenAltFormY = "controls.stylus.bottom-screen-alt-form-y";
    public const string BottomScreenDirectionalSwipeAssist = "controls.stylus.bottom-screen-directional-swipe-assist";
    public const string BottomScreenAffinityVoltDriverX = "controls.stylus.bottom-screen-affinity-volt-driver-x";
    public const string BottomScreenAffinityVoltDriverY = "controls.stylus.bottom-screen-affinity-volt-driver-y";
    public const string BottomScreenAffinityBattlehammerX = "controls.stylus.bottom-screen-affinity-battlehammer-x";
    public const string BottomScreenAffinityBattlehammerY = "controls.stylus.bottom-screen-affinity-battlehammer-y";
    public const string BottomScreenAffinityImperialistX = "controls.stylus.bottom-screen-affinity-imperialist-x";
    public const string BottomScreenAffinityImperialistY = "controls.stylus.bottom-screen-affinity-imperialist-y";
    public const string BottomScreenAffinityJudicatorX = "controls.stylus.bottom-screen-affinity-judicator-x";
    public const string BottomScreenAffinityJudicatorY = "controls.stylus.bottom-screen-affinity-judicator-y";
    public const string BottomScreenAffinityMagmaulX = "controls.stylus.bottom-screen-affinity-magmaul-x";
    public const string BottomScreenAffinityMagmaulY = "controls.stylus.bottom-screen-affinity-magmaul-y";
    public const string BottomScreenAffinityShockCoilX = "controls.stylus.bottom-screen-affinity-shock-coil-x";
    public const string BottomScreenAffinityShockCoilY = "controls.stylus.bottom-screen-affinity-shock-coil-y";
    public const string BottomScreenAffinityPowerBeamX = "controls.stylus.bottom-screen-affinity-power-beam-x";
    public const string BottomScreenAffinityPowerBeamY = "controls.stylus.bottom-screen-affinity-power-beam-y";
    public const string BottomScreenAffinityMissileX = "controls.stylus.bottom-screen-affinity-missile-x";
    public const string BottomScreenAffinityMissileY = "controls.stylus.bottom-screen-affinity-missile-y";
    public const string BottomScreenAffinityAltFormX = "controls.stylus.bottom-screen-affinity-alt-form-x";
    public const string BottomScreenAffinityAltFormY = "controls.stylus.bottom-screen-affinity-alt-form-y";
    public const string BottomScreenStyle = "controls.stylus.bottom-screen-style";
    public const string BottomScreenScale = "controls.stylus.bottom-screen-scale";
    public const string BottomScreenCenterX = "controls.stylus.bottom-screen-center-x";
    public const string BottomScreenCenterY = "controls.stylus.bottom-screen-center-y";
    public const string BottomScreenOpacity = "controls.stylus.bottom-screen-opacity";
    public const string BottomScreenLabels = "controls.stylus.bottom-screen-labels";

    public const string PlayerName = "gameplay.player-name";
    public const string Hunter = "gameplay.hunter";
    public const string ShowOnlinePresence = "gameplay.show-online-presence";
    public const string Updates = "system.updates";
    public const string GameFiles = "system.game-files";
    public const string DebugLogging = "system.debug-logging";
    public const string ShareLogs = "system.share-logs";
    public const string PreferredRegion = "network.preferred-region";
    public const string NetworkDiagnostics = "network.diagnostics";
    public const string ReducedMotion = "accessibility.reduced-motion";
    public const string CosmeticQuality = "accessibility.cosmetics.quality";
    public const string ShowOtherPlayerCosmetics = "accessibility.cosmetics.show-others";
    public const string ReduceCosmeticFlashes = "accessibility.cosmetics.reduce-flashes";
    public const string ForceStrongTeamColors = "accessibility.cosmetics.strong-team-colors";
    public const string DisableCosmeticDistortion = "accessibility.cosmetics.disable-distortion";
    public const string DisableCosmeticParticles = "accessibility.cosmetics.disable-particles";

    public static string KeyBinding(string propertyName) => "controls.key." + propertyName;
    public static string PadBinding(string action) => "controls.pad." + action;
    public static string TouchBinding(string control) => "controls.touch." + control;
    public static string Stylus(string propertyName) => "controls.stylus." + propertyName;

    /// <summary>All fixed IDs; dynamic rows are added by SettingRegistry.</summary>
    public static IReadOnlyList<string> Fixed { get; } =
    [
        WindowMode, RenderScale, FieldOfView, FpsLimit, GraphicsPreset, TextureFiltering,
        Anisotropy, Msaa, Bloom, DynamicLighting, Lighting, Fog, FpsCounter,
        TexturePack, VisualStyle, ProHud, ProHudWeapon, ReticleOpacity, ReticleScale, CrosshairSize,
        CrosshairStyle, HitMarkers, HitMarkerTiming, HeadshotCue,
        KillConfirmation, Killcam, RadarStyle, RadarOrientation, RadarAnchor, RadarScale,
        RadarOffsetX, RadarOffsetY, RadarRange, RadarOpacity, RadarElevation,
        RadarPreset, RadarColors, RadarMarkerScale, RadarMarkerOpacity, RadarMarkerOutline, RadarEdgeArrows,
        RadarLabels, RadarObjectiveEmphasis, RadarEnemies, RadarTeammates, RadarObjectives,
        RadarFlags, RadarBases, RadarNodes, RadarDefenders, RadarResources, RadarWeapons,
        RadarAmmo, RadarHealth, RadarPowerups, RadarFloors, RadarElevationThreshold,
        RadarZoom, RadarAutoMinimum, RadarAutoMaximum, RadarZoomSmoothing, RadarMapFill,
        RadarMapOutlines, RadarFloorBrightness, RadarAdjacentOpacity, RadarBackgroundDim,
        RadarBackgroundBlur, RadarGrid, RadarRings, RadarCompass, RadarPersistence,
        RadarPulse, RadarEdgeScale, RadarPriority,
        FeedbackVolume, SfxVolume, MusicVolume,
        AnnouncerPack, MusicPack, Language, MouseSensitivity,
        DynamicCrosshairTravel, DynamicCrosshairSensitivity,
        DynamicCrosshairTurnSpeed, MouseInvertY,
        MouseInvertX, ScrollAllWeapons, MorphBallMouseFlickBoost,
        MorphBallStickFlickBoost, MorphBallSwipeBoost, ChatKey, ControllerPreset,
        ControllerHorizontalSensitivity, ControllerVerticalSensitivity,
        ControllerInvertY, ControllerHaptics, ControllerHapticsStrength, ControllerMoveDeadZone,
        ControllerLookDeadZone, ControllerOuterDeadZone, ControllerMoveActivate,
        ControllerMoveRelease, ControllerResponseCurve, ControllerExponent,
        ControllerTurnAcceleration, ControllerYawRate,
        ControllerPitchRate, ControllerOuterBoost, ControllerOuterBoostStart,
        ControllerOuterYawBoost, ControllerOuterPitchBoost, ControllerBoostDelay,
        ControllerBoostRamp, ControllerTriggerPress, ControllerTriggerRelease,
        ControllerZoom, ControllerZoomVertical, ControllerAutoCalibration,
        ControllerStickAimMode, ControllerGyro, ControllerGyroActivation, ControllerGyroSensitivity,
        ControllerGyroInvertX, ControllerGyroInvertY, ControllerTelemetry,
        TouchButtons, StylusAiming, StylusSensitivity, StylusInvertY,
        StylusPrimary, StylusSecondary, StylusClassicGestures,
        StylusDoubleTapJump, StylusFlickBoost, StylusPressureToFire,
        StylusPressureThreshold, BottomScreenMode, BottomScreenActivation,
        BottomScreenCursorSensitivity, BottomScreenCursorStartX, BottomScreenCursorStartY,
        BottomScreenPowerBeamX, BottomScreenPowerBeamY, BottomScreenMissileX,
        BottomScreenMissileY, BottomScreenNextWeaponX, BottomScreenNextWeaponY,
        BottomScreenWeaponSelectX, BottomScreenWeaponSelectY, BottomScreenAltFormX,
        BottomScreenAltFormY, BottomScreenDirectionalSwipeAssist,
        BottomScreenAffinityVoltDriverX, BottomScreenAffinityVoltDriverY,
        BottomScreenAffinityBattlehammerX, BottomScreenAffinityBattlehammerY,
        BottomScreenAffinityImperialistX, BottomScreenAffinityImperialistY,
        BottomScreenAffinityJudicatorX, BottomScreenAffinityJudicatorY,
        BottomScreenAffinityMagmaulX, BottomScreenAffinityMagmaulY,
        BottomScreenAffinityShockCoilX, BottomScreenAffinityShockCoilY,
        BottomScreenAffinityPowerBeamX, BottomScreenAffinityPowerBeamY,
        BottomScreenAffinityMissileX, BottomScreenAffinityMissileY,
        BottomScreenAffinityAltFormX, BottomScreenAffinityAltFormY,
        BottomScreenStyle,
        BottomScreenScale, BottomScreenCenterX, BottomScreenCenterY,
        BottomScreenOpacity, BottomScreenLabels,
        PlayerName, Hunter, ShowOnlinePresence, Updates, GameFiles, DebugLogging, ShareLogs,
        PreferredRegion, NetworkDiagnostics, ReducedMotion, CosmeticQuality,
        ShowOtherPlayerCosmetics, ReduceCosmeticFlashes, ForceStrongTeamColors,
        DisableCosmeticDistortion, DisableCosmeticParticles
    ];
}
