using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MphRead.Hud.Radar;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.Settings;

/// <summary>
/// Descriptive inventory of the settings surface.
///
/// The registry deliberately contains no control factories and no delegates
/// that close over a <c>MenuSettings</c> instance. A view binds a descriptor to
/// the current lifecycle when it is created; this inventory remains safe to
/// inspect while a view is being torn down and rebuilt.
/// </summary>
public static class SettingRegistry
{
    public const string MenuSettingsFile = "settings.json";
    public const string ControlsFile = "controls.txt";
    public const string LauncherFile = "launcher.txt";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; }
        = BuildDescriptors();

    public static IReadOnlyList<SettingDescriptor> All => Descriptors;

    public static IReadOnlyList<SettingExclusion> Exclusions { get; }
        = BuildExclusions();

    public static IReadOnlyList<SettingAlias> Aliases { get; }
        = BuildAliases();

    static SettingRegistry()
    {
        ValidateOrThrow(Descriptors, Exclusions, Aliases);
    }

    public static SettingDescriptor Get(string id)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        return Descriptors.FirstOrDefault(descriptor => descriptor.Id == id)
            ?? throw new KeyNotFoundException($"Unknown setting descriptor '{id}'.");
    }

    /// <summary>Validate the built-in inventory at an explicit call site.</summary>
    public static void Validate()
        => ValidateOrThrow(Descriptors, Exclusions, Aliases);

    /// <summary>
    /// Validate that a concrete SettingsView rendered every applicable row.
    /// Platform-gated descriptors are ignored when their platform is not the
    /// requested one; this is what lets Android omit desktop-only controls.
    /// </summary>
    public static void ValidateRenderedRows(IEnumerable<string> renderedRowIds,
        SettingPlatform platform = SettingPlatform.All)
    {
        if (renderedRowIds == null) throw new ArgumentNullException(nameof(renderedRowIds));
        IReadOnlyList<string> errors = GetValidationErrors(Descriptors, Exclusions, Aliases,
            renderedRowIds, platform);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(String.Join(Environment.NewLine, errors));
        }
    }

    /// <summary>
    /// Return validation diagnostics instead of throwing, useful to tests and
    /// tooling that wants to report all broken descriptors at once.
    /// </summary>
    public static IReadOnlyList<string> GetValidationErrors(
        IEnumerable<SettingDescriptor> descriptors,
        IEnumerable<SettingExclusion>? exclusions = null,
        IEnumerable<SettingAlias>? aliases = null,
        IEnumerable<string>? renderedRowIds = null,
        SettingPlatform platform = SettingPlatform.All)
    {
        if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));
        var items = descriptors.ToArray();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rows = new HashSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var aliasKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (SettingDescriptor descriptor in items)
        {
            if (String.IsNullOrWhiteSpace(descriptor.Id))
            {
                errors.Add("Descriptor has an empty ID.");
            }
            else if (!ids.Add(descriptor.Id))
            {
                errors.Add($"Duplicate descriptor ID '{descriptor.Id}'.");
            }

            if (String.IsNullOrWhiteSpace(descriptor.Label))
            {
                errors.Add($"Descriptor '{descriptor.Id}' has an empty label.");
            }
            if (descriptor.Platform == SettingPlatform.None
                || (descriptor.Platform & ~SettingPlatform.All) != 0)
            {
                errors.Add($"Descriptor '{descriptor.Id}' has invalid platform gating.");
            }
            if (descriptor.Availability != SettingAvailability.Available
                && String.IsNullOrWhiteSpace(descriptor.AvailabilityReason))
            {
                errors.Add($"Descriptor '{descriptor.Id}' must explain its unavailable state.");
            }

            bool hasFile = descriptor.PersistenceFile != null;
            bool hasKey = descriptor.PersistenceKey != null;
            if (hasFile != hasKey
                || hasFile && String.IsNullOrWhiteSpace(descriptor.PersistenceFile)
                || hasKey && String.IsNullOrWhiteSpace(descriptor.PersistenceKey))
            {
                errors.Add($"Descriptor '{descriptor.Id}' has an incomplete persistence identity.");
            }
            if (hasFile && hasKey && !owners.TryAdd(descriptor.StorageIdentity, descriptor.Id))
            {
                errors.Add($"Duplicate persistence owner '{descriptor.PersistenceFile}={descriptor.PersistenceKey}'.");
            }

            if (String.IsNullOrWhiteSpace(descriptor.RowId))
            {
                errors.Add($"Descriptor '{descriptor.Id}' has no rendered row ID.");
            }
            else if (!rows.Add(descriptor.RowId!))
            {
                errors.Add($"Duplicate rendered row ID '{descriptor.RowId}'.");
            }

            if (descriptor.Kind == SettingControlKind.Choice)
            {
                if (!descriptor.DynamicChoices && descriptor.Choices.Count == 0)
                {
                    errors.Add($"Choice descriptor '{descriptor.Id}' has no choices.");
                }
                var choiceIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (SettingChoice choice in descriptor.Choices)
                {
                    if (String.IsNullOrWhiteSpace(choice.Id)
                        || String.IsNullOrWhiteSpace(choice.Label))
                    {
                        errors.Add($"Choice descriptor '{descriptor.Id}' has an empty choice.");
                    }
                    else if (!choiceIds.Add(choice.Id))
                    {
                        errors.Add($"Choice descriptor '{descriptor.Id}' has duplicate choice '{choice.Id}'.");
                    }
                }
                if (descriptor.DefaultValue is string defaultChoice
                    && choiceIds.Count != 0 && !choiceIds.Contains(defaultChoice))
                {
                    errors.Add($"Choice descriptor '{descriptor.Id}' has an invalid default choice.");
                }
            }
            if (descriptor.Kind == SettingControlKind.Slider)
            {
                if (!descriptor.Minimum.HasValue || !descriptor.Maximum.HasValue
                    || !descriptor.Step.HasValue)
                {
                    errors.Add($"Slider descriptor '{descriptor.Id}' has no complete range.");
                }
                else if (!Finite(descriptor.Minimum.Value)
                    || !Finite(descriptor.Maximum.Value)
                    || !Finite(descriptor.Step.Value)
                    || descriptor.Minimum > descriptor.Maximum
                    || descriptor.Step <= 0)
                {
                    errors.Add($"Slider descriptor '{descriptor.Id}' has an invalid range.");
                }
            }

            if (descriptor.DependsOn != null
                && (descriptor.DependsOn == descriptor.Id
                    || !items.Any(candidate => candidate.Id == descriptor.DependsOn)))
            {
                errors.Add($"Descriptor '{descriptor.Id}' has an invalid dependency '{descriptor.DependsOn}'.");
            }

            if (descriptor.VisibleWhen != null || descriptor.EnabledWhen != null
                || descriptor.DisabledReason != null || descriptor.GetValue != null
                || descriptor.SetValue != null)
            {
                errors.Add($"Descriptor '{descriptor.Id}' contains a lifecycle delegate; use metadata-only persistence.");
            }
        }

        foreach (SettingDescriptor descriptor in items)
        {
            string? dependency = descriptor.DependsOn;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (dependency != null && visited.Add(dependency))
            {
                dependency = items.FirstOrDefault(candidate => candidate.Id == dependency)?.DependsOn;
            }
            if (dependency != null)
            {
                errors.Add($"Descriptor dependency cycle includes '{descriptor.Id}'.");
            }
        }

        if (exclusions != null)
        {
            var exclusionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettingExclusion exclusion in exclusions)
            {
                if (String.IsNullOrWhiteSpace(exclusion.Id)
                    || String.IsNullOrWhiteSpace(exclusion.Reason))
                {
                    errors.Add("Setting exclusion must have an ID and specific reason.");
                }
                else if (!exclusionIds.Add(exclusion.Id))
                {
                    errors.Add($"Duplicate setting exclusion '{exclusion.Id}'.");
                }
                if (ids.Contains(exclusion.Id))
                {
                    errors.Add($"Setting '{exclusion.Id}' is both a descriptor and an exclusion.");
                }
                if ((exclusion.PersistenceFile == null) != (exclusion.PersistenceKey == null))
                {
                    errors.Add($"Setting exclusion '{exclusion.Id}' has incomplete persistence identity.");
                }
            }
        }

        if (aliases != null)
        {
            foreach (SettingAlias alias in aliases)
            {
                if (String.IsNullOrWhiteSpace(alias.Key)
                    || String.IsNullOrWhiteSpace(alias.Reason)
                    || alias.CanonicalIds.Count == 0)
                {
                    errors.Add("Setting alias must have a key, target, and specific reason.");
                }
                else if (!aliasKeys.Add(alias.PersistenceFile + "\n" + alias.Key))
                {
                    errors.Add($"Duplicate setting alias '{alias.PersistenceFile}={alias.Key}'.");
                }
                foreach (string id in alias.CanonicalIds)
                {
                    if (!ids.Contains(id)) errors.Add($"Setting alias '{alias.Key}' targets unknown descriptor '{id}'.");
                }
            }
        }

        if (renderedRowIds != null)
        {
            var rendered = new HashSet<string>(renderedRowIds, StringComparer.Ordinal);
            foreach (SettingDescriptor descriptor in items)
            {
                if ((descriptor.Platform & platform) == 0) continue;
                if (descriptor.RowId == null || !rendered.Contains(descriptor.RowId))
                {
                    errors.Add($"Descriptor '{descriptor.Id}' row '{descriptor.RowId}' was not rendered.");
                }
            }
        }
        return errors;
    }

    public static void ValidateOrThrow(
        IEnumerable<SettingDescriptor> descriptors,
        IEnumerable<SettingExclusion>? exclusions = null,
        IEnumerable<SettingAlias>? aliases = null)
    {
        IReadOnlyList<string> errors = GetValidationErrors(descriptors, exclusions, aliases);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(String.Join(Environment.NewLine, errors));
        }
    }

    private static bool Finite(double value) => Double.IsFinite(value);

    private static SettingDescriptor Json(string id, string label, SettingCategory category,
        SettingControlKind kind, string key, string rowId, string? group = null,
        bool advanced = false, string? description = null, string? dependsOn = null,
        IReadOnlyList<SettingChoice>? choices = null, bool dynamicChoices = false,
        object? defaultValue = null, double? minimum = null, double? maximum = null,
        double? step = null, SettingPlatform platform = SettingPlatform.All,
        SettingAvailability availability = SettingAvailability.Available,
        string? availabilityReason = null)
        => new()
        {
            Id = id, Label = label, Description = description, Category = category,
            Group = group, Scope = SettingScope.ClientPreference, Kind = kind,
            Advanced = advanced, Platform = platform, Availability = availability,
            AvailabilityReason = availabilityReason, RowId = rowId,
            PersistenceFile = MenuSettingsFile, PersistenceKey = key,
            DependsOn = dependsOn, Choices = choices ?? Array.Empty<SettingChoice>(),
            DynamicChoices = dynamicChoices, DefaultValue = defaultValue,
            Minimum = minimum, Maximum = maximum, Step = step
        };

    private static SettingDescriptor Controls(string id, string label,
        SettingControlKind kind, string key, string rowId, string? group = null,
        bool advanced = false, string? dependsOn = null,
        IReadOnlyList<SettingChoice>? choices = null, bool dynamicChoices = false,
        object? defaultValue = null, double? minimum = null, double? maximum = null,
        double? step = null, SettingPlatform platform = SettingPlatform.All,
        SettingAvailability availability = SettingAvailability.Available,
        string? availabilityReason = null, params string[] aliases)
        => new()
        {
            Id = id, Label = label, Category = SettingCategory.Controls,
            Group = group, Scope = platform == SettingPlatform.Android
                ? SettingScope.PlatformPreference : SettingScope.ClientPreference,
            Kind = kind, Advanced = advanced, Platform = platform,
            Availability = availability, AvailabilityReason = availabilityReason,
            RowId = rowId, PersistenceFile = ControlsFile, PersistenceKey = key,
            PersistenceAliases = aliases, DependsOn = dependsOn,
            Choices = choices ?? Array.Empty<SettingChoice>(),
            DynamicChoices = dynamicChoices, DefaultValue = defaultValue,
            Minimum = minimum, Maximum = maximum, Step = step
        };

    private static SettingDescriptor Launcher(string id, string label,
        SettingCategory category, SettingControlKind kind, string key, string rowId,
        IReadOnlyList<SettingChoice>? choices = null, bool dynamicChoices = false,
        object? defaultValue = null, SettingScope scope = SettingScope.ClientPreference,
        SettingPlatform platform = SettingPlatform.All)
        => new()
        {
            Id = id, Label = label, Category = category, Scope = scope, Kind = kind,
            Platform = platform, RowId = rowId, PersistenceFile = LauncherFile,
            PersistenceKey = key, Choices = choices ?? Array.Empty<SettingChoice>(),
            DynamicChoices = dynamicChoices, DefaultValue = defaultValue
        };

    private static SettingDescriptor Info(string id, string label, SettingCategory category,
        SettingControlKind kind, string rowId, SettingAvailability availability = SettingAvailability.Available,
        string? reason = null, bool advanced = false)
        => new()
        {
            Id = id, Label = label, Category = category, Scope = SettingScope.Diagnostics,
            Kind = kind, Platform = SettingPlatform.All, RowId = rowId,
            Availability = availability, AvailabilityReason = reason, Advanced = advanced
        };

    private static IReadOnlyList<SettingChoice> ChoiceList(params string[] labels)
        => labels.Select(value => new SettingChoice(value, value)).ToArray();

    private static IReadOnlyList<SettingChoice> EnumChoices<T>() where T : struct, Enum
        => Enum.GetNames<T>().Select(value => new SettingChoice(value, value)).ToArray();

    private static IReadOnlyList<SettingDescriptor> BuildDescriptors()
    {
        var descriptors = new List<SettingDescriptor>
        {
            Launcher("graphics.window-mode", "Window mode", SettingCategory.Graphics,
                SettingControlKind.Choice, "window_mode", SettingRowIds.WindowMode,
                ChoiceList("Windowed", "Borderless fullscreen")),
            Json("graphics.render-scale", "Render scale", SettingCategory.Graphics,
                SettingControlKind.Slider, "ResolutionScale", SettingRowIds.RenderScale,
                minimum: 25, maximum: 100, step: 1),
            Json("graphics.field-of-view", "Field of view", SettingCategory.Graphics,
                SettingControlKind.Slider, "FieldOfView", SettingRowIds.FieldOfView,
                minimum: RenderOptions.MinFieldOfView,
                maximum: RenderOptions.MaxFieldOfView, step: 1,
                defaultValue: RenderOptions.DefaultFieldOfView.ToString(CultureInfo.InvariantCulture)),
            Json("graphics.fps-limit", "Frame limit", SettingCategory.Graphics,
                SettingControlKind.Slider, "FrameRateCap", SettingRowIds.FpsLimit,
                minimum: 0, maximum: 12, step: 1),
            Json("graphics.quality", "Graphics preset", SettingCategory.Graphics,
                SettingControlKind.Choice, "GraphicsPreset", SettingRowIds.GraphicsPreset,
                choices: ChoiceList("Original", "Enhanced", "Performance")),
            Json("graphics.texture-filtering", "Texture filtering", SettingCategory.Graphics,
                SettingControlKind.Choice, "TextureFilteringPreset", SettingRowIds.TextureFiltering,
                choices: ChoiceList("Original", "Smooth", "Enhanced")),
            Json("graphics.texture-detail", "Texture detail", SettingCategory.Graphics,
                SettingControlKind.Choice, "Anisotropy", SettingRowIds.Anisotropy,
                choices: ChoiceList("Off", "2x", "4x", "8x", "16x")),
            Json("graphics.edge-smoothing", "Edge smoothing", SettingCategory.Graphics,
                SettingControlKind.Choice, "Msaa", SettingRowIds.Msaa,
                choices: ChoiceList("Off", "2x", "4x")),
            Json("graphics.bloom", "Bloom", SettingCategory.Graphics,
                SettingControlKind.Toggle, "Bloom", SettingRowIds.Bloom),
            Json("graphics.dynamic-lighting", "Dynamic lighting", SettingCategory.Graphics,
                SettingControlKind.Toggle, "DynamicVisualLights", SettingRowIds.DynamicLighting),
            Json("graphics.lighting", "Lighting", SettingCategory.Graphics,
                SettingControlKind.Toggle, "Lighting", SettingRowIds.Lighting),
            Json("graphics.fog", "Fog", SettingCategory.Graphics,
                SettingControlKind.Toggle, "Fog", SettingRowIds.Fog),
            Json("graphics.fps-counter", "FPS counter", SettingCategory.Graphics,
                SettingControlKind.Toggle, "ShowFps", SettingRowIds.FpsCounter),
            Json("graphics.texture-pack", "Texture pack", SettingCategory.Graphics,
                SettingControlKind.Choice, "TexturePack", SettingRowIds.TexturePack,
                choices: ChoiceList("Original", "Enhanced (default)")),
            Json("graphics.visual-style", "Visual style", SettingCategory.Graphics,
                SettingControlKind.Choice, "VisualStyle", SettingRowIds.VisualStyle,
                choices: ChoiceList("Original", "Cel shaded", "Flat", "Pixelated", "Retro")),

            Json("accessibility.cosmetics.quality", "Cosmetic effects", SettingCategory.Accessibility,
                SettingControlKind.Choice, "CosmeticQuality", SettingRowIds.CosmeticQuality,
                choices: ChoiceList("Off", "Reduced", "Full")),
            Json("accessibility.cosmetics.show-others", "Show other player cosmetics",
                SettingCategory.Accessibility, SettingControlKind.Toggle,
                "ShowOtherPlayerCosmetics", SettingRowIds.ShowOtherPlayerCosmetics),
            Json("accessibility.cosmetics.reduce-flashes", "Reduce cosmetic flashes",
                SettingCategory.Accessibility, SettingControlKind.Toggle,
                "ReduceCosmeticFlashes", SettingRowIds.ReduceCosmeticFlashes),
            Json("accessibility.cosmetics.strong-team-colors", "Use strong team colors",
                SettingCategory.Accessibility, SettingControlKind.Toggle,
                "ForceStrongTeamColors", SettingRowIds.ForceStrongTeamColors),
            Json("accessibility.cosmetics.disable-distortion", "Disable cosmetic distortion",
                SettingCategory.Accessibility, SettingControlKind.Toggle,
                "DisableCosmeticDistortion", SettingRowIds.DisableCosmeticDistortion),
            Json("accessibility.cosmetics.disable-particles", "Disable cosmetic particles",
                SettingCategory.Accessibility, SettingControlKind.Toggle,
                "DisableCosmeticParticles", SettingRowIds.DisableCosmeticParticles),

            Json("hud.pro", "Pro HUD", SettingCategory.Hud, SettingControlKind.Toggle,
                "ProHud", SettingRowIds.ProHud),
            Json("hud.pro-weapon", "Weapon motion", SettingCategory.Hud, SettingControlKind.Choice,
                "ProHudFixedWeapon", SettingRowIds.ProHudWeapon,
                dependsOn: "hud.pro", choices: ChoiceList("Static", "Dynamic")),
            Json("hud.reticle-opacity", "Reticle opacity", SettingCategory.Hud,
                SettingControlKind.Slider, "ReticleOpacity", SettingRowIds.ReticleOpacity,
                minimum: .2, maximum: 1, step: .01),
            Json("hud.reticle-scale", "Reticle size", SettingCategory.Hud,
                SettingControlKind.Slider, "ReticleScale", SettingRowIds.ReticleScale,
                minimum: Features.MinimumReticleScale,
                maximum: Features.MaximumReticleScale, step: .05,
                defaultValue: "1"),
            Json("hud.crosshair-size", "Crosshair size", SettingCategory.Hud,
                SettingControlKind.Choice, "CrosshairSize", SettingRowIds.CrosshairSize,
                dependsOn: "hud.pro", choices: ChoiceList("Small", "Medium", "Big")),
            Json("hud.crosshair-style", "Crosshair type", SettingCategory.Hud,
                SettingControlKind.Choice, "CrosshairStyle", SettingRowIds.CrosshairStyle,
                dependsOn: "hud.pro", choices: ChoiceList("Cross", "Dot", "Cross + dot", "Circle", "Brackets")),
            Json("hud.hit-markers", "Hit markers", SettingCategory.Hud,
                SettingControlKind.Choice, "HitMarkers", SettingRowIds.HitMarkers,
                choices: ChoiceList("Off", "Visual", "Visual + audio")),
            Json("hud.hit-marker-timing", "Hit marker timing", SettingCategory.Hud,
                SettingControlKind.Choice, "HitMarkerTiming", SettingRowIds.HitMarkerTiming,
                choices: ChoiceList("Confirmed", "Instant")),
            Json("hud.headshot-cue", "Headshot cue", SettingCategory.Hud,
                SettingControlKind.Toggle, "HeadshotCue", SettingRowIds.HeadshotCue),
            Json("hud.kill-confirmation", "Kill confirmation", SettingCategory.Hud,
                SettingControlKind.Toggle, "KillConfirmation", SettingRowIds.KillConfirmation),
            Json("hud.killcam", "Killcam", SettingCategory.Hud,
                SettingControlKind.Toggle, "Killcam", SettingRowIds.Killcam,
                description: "Locally opt out of killcams allowed by the server."),
            Json("hud.radar.style", "Radar style", SettingCategory.Hud,
                SettingControlKind.Choice, "RadarStyle", SettingRowIds.RadarStyle,
                choices: ChoiceList("Classic", "Enhanced")),
            Json("hud.radar.orientation", "Radar orientation", SettingCategory.Hud,
                SettingControlKind.Choice, "RadarOrientation", SettingRowIds.RadarOrientation,
                choices: ChoiceList("Heading", "North")),
            Json("hud.radar.anchor", "Radar anchor", SettingCategory.Hud,
                SettingControlKind.Choice, "RadarPosition", SettingRowIds.RadarAnchor,
                choices: ChoiceList("Top right", "Top left", "Bottom right", "Bottom left", "Custom")),
            Json("hud.radar.scale", "Radar scale", SettingCategory.Hud,
                SettingControlKind.Slider, "RadarScale", SettingRowIds.RadarScale,
                minimum: RadarSettings.MinimumScale, maximum: RadarSettings.MaximumScale, step: .01),
            Json("hud.radar.offset-x", "Radar horizontal offset", SettingCategory.Hud,
                SettingControlKind.Slider, "RadarOffsetX", SettingRowIds.RadarOffsetX,
                minimum: -256, maximum: 256, step: 1),
            Json("hud.radar.offset-y", "Radar vertical offset", SettingCategory.Hud,
                SettingControlKind.Slider, "RadarOffsetY", SettingRowIds.RadarOffsetY,
                minimum: -192, maximum: 192, step: 1),
            Json("hud.radar.range", "Radar range", SettingCategory.Hud,
                SettingControlKind.Slider, "RadarRange", SettingRowIds.RadarRange,
                minimum: RadarSettings.MinimumRange, maximum: RadarSettings.MaximumRange, step: 5),
            Json("hud.radar.opacity", "Radar opacity", SettingCategory.Hud,
                SettingControlKind.Slider, "RadarOpacity", SettingRowIds.RadarOpacity,
                minimum: RadarSettings.MinimumOpacity, maximum: RadarSettings.MaximumOpacity, step: .01),
            Json("hud.radar.elevation", "Radar elevation markers", SettingCategory.Hud,
                SettingControlKind.Toggle, "RadarElevationIndicators", SettingRowIds.RadarElevation),

            Json("audio.feedback-volume", "Combat feedback", SettingCategory.Audio,
                SettingControlKind.Slider, "FeedbackVolume", SettingRowIds.FeedbackVolume,
                minimum: 0, maximum: 1, step: .01),
            Json("audio.sfx-volume", "Sound effects", SettingCategory.Audio,
                SettingControlKind.Slider, "SfxVolume", SettingRowIds.SfxVolume,
                minimum: 0, maximum: 1, step: .01),
            Json("audio.music-volume", "Music", SettingCategory.Audio,
                SettingControlKind.Slider, "MusicVolume", SettingRowIds.MusicVolume,
                minimum: 0, maximum: 1, step: .01),
            Launcher("audio.announcer-pack", "Announcer", SettingCategory.Audio,
                SettingControlKind.Choice, "announcer_pack", SettingRowIds.AnnouncerPack,
                dynamicChoices: true),
            Launcher("audio.music-pack", "Music pack", SettingCategory.Audio,
                SettingControlKind.Choice, "music_pack", SettingRowIds.MusicPack,
                dynamicChoices: true),
            Json("audio.language", "Text language", SettingCategory.Audio,
                SettingControlKind.Choice, "Language", SettingRowIds.Language,
                choices: EnumChoices<Language>()),

            Controls("controls.dynamic-crosshair.travel", "Free-aim travel",
                SettingControlKind.Slider, "dynamic_crosshair_travel_degrees",
                SettingRowIds.DynamicCrosshairTravel, group: "Dynamic crosshair",
                minimum: DynamicCrosshairTuning.DefaultTravelDegrees,
                maximum: DynamicCrosshairTuning.MaximumTravelDegrees, step: 1),
            Controls("controls.dynamic-crosshair.sensitivity", "Crosshair sensitivity",
                SettingControlKind.Slider, "dynamic_crosshair_sensitivity",
                SettingRowIds.DynamicCrosshairSensitivity, group: "Dynamic crosshair",
                minimum: DynamicCrosshairTuning.MinimumMovementSensitivity,
                maximum: DynamicCrosshairTuning.MaximumMovementSensitivity, step: .01),
            Controls("controls.dynamic-crosshair.turn-speed", "Camera turn speed",
                SettingControlKind.Slider, "dynamic_crosshair_turn_speed",
                SettingRowIds.DynamicCrosshairTurnSpeed, group: "Dynamic crosshair",
                minimum: DynamicCrosshairTuning.MinimumTurnSpeed,
                maximum: DynamicCrosshairTuning.MaximumTurnSpeed, step: .01),
            Controls("controls.mouse-sensitivity", "Mouse sensitivity", SettingControlKind.Slider,
                "sensitivity", SettingRowIds.MouseSensitivity, group: "General",
                minimum: .1, maximum: 3, step: .01),
            Controls("controls.mouse-invert-y", "Invert vertical aim", SettingControlKind.Toggle,
                "invert_y", SettingRowIds.MouseInvertY, group: "General"),
            Controls("controls.mouse-invert-x", "Invert horizontal aim", SettingControlKind.Toggle,
                "invert_x", SettingRowIds.MouseInvertX, group: "General"),
            Controls("controls.scroll-all-weapons", "Wheel cycles every weapon", SettingControlKind.Toggle,
                "scroll_all_weapons", SettingRowIds.ScrollAllWeapons, group: "General"),
            Controls("controls.morph-ball.mouse-flick-boost", "Morph Ball mouse flick boost", SettingControlKind.Toggle,
                "morph_ball_mouse_flick_boost", SettingRowIds.MorphBallMouseFlickBoost, group: "Morph Ball"),
            Controls("controls.morph-ball.stick-flick-boost", "Morph Ball stick flick boost", SettingControlKind.Toggle,
                "morph_ball_stick_flick_boost", SettingRowIds.MorphBallStickFlickBoost, group: "Morph Ball"),
            Controls("controls.morph-ball.swipe-boost", "Morph Ball swipe boost", SettingControlKind.Toggle,
                "morph_ball_swipe_boost", SettingRowIds.MorphBallSwipeBoost, group: "Morph Ball",
                platform: SettingPlatform.Android),
            Controls("controls.chat-key", "Chat key", SettingControlKind.KeyBinding,
                "chat_key", SettingRowIds.ChatKey, group: "Bindings"),
            Controls("controls.controller-preset", "Controller preset", SettingControlKind.Choice,
                "controller_preset", SettingRowIds.ControllerPreset, group: "General",
                choices: EnumChoices<ControllerPreset>(), aliases: new[] { "gamepad_preset" }),
            Controls("controls.controller-horizontal-sensitivity", "Horizontal sensitivity", SettingControlKind.Slider,
                "gamepad_horizontal_sensitivity", SettingRowIds.ControllerHorizontalSensitivity, group: "General",
                minimum: .01, maximum: 10, step: .01, aliases: new[] { "gamepad_look" }),
            Controls("controls.controller-vertical-sensitivity", "Vertical sensitivity", SettingControlKind.Slider,
                "gamepad_vertical_sensitivity", SettingRowIds.ControllerVerticalSensitivity, group: "General",
                minimum: .01, maximum: 10, step: .01, aliases: new[] { "gamepad_look" }),
            Controls("controls.controller-invert-y", "Invert vertical aim (stick)", SettingControlKind.Toggle,
                "gamepad_invert_y", SettingRowIds.ControllerInvertY, group: "General"),
            Controls("controls.controller-haptics", "Rumble and haptics", SettingControlKind.Toggle,
                "gamepad_haptics_enabled", SettingRowIds.ControllerHaptics, group: "General",
                aliases: new[] { "gamepad_haptics" }),
            Controls("controls.controller-haptics-strength", "Vibration strength", SettingControlKind.Slider,
                "gamepad_haptics_strength", SettingRowIds.ControllerHapticsStrength, group: "General",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.controller-move-dead-zone", "Move dead zone", SettingControlKind.Slider,
                "gamepad_move_deadzone", SettingRowIds.ControllerMoveDeadZone, group: "Stick response",
                minimum: 0, maximum: .9, step: .01, aliases: new[] { "gamepad_deadzone" }),
            Controls("controls.controller-look-dead-zone", "Look dead zone", SettingControlKind.Slider,
                "gamepad_look_deadzone", SettingRowIds.ControllerLookDeadZone, group: "Stick response",
                minimum: 0, maximum: .9, step: .01, aliases: new[] { "gamepad_deadzone" }),
            Controls("controls.controller-outer-dead-zone", "Outer dead zone", SettingControlKind.Slider,
                "gamepad_outer_deadzone", SettingRowIds.ControllerOuterDeadZone, group: "Stick response",
                minimum: 0, maximum: .5, step: .01),
            Controls("controls.controller-move-activate", "Move activation threshold", SettingControlKind.Slider,
                "gamepad_move_activate", SettingRowIds.ControllerMoveActivate, group: "Movement thresholds",
                minimum: 0, maximum: 1, step: .01, aliases: new[] { "gamepad_move_activate_threshold" }),
            Controls("controls.controller-move-release", "Move release threshold", SettingControlKind.Slider,
                "gamepad_move_release", SettingRowIds.ControllerMoveRelease, group: "Movement thresholds",
                minimum: 0, maximum: 1, step: .01, aliases: new[] { "gamepad_move_release_threshold" }),
            Controls("controls.controller-exponent", "Response exponent", SettingControlKind.Slider,
                "gamepad_look_exponent", SettingRowIds.ControllerExponent, group: "Look response",
                minimum: .05, maximum: 8, step: .05, aliases: new[] { "gamepad_response_exponent" }),
            Controls("controls.controller-response-curve", "Response curve", SettingControlKind.Choice,
                "gamepad_response_curve", SettingRowIds.ControllerResponseCurve, group: "Look response",
                choices: EnumChoices<GamepadResponseCurvePreset>()),
            Controls("controls.controller-turn-acceleration", "Turn acceleration", SettingControlKind.Choice,
                "gamepad_turn_acceleration", SettingRowIds.ControllerTurnAcceleration, group: "Outer ring",
                choices: EnumChoices<GamepadTurnAccelerationPreset>()),
            Controls("controls.controller-yaw-rate", "Yaw rate", SettingControlKind.Slider,
                "gamepad_yaw_rate", SettingRowIds.ControllerYawRate, group: "Look response",
                minimum: 0, maximum: 2000, step: 1, aliases: new[] { "gamepad_yaw" }),
            Controls("controls.controller-pitch-rate", "Pitch rate", SettingControlKind.Slider,
                "gamepad_pitch_rate", SettingRowIds.ControllerPitchRate, group: "Look response",
                minimum: 0, maximum: 2000, step: 1, aliases: new[] { "gamepad_pitch" }),
            Controls("controls.controller-outer-boost", "Outer-ring boost", SettingControlKind.Toggle,
                "gamepad_outer_boost_enabled", SettingRowIds.ControllerOuterBoost, group: "Outer ring"),
            Controls("controls.controller-outer-boost-start", "Outer boost threshold", SettingControlKind.Slider,
                "gamepad_outer_boost_start", SettingRowIds.ControllerOuterBoostStart, group: "Outer ring",
                minimum: 0, maximum: 1, step: .01, dependsOn: "controls.controller-outer-boost"),
            Controls("controls.controller-outer-yaw-boost", "Outer yaw boost", SettingControlKind.Slider,
                "gamepad_outer_yaw_boost", SettingRowIds.ControllerOuterYawBoost, group: "Outer ring",
                minimum: 0, maximum: 2000, step: 1, dependsOn: "controls.controller-outer-boost"),
            Controls("controls.controller-outer-pitch-boost", "Outer pitch boost", SettingControlKind.Slider,
                "gamepad_outer_pitch_boost", SettingRowIds.ControllerOuterPitchBoost, group: "Outer ring",
                minimum: 0, maximum: 2000, step: 1, dependsOn: "controls.controller-outer-boost"),
            Controls("controls.controller-boost-delay", "Outer boost delay", SettingControlKind.Slider,
                "gamepad_boost_delay", SettingRowIds.ControllerBoostDelay, group: "Outer ring",
                minimum: 0, maximum: 10, step: .001, dependsOn: "controls.controller-outer-boost"),
            Controls("controls.controller-boost-ramp", "Outer boost ramp", SettingControlKind.Slider,
                "gamepad_boost_ramp", SettingRowIds.ControllerBoostRamp, group: "Outer ring",
                minimum: .001, maximum: 10, step: .001, dependsOn: "controls.controller-outer-boost"),
            Controls("controls.controller-trigger-press", "Trigger press threshold", SettingControlKind.Slider,
                "gamepad_trigger_press", SettingRowIds.ControllerTriggerPress, group: "Triggers",
                minimum: 0, maximum: 1, step: .01, aliases: new[] { "gamepad_trigger_press_threshold" }),
            Controls("controls.controller-trigger-release", "Trigger release threshold", SettingControlKind.Slider,
                "gamepad_trigger_release", SettingRowIds.ControllerTriggerRelease, group: "Triggers",
                minimum: 0, maximum: 1, step: .01, aliases: new[] { "gamepad_trigger_release_threshold" }),
            Controls("controls.controller-zoom-horizontal", "Zoom horizontal sensitivity", SettingControlKind.Slider,
                "gamepad_zoom_horizontal_multiplier", SettingRowIds.ControllerZoom, group: "General",
                minimum: .01, maximum: 10, step: .01,
                aliases: new[] { "gamepad_zoom_multiplier", "gamepad_zoom" }),
            Controls("controls.controller-zoom-vertical", "Zoom vertical sensitivity", SettingControlKind.Slider,
                "gamepad_zoom_vertical_multiplier", SettingRowIds.ControllerZoomVertical, group: "General",
                minimum: .01, maximum: 10, step: .01),
            Controls("controls.controller-auto-calibration", "Automatic stick calibration", SettingControlKind.Toggle,
                "gamepad_auto_calibration", SettingRowIds.ControllerAutoCalibration,
                group: "Stick response"),
            Controls("controls.controller-stick-aim-mode", "Right-stick aim mode", SettingControlKind.Choice,
                "gamepad_stick_aim_mode", SettingRowIds.ControllerStickAimMode,
                group: "Look response", choices: ChoiceList("Traditional", "Flick Stick")),
            Controls("controls.controller-gyro", "Gyro aiming", SettingControlKind.Choice,
                "gamepad_gyro_mode", SettingRowIds.ControllerGyro, group: "Gyro",
                choices: ChoiceList("Off", "Always", "Zoom Only", "Hold Button"),
                platform: SettingPlatform.Desktop,
                aliases: new[] { "gamepad_gyro", "gamepad_gyro_enabled" }),
            Controls("controls.controller-gyro-activation", "Gyro hold button", SettingControlKind.Choice,
                "gamepad_gyro_activation", SettingRowIds.ControllerGyroActivation, group: "Gyro",
                choices: ChoiceList("Left Trigger", "Left Bumper", "Right Thumb"),
                platform: SettingPlatform.Desktop,
                dependsOn: "controls.controller-gyro"),
            Controls("controls.controller-gyro-sensitivity", "Gyro sensitivity", SettingControlKind.Slider,
                "gamepad_gyro_sensitivity", SettingRowIds.ControllerGyroSensitivity, group: "Gyro",
                minimum: .01, maximum: 10, step: .01, platform: SettingPlatform.Desktop),
            Controls("controls.controller-gyro-invert-x", "Invert gyro horizontal aim", SettingControlKind.Toggle,
                "gamepad_gyro_invert_x", SettingRowIds.ControllerGyroInvertX, group: "Gyro",
                platform: SettingPlatform.Desktop),
            Controls("controls.controller-gyro-invert-y", "Invert gyro vertical aim", SettingControlKind.Toggle,
                "gamepad_gyro_invert_y", SettingRowIds.ControllerGyroInvertY, group: "Gyro",
                platform: SettingPlatform.Desktop),
            Controls("controls.controller-telemetry", "Local input-balance diagnostics", SettingControlKind.Toggle,
                "input_balance_telemetry", SettingRowIds.ControllerTelemetry, group: "Feedback and diagnostics"),

            Launcher("gameplay.player-name", "Display name", SettingCategory.Player,
                SettingControlKind.Text, "player_name", SettingRowIds.PlayerName),
            Launcher("gameplay.hunter", "Preferred Hunter", SettingCategory.Player,
                SettingControlKind.Choice, "hunter", SettingRowIds.Hunter,
                choices: ChoiceList("Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Random")),
            Launcher("gameplay.show-online-presence", "Show me in Online Players",
                SettingCategory.Player, SettingControlKind.Toggle, "show_online_presence",
                SettingRowIds.ShowOnlinePresence, defaultValue: true),
            Launcher("system.updates", "Updates", SettingCategory.System,
                SettingControlKind.Choice, "update_policy", SettingRowIds.Updates,
                choices: ChoiceList("Automatic", "Notify only", "Off")),
            Info("system.game-files", "Game files", SettingCategory.System,
                SettingControlKind.Action, SettingRowIds.GameFiles),
            Launcher("system.debug-logging", "Debug logging", SettingCategory.System,
                SettingControlKind.Toggle, "debug_logs", SettingRowIds.DebugLogging,
                scope: SettingScope.Diagnostics),
            Info("system.share-logs", "Share logs", SettingCategory.System,
                SettingControlKind.Action, SettingRowIds.ShareLogs),
            Launcher("network.preferred-region", "Preferred region", SettingCategory.Network,
                SettingControlKind.Choice, "preferred_region", SettingRowIds.PreferredRegion,
                choices: ChoiceList("Automatic"), dynamicChoices: true, defaultValue: "Automatic"),
            Json("network.diagnostics", "Network diagnostics", SettingCategory.Network,
                SettingControlKind.Toggle, "AdvancedNetwork", SettingRowIds.NetworkDiagnostics,
                advanced: true),
            Launcher("accessibility.reduced-motion", "Reduce interface motion",
                SettingCategory.Accessibility, SettingControlKind.Toggle, "reduced_motion",
                SettingRowIds.ReducedMotion),

            Controls("controls.touch-buttons", "Show on-screen buttons", SettingControlKind.Toggle,
                "touch_buttons", SettingRowIds.TouchButtons, group: "General",
                platform: SettingPlatform.Android),
            Controls("controls.stylus-aiming", "Stylus aiming", SettingControlKind.Toggle,
                "stylus_aiming", SettingRowIds.StylusAiming, group: "General"),
            Controls("controls.stylus-sensitivity", "Stylus sensitivity", SettingControlKind.Slider,
                "stylus_sensitivity", SettingRowIds.StylusSensitivity, group: "Aim",
                minimum: .01, maximum: 10, step: .01),
            Controls("controls.stylus-invert-y", "Invert stylus vertical aim", SettingControlKind.Toggle,
                "stylus_invert_y", SettingRowIds.StylusInvertY, group: "Aim"),
            Controls("controls.stylus-primary", "Stylus primary button", SettingControlKind.Choice,
                "stylus_primary", SettingRowIds.StylusPrimary, group: "Bindings",
                choices: EnumChoices<StylusAction>()),
            Controls("controls.stylus-secondary", "Stylus secondary button", SettingControlKind.Choice,
                "stylus_secondary", SettingRowIds.StylusSecondary, group: "Bindings",
                choices: EnumChoices<StylusAction>()),
            Controls("controls.stylus-classic-gestures", "DS-style stylus gestures", SettingControlKind.Toggle,
                "stylus_classic_gestures", SettingRowIds.StylusClassicGestures, group: "General"),
            Controls("controls.stylus-double-tap-jump", "Double tap to jump", SettingControlKind.Toggle,
                "stylus_double_tap_jump", SettingRowIds.StylusDoubleTapJump, group: "General"),
            Controls("controls.stylus-flick-boost", "Flick to boost", SettingControlKind.Toggle,
                "stylus_flick_boost", SettingRowIds.StylusFlickBoost, group: "General"),
            Controls("controls.stylus-pressure-to-fire", "Pressure to fire", SettingControlKind.Toggle,
                "stylus_pressure_to_fire", SettingRowIds.StylusPressureToFire, group: "Advanced"),
            Controls("controls.stylus-pressure-threshold", "Pressure threshold", SettingControlKind.Slider,
                "stylus_pressure_threshold", SettingRowIds.StylusPressureThreshold, group: "Advanced",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-mode", "DS bottom screen", SettingControlKind.Choice,
                "bottom_screen_mode", SettingRowIds.BottomScreenMode, group: "General",
                choices: ChoiceList("Off", "Popup", "Always visible")),
            Controls("controls.stylus-bottom-screen-activation", "Touch screen activation", SettingControlKind.Choice,
                "bottom_screen_activation", SettingRowIds.BottomScreenActivation, group: "General",
                choices: ChoiceList("Toggle", "Hold")),
            Controls("controls.stylus-bottom-screen-cursor-sensitivity", "Touch cursor sensitivity", SettingControlKind.Slider,
                "bottom_screen_cursor_sensitivity", SettingRowIds.BottomScreenCursorSensitivity, group: "Bottom screen",
                minimum: .1, maximum: 4, step: .01),
            Controls("controls.stylus-bottom-screen-cursor-start-x", "Touch cursor start horizontal", SettingControlKind.Slider,
                "bottom_screen_cursor_start_x", SettingRowIds.BottomScreenCursorStartX, group: "Bottom screen",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-cursor-start-y", "Touch cursor start vertical", SettingControlKind.Slider,
                "bottom_screen_cursor_start_y", SettingRowIds.BottomScreenCursorStartY, group: "Bottom screen",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-power-beam-x", "Classic DS Power Beam X", SettingControlKind.Slider,
                "bottom_screen_power_beam_x", SettingRowIds.BottomScreenPowerBeamX, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-power-beam-y", "Classic DS Power Beam Y", SettingControlKind.Slider,
                "bottom_screen_power_beam_y", SettingRowIds.BottomScreenPowerBeamY, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-missile-x", "Classic DS Missile X", SettingControlKind.Slider,
                "bottom_screen_missile_x", SettingRowIds.BottomScreenMissileX, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-missile-y", "Classic DS Missile Y", SettingControlKind.Slider,
                "bottom_screen_missile_y", SettingRowIds.BottomScreenMissileY, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-next-weapon-x", "Classic DS Next weapon X", SettingControlKind.Slider,
                "bottom_screen_next_weapon_x", SettingRowIds.BottomScreenNextWeaponX, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-next-weapon-y", "Classic DS Next weapon Y", SettingControlKind.Slider,
                "bottom_screen_next_weapon_y", SettingRowIds.BottomScreenNextWeaponY, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-weapon-select-x", "Classic DS Weapon selector X", SettingControlKind.Slider,
                "bottom_screen_weapon_select_x", SettingRowIds.BottomScreenWeaponSelectX, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-weapon-select-y", "Classic DS Weapon selector Y", SettingControlKind.Slider,
                "bottom_screen_weapon_select_y", SettingRowIds.BottomScreenWeaponSelectY, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-alt-form-x", "Classic DS Alt form X", SettingControlKind.Slider,
                "bottom_screen_alt_form_x", SettingRowIds.BottomScreenAltFormX, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-alt-form-y", "Classic DS Alt form Y", SettingControlKind.Slider,
                "bottom_screen_alt_form_y", SettingRowIds.BottomScreenAltFormY, group: "Classic DS",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-directional-swipe-assist", "Directional swipe assist", SettingControlKind.Toggle,
                "bottom_screen_directional_swipe_assist", SettingRowIds.BottomScreenDirectionalSwipeAssist, group: "Bottom screen"),
            Controls("controls.stylus-bottom-screen-affinity-volt-driver-x", "Affinity Volt Driver X", SettingControlKind.Slider,
                "bottom_screen_affinity_volt_driver_x", SettingRowIds.BottomScreenAffinityVoltDriverX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-volt-driver-y", "Affinity Volt Driver Y", SettingControlKind.Slider,
                "bottom_screen_affinity_volt_driver_y", SettingRowIds.BottomScreenAffinityVoltDriverY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-battlehammer-x", "Affinity Battlehammer X", SettingControlKind.Slider,
                "bottom_screen_affinity_battlehammer_x", SettingRowIds.BottomScreenAffinityBattlehammerX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-battlehammer-y", "Affinity Battlehammer Y", SettingControlKind.Slider,
                "bottom_screen_affinity_battlehammer_y", SettingRowIds.BottomScreenAffinityBattlehammerY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-imperialist-x", "Affinity Imperialist X", SettingControlKind.Slider,
                "bottom_screen_affinity_imperialist_x", SettingRowIds.BottomScreenAffinityImperialistX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-imperialist-y", "Affinity Imperialist Y", SettingControlKind.Slider,
                "bottom_screen_affinity_imperialist_y", SettingRowIds.BottomScreenAffinityImperialistY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-judicator-x", "Affinity Judicator X", SettingControlKind.Slider,
                "bottom_screen_affinity_judicator_x", SettingRowIds.BottomScreenAffinityJudicatorX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-judicator-y", "Affinity Judicator Y", SettingControlKind.Slider,
                "bottom_screen_affinity_judicator_y", SettingRowIds.BottomScreenAffinityJudicatorY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-magmaul-x", "Affinity Magmaul X", SettingControlKind.Slider,
                "bottom_screen_affinity_magmaul_x", SettingRowIds.BottomScreenAffinityMagmaulX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-magmaul-y", "Affinity Magmaul Y", SettingControlKind.Slider,
                "bottom_screen_affinity_magmaul_y", SettingRowIds.BottomScreenAffinityMagmaulY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-shock-coil-x", "Affinity Shock Coil X", SettingControlKind.Slider,
                "bottom_screen_affinity_shock_coil_x", SettingRowIds.BottomScreenAffinityShockCoilX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-shock-coil-y", "Affinity Shock Coil Y", SettingControlKind.Slider,
                "bottom_screen_affinity_shock_coil_y", SettingRowIds.BottomScreenAffinityShockCoilY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-power-beam-x", "Affinity Power Beam X", SettingControlKind.Slider,
                "bottom_screen_affinity_power_beam_x", SettingRowIds.BottomScreenAffinityPowerBeamX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-power-beam-y", "Affinity Power Beam Y", SettingControlKind.Slider,
                "bottom_screen_affinity_power_beam_y", SettingRowIds.BottomScreenAffinityPowerBeamY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-missile-x", "Affinity Missile X", SettingControlKind.Slider,
                "bottom_screen_affinity_missile_x", SettingRowIds.BottomScreenAffinityMissileX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-missile-y", "Affinity Missile Y", SettingControlKind.Slider,
                "bottom_screen_affinity_missile_y", SettingRowIds.BottomScreenAffinityMissileY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-alt-form-x", "Affinity Alt form X", SettingControlKind.Slider,
                "bottom_screen_affinity_alt_form_x", SettingRowIds.BottomScreenAffinityAltFormX, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-affinity-alt-form-y", "Affinity Alt form Y", SettingControlKind.Slider,
                "bottom_screen_affinity_alt_form_y", SettingRowIds.BottomScreenAffinityAltFormY, group: "Affinity button placement",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-style", "Bottom screen style", SettingControlKind.Choice,
                "bottom_screen_style", SettingRowIds.BottomScreenStyle, group: "Bottom screen",
                choices: ChoiceList("Classic DS", "Affinity selector")),
            Controls("controls.stylus-bottom-screen-scale", "Bottom screen size", SettingControlKind.Slider,
                "bottom_screen_scale", SettingRowIds.BottomScreenScale, group: "Bottom screen",
                minimum: .4, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-center-x", "Popup position X", SettingControlKind.Slider,
                "bottom_screen_center_x", SettingRowIds.BottomScreenCenterX, group: "Bottom screen",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-center-y", "Popup position Y", SettingControlKind.Slider,
                "bottom_screen_center_y", SettingRowIds.BottomScreenCenterY, group: "Bottom screen",
                minimum: 0, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-opacity", "Bottom screen opacity", SettingControlKind.Slider,
                "bottom_screen_opacity", SettingRowIds.BottomScreenOpacity, group: "Bottom screen",
                minimum: .05, maximum: 1, step: .01),
            Controls("controls.stylus-bottom-screen-labels", "Bottom screen labels", SettingControlKind.Toggle,
                "bottom_screen_labels", SettingRowIds.BottomScreenLabels, group: "Bottom screen")
        };

        foreach (PropertyInfo property in InputSettings.Bindings)
        {
            string rowId = SettingRowIds.KeyBinding(property.Name);
            descriptors.Add(Controls("controls.key." + property.Name,
                InputSettings.ActionName(property), SettingControlKind.KeyBinding,
                property.Name, rowId, group: "Bindings"));
        }
        foreach (PadAction action in PadBindings.Actions)
        {
            string key = PadBindings.SettingKey(action);
            descriptors.Add(Controls(SettingRowIds.PadBinding(action.ToString()), PadBindings.Name(action),
                SettingControlKind.GamepadBinding, key, SettingRowIds.PadBinding(action.ToString()),
                group: "Bindings"));
        }
        foreach ((TouchControl control, string label) in TouchSettings.Order)
        {
            string key = TouchSettings.SettingKey(control);
            descriptors.Add(Controls(SettingRowIds.TouchBinding(control.ToString()), label,
                SettingControlKind.Toggle, key, SettingRowIds.TouchBinding(control.ToString()),
                group: "General", platform: SettingPlatform.Android));
        }
        return descriptors;
    }

    private static IReadOnlyList<SettingExclusion> BuildExclusions()
        => new SettingExclusion[]
        {
            new("legacy.match.point-goal", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "PointGoal"),
            new("legacy.match.time-limit", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "TimeLimit"),
            new("legacy.match.time-goal", "Legacy authoritative match rule retained for backward-compatible reads only.", MenuSettingsFile, "TimeGoal"),
            new("legacy.match.auto-reset", "Legacy authoritative match rule retained for backward-compatible reads only.", MenuSettingsFile, "AutoReset"),
            new("legacy.match.damage-level", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "DamageLevel"),
            new("legacy.match.team-play", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "TeamPlay"),
            new("legacy.match.friendly-fire", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "FriendlyFire"),
            new("legacy.match.hunter-radar", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "HunterRadar"),
            new("legacy.match.affinity-weapons", "Authoritative online match rule; owned by the lobby host, not a local SettingsView.", MenuSettingsFile, "AffinityWeapons"),
            new("legacy.menu.room-key", "Launch/session state; the Node and match handoff own it.", MenuSettingsFile, "RoomKey"),
            new("legacy.menu.mode", "Launch/session state; the Node and match handoff own it.", MenuSettingsFile, "Mode"),
            new("legacy.menu.players", "Launch roster state; not a player preference.", MenuSettingsFile, "Player1"),
            new("legacy.menu.player2", "Launch roster state; not a player preference.", MenuSettingsFile, "Player2"),
            new("legacy.menu.player3", "Launch roster state; not a player preference.", MenuSettingsFile, "Player3"),
            new("legacy.menu.player4", "Launch roster state; not a player preference.", MenuSettingsFile, "Player4"),
            new("legacy.menu.models", "Launch roster state; not a player preference.", MenuSettingsFile, "Models"),
            new("legacy.menu.mph-version", "Content compatibility identity; not a player preference.", MenuSettingsFile, "MphVersion"),
            new("legacy.menu.fh-version", "Content compatibility identity; not a player preference.", MenuSettingsFile, "FhVersion"),
            new("legacy.menu.cel-bands", "Renderer-fixed compatibility value; no player control exists.", MenuSettingsFile, "CelBands"),
            new("legacy.menu.cel-edge", "Renderer-fixed compatibility value; no player control exists.", MenuSettingsFile, "CelEdge"),
            new("legacy.menu.cel-shading", "Migration key retained for backward-compatible reads; VisualStyle is canonical.", MenuSettingsFile, "CelShading"),
            new("legacy.graphics.texture-filtering", "Migration key retained for backward-compatible reads; TextureFilteringPreset is canonical.", MenuSettingsFile, "TextureFiltering"),
            new("hud.radar.profile-document", "Aggregate radar customization document owned by the radar settings rows.", MenuSettingsFile, "RadarProfileJson"),
            new("hud.radar.mode-profile-document", "Optional per-mode radar profile document edited through radar profile import/export.", MenuSettingsFile, "RadarModeProfilesJson"),
            new("hud.radar.device-profile-document", "Optional per-device radar layout document edited through radar profile import/export.", MenuSettingsFile, "RadarDeviceProfilesJson"),
            new("launcher.backend-address", "Gateway/backend endpoint owned by the advanced shell service settings.", LauncherFile, "backend_address"),
            new("launcher.last-role", "Legacy launch state, not a player-facing preference.", LauncherFile, "last_role"),
            new("launcher.bots", "Offline launch state, not a normal SettingsView preference.", LauncherFile, "bots"),
            new("launcher.bot-level", "Offline launch state, not a normal SettingsView preference.", LauncherFile, "bot_level"),
            new("launcher.last-kind", "Legacy launch state, not a player-facing preference.", LauncherFile, "last_kind"),
            new("controls.schema", "Controls-file schema marker; not a player-facing preference.", ControlsFile, "input_schema"),
            new("controls.internal-aim-assist",
                "Internal persisted controller policy; intentionally absent from player-facing settings.",
                ControlsFile, "gamepad_aim_assist"),
            new("controls.internal-aim-assist-enabled",
                "Canonical internal aim-assist enable value; not player-facing.",
                ControlsFile, "gamepad_aim_assist_enabled"),
            new("controls.internal-aim-assist-strength",
                "Internal clamped aim-assist strength; not player-facing.",
                ControlsFile, "gamepad_aim_assist_strength")
        };

    private static IReadOnlyList<SettingAlias> BuildAliases()
        => new SettingAlias[]
        {
            new("gamepad_deadzone", new[] { "controls.controller-move-dead-zone", "controls.controller-look-dead-zone" }, "Legacy shared dead-zone key applies to both canonical stick zones."),
            new("gamepad_look", new[] { "controls.controller-horizontal-sensitivity", "controls.controller-vertical-sensitivity" }, "Legacy shared look multiplier applies to both canonical axes."),
            new("gamepad_invert_y", new[] { "controls.controller-invert-y" }, "Legacy controller inversion key."),
            new("gamepad_preset", new[] { "controls.controller-preset" }, "Legacy controller preset key."),
            new("auto_update", new[] { "system.updates" }, "Legacy launcher update boolean migrated to UpdatePolicy.", LauncherFile),
            new("gamepad_gyro", new[] { "controls.controller-gyro" }, "Legacy gyro enable key."),
            new("gamepad_haptics", new[] { "controls.controller-haptics" }, "Legacy haptics enable key.")
        };
}
