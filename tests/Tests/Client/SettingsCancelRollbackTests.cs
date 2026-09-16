using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MphRead.Entities;
using MphRead.Hud.Radar;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Settings;
using Keys = MphRead.Mods.Input.PrimeKey;
using MouseButton = MphRead.Mods.Input.PrimeMouseButton;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsCancelRollbackTests
{
    [AvaloniaFact]
    public void FieldOfViewPreviewsImmediatelyAndCancelRestoresOriginalValue()
    {
        int prior = RenderOptions.FieldOfView;
        SettingsView? view = null;
        try
        {
            RenderOptions.FieldOfView = 80;
            view = new SettingsView(new MenuSettings { FieldOfView = "80" });
            SliderRow slider = FieldOfViewSlider(view);
            ((IControllerNavigable)slider).ControllerAdjust(1);

            Assert.Equal(81, RenderOptions.FieldOfView);

            slider.Value = 96;

            Assert.Equal(96, RenderOptions.FieldOfView);

            view.CancelForTests();

            Assert.Equal(80, RenderOptions.FieldOfView);
        }
        finally
        {
            view?.Dispose();
            RenderOptions.FieldOfView = prior;
        }
    }

    [AvaloniaFact]
    public void SuspendedSettingsDraftSurvivesHideThenRestoresOnOrdinaryDetach()
    {
        int prior = RenderOptions.FieldOfView;
        SettingsView? view = null;
        Window? window = null;
        try
        {
            RenderOptions.FieldOfView = 80;
            view = new SettingsView(new MenuSettings { FieldOfView = "80" });
            FieldOfViewSlider(view).Value = 96;
            window = new Window { Content = view };
            window.Show();

            // The coordinator marks this before hiding the native surface, so
            // detachment stops observers without completing the draft.
            view.SetVisualHostSuspended(true);
            window.Content = null;
            Assert.Equal(96, RenderOptions.FieldOfView);

            // Restore clears the suspension before reattaching. A later
            // ordinary detach must therefore complete the uncommitted draft.
            view.SetVisualHostSuspended(false);
            window.Content = view;
            window.Content = null;
            Assert.Equal(80, RenderOptions.FieldOfView);
        }
        finally
        {
            window?.Close();
            view?.Dispose();
            RenderOptions.FieldOfView = prior;
        }
    }

    [AvaloniaFact]
    public void SettingsCancelRestoresPreviousValue()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        SettingsView? view = null;
        try
        {
            InputSettings.Reset();
            InputSettings.MouseSensitivity = InputSettings.MaximumMouseSensitivity;
            view = new SettingsView(new MenuSettings());

            SliderRow slider = Slider(view, "_sensitivity");
            int uiMinimum = (int)Math.Round(InputSettings.MinimumMouseSensitivity
                / InputSettings.MouseSensitivityStep);
            int uiMaximum = (int)Math.Round(InputSettings.UiMaximumMouseSensitivity
                / InputSettings.MouseSensitivityStep);
            Assert.Equal(uiMaximum, slider.Value);

            slider.Value = int.MinValue;
            Assert.Equal(uiMinimum, slider.Value);
            slider.Value = int.MaxValue;
            Assert.Equal(uiMaximum, slider.Value);

            slider.Value--;
            view.CancelForTests();

            // The launcher exposes at most 3x, but cancelling must retain a
            // valid persisted value that was previously above that UI range.
            Assert.Equal(InputSettings.MaximumMouseSensitivity,
                InputSettings.MouseSensitivity);
        }
        finally
        {
            view?.Dispose();
            prior.Restore();
        }
    }

    [AvaloniaFact]
    public void CancelRestoresExactInputStateAfterEagerPresetBindingAndResetMutations()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        SettingsView? view = null;
        try
        {
            InputSettings.Reset();
            var moveUp = InputSettings.Bindings.Single(property =>
                property.Name == nameof(ClientPlayerBindings.MoveUp));
            InputSettings.DynamicCrosshairTravelDegrees = 17.25f;
            InputSettings.DynamicCrosshairSensitivity = 1.765432f;
            InputSettings.DynamicCrosshairTurnSpeed = .654321f;
            InputSettings.MouseSensitivity = 1.234567f;
            InputSettings.ChatKey = Keys.Y;
            InputSettings.GamepadResponseCurve = GamepadResponseCurvePreset.Precision;
            InputSettings.GamepadLookExponent = 2.345678f;
            InputSettings.GamepadTurnAcceleration = GamepadTurnAccelerationPreset.Fast;
            InputSettings.GamepadOuterYawBoost = 777.125f;
            InputSettings.GamepadGyroMode = GamepadGyroMode.Always;
            InputSettings.GamepadGyroSensitivity = 2.456789f;
            InputSettings.GamepadHapticsEnabled = false;
            InputSettings.InputBalanceTelemetryEnabled = true;
            InputSettings.StylusAimingEnabled = false;
            InputSettings.StylusSensitivity = 3.456789f;
            TouchSettings.ButtonsVisible = false;
            TouchSettings.SetEnabled(TouchControl.Shoot, false);
            InputSettings.Rebind(moveUp, ButtonType.Key, Keys.F, MouseButton.Left);
            PadBindings.Set(PadAction.Jump, GamepadButtons.B);
            ControllerPreset expectedPreset = InputSettings.ControllerPreset;

            view = new SettingsView(new MenuSettings());

            InputSettings.ChatKey = Keys.Q;
            InputSettings.ApplyPreset(ControllerPreset.Competitive);
            InputSettings.Rebind(moveUp, ButtonType.Key, Keys.G, MouseButton.Left);
            PadBindings.Set(PadAction.Jump, GamepadButtons.X);
            InputSettings.Reset();
            view.CancelForTests();

            Assert.Equal(17.25f, InputSettings.DynamicCrosshairTravelDegrees);
            Assert.Equal(1.765432f, InputSettings.DynamicCrosshairSensitivity);
            Assert.Equal(.654321f, InputSettings.DynamicCrosshairTurnSpeed);
            Assert.Equal(1.234567f, InputSettings.MouseSensitivity);
            Assert.Equal(Keys.Y, InputSettings.ChatKey);
            Assert.Equal(GamepadResponseCurvePreset.Precision,
                InputSettings.GamepadResponseCurve);
            Assert.Equal(2.345678f, InputSettings.GamepadLookExponent);
            Assert.Equal(GamepadTurnAccelerationPreset.Fast,
                InputSettings.GamepadTurnAcceleration);
            Assert.Equal(777.125f, InputSettings.GamepadOuterYawBoost);
            Assert.Equal(GamepadGyroMode.Always, InputSettings.GamepadGyroMode);
            Assert.Equal(2.456789f, InputSettings.GamepadGyroSensitivity);
            Assert.False(InputSettings.GamepadHapticsEnabled);
            Assert.True(InputSettings.InputBalanceTelemetryEnabled);
            Assert.False(InputSettings.StylusAimingEnabled);
            Assert.Equal(3.456789f, InputSettings.StylusSensitivity);
            Assert.False(TouchSettings.ButtonsVisible);
            Assert.False(TouchSettings.IsEnabled(TouchControl.Shoot));
            Assert.Equal(Keys.F, InputSettings.Bind(moveUp).Key);
            Assert.Equal(GamepadButtons.B, PadBindings.Get(PadAction.Jump));
            Assert.Equal(expectedPreset, InputSettings.ControllerPreset);
        }
        finally
        {
            view?.Dispose();
            prior.Restore();
        }
    }

    [AvaloniaFact]
    public void SaveAcceptsEagerChatAndPadBindingChangesInsteadOfRollingThemBack()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        int priorFieldOfView = RenderOptions.FieldOfView;
        string previousDirectory = LauncherPrefs.Directory;
        string temporaryDirectory = Directory.CreateTempSubdirectory(
            "project-prime-settings-cancel-").FullName;
        SettingsView? view = null;
        try
        {
            LauncherPrefs.Directory = temporaryDirectory;
            InputSettings.Reset();
            InputSettings.MouseSensitivity = 1.234567f;
            InputSettings.StylusSensitivity = 3.456789f;
            RenderOptions.FieldOfView = 80;
            var settings = new MenuSettings { FieldOfView = "80" };
            view = new SettingsView(settings, inGame: false, scene: null,
                captureTouchControls: true, captureGyroSupported: null,
                captureAdvancedControllerExpanded: null);
            InputSettings.ChatKey = Keys.Q;
            PadBindings.Set(PadAction.Jump, GamepadButtons.X);
            FieldOfViewSlider(view).Value = 96;

            view.CommitForTests();

            Assert.True(view.Saved);
            Assert.Equal(Keys.Q, InputSettings.ChatKey);
            Assert.Equal(GamepadButtons.X, PadBindings.Get(PadAction.Jump));
            Assert.Equal(1.234567f, InputSettings.MouseSensitivity);
            Assert.Equal(3.456789f, InputSettings.StylusSensitivity);
            Assert.Equal("96", settings.FieldOfView);
            Assert.Equal(96, RenderOptions.FieldOfView);
        }
        finally
        {
            view?.Dispose();
            prior.Restore();
            RenderOptions.FieldOfView = priorFieldOfView;
            LauncherPrefs.Directory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void DynamicCrosshairSlidersCommitSharedTuningValues()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        string previousDirectory = LauncherPrefs.Directory;
        string temporaryDirectory = Directory.CreateTempSubdirectory(
            "project-prime-crosshair-settings-").FullName;
        SettingsView? view = null;
        try
        {
            LauncherPrefs.Directory = temporaryDirectory;
            InputSettings.Reset();
            view = new SettingsView(new MenuSettings());

            Assert.Contains(SettingRowIds.DynamicCrosshairTravel, view.RenderedRowIds);
            Assert.Contains(SettingRowIds.DynamicCrosshairSensitivity,
                view.RenderedRowIds);
            Assert.Contains(SettingRowIds.DynamicCrosshairTurnSpeed,
                view.RenderedRowIds);
            Slider(view, "_dynamicCrosshairTravel").Value = 18;
            Slider(view, "_dynamicCrosshairSensitivity").Value = 175;
            Slider(view, "_dynamicCrosshairTurnSpeed").Value = 60;

            view.CommitForTests();

            Assert.Equal(18, InputSettings.DynamicCrosshairTravelDegrees);
            Assert.Equal(1.75f, InputSettings.DynamicCrosshairSensitivity);
            Assert.Equal(.6f, InputSettings.DynamicCrosshairTurnSpeed);
        }
        finally
        {
            view?.Dispose();
            prior.Restore();
            LauncherPrefs.Directory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void DisposingAnUncommittedSettingsPageRestoresEagerChanges()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        try
        {
            InputSettings.Reset();
            InputSettings.ChatKey = Keys.Y;
            PadBindings.Set(PadAction.Jump, GamepadButtons.B);
            var view = new SettingsView(new MenuSettings());

            InputSettings.ChatKey = Keys.Q;
            PadBindings.Set(PadAction.Jump, GamepadButtons.X);
            view.Dispose();

            Assert.Equal(Keys.Y, InputSettings.ChatKey);
            Assert.Equal(GamepadButtons.B, PadBindings.Get(PadAction.Jump));
        }
        finally
        {
            prior.Restore();
        }
    }

    [AvaloniaFact]
    public void DiscardRestoresImportedRadarProfiles()
    {
        RadarProfile priorDefault = RadarSettings.DefaultProfile;
        IReadOnlyDictionary<string, RadarProfile> priorModes = RadarSettings.ModeProfiles;
        IReadOnlyDictionary<RadarDeviceClass, RadarProfile> priorDevices
            = RadarSettings.DeviceProfiles;
        SettingsView? view = null;
        try
        {
            RadarSettings.Apply(new RadarProfile
            {
                Name = "Original", Range = 40, ShowResources = false,
                ShowWeapons = false, ShowAmmo = false, ShowHealth = false,
                ShowPowerups = false
            });
            RadarSettings.ClearModeProfiles();
            RadarSettings.SetModeProfile("Capture",
                new RadarProfile { Name = "Original capture", Range = 44 });
            RadarSettings.ClearDeviceProfiles();
            RadarSettings.SetDeviceProfile(RadarDeviceClass.Handheld,
                new RadarProfile { Name = "Original handheld", Range = 36 });
            view = new SettingsView(new MenuSettings());

            RadarSettings.Apply(new RadarProfile
            {
                Name = "Imported", Range = 70, ShowResources = true,
                ShowWeapons = true, ShowAmmo = true, ShowHealth = true,
                ShowPowerups = true
            });
            RadarSettings.ClearModeProfiles();
            RadarSettings.SetModeProfile("Battle",
                new RadarProfile { Name = "Imported battle", Range = 72 });
            RadarSettings.ClearDeviceProfiles();
            RadarSettings.SetDeviceProfile(RadarDeviceClass.Television,
                new RadarProfile { Name = "Imported television", Range = 68 });

            view.CancelForTests();

            Assert.Equal("Original", RadarSettings.DefaultProfile.Name);
            Assert.False(RadarSettings.DefaultProfile.ShowResources);
            Assert.False(RadarSettings.DefaultProfile.ShowWeapons);
            Assert.False(RadarSettings.DefaultProfile.ShowAmmo);
            Assert.False(RadarSettings.DefaultProfile.ShowHealth);
            Assert.False(RadarSettings.DefaultProfile.ShowPowerups);
            Assert.Equal("Original capture", RadarSettings.ForMode("Capture").Name);
            Assert.DoesNotContain("Battle", RadarSettings.ModeProfiles.Keys);
            Assert.Equal("Original handheld",
                RadarSettings.ForContext("Battle", RadarDeviceClass.Handheld).Name);
            Assert.DoesNotContain(RadarDeviceClass.Television,
                RadarSettings.DeviceProfiles.Keys);
        }
        finally
        {
            view?.Dispose();
            RadarSettings.Apply(priorDefault);
            RadarSettings.ClearModeProfiles();
            foreach ((string mode, RadarProfile profile) in priorModes)
                RadarSettings.SetModeProfile(mode, profile);
            RadarSettings.ClearDeviceProfiles();
            foreach ((RadarDeviceClass device, RadarProfile profile) in priorDevices)
                RadarSettings.SetDeviceProfile(device, profile);
        }
    }

    private static SliderRow FieldOfViewSlider(SettingsView view)
        => Slider(view, "_fieldOfView");

    private static SliderRow Slider(SettingsView view, string field)
        => Assert.IsType<SliderRow>(typeof(SettingsView).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
}
