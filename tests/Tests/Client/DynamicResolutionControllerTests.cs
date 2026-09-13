using System;
using System.IO;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Mods;
using Xunit;

public sealed class DynamicResolutionControllerTests
{
    [Fact]
    public void MissingGpuTimestampsHoldScaleWithoutCpuProxy()
    {
        var controller = new DynamicResolutionController(85);
        DynamicResolutionDecision decision = controller.Update(
            GraphicsPreset.Enhanced, 100, gpuFrameMilliseconds: null);
        Assert.Equal(85, decision.ScalePercent);
        Assert.Equal(DynamicResolutionStatus.GpuTimingUnavailable,
            decision.Status);
    }

    [Fact]
    public void SustainedGpuPressureReducesInBoundedSteps()
    {
        var controller = new DynamicResolutionController();
        DynamicResolutionDecision decision = default;
        for (int i = 0; i < DynamicResolutionController.ReductionWindow; i++)
            decision = controller.Update(GraphicsPreset.Enhanced, 100, 25);
        Assert.Equal(95, decision.ScalePercent);
        Assert.Equal(DynamicResolutionStatus.Reduced, decision.Status);
    }

    [Fact]
    public void LegacyModesResetToTheConfiguredStaticScale()
    {
        var controller = new DynamicResolutionController(75);
        DynamicResolutionDecision decision = controller.Update(
            GraphicsPreset.Performance, 90, 30);
        Assert.Equal(90, decision.ScalePercent);
        Assert.Equal(DynamicResolutionStatus.Disabled, decision.Status);
    }

    [Fact]
    public void UserConfiguredScaleBelowDynamicFloorRemainsAuthoritative()
    {
        var controller = new DynamicResolutionController(25);
        DynamicResolutionDecision decision = controller.Update(
            GraphicsPreset.Enhanced, 25, gpuFrameMilliseconds: 40);

        Assert.Equal(25, decision.ScalePercent);
        Assert.Equal(DynamicResolutionStatus.Holding, decision.Status);
    }

    [Fact]
    public void SdlPresentationFeedsCompletedGpuTelemetryIntoTheNextFrame()
    {
        string host = ReadRepositoryFile(
            "src/Client/Rendering/Platform/SdlGameHost.cs");
        string presentation = ReadRepositoryFile(
            "src/Client.Presentation/Rendering/Renderer.cs");

        Assert.Contains("ActivePresentation.UpdateDynamicResolution(_telemetry.Telemetry)",
            host, StringComparison.Ordinal);
        Assert.Contains("telemetry.GpuFrameMilliseconds", presentation,
            StringComparison.Ordinal);
        Assert.Contains("Scaled(Size.X, EffectiveRenderScale)", presentation,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        string testsDirectory = Path.GetDirectoryName(sourcePath)!;
        string repository = Path.GetFullPath(Path.Combine(testsDirectory,
            "../../.."));
        return File.ReadAllText(Path.Combine(repository, relativePath));
    }
}
