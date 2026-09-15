using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Input;
using Xunit;

namespace MphRead.Tests;

public sealed class ControllerDiagnosticReportTests
{
    [Fact]
    public void ReportPreservesUnknownCapabilitiesAndDropsNonFiniteSamples()
    {
        ControllerCapabilitySnapshot capabilities = ControllerCapabilitySnapshot.Connected(
            ControllerBackend.Sdl, "gamepad:1", "Test pad", ControllerFamily.Xbox,
            hasGyroscope: null, hasRumble: false, hasAnalogTriggers: true,
            guid: "guid", vendorId: 1, productId: 2, productVersion: 3,
            hasAccelerometer: null, sdlMapping: "mapping", sdlRuntimeVersion: "3.4.16");
        var raw = new GamepadState
        {
            Connected = true,
            Name = "Test pad",
            Family = ControllerFamily.Xbox,
            LeftX = float.NaN,
            RightY = float.PositiveInfinity,
            LeftTrigger = .5f,
            Buttons = GamepadButtons.A
        };

        ControllerDiagnosticReport report = ControllerDiagnosticReportBuilder.Capture(
            capabilities, raw, GamepadButtons.B, .1f, .2f, float.NaN, .3f,
            .4f, float.NegativeInfinity, DateTimeOffset.UnixEpoch);

        Assert.Equal(ControllerFeatureAvailability.Unknown, report.Gyroscope);
        Assert.Equal(ControllerFeatureAvailability.Unsupported, report.Rumble);
        Assert.Equal(ControllerFeatureAvailability.Supported, report.AnalogTriggers);
        Assert.Null(report.RawState.LeftX);
        Assert.Null(report.RawState.RightY);
        Assert.Null(report.EffectiveState.AimDeltaX);
        Assert.Null(report.EffectiveState.AimVelocityY);
        Assert.Equal("3.4.16", report.SdlRuntimeVersion);
    }

    [Fact]
    public void ReportExportUsesUniqueFilesAndContainsNoMachinePaths()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-controller-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var capabilities = ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl, "gamepad:1", "Test pad", ControllerFamily.Generic,
                guid: "guid", sdlMapping: "mapping");
            var state = new GamepadState { Connected = true, Name = "Test pad" };
            ControllerDiagnosticReport report = ControllerDiagnosticReportBuilder.Capture(
                capabilities, state, GamepadButtons.None, 0, 0, 0, 0, 0, 0,
                DateTimeOffset.UnixEpoch);

            (string jsonPath, string textPath) first = ControllerDiagnosticReportBuilder.WriteToDirectory(directory, report);
            (string jsonPath, string textPath) second = ControllerDiagnosticReportBuilder.WriteToDirectory(directory, report);

            Assert.NotEqual(first.jsonPath, second.jsonPath);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(first.jsonPath));
            Assert.Equal("guid", document.RootElement.GetProperty("guid").GetString());
            string json = File.ReadAllText(first.jsonPath);
            string text = File.ReadAllText(first.textPath);
            Assert.DoesNotContain("serial", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("machine", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(directory, json, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
