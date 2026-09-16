using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MphRead.Mods;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class RenderBenchmarkTests
{
    [Fact]
    public void OptionsUseRepeatableDefaults()
    {
        Assert.True(RenderBenchmarkOptions.TryParse(["-renderbench"],
            out RenderBenchmarkOptions? options, out string? error), error);

        Assert.NotNull(options);
        Assert.Equal(30, options.MeasurementSeconds);
        Assert.Equal(RenderBenchmarkOptions.DefaultRoom, options.Room);
        Assert.Equal(8, options.Players);
        Assert.Equal(GraphicsPreset.Original, options.Preset);
        Assert.Equal(100, options.RenderScale);
        Assert.Equal(MphRead.Mods.Render.FrameTiming.MaxCap, options.FrameRateCap);
        Assert.False(options.NativeTimingEnabled);
        Assert.Equal("renderbench.json", Path.GetFileName(options.OutputPath));
        Assert.Equal(1920, RenderBenchmarkOptions.Resolution.X);
        Assert.Equal(1080, RenderBenchmarkOptions.Resolution.Y);
    }

    [Fact]
    public void OptionsParseExplicitBenchmarkConfiguration()
    {
        string output = Path.Combine(Path.GetTempPath(), "prime-benchmark.json");
        string[] args = ["-renderbench", "-renderbench-seconds", "12.5",
            "-renderbench-room", "MP1 SANCTORUS", "-renderbench-players", "4",
            "-renderbench-output", output, "-renderbench-preset", "performance",
            "-renderbench-scale", "50", "-renderbench-cap", "240",
            "-renderbench-native-timing", "on"];

        Assert.True(RenderBenchmarkOptions.TryParse(args,
            out RenderBenchmarkOptions? options, out string? error), error);

        Assert.NotNull(options);
        Assert.Equal(12.5, options.MeasurementSeconds);
        Assert.Equal("MP1 SANCTORUS", options.Room);
        Assert.Equal(4, options.Players);
        Assert.Equal(Path.GetFullPath(output), options.OutputPath);
        Assert.Equal(GraphicsPreset.Performance, options.Preset);
        Assert.Equal(50, options.RenderScale);
        Assert.Equal(240, options.FrameRateCap);
        Assert.True(options.NativeTimingEnabled);
    }

    [Theory]
    [InlineData("-renderbench-seconds", "0")]
    [InlineData("-renderbench-players", "0")]
    [InlineData("-renderbench-preset", "cinematic")]
    [InlineData("-renderbench-scale", "24")]
    [InlineData("-renderbench-cap", "29")]
    [InlineData("-renderbench-native-timing", "maybe")]
    public void OptionsRejectInvalidValues(string flag, string value)
    {
        Assert.False(RenderBenchmarkOptions.TryParse(["-renderbench", flag, value],
            out _, out string? error));
        Assert.False(String.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void OptionsSupportDisplayCapForComparisonMatrix()
    {
        Assert.True(RenderBenchmarkOptions.TryParse(
            ["-renderbench", "-renderbench-cap", "display"],
            out RenderBenchmarkOptions? options, out string? error), error);
        Assert.NotNull(options);
        Assert.Equal(MphRead.Mods.Render.FrameTiming.DisplayRate,
            options.FrameRateCap);
    }

    [Fact]
    public void OptionsSupportExplicitNativeTimingOff()
    {
        Assert.True(RenderBenchmarkOptions.TryParse(
            ["-renderbench", "-renderbench-native-timing", "off"],
            out RenderBenchmarkOptions? options, out string? error), error);
        Assert.NotNull(options);
        Assert.False(options.NativeTimingEnabled);
    }

    [Fact]
    public void NativeTimingMillisecondsUsesStopwatchFrequency()
    {
        Assert.Equal(1000d,
            RenderBenchmarkSession.NativeMilliseconds(Stopwatch.Frequency), 10);
        Assert.Equal(0d, RenderBenchmarkSession.NativeMilliseconds(0), 10);
    }

    [Fact]
    public void OnlineMetricUsesBoundedSamplesAndWholeRunMean()
    {
        var metric = new OnlineMetric();
        for (int i = 1; i <= OnlineMetric.SampleCapacity * 3; i++) metric.Record(i);

        Assert.Equal((OnlineMetric.SampleCapacity * 3 + 1) / 2d, metric.Mean, 8);
        Assert.InRange(metric.Percentile(0.50), 1,
            OnlineMetric.SampleCapacity * 3);
        Assert.InRange(metric.Percentile(0.99), metric.Percentile(0.50),
            OnlineMetric.SampleCapacity * 3);
    }

    [Fact]
    public void ReportKeepsUnavailableTimingsExplicit()
    {
        string json = JsonSerializer.Serialize(new RenderBenchmarkReport
        {
            Platform = "windows",
            PresentMsMean = null,
            PresentTimingStatus = "unavailable",
            GpuFrameMsMean = null,
            GpuTimingStatus = "unavailable"
        }, RenderBenchmarkJson.Options);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("presentMsMean").ValueKind);
        Assert.Equal("unavailable", root.GetProperty("presentTimingStatus").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("gpuFrameMsMean").ValueKind);
        Assert.False(root.GetProperty("nativeTimingEnabled").GetBoolean());
    }

    [Fact]
    public void BuildIdentityIsExactGitCommit()
    {
        Assert.Matches(new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant),
            RenderBenchmarkSession.GetBuildCommit());
    }
}
