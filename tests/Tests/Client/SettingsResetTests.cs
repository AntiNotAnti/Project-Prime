using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class SettingsResetTests
{
    [Fact]
    public void FullResetRestoresControllerDiagnosticsDefaults()
    {
        try
        {
            InputSettings.GamepadHapticsEnabled = false;
            InputSettings.InputBalanceTelemetryEnabled = true;

            InputSettings.Reset();

            Assert.True(InputSettings.GamepadHapticsEnabled);
            Assert.False(InputSettings.InputBalanceTelemetryEnabled);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void BoostTimingSliderRetainsMillisecondDefaults()
    {
        Assert.Equal(180, SettingsView.BoostSecondsToSlider(.18f));
        Assert.Equal(.18f, SettingsView.BoostSliderToSeconds(180));
        Assert.Equal(120, SettingsView.BoostSecondsToSlider(.12f, .001f));
        Assert.Equal(.12f, SettingsView.BoostSliderToSeconds(120, .001f));
        Assert.Equal(1, SettingsView.BoostSecondsToSlider(.001f, .001f));
        Assert.Equal(.001f, SettingsView.BoostSliderToSeconds(1, .001f));
    }

    [Fact]
    public void AimResetPreservesBindingsAndUnrelatedInputPreferences()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.MouseSensitivity = 2;
            InputSettings.ChatKey = Keys.Y;
            TouchSettings.ButtonsVisible = false;
            TouchSettings.SetEnabled(TouchControl.Shoot, false);
            InputSettings.StylusSensitivity = 2;
            PadBindings.Set(PadAction.Jump, GamepadButtons.B);
            InputSettings.GamepadOuterYawBoost = 777;
            InputSettings.GamepadHorizontalSensitivity = 2;

            InputSettings.ResetControllerAimSettings();

            Assert.Equal(2, InputSettings.MouseSensitivity);
            Assert.Equal(Keys.Y, InputSettings.ChatKey);
            Assert.False(TouchSettings.ButtonsVisible);
            Assert.False(TouchSettings.IsEnabled(TouchControl.Shoot));
            Assert.Equal(2, InputSettings.StylusSensitivity);
            Assert.Equal(GamepadButtons.B, PadBindings.Get(PadAction.Jump));
            Assert.Equal(777, InputSettings.GamepadOuterYawBoost);
            Assert.Equal(1, InputSettings.GamepadHorizontalSensitivity);
            Assert.Equal(.25f, InputSettings.GamepadMoveActivateThreshold);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void AdvancedResetPreservesAimBindingsAndUnrelatedInputPreferences()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.MouseSensitivity = 2;
            TouchSettings.ButtonsVisible = false;
            InputSettings.StylusSensitivity = 2;
            PadBindings.Set(PadAction.Jump, GamepadButtons.B);
            InputSettings.GamepadHorizontalSensitivity = 2;
            InputSettings.GamepadOuterDeadZone = .4f;
            InputSettings.GamepadOuterBoostEnabled = false;
            InputSettings.GamepadTriggerPressThreshold = .8f;

            InputSettings.ResetControllerAdvancedTuning();

            Assert.Equal(2, InputSettings.MouseSensitivity);
            Assert.False(TouchSettings.ButtonsVisible);
            Assert.Equal(2, InputSettings.StylusSensitivity);
            Assert.Equal(GamepadButtons.B, PadBindings.Get(PadAction.Jump));
            Assert.Equal(2, InputSettings.GamepadHorizontalSensitivity);
            Assert.Equal(.02f, InputSettings.GamepadOuterDeadZone);
            Assert.True(InputSettings.GamepadOuterBoostEnabled);
            Assert.Equal(.20f, InputSettings.GamepadTriggerPressThreshold);
            Assert.Equal(.12f, InputSettings.GamepadTriggerReleaseThreshold);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void BindingResetLeavesFeelTouchAndStylusPreferencesUntouched()
    {
        InputSettings.Reset();
        try
        {
            var moveUp = InputSettings.Bindings.First(property =>
                property.Name == nameof(ClientPlayerBindings.MoveUp));
            InputSettings.Rebind(moveUp, ButtonType.Key, Keys.F, MouseButton.Left);
            InputSettings.MouseSensitivity = 2;
            InputSettings.ChatKey = Keys.Y;
            TouchSettings.ButtonsVisible = false;
            InputSettings.StylusSensitivity = 2;
            InputSettings.GamepadHorizontalSensitivity = 2;
            PadBindings.Set(PadAction.Jump, GamepadButtons.B);

            InputSettings.ResetBindings();

            Assert.Equal(Keys.W, InputSettings.Bind(moveUp).Key);
            Assert.Equal(Keys.T, InputSettings.ChatKey);
            Assert.Equal(GamepadButtons.A, PadBindings.Get(PadAction.Jump));
            Assert.Equal(2, InputSettings.MouseSensitivity);
            Assert.False(TouchSettings.ButtonsVisible);
            Assert.Equal(2, InputSettings.StylusSensitivity);
            Assert.Equal(2, InputSettings.GamepadHorizontalSensitivity);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void PairedThresholdEditsKeepReleaseAtOrBelowActivation()
    {
        InputSettings.Reset();
        try
        {
            InputSettings.GamepadMoveActivateThreshold = .35f;
            InputSettings.GamepadMoveReleaseThreshold = .9f;
            Assert.Equal(.35f, InputSettings.GamepadMoveReleaseThreshold);
            InputSettings.GamepadMoveActivateThreshold = .2f;
            Assert.Equal(.2f, InputSettings.GamepadMoveReleaseThreshold);

            InputSettings.GamepadTriggerPressThreshold = .4f;
            InputSettings.GamepadTriggerReleaseThreshold = .8f;
            Assert.Equal(.4f, InputSettings.GamepadTriggerReleaseThreshold);
            InputSettings.GamepadTriggerPressThreshold = .1f;
            Assert.Equal(.1f, InputSettings.GamepadTriggerReleaseThreshold);
        }
        finally
        {
            InputSettings.Reset();
        }
    }

    [Fact]
    public void LauncherLoadClearsRemovedDebugAndMotionPreferences()
    {
        string previousDirectory = LauncherPrefs.Directory;
        string directory = Path.Combine(Path.GetTempPath(),
            "fruity-prime-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "launcher.txt");
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            LauncherPrefs.Directory = directory;
            File.WriteAllLines(path, new[] { "debug_logs=true", "reduced_motion=true" });
            LauncherPrefs.Load();
            Assert.True(LauncherPrefs.DebugLogs);
            Assert.True(LauncherPrefs.ReducedMotion);

            File.WriteAllLines(path, new[] { "player_name=Player" });
            LauncherPrefs.Load();
            Assert.False(LauncherPrefs.DebugLogs);
            Assert.False(LauncherPrefs.ReducedMotion);
        }
        finally
        {
            LauncherPrefs.Directory = previousDirectory;
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PreferredRegionsUseObservedAndPersistedExactIds()
    {
        const string observedLow = "p0-region-a";
        const string observedHigh = "p0-region-z";
        const string persistedOnly = "p0-region-persisted";
        const string invalidControl = "p0-region-bad\nvalue";
        const string invalidWhitespace = "   ";
        string tooLong = new('x', 33);
        string previous = LauncherPrefs.PreferredRegion;
        try
        {
            LauncherPrefs.ObservePreferredRegions(new[]
            {
                observedHigh, observedLow, observedHigh, invalidControl, invalidWhitespace, tooLong
            });
            LauncherPrefs.PreferredRegion = persistedOnly;

            IReadOnlyList<string> choices = LauncherPrefs.PreferredRegionChoices;
            Assert.Equal("Automatic", choices[0]);
            Assert.True(Array.IndexOf(choices.ToArray(), observedLow)
                < Array.IndexOf(choices.ToArray(), observedHigh));
            Assert.Equal(1, choices.Count(choice =>
                choice.Equals(observedHigh, StringComparison.Ordinal)));
            Assert.Contains(persistedOnly, choices);
            Assert.DoesNotContain(invalidControl, choices);
            Assert.DoesNotContain(invalidWhitespace, choices);
            Assert.DoesNotContain(tooLong, choices);
            Assert.Contains("preferred_region=" + persistedOnly, LauncherPrefs.GetSaveLines());
            LauncherPrefs.PreferredRegion = " automatic ";
            Assert.Equal("Automatic", LauncherPrefs.PreferredRegion);
        }
        finally
        {
            LauncherPrefs.PreferredRegion = previous;
        }
    }

    [Fact]
    public void PreferredRegionObservationIsBoundedAndKeepsPersistedChoice()
    {
        const string persistedOnly = "p0-overflow-persisted";
        string[] observed = Enumerable.Range(0, LauncherPrefs.MaxObservedPreferredRegions + 32)
            .Select(index => $"p0-overflow-{index:D3}")
            .ToArray();
        string previous = LauncherPrefs.PreferredRegion;
        try
        {
            LauncherPrefs.ObservePreferredRegions(observed);
            LauncherPrefs.PreferredRegion = persistedOnly;

            IReadOnlyList<string> choices = LauncherPrefs.PreferredRegionChoices;
            Assert.Equal(LauncherPrefs.MaxObservedPreferredRegions,
                choices.Count(choice => choice.StartsWith("p0-overflow-", StringComparison.Ordinal)
                    && !choice.Equals(persistedOnly, StringComparison.Ordinal)));
            Assert.Contains(persistedOnly, choices);
            Assert.DoesNotContain("p0-overflow-128", choices);

            LauncherPrefs.ObservePreferredRegions(new[] { "p0-overflow-fresh" });
            choices = LauncherPrefs.PreferredRegionChoices;
            Assert.Contains("p0-overflow-fresh", choices);
            Assert.DoesNotContain("p0-overflow-000", choices);
            Assert.Contains(persistedOnly, choices);
        }
        finally
        {
            LauncherPrefs.PreferredRegion = previous;
        }
    }
}
