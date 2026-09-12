using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Settings;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class SettingsRegistryTests
{
    [Fact]
    public void BuiltInInventoryIsValidAndLifecycleSafe()
    {
        SettingRegistry.Validate();

        Assert.All(SettingRegistry.Descriptors, descriptor =>
        {
            Assert.False(String.IsNullOrWhiteSpace(descriptor.RowId));
            Assert.Null(descriptor.GetValue);
            Assert.Null(descriptor.SetValue);
        });
    }

    [Fact]
    public void EveryMenuSettingsPropertyHasOneIndependentOwner()
    {
        var descriptorKeys = SettingRegistry.Descriptors
            .Where(descriptor => descriptor.PersistenceFile == SettingRegistry.MenuSettingsFile)
            .Select(descriptor => descriptor.PersistenceKey!)
            .ToHashSet(StringComparer.Ordinal);
        var excludedKeys = SettingRegistry.Exclusions
            .Where(exclusion => exclusion.PersistenceFile == SettingRegistry.MenuSettingsFile)
            .Select(exclusion => exclusion.PersistenceKey)
            .Where(key => key != null)
            .ToHashSet(StringComparer.Ordinal);

        string[] missing = typeof(MenuSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Where(name => !descriptorKeys.Contains(name) && !excludedKeys.Contains(name))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void KillcamPreferenceDefaultsOnPersistsAndCanOnlyReduceServerPolicy()
    {
        var defaults = new MenuSettings();
        SettingDescriptor descriptor = SettingRegistry.Get("hud.killcam");

        Assert.Equal("on", defaults.Killcam);
        Assert.Equal("Killcam", descriptor.PersistenceKey);
        Assert.Equal(SettingControlKind.Toggle, descriptor.Kind);

        string json = JsonSerializer.Serialize(new MenuSettings { Killcam = "off" });
        MenuSettings restored = JsonSerializer.Deserialize<MenuSettings>(json)!;
        Assert.Equal("off", restored.Killcam);
        Assert.Equal("on", JsonSerializer.Deserialize<MenuSettings>("{}")!.Killcam);

        MenuSettings restore = GameSettings.Current ?? defaults;
        try
        {
            GameSettings.Apply(new MenuSettings { Killcam = "on" });
            Assert.Equal(KillcamPolicy.Immediate,
                GameSettings.ResolveKillcamPolicy(KillcamPolicy.Immediate));
            Assert.Equal(KillcamPolicy.PostRound,
                GameSettings.ResolveKillcamPolicy(KillcamPolicy.PostRound));
            Assert.Equal(KillcamPolicy.Disabled,
                GameSettings.ResolveKillcamPolicy(KillcamPolicy.Disabled));

            GameSettings.Apply(new MenuSettings { Killcam = "off" });
            Assert.Equal(KillcamPolicy.Disabled,
                GameSettings.ResolveKillcamPolicy(KillcamPolicy.Immediate));
            Assert.Equal(KillcamPolicy.Disabled,
                GameSettings.ResolveKillcamPolicy(KillcamPolicy.PostRound));
        }
        finally
        {
            GameSettings.Apply(restore);
        }
    }

    [Fact]
    public void RadarAppearancePreferencesPersistAndClampWithoutChangingRevealPolicy()
    {
        MenuSettings restore = GameSettings.Current ?? new MenuSettings();
        try
        {
            var defaults = new MenuSettings();
            Assert.Equal("40", defaults.RadarRange);
            Assert.Equal("1.0", defaults.RadarOpacity);
            Assert.Equal("on", defaults.RadarElevationIndicators);

            SettingDescriptor range = SettingRegistry.Get("hud.radar.range");
            SettingDescriptor opacity = SettingRegistry.Get("hud.radar.opacity");
            SettingDescriptor elevation = SettingRegistry.Get("hud.radar.elevation");
            Assert.Equal(nameof(MenuSettings.RadarRange), range.PersistenceKey);
            Assert.Equal(nameof(MenuSettings.RadarOpacity), opacity.PersistenceKey);
            Assert.Equal(nameof(MenuSettings.RadarElevationIndicators), elevation.PersistenceKey);

            GameSettings.Apply(new MenuSettings
            {
                RadarRange = "999",
                RadarOpacity = "0.1",
                RadarElevationIndicators = "off"
            });
            Assert.Equal(Hud.Radar.RadarSettings.MaximumRange,
                Hud.Radar.RadarSettings.Range);
            Assert.Equal(Hud.Radar.RadarSettings.MinimumOpacity,
                Hud.Radar.RadarSettings.Opacity);
            Assert.False(Hud.Radar.RadarSettings.ElevationIndicators);

            GameSettings.Apply(new MenuSettings
            {
                RadarRange = "invalid",
                RadarOpacity = "invalid",
                RadarElevationIndicators = "invalid"
            });
            Assert.Equal(Hud.Radar.RadarSettings.DefaultRange,
                Hud.Radar.RadarSettings.Range);
            Assert.Equal(1, Hud.Radar.RadarSettings.Opacity);
            Assert.True(Hud.Radar.RadarSettings.ElevationIndicators);
        }
        finally
        {
            GameSettings.Apply(restore);
        }
    }

    [Fact]
    public void FieldOfViewPreferencePersistsClampsAndPreservesZoomRatio()
    {
        MenuSettings restore = GameSettings.Current ?? new MenuSettings();
        try
        {
            var defaults = new MenuSettings();
            SettingDescriptor descriptor = SettingRegistry.Get("graphics.field-of-view");
            Assert.Equal("78", defaults.FieldOfView);
            Assert.Equal("FieldOfView", descriptor.PersistenceKey);
            Assert.Equal(RenderOptions.MinFieldOfView, descriptor.Minimum);
            Assert.Equal(RenderOptions.MaxFieldOfView, descriptor.Maximum);

            string json = JsonSerializer.Serialize(new MenuSettings { FieldOfView = "95" });
            MenuSettings restored = JsonSerializer.Deserialize<MenuSettings>(json)!;
            GameSettings.Apply(restored);
            Assert.Equal(95, RenderOptions.FieldOfView);
            Assert.Equal(95f, RenderOptions.ResolvePlayerFieldOfView(78, 78));
            Assert.Equal(47.5f, RenderOptions.ResolvePlayerFieldOfView(39, 78));

            GameSettings.Apply(new MenuSettings { FieldOfView = "999" });
            Assert.Equal(RenderOptions.MaxFieldOfView, RenderOptions.FieldOfView);
            GameSettings.Apply(new MenuSettings { FieldOfView = "invalid" });
            Assert.Equal(RenderOptions.DefaultFieldOfView, RenderOptions.FieldOfView);
        }
        finally
        {
            GameSettings.Apply(restore);
        }
    }

    [Fact]
    public void FeatureCommitKeysAreCanonicalRegistryOwners()
    {
        var committedKeys = FeaturesSettings.Commit().Keys.ToHashSet(StringComparer.Ordinal);
        var descriptorKeys = SettingRegistry.Descriptors
            .Where(descriptor => descriptor.PersistenceFile == SettingRegistry.MenuSettingsFile)
            .Select(descriptor => descriptor.PersistenceKey!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(committedKeys, key => Assert.Contains(key, descriptorKeys));
        Assert.Equal(new[] { "CrosshairSize", "CrosshairStyle", "ProHud",
            "ProHudFixedWeapon", "ReticleOpacity", "ReticleScale" },
            committedKeys.OrderBy(key => key));

        SettingDescriptor proHud = SettingRegistry.Get("hud.pro");
        Assert.Equal("Pro HUD", proHud.Label);
        SettingDescriptor reticleScale = SettingRegistry.Get("hud.reticle-scale");
        Assert.Equal(nameof(Features.ReticleScale), reticleScale.PersistenceKey);
        Assert.Equal(Features.MinimumReticleScale, reticleScale.Minimum);
        Assert.Equal(Features.MaximumReticleScale, reticleScale.Maximum);
    }

    [Fact]
    public void EveryControlsSaveKeyHasCanonicalOwnerAliasOrExclusion()
    {
        var acceptedKeys = SettingRegistry.Descriptors
            .Where(descriptor => descriptor.PersistenceFile == SettingRegistry.ControlsFile)
            .SelectMany(descriptor => new[] { descriptor.PersistenceKey! }
                .Concat(descriptor.PersistenceAliases))
            .Concat(SettingRegistry.Exclusions
                .Where(exclusion => exclusion.PersistenceFile == SettingRegistry.ControlsFile)
                .Select(exclusion => exclusion.PersistenceKey)
                .Where(key => key != null)
                .Select(key => key!))
            .ToHashSet(StringComparer.Ordinal);
        string[] savedKeys = InputSettings.GetSaveLines()
            .Select(line =>
            {
                int split = line.IndexOf('=');
                return split > 0 ? line[..split].Trim() : "";
            })
            .Where(key => key.Length != 0 && key[0] != '#')
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string[] missing = savedKeys.Where(key => !acceptedKeys.Contains(key)).ToArray();

        Assert.Empty(missing);
        Assert.Contains("gamepad_deadzone", savedKeys);
        Assert.Contains("gamepad_look", savedKeys);
        Assert.Contains("input_schema", savedKeys);
    }

    [Fact]
    public void AimAssistPersistsInternallyButHasNoPlayerConfigurableDescriptor()
    {
        InputSettings.Reset();
        try
        {
            Assert.DoesNotContain(SettingRegistry.Descriptors, descriptor =>
                descriptor.Id.Contains("aim-assist", StringComparison.OrdinalIgnoreCase)
                || descriptor.Label.Contains("aim assist", StringComparison.OrdinalIgnoreCase));

            InputSettings.LoadLines(new[]
            {
                "gamepad_aim_assist_enabled=false",
                "gamepad_aim_assist_strength=0.35"
            });

            Assert.False(InputSettings.GamepadAimAssistEnabled);
            Assert.Equal(.35f, InputSettings.GamepadAimAssistStrength);

            InputSettings.ApplyPreset(ControllerPreset.Competitive);
            InputSettings.GamepadResponseCurve = GamepadResponseCurvePreset.Precision;
            InputSettings.GamepadTurnAcceleration = GamepadTurnAccelerationPreset.Fast;
            Assert.False(InputSettings.GamepadAimAssistEnabled);
            Assert.Equal(.35f, InputSettings.GamepadAimAssistStrength);

            Assert.Contains("gamepad_aim_assist_enabled=false",
                InputSettings.GetSaveLines());
            Assert.Contains("gamepad_aim_assist_strength=0.35",
                InputSettings.GetSaveLines());
        }
        finally
        {
            InputSettings.Reset();
        }

        Assert.True(InputSettings.GamepadAimAssistEnabled);
        Assert.Equal(1f, InputSettings.GamepadAimAssistStrength);
    }

    [Fact]
    public void EveryLauncherSaveKeyHasCanonicalOwnerAliasOrExclusion()
    {
        var acceptedKeys = SettingRegistry.Descriptors
            .Where(descriptor => descriptor.PersistenceFile == SettingRegistry.LauncherFile)
            .SelectMany(descriptor => new[] { descriptor.PersistenceKey! }
                .Concat(descriptor.PersistenceAliases))
            .Concat(SettingRegistry.Exclusions
                .Where(exclusion => exclusion.PersistenceFile == SettingRegistry.LauncherFile)
                .Select(exclusion => exclusion.PersistenceKey)
                .Where(key => key != null)
                .Select(key => key!))
            .ToHashSet(StringComparer.Ordinal);
        string[] savedKeys = LauncherPrefs.GetSaveLines()
            .Select(line =>
            {
                int split = line.IndexOf('=');
                return split > 0 ? line[..split].Trim() : "";
            })
            .Where(key => key.Length != 0 && key[0] != '#')
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string[] missing = savedKeys.Where(key => !acceptedKeys.Contains(key)).ToArray();

        Assert.Empty(missing);
        Assert.Contains("preferred_region", savedKeys);
    }

    [Fact]
    public void ControllerPreferencesNormalizeNonFiniteValuesAndKeepPairedThresholds()
    {
        Assert.Equal(.001, SettingRegistry.Get("controls.controller-boost-delay").Step);
        Assert.Equal(.001, SettingRegistry.Get("controls.controller-boost-ramp").Step);

        InputSettings.Reset();
        try
        {
            InputSettings.GamepadLookExponent = float.NaN;
            InputSettings.GamepadYawRate = float.PositiveInfinity;
            InputSettings.GamepadBoostDelaySeconds = float.NegativeInfinity;
            Assert.Equal(1.60f, InputSettings.GamepadLookExponent);
            Assert.Equal(300f, InputSettings.GamepadYawRate);
            Assert.Equal(.18f, InputSettings.GamepadBoostDelaySeconds);

            InputSettings.GamepadMoveActivateThreshold = .35f;
            InputSettings.GamepadMoveReleaseThreshold = .9f;
            Assert.Equal(.35f, InputSettings.GamepadMoveReleaseThreshold);
            InputSettings.GamepadMoveActivateThreshold = .2f;
            Assert.Equal(.2f, InputSettings.GamepadMoveReleaseThreshold);

            InputSettings.GamepadTriggerPressThreshold = .4f;
            InputSettings.GamepadTriggerReleaseThreshold = .8f;
            Assert.Equal(.4f, InputSettings.GamepadTriggerReleaseThreshold);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void PreferredRegionIsFiniteAndRoundTripsThroughSaveLines()
    {
        SettingDescriptor descriptor = SettingRegistry.Get("network.preferred-region");
        Assert.True(descriptor.DynamicChoices);
        Assert.Equal(new[] { "Automatic" },
            descriptor.Choices.Select(choice => choice.Id).ToArray());
        Assert.Equal("Automatic", descriptor.DefaultValue);

        string previous = LauncherPrefs.PreferredRegion;
        string previousDirectory = LauncherPrefs.Directory;
        string? temporaryDirectory = null;
        try
        {
            LauncherPrefs.PreferredRegion = "europe";
            Assert.Equal("europe", LauncherPrefs.PreferredRegion);
            Assert.Contains("preferred_region=europe", LauncherPrefs.GetSaveLines());

            temporaryDirectory = System.IO.Directory.CreateTempSubdirectory("project-prime-settings-").FullName;
            LauncherPrefs.Directory = temporaryDirectory;
            LauncherPrefs.Save();
            LauncherPrefs.PreferredRegion = "Automatic";
            LauncherPrefs.Load();
            Assert.Equal("europe", LauncherPrefs.PreferredRegion);

            LauncherPrefs.PreferredRegion = "not-a-region";
            Assert.Equal("not-a-region", LauncherPrefs.PreferredRegion);
            Assert.Contains("preferred_region=not-a-region", LauncherPrefs.GetSaveLines());
            LauncherPrefs.PreferredRegion = "";
            Assert.Equal("Automatic", LauncherPrefs.PreferredRegion);
        }
        finally
        {
            LauncherPrefs.Directory = previousDirectory;
            LauncherPrefs.PreferredRegion = previous;
            if (temporaryDirectory != null)
            {
                System.IO.Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void MatchRulesAreExplicitlyExcludedFromNormalSettings()
    {
        Assert.Equal("on", new MenuSettings().HunterRadar);
        string[] ids = SettingRegistry.Exclusions.Select(exclusion => exclusion.Id).ToArray();
        Assert.Contains("legacy.match.point-goal", ids);
        Assert.Contains("legacy.match.time-limit", ids);
        Assert.Contains("legacy.match.damage-level", ids);
        Assert.Contains("legacy.match.team-play", ids);
        Assert.Contains("legacy.match.friendly-fire", ids);
        Assert.Contains("legacy.match.hunter-radar", ids);
        Assert.Contains("legacy.match.affinity-weapons", ids);
        Assert.DoesNotContain(SettingRegistry.Descriptors,
            descriptor => descriptor.PersistenceKey is "PointGoal" or "TimeLimit"
                or "DamageLevel" or "TeamPlay" or "FriendlyFire"
                or "HunterRadar" or "AffinityWeapons");
    }

    [Fact]
    public void DuplicateDescriptorIdsAreRejected()
    {
        SettingDescriptor first = Descriptor("duplicate", "first", "one");
        SettingDescriptor second = Descriptor("duplicate", "second", "two");

        var errors = SettingRegistry.GetValidationErrors(new[] { first, second });

        Assert.Contains(errors, error => error.Contains("Duplicate descriptor ID", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicatePersistenceOwnersAreRejected()
    {
        SettingDescriptor first = Descriptor("first", "same", "key");
        SettingDescriptor second = Descriptor("second", "same", "key");

        var errors = SettingRegistry.GetValidationErrors(new[] { first, second });

        Assert.Contains(errors, error => error.Contains("Duplicate persistence owner", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingRenderedRowIdIsRejected()
    {
        SettingDescriptor descriptor = Descriptor("missing-row", "key", "value") with { RowId = null };

        var errors = SettingRegistry.GetValidationErrors(new[] { descriptor });

        Assert.Contains(errors, error => error.Contains("no rendered row ID", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidDependencyChoicePlatformAndRangeAreRejected()
    {
        SettingDescriptor invalidChoice = Descriptor("choice", "choice", "value") with
        {
            Kind = SettingControlKind.Choice,
            Choices = Array.Empty<SettingChoice>()
        };
        SettingDescriptor invalidDependency = Descriptor("dependent", "dependent", "other") with
        {
            DependsOn = "does-not-exist"
        };
        SettingDescriptor invalidPlatform = Descriptor("platform", "platform", "platform") with
        {
            Platform = SettingPlatform.None
        };
        SettingDescriptor invalidRange = Descriptor("range", "range", "range") with
        {
            Kind = SettingControlKind.Slider,
            Minimum = 5,
            Maximum = 1,
            Step = 0
        };

        var errors = SettingRegistry.GetValidationErrors(new[]
            { invalidChoice, invalidDependency, invalidPlatform, invalidRange });

        Assert.Contains(errors, error => error.Contains("has no choices", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid dependency", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid platform", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid range", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderedRowValidationDetectsMissingRowsAndHonorsPlatformGating()
    {
        SettingDescriptor desktop = Descriptor("desktop", "desktop", "desktop") with
        {
            Platform = SettingPlatform.Desktop
        };
        SettingDescriptor android = Descriptor("android", "android", "android") with
        {
            Platform = SettingPlatform.Android
        };
        SettingDescriptor[] descriptors = { desktop, android };
        string[] rows = { "desktop" };

        var errors = SettingRegistry.GetValidationErrors(descriptors,
            renderedRowIds: rows, platform: SettingPlatform.Desktop);
        Assert.Empty(errors);

        errors = SettingRegistry.GetValidationErrors(descriptors,
            renderedRowIds: Array.Empty<string>(), platform: SettingPlatform.Desktop);
        Assert.Contains(errors, error => error.Contains("row 'desktop' was not rendered", StringComparison.Ordinal));
    }

    [Fact]
    public void LifecycleDelegatesAreRejectedFromMetadataRegistry()
    {
        SettingDescriptor descriptor = Descriptor("delegate", "delegate", "delegate") with
        {
            GetValue = () => new object()
        };

        var errors = SettingRegistry.GetValidationErrors(new[] { descriptor });

        Assert.Contains(errors, error => error.Contains("lifecycle delegate", StringComparison.Ordinal));
    }

    private static SettingDescriptor Descriptor(string id, string file, string key)
        => new()
        {
            Id = id,
            Label = id,
            Category = SettingCategory.System,
            Scope = SettingScope.ClientPreference,
            Kind = SettingControlKind.Toggle,
            RowId = id,
            PersistenceFile = file,
            PersistenceKey = key
        };
}
