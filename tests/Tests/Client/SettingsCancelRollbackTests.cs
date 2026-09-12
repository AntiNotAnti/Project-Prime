using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Windowing.GraphicsLibraryFramework;
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
    public void CancelRestoresExactInputStateAfterEagerPresetBindingAndResetMutations()
    {
        InputSettings.Snapshot prior = InputSettings.CaptureSnapshot();
        SettingsView? view = null;
        try
        {
            InputSettings.Reset();
            var moveUp = InputSettings.Bindings.Single(property =>
                property.Name == nameof(ClientPlayerBindings.MoveUp));
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

    private static SliderRow FieldOfViewSlider(SettingsView view)
        => Assert.IsType<SliderRow>(typeof(SettingsView).GetField("_fieldOfView",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));
}
