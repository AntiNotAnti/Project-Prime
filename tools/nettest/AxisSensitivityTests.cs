using System;
using System.Globalization;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.Launcher;

namespace MphRead.NetTest;

internal static class AxisSensitivityTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("AXIS SENSITIVITY " + name);
        }
        var previousDirectory = LauncherPrefs.Directory;
        float previousMouse = InputSettings.MouseSensitivity;
        float previousX = InputSettings.HorizontalSensitivity;
        float previousY = InputSettings.VerticalSensitivity;
        float previousZoom = InputSettings.ImperialistZoomSensitivity;
        string directory = Path.Combine(Path.GetTempPath(),
            "prime-axis-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            LauncherPrefs.Directory = directory;
            InputSettings.MouseSensitivity = 1.4f;
            InputSettings.ImperialistZoomSensitivity = .6f;
            InputSettings.HorizontalSensitivity = 1.25f;
            InputSettings.VerticalSensitivity = .75f;
            Check(MathF.Abs(InputSettings.MouseAimSensitivityX(false) - 1.75f) < .0001f,
                "X applies base multiplier");
            Check(MathF.Abs(InputSettings.MouseAimSensitivityY(false) - 1.05f) < .0001f,
                "Y applies base multiplier");
            Check(MathF.Abs(InputSettings.MouseAimSensitivityX(true) - 1.05f) < .0001f,
                "scoped X composes both modifiers");
            Check(MathF.Abs(InputSettings.MouseAimSensitivityY(true) - .63f) < .0001f,
                "scoped Y composes both modifiers");
            InputSettings.HorizontalSensitivity = float.NaN;
            InputSettings.VerticalSensitivity = float.PositiveInfinity;
            Check(InputSettings.HorizontalSensitivity == 1 &&
                InputSettings.VerticalSensitivity == 1, "reject nonfinite inputs");
            InputSettings.HorizontalSensitivity = -3;
            InputSettings.VerticalSensitivity = 50;
            Check(InputSettings.HorizontalSensitivity == .01f &&
                InputSettings.VerticalSensitivity == 3f, "axis clamp");
            InputSettings.HorizontalSensitivity = 1.25f;
            InputSettings.VerticalSensitivity = .75f;
            InputSettings.Save();
            string text = File.ReadAllText(Path.Combine(directory, "controls.txt"));
            Check(text.Contains("horizontal_sensitivity=1.25", StringComparison.Ordinal)
                && text.Contains("vertical_sensitivity=0.75", StringComparison.Ordinal),
                "invariant-culture persistence");
            InputSettings.HorizontalSensitivity = InputSettings.VerticalSensitivity = 1;
            InputSettings.Load();
            Check(MathF.Abs(InputSettings.HorizontalSensitivity - 1.25f) < .0001f
                && MathF.Abs(InputSettings.VerticalSensitivity - .75f) < .0001f,
                "roundtrip via real controls parser");
        }
        finally
        {
            LauncherPrefs.Directory = previousDirectory;
            InputSettings.MouseSensitivity = previousMouse;
            InputSettings.HorizontalSensitivity = previousX;
            InputSettings.VerticalSensitivity = previousY;
            InputSettings.ImperialistZoomSensitivity = previousZoom;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        Console.WriteLine("AXIS SENSITIVITY PASS " + checks);
        return 0;
    }
}
