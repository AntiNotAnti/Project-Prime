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
    public const string CelShading = "graphics.cel-shading";

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

    public const string FeedbackVolume = "audio.feedback-volume";
    public const string SfxVolume = "audio.sfx-volume";
    public const string MusicVolume = "audio.music-volume";
    public const string AnnouncerPack = "audio.announcer-pack";
    public const string MusicPack = "audio.music-pack";
    public const string Language = "audio.language";

    public const string MouseSensitivity = "controls.mouse-sensitivity";
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

    public const string PlayerName = "gameplay.player-name";
    public const string Hunter = "gameplay.hunter";
    public const string Updates = "system.updates";
    public const string GameFiles = "system.game-files";
    public const string DebugLogging = "system.debug-logging";
    public const string ShareLogs = "system.share-logs";
    public const string PreferredRegion = "network.preferred-region";
    public const string NetworkDiagnostics = "network.diagnostics";
    public const string ReducedMotion = "accessibility.reduced-motion";

    public static string KeyBinding(string propertyName) => "controls.key." + propertyName;
    public static string PadBinding(string action) => "controls.pad." + action;
    public static string TouchBinding(string control) => "controls.touch." + control;
    public static string Stylus(string propertyName) => "controls.stylus." + propertyName;

    /// <summary>All fixed IDs; dynamic rows are added by SettingRegistry.</summary>
    public static IReadOnlyList<string> Fixed { get; } =
    [
        WindowMode, RenderScale, FieldOfView, FpsLimit, GraphicsPreset, TextureFiltering,
        Anisotropy, Msaa, Bloom, DynamicLighting, Lighting, Fog, FpsCounter,
        CelShading, ProHud, ProHudWeapon, ReticleOpacity, ReticleScale, CrosshairSize,
        CrosshairStyle, HitMarkers, HitMarkerTiming, HeadshotCue,
        KillConfirmation, Killcam, RadarStyle, RadarOrientation, RadarAnchor, RadarScale,
        RadarOffsetX, RadarOffsetY, RadarRange, RadarOpacity, RadarElevation,
        FeedbackVolume, SfxVolume, MusicVolume,
        AnnouncerPack, MusicPack, Language, MouseSensitivity, MouseInvertY,
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
        ControllerZoom, ControllerGyro, ControllerGyroActivation, ControllerGyroSensitivity,
        ControllerGyroInvertX, ControllerGyroInvertY, ControllerTelemetry,
        TouchButtons, StylusAiming, StylusSensitivity, StylusInvertY,
        StylusPrimary, StylusSecondary, StylusClassicGestures,
        StylusDoubleTapJump, StylusFlickBoost, StylusPressureToFire,
        StylusPressureThreshold,
        PlayerName, Hunter, Updates, GameFiles, DebugLogging, ShareLogs,
        PreferredRegion, NetworkDiagnostics, ReducedMotion
    ];
}
