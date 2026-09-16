using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// Controller diagnostics through the shipping SDL event path. This utility
/// deliberately creates one <see cref="SdlGameHost"/> and calls its
/// host-thread pump; it never starts a second event thread or polls GLFW.
/// </summary>
internal static class SdlGamepadProbe
{
    private const double MaximumSeconds = 300;

    public static int Run(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > MaximumSeconds)
        {
            Console.Error.WriteLine($"[gamepad] seconds must be between 0 and {MaximumSeconds:0}.");
            return 2;
        }

        try
        {
            using var host = new SdlGameHost(new Vector2i(16, 16),
                "Project Prime — controller diagnostics", showWindow: false);
            return Watch(host, seconds);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"[gamepad] SDL host unavailable: {exception.Message}");
            return 1;
        }
    }

    private static int Watch(SdlGameHost host, double seconds)
    {
        Console.WriteLine($"[gamepad] SDL diagnostics for {seconds:0} s. "
            + $"move dead zone {InputSettings.GamepadMoveDeadZone.ToString("0.00", CultureInfo.InvariantCulture)}, "
            + $"look dead zone {InputSettings.GamepadLookDeadZone.ToString("0.00", CultureInfo.InvariantCulture)}, "
            + $"gyro {(InputSettings.GamepadGyroEnabled ? "on" : "off")}");
        Console.WriteLine("[gamepad] buttons: " + String.Join(", ",
            PadBindings.Actions.Select(action =>
                $"{PadBindings.Name(action)} {PadBindings.Describe(PadBindings.Get(action))}")));

        var clock = Stopwatch.StartNew();
        string last = "";
        bool everConnected = false;
        bool everMoved = false;
        ControllerCapabilitySnapshot capabilities = ControllerCapabilities.Current;
        while (clock.Elapsed.TotalSeconds < seconds && host.PumpToolEvents())
        {
            // This is the same fixed-step processing used by gameplay. It
            // advances the actual movement/trigger/look processors, including
            // gyro look when the configured settings permit it.
            GamepadInput.BeginFrame(allowLook: true, zoomed: false);
            GamepadState state = GamepadInput.State;
            ControllerCapabilitySnapshot current = ControllerCapabilities.Current;
            if (current != capabilities)
            {
                capabilities = current;
                Console.WriteLine($"  device transition: {DescribeCapabilities(capabilities)}");
            }
            everConnected |= state.Connected;
            string line = Describe(state);
            if (!StringComparer.Ordinal.Equals(line, last))
            {
                last = line;
                Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0}s {line}");
            }
            GamepadMovementSample movement = GamepadInput.Movement;
            everMoved |= state.Connected
                && (GamepadInput.EffectiveButtons != GamepadButtons.None
                    || movement.Active
                    || MathF.Abs(state.RightX) > 0.5f
                    || MathF.Abs(state.RightY) > 0.5f);
            Thread.Sleep(16);
        }

        ControllerDiagnosticReport report = ControllerDiagnosticReportBuilder.Capture(
            capabilities,
            GamepadInput.State,
            GamepadInput.EffectiveButtons,
            GamepadInput.Movement.Vector.X,
            GamepadInput.Movement.Vector.Y,
            GamepadInput.AimDeltaX,
            GamepadInput.AimDeltaY,
            GamepadInput.AimAngularVelocity.X,
            GamepadInput.AimAngularVelocity.Y,
            processing: ProcessingDiagnostics());
        string diagnosticsDirectory = Path.Combine(LauncherPrefs.Directory, "logs");
        try
        {
            (string json, string text) = ControllerDiagnosticReportBuilder.WriteToDirectory(
                diagnosticsDirectory, report);
            Console.WriteLine($"[gamepad] report JSON: {json}");
            Console.WriteLine($"[gamepad] report text: {text}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[gamepad] report could not be written: {exception.Message}");
        }

        if (!everConnected)
        {
            Console.WriteLine("[gamepad] FAIL: SDL saw no connected gamepad.");
            return 1;
        }
        if (!everMoved)
        {
            Console.WriteLine("[gamepad] a gamepad is connected, but no input exceeded the configured thresholds.");
            return 1;
        }
        Console.WriteLine("[gamepad] PASS: SDL input reached GamepadInput and its configured bindings/processors.");
        return 0;
    }

    private static ControllerDiagnosticProcessing ProcessingDiagnostics()
    {
        ControllerStickCalibrationSample calibration = GamepadInput.LookCalibration;
        GamepadLookSample look = GamepadInput.ProcessedLook;
        GyroConditioningStatus gyro = GamepadGyro.Status.Conditioning;
        return new ControllerDiagnosticProcessing(calibration.Value.X,
            calibration.Value.Y, calibration.InnerDeadzone,
            calibration.OuterDeadzone, calibration.LearnedCenter.X,
            calibration.LearnedCenter.Y, calibration.LearnedNoise,
            calibration.LearnedMaximumMagnitude, look.Magnitude,
            look.ResponseMagnitude, look.BoostProgress,
            gyro.CalibrationState, gyro.CalibrationSamples,
            gyro.HasFreshOutput);
    }

    private static string Describe(GamepadState state)
    {
        if (!state.Connected) return "no pad";
        var text = new StringBuilder();
        text.Append($"{state.Name} L({state.LeftX,5:0.00},{state.LeftY,5:0.00})");
        text.Append($" R({state.RightX,5:0.00},{state.RightY,5:0.00})");
        text.Append($" LT{state.LeftTrigger:0.00} RT{state.RightTrigger:0.00}");
        text.Append($" aim({GamepadInput.AimDeltaX,6:0.00},{GamepadInput.AimDeltaY,6:0.00})");
        if (GamepadInput.EffectiveButtons != GamepadButtons.None)
            text.Append($" {GamepadInput.EffectiveButtons}");
        return text.ToString();
    }

    private static string DescribeCapabilities(ControllerCapabilitySnapshot value)
        => $"{value.DeviceName ?? "unknown"} family={value.Family} "
            + $"guid={value.Guid ?? "unknown"} vendor={value.VendorId?.ToString() ?? "unknown"} "
            + $"product={value.ProductId?.ToString() ?? "unknown"} "
            + $"gyro={Feature(value.HasGyroscope)} accel={Feature(value.HasAccelerometer)} "
            + $"triggers={Feature(value.HasAnalogTriggers)} rumble={Feature(value.HasRumble)} "
            + $"SDL={value.SdlRuntimeVersion ?? "unknown"}";

    private static string Feature(bool? value) => value switch
    {
        true => "supported",
        false => "unsupported",
        _ => "unknown"
    };
}
