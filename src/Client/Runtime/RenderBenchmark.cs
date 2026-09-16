using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Mods;

internal sealed record RenderBenchmarkOptions(
    double MeasurementSeconds,
    string Room,
    int Players,
    string OutputPath,
    GraphicsPreset Preset,
    int RenderScale,
    int FrameRateCap)
{
    public bool NativeTimingEnabled { get; init; }
    public const double WarmupSeconds = 5;
    public static readonly Vector2i Resolution = new(1920, 1080);
    public const string DefaultRoom = "MP1 SANCTORUS";

    public static bool TryParse(string[] args, out RenderBenchmarkOptions? options,
        out string? error)
    {
        options = null;
        error = null;
        double seconds = 30;
        int players = 8;
        string room = DefaultRoom;
        string output = Path.Combine(ConsoleSetup.LaunchDirectory, "renderbench.json");
        GraphicsPreset preset = GraphicsPreset.Original;
        int renderScale = 100;
        int frameRateCap = FrameTiming.MaxCap;
        bool nativeTimingEnabled = false;

        if (!TryValue(args, "renderbench-seconds", out string? secondsText, out error)
            || !TryValue(args, "renderbench-room", out string? roomText, out error)
            || !TryValue(args, "renderbench-players", out string? playersText, out error)
            || !TryValue(args, "renderbench-output", out string? outputText, out error)
            || !TryValue(args, "renderbench-preset", out string? presetText, out error)
            || !TryValue(args, "renderbench-scale", out string? scaleText, out error)
            || !TryValue(args, "renderbench-cap", out string? capText, out error)
            || !TryValue(args, "renderbench-native-timing", out string? nativeTimingText,
                out error))
            return false;

        if (secondsText != null && (!Double.TryParse(secondsText,
                NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
            || !Double.IsFinite(seconds) || seconds <= 0 || seconds > 3600))
        {
            error = "-renderbench-seconds must be greater than 0 and no more than 3600.";
            return false;
        }
        if (playersText != null && (!Int32.TryParse(playersText,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out players)
            || players < 1 || players > PlayerEntity.SlotCapacity))
        {
            error = $"-renderbench-players must be between 1 and {PlayerEntity.SlotCapacity}.";
            return false;
        }
        if (roomText != null)
        {
            room = roomText.Trim();
            if (room.Length == 0)
            {
                error = "-renderbench-room cannot be empty.";
                return false;
            }
        }
        if (outputText != null)
        {
            if (String.IsNullOrWhiteSpace(outputText))
            {
                error = "-renderbench-output cannot be empty.";
                return false;
            }
            output = Path.GetFullPath(outputText, ConsoleSetup.LaunchDirectory);
        }
        if (presetText != null)
        {
            string normalized = presetText.Trim().ToLowerInvariant();
            preset = normalized switch
            {
                "original" => GraphicsPreset.Original,
                "performance" or "perf" => GraphicsPreset.Performance,
                "enhanced" => GraphicsPreset.Enhanced,
                _ => (GraphicsPreset)Byte.MaxValue
            };
            if (!Enum.IsDefined(preset))
            {
                error = "-renderbench-preset must be original, performance, or enhanced.";
                return false;
            }
        }
        if (scaleText != null && (!Int32.TryParse(scaleText,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out renderScale)
            || renderScale < RenderOptions.MinScale || renderScale > 100))
        {
            error = $"-renderbench-scale must be between {RenderOptions.MinScale} and 100.";
            return false;
        }
        if (capText != null && !TryParseCap(capText, out frameRateCap))
        {
            error = $"-renderbench-cap must be display, unlimited, or {FrameTiming.MinCap}-{FrameTiming.MaxCap}.";
            return false;
        }
        if (nativeTimingText != null)
        {
            string normalized = nativeTimingText.Trim().ToLowerInvariant();
            if (normalized == "on") nativeTimingEnabled = true;
            else if (normalized == "off") nativeTimingEnabled = false;
            else
            {
                error = "-renderbench-native-timing must be on or off.";
                return false;
            }
        }
        options = new RenderBenchmarkOptions(seconds, room, players,
            Path.GetFullPath(output), preset, renderScale, frameRateCap)
        {
            NativeTimingEnabled = nativeTimingEnabled
        };
        return true;
    }

    private static bool TryParseCap(string value, out int cap)
    {
        if (value.Equals("display", StringComparison.OrdinalIgnoreCase)
            || value.Equals("vsync", StringComparison.OrdinalIgnoreCase))
        {
            cap = FrameTiming.DisplayRate;
            return true;
        }
        if (value.Equals("unlimited", StringComparison.OrdinalIgnoreCase)
            || value.Equals("uncapped", StringComparison.OrdinalIgnoreCase))
        {
            cap = FrameTiming.MaxCap;
            return true;
        }
        return Int32.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out cap)
            && cap >= FrameTiming.MinCap && cap <= FrameTiming.MaxCap;
    }

    private static bool TryValue(string[] args, string name, out string? value,
        out string? error)
    {
        value = null;
        error = null;
        string flag = "-" + name;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                error = $"{flag} requires a value.";
                return false;
            }
            value = args[++i];
        }
        return true;
    }
}

internal static class RenderBenchmark
{
    private const uint BenchmarkRng1 = Rng.Rng1StartValue;
    private const uint BenchmarkRng2 = Rng.Rng2StartValue;
    private const uint BenchmarkSpawnSeed = 0x5052494D;

    public static int Run(RenderBenchmarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RenderQualitySnapshot oldQuality = RenderOptions.CaptureSnapshot();
        int oldScale = RenderOptions.ResolutionScale;
        int oldCap = FrameTiming.FrameRateCap;
        WindowStartMode oldWindowMode = WindowMode.Startup;
        bool oldNativeTiming = MphRead.RenderTelemetryConfiguration.NativeTimingEnabled;
        try
        {
            MphRead.RenderTelemetryConfiguration.NativeTimingEnabled
                = options.NativeTimingEnabled;
            ApplyPreset(options.Preset);
            RenderOptions.ResolutionScale = options.RenderScale;
            // The product's "Unlimited" setting resolves to its documented
            // 500 FPS ceiling, not a separate busy-spin mode.
            FrameTiming.FrameRateCap = options.FrameRateCap;
            WindowMode.Startup = WindowStartMode.Windowed;

            RoomContentPreparationResult preparation = MapPreparation.PrepareRoomAsync(
                new RoomContentRequest(options.Room, null,
                    GameplayContentIdentity.Tool("render-benchmark"),
                    RoomContentPurpose.Audit),
                System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            MapPreparation.RequirePreparedRoom(preparation);

            var scene = new Scene(features: ClientMatchFeatures.Capture());
            using var host = new SdlGameHost(RenderBenchmarkOptions.Resolution,
                "Project Prime — render benchmark", showWindow: true);
            var session = new RenderBenchmarkSession(options);
            host.RunSceneObserved(scene, presentation =>
            {
                scene.Players.MaxPlayers = Math.Max(scene.Players.MaxPlayers,
                    options.Players);
                for (int i = 0; i < options.Players; i++)
                {
                    scene.AddPlayer(PlayableHunterCatalog.FromIndex(
                        i % PlayableHunterCatalog.Count), recolor: 0, team: -1);
                }
                for (int i = 0; i < scene.Players.Count; i++)
                {
                    PlayerEntity player = scene.Players[i];
                    player.IsBot = i > 0;
                    player.BotLevel = i > 0 ? 1 : 0;
                    if (i >= options.Players) player.LoadFlags &= ~LoadFlags.Active;
                }
                scene.Players.ActiveCount = options.Players;
                scene.LocalPlayerSlot = 0;
                presentation.AddRoom(options.Room, GameMode.Battle,
                    playerCount: NetConfig.RoomPlayerCount);
                scene.Random.SetRng1(BenchmarkRng1);
                scene.Random.SetRng2(BenchmarkRng2);
                scene.SpawnDirector.Reset(BenchmarkSpawnSeed);
            }, beforeCleanup: null, started: null, suspendFrame: null,
                exitPresentation: SceneExitPresentation.HideWindow,
                transitionGeneration: 0, firstFramePresented: null,
                windowPrepared: null,
                sceneServices: new ClientSceneServices(forceSpawn: true),
                frameObserver: session, suppressNativeInput: true);

            RenderBenchmarkReport report = session.CreateReport(host);
            string? directory = Path.GetDirectoryName(options.OutputPath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(report,
                RenderBenchmarkJson.Options));
            Console.WriteLine($"renderbench: PASS {report.PresentedFps:0.0} FPS; "
                + $"report={options.OutputPath}");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"renderbench: FAIL: {exception.Message}");
            return 1;
        }
        finally
        {
            RenderOptions.GraphicsPreset = oldQuality.GraphicsPreset;
            RenderOptions.TextureFilteringPreset = oldQuality.TextureFilteringPreset;
            RenderOptions.Anisotropy = oldQuality.Anisotropy;
            RenderOptions.Msaa = oldQuality.Msaa;
            RenderOptions.Bloom = oldQuality.Bloom;
            RenderOptions.DynamicVisualLights = oldQuality.DynamicVisualLights;
            RenderOptions.ResolutionScale = oldScale;
            FrameTiming.FrameRateCap = oldCap;
            WindowMode.Startup = oldWindowMode;
            MphRead.RenderTelemetryConfiguration.NativeTimingEnabled = oldNativeTiming;
            ContentEnvironment.UnmountMap();
        }
    }

    private static void ApplyPreset(GraphicsPreset preset)
    {
        RenderOptions.GraphicsPreset = preset;
        RenderOptions.TextureFilteringPreset = RenderOptions.TextureFilteringFor(preset);
        RenderOptions.Anisotropy = RenderOptions.AnisotropyFor(preset);
        RenderOptions.Msaa = RenderOptions.MsaaFor(preset);
        RenderOptions.Bloom = RenderOptions.BloomFor(preset);
        RenderOptions.DynamicVisualLights = RenderOptions.DynamicVisualLightsFor(preset);
    }
}

internal sealed class RenderBenchmarkSession : ISdlGameHostFrameObserver
{
    private readonly RenderBenchmarkOptions _options;
    private readonly Stopwatch _clock = new();
    private readonly OnlineMetric _cpuFrame = new();
    private readonly OnlineMetric _hostWork = new();
    private readonly OnlineMetric _frameTiming = new();
    private readonly OnlineMetric _simulation = new();
    private readonly OnlineMetric _presentationBuild = new();
    private readonly OnlineMetric _gpuEncode = new();
    private readonly OnlineMetric _swapchainAcquire = new();
    private readonly OnlineMetric _submit = new();
    private readonly OnlineMetric _softwarePacing = new();
    private readonly OnlineMetric _drawCalls = new();
    private readonly OnlineMetric _pipelineBinds = new();
    private readonly OnlineMetric _samplerBinds = new();
    private readonly OnlineMetric _uploadBytes = new();
    private readonly OnlineMetric _vertexUniformPushCalls = new();
    private readonly OnlineMetric _vertexUniformRequestedBytes = new();
    private readonly OnlineMetric _vertexUniformAlignedBytes = new();
    private readonly OnlineMetric _vertexUniformNativeTicks = new();
    private readonly OnlineMetric _fragmentUniformPushCalls = new();
    private readonly OnlineMetric _fragmentUniformRequestedBytes = new();
    private readonly OnlineMetric _fragmentUniformAlignedBytes = new();
    private readonly OnlineMetric _fragmentUniformNativeTicks = new();
    private readonly OnlineMetric _vertexBufferBindCalls = new();
    private readonly OnlineMetric _vertexBufferBindNativeTicks = new();
    private readonly OnlineMetric _indexBufferBindCalls = new();
    private readonly OnlineMetric _indexBufferBindNativeTicks = new();
    private readonly OnlineMetric _indexedDrawNativeCalls = new();
    private readonly OnlineMetric _indexedDrawNativeTicks = new();
    private readonly OnlineMetric _descriptorBearingDraws = new();
    private readonly OnlineMetric _pipelineBindNativeCalls = new();
    private readonly OnlineMetric _pipelineBindNativeTicks = new();
    private readonly OnlineMetric _samplerBindNativeCalls = new();
    private readonly OnlineMetric _samplerBindNativeTicks = new();
    private bool _measuring;
    private bool _started;
    private bool _finished;
    private long _lastTelemetryFrame;
    private double _measurementStart;
    private double _measurementEnd;
    private Vector2i _measurementFramebuffer;
    private string? _invalidReason;

    public RenderBenchmarkSession(RenderBenchmarkOptions options) => _options = options;

    public void OnFrameCompleted(SdlGameHost host)
    {
        if (!_started)
        {
            _started = true;
            _clock.Start();
            return;
        }
        double now = _clock.Elapsed.TotalSeconds;
        if (!_measuring)
        {
            FramePhaseTimingSample warmupSample = host.Timing.LatestRuntimeSample;
            if (now < RenderBenchmarkOptions.WarmupSeconds || !warmupSample.Presented) return;
            _measuring = true;
            _measurementStart = now;
            _measurementFramebuffer = host.FramebufferSize;
            host.Timing.ResetDiagnostics();
            _lastTelemetryFrame = host.Backend.Telemetry.FrameNumber;
        }

        ValidateStableConfiguration(host);
        FramePhaseTimingSample sample = host.Timing.LatestRuntimeSample;
        if (sample.Presented)
        {
            _cpuFrame.Record(sample.PresentedWholeFrameMilliseconds);
            _hostWork.Record(Sum(sample.HostWorkMilliseconds,
                sample.InputMilliseconds));
            _frameTiming.Record(sample.FrameTimingAdvanceMilliseconds);
            _simulation.Record(sample.SimulationMilliseconds);
            _presentationBuild.Record(sample.DrawListBuildMilliseconds);
            _gpuEncode.Record(sample.RenderEncodeMilliseconds);
            _swapchainAcquire.Record(sample.SwapchainAcquireMilliseconds);
            _submit.Record(sample.RenderSubmitMilliseconds);
            _softwarePacing.Record(sample.SoftwarePacingMilliseconds);
        }

        RenderTelemetrySnapshot telemetry = host.Backend.Telemetry;
        if (telemetry.FrameNumber > _lastTelemetryFrame)
        {
            _lastTelemetryFrame = telemetry.FrameNumber;
            if (telemetry.Submitted)
            {
                _drawCalls.Record(telemetry.DrawCallCount);
                _pipelineBinds.Record(telemetry.PipelineBindCount);
                _samplerBinds.Record(telemetry.SamplerBindCount);
                _uploadBytes.Record(telemetry.UploadCommittedBytes);
                _vertexUniformPushCalls.Record(telemetry.VertexUniformPushCalls);
                _vertexUniformRequestedBytes.Record(telemetry.VertexUniformRequestedBytes);
                _vertexUniformAlignedBytes.Record(telemetry.VertexUniformAlignedBytes);
                _vertexUniformNativeTicks.Record(telemetry.VertexUniformNativeTicks);
                _fragmentUniformPushCalls.Record(telemetry.FragmentUniformPushCalls);
                _fragmentUniformRequestedBytes.Record(telemetry.FragmentUniformRequestedBytes);
                _fragmentUniformAlignedBytes.Record(telemetry.FragmentUniformAlignedBytes);
                _fragmentUniformNativeTicks.Record(telemetry.FragmentUniformNativeTicks);
                _vertexBufferBindCalls.Record(telemetry.VertexBufferBindCalls);
                _vertexBufferBindNativeTicks.Record(telemetry.VertexBufferBindNativeTicks);
                _indexBufferBindCalls.Record(telemetry.IndexBufferBindCalls);
                _indexBufferBindNativeTicks.Record(telemetry.IndexBufferBindNativeTicks);
                _indexedDrawNativeCalls.Record(telemetry.IndexedDrawNativeCalls);
                _indexedDrawNativeTicks.Record(telemetry.IndexedDrawNativeTicks);
                _descriptorBearingDraws.Record(telemetry.DescriptorBearingDraws);
                _pipelineBindNativeCalls.Record(telemetry.PipelineBindNativeCalls);
                _pipelineBindNativeTicks.Record(telemetry.PipelineBindNativeTicks);
                _samplerBindNativeCalls.Record(telemetry.SamplerBindNativeCalls);
                _samplerBindNativeTicks.Record(telemetry.SamplerBindNativeTicks);
            }
        }

        if (now - _measurementStart >= _options.MeasurementSeconds)
        {
            _measurementEnd = now;
            _finished = true;
            host.StopScene();
        }
    }

    private static double Sum(double left, double right)
    {
        bool leftValid = Double.IsFinite(left) && left >= 0;
        bool rightValid = Double.IsFinite(right) && right >= 0;
        if (leftValid && rightValid) return left + right;
        if (leftValid) return left;
        return rightValid ? right : double.NaN;
    }

    internal static double NativeMilliseconds(double ticks)
        => ticks * (1000d / Stopwatch.Frequency);

    private void ValidateStableConfiguration(SdlGameHost host)
    {
        string? reason = null;
        if (!host.IsFocused) reason = "window focus was lost";
        else if (host.IsMinimized) reason = "window was minimized";
        else if (host.LogicalSize != RenderBenchmarkOptions.Resolution)
            reason = "window resolution changed";
        else if (host.FramebufferSize != RenderBenchmarkOptions.Resolution)
            reason = "drawable resolution is not the required 1920x1080";
        else if (host.FramebufferSize != _measurementFramebuffer)
            reason = "drawable resolution changed";
        else if (RenderOptions.GraphicsPreset != _options.Preset)
            reason = "graphics preset changed";
        else if (RenderOptions.ResolutionScale != _options.RenderScale)
            reason = "render scale changed";
        else if (FrameTiming.FrameRateCap != _options.FrameRateCap)
            reason = "frame-rate cap changed";
        _invalidReason ??= reason;
    }

    public RenderBenchmarkReport CreateReport(SdlGameHost host)
    {
        if (!_finished) throw new InvalidOperationException("The benchmark ended before measurement completed.");
        if (_invalidReason != null)
            throw new InvalidOperationException($"Benchmark invalidated: {_invalidReason}.");
        FrameTimingDiagnosticsSnapshot timing = host.Timing.CaptureDiagnostics();
        RenderBackendInfo backend = host.Backend.Info;
        double elapsed = Math.Max(Double.Epsilon, _measurementEnd - _measurementStart);
        return new RenderBenchmarkReport
        {
            Platform = PlatformName(),
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                ?? RuntimeInformation.ProcessArchitecture.ToString(),
            ProductVersion = Branding.EngineVersion.ToString(),
            BuildIdentity = GetBuildCommit(),
            RequestedGpuBackend = backend.RequestedDriver ?? "auto",
            GpuBackend = backend.Driver,
            GpuDevice = backend.DeviceName ?? "unavailable",
            GpuDriverInfo = backend.DeviceDriverInfo ?? "unavailable",
            SdlVersion = backend.RuntimeVersion ?? "unavailable",
            GpuDebug = backend.GpuDebug,
            Resolution = $"{_measurementFramebuffer.X}x{_measurementFramebuffer.Y}",
            RequestedLogicalResolution =
                $"{RenderBenchmarkOptions.Resolution.X}x{RenderBenchmarkOptions.Resolution.Y}",
            DrawableResolution =
                $"{_measurementFramebuffer.X}x{_measurementFramebuffer.Y}",
            RenderScale = _options.RenderScale,
            Preset = RenderOptions.FormatGraphicsPreset(_options.Preset),
            Room = _options.Room,
            Players = _options.Players,
            Workload = "fixed-60hz-bots-neutral-local-input",
            WorkloadSeed = $"rng1=0x{Rng.Rng1StartValue:X8};rng2=0x{Rng.Rng2StartValue:X8};spawn=0x5052494D",
            NativeInputSuppressed = true,
            WarmupSeconds = RenderBenchmarkOptions.WarmupSeconds,
            MeasurementSeconds = elapsed,
            FrameRateCap = _options.FrameRateCap,
            FrameRateCapMode = _options.FrameRateCap == FrameTiming.DisplayRate
                ? "display" : _options.FrameRateCap == FrameTiming.MaxCap
                    ? "product-unlimited-ceiling" : "explicit",
            PresentMode = backend.PresentMode,
            PresentedFps = timing.PresentedFrames / elapsed,
            CpuFrameMsMean = _cpuFrame.Mean,
            CpuFrameMsP50 = _cpuFrame.Percentile(0.50),
            CpuFrameMsP95 = _cpuFrame.Percentile(0.95),
            CpuFrameMsP99 = _cpuFrame.Percentile(0.99),
            InputMsMean = _hostWork.Mean,
            FrameTimingMsMean = _frameTiming.Mean,
            SimulationMsMean = _simulation.Mean,
            PresentationBuildMsMean = _presentationBuild.Mean,
            PresentationBuildTimingStatus =
                "combined: presentation/interpolation and RenderFrame construction share OnDrawFrame",
            GpuEncodeMsMean = _gpuEncode.Mean,
            SwapchainAcquireMsMean = _swapchainAcquire.Mean,
            SubmitMsMean = _submit.Mean,
            SoftwarePacingMsMean = _softwarePacing.Mean,
            PresentMsMean = null,
            PresentTimingStatus = "unavailable: backend submission and native present share one call",
            GpuFrameMsMean = null,
            GpuTimingStatus = host.Backend.Telemetry.GpuFrameTimeStatus,
            DrawCallsMean = _drawCalls.Mean,
            PipelineBindsMean = _pipelineBinds.Mean,
            SamplerBindsMean = _samplerBinds.Mean,
            UploadBytesMean = _uploadBytes.Mean,
            NativeTimingEnabled = _options.NativeTimingEnabled,
            VertexUniformPushCallsMean = _vertexUniformPushCalls.Mean,
            VertexUniformRequestedBytesMean = _vertexUniformRequestedBytes.Mean,
            VertexUniformAlignedBytesMean = _vertexUniformAlignedBytes.Mean,
            VertexUniformNativeMsMean = NativeMilliseconds(_vertexUniformNativeTicks.Mean),
            FragmentUniformPushCallsMean = _fragmentUniformPushCalls.Mean,
            FragmentUniformRequestedBytesMean = _fragmentUniformRequestedBytes.Mean,
            FragmentUniformAlignedBytesMean = _fragmentUniformAlignedBytes.Mean,
            FragmentUniformNativeMsMean = NativeMilliseconds(_fragmentUniformNativeTicks.Mean),
            VertexBufferBindCallsMean = _vertexBufferBindCalls.Mean,
            VertexBufferBindMsMean = NativeMilliseconds(_vertexBufferBindNativeTicks.Mean),
            IndexBufferBindCallsMean = _indexBufferBindCalls.Mean,
            IndexBufferBindMsMean = NativeMilliseconds(_indexBufferBindNativeTicks.Mean),
            IndexedDrawNativeCallsMean = _indexedDrawNativeCalls.Mean,
            IndexedDrawNativeMsMean = NativeMilliseconds(_indexedDrawNativeTicks.Mean),
            DescriptorBearingDrawsMean = _descriptorBearingDraws.Mean,
            PipelineBindNativeCallsMean = _pipelineBindNativeCalls.Mean,
            PipelineBindNativeMsMean = NativeMilliseconds(_pipelineBindNativeTicks.Mean),
            SamplerBindNativeCallsMean = _samplerBindNativeCalls.Mean,
            SamplerBindNativeMsMean = NativeMilliseconds(_samplerBindNativeTicks.Mean),
            TickAttempts = timing.TickAttempts,
            AcquiredFrames = timing.AcquiredFrames,
            SubmittedFrames = timing.SubmittedFrames,
            PresentedFrames = timing.PresentedFrames,
            UnacquiredFrames = timing.UnacquiredFrames,
            SimulationSteps = timing.TotalSteps,
            DroppedSimulationSteps = timing.DroppedSteps,
            Stalls = timing.Stalls,
            AllocatedBytesPerSecond = timing.GcAllocatedBytesPerSecond,
            Gen0Collections = timing.Gen0Collections,
            Gen1Collections = timing.Gen1Collections,
            Gen2Collections = timing.Gen2Collections,
            PercentileSampleCapacity = OnlineMetric.SampleCapacity
        };
    }

    internal static string GetBuildCommit()
    {
        foreach (AssemblyMetadataAttribute metadata in Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == "ProjectPrimeGitCommit"
                && !String.IsNullOrWhiteSpace(metadata.Value))
                return metadata.Value;
        }
        return "unavailable";
    }

    private static string PlatformName()
        => OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : OperatingSystem.IsLinux() ? "linux" : "unknown";
}

internal sealed class OnlineMetric
{
    public const int SampleCapacity = 1024;
    private readonly double[] _samples = new double[SampleCapacity];
    private long _seen;
    private int _count;
    private double _mean;
    private ulong _random = 0x9E3779B97F4A7C15UL;

    public double Mean => _count == 0 ? 0 : _mean;

    public void Record(double value)
    {
        if (!Double.IsFinite(value) || value < 0) return;
        _seen++;
        _mean += (value - _mean) / _seen;
        if (_count < _samples.Length)
        {
            _samples[_count++] = value;
            return;
        }
        ulong slot = NextRandom() % (ulong)_seen;
        if (slot < (ulong)_samples.Length) _samples[(int)slot] = value;
    }

    public double Percentile(double percentile)
    {
        if (_count == 0) return 0;
        double[] copy = new double[_count];
        Array.Copy(_samples, copy, _count);
        Array.Sort(copy);
        int index = (int)Math.Ceiling(Math.Clamp(percentile, 0, 1) * _count) - 1;
        return copy[Math.Clamp(index, 0, _count - 1)];
    }

    private ulong NextRandom()
    {
        ulong x = _random;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _random = x;
        return x * 0x2545F4914F6CDD1DUL;
    }
}

internal sealed class RenderBenchmarkReport
{
    public string Platform { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string ProcessArchitecture { get; init; } = "";
    public string Cpu { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string BuildIdentity { get; init; } = "";
    public string RequestedGpuBackend { get; init; } = "";
    public string GpuBackend { get; init; } = "";
    public string GpuDevice { get; init; } = "";
    public string GpuDriverInfo { get; init; } = "";
    public string SdlVersion { get; init; } = "";
    public bool GpuDebug { get; init; }
    public string Resolution { get; init; } = "";
    public string RequestedLogicalResolution { get; init; } = "";
    public string DrawableResolution { get; init; } = "";
    public int RenderScale { get; init; }
    public string Preset { get; init; } = "";
    public string Room { get; init; } = "";
    public int Players { get; init; }
    public string Workload { get; init; } = "";
    public string WorkloadSeed { get; init; } = "";
    public bool NativeInputSuppressed { get; init; }
    public double WarmupSeconds { get; init; }
    public double MeasurementSeconds { get; init; }
    public int FrameRateCap { get; init; }
    public string FrameRateCapMode { get; init; } = "";
    public string PresentMode { get; init; } = "";
    public double PresentedFps { get; init; }
    public double CpuFrameMsMean { get; init; }
    public double CpuFrameMsP50 { get; init; }
    public double CpuFrameMsP95 { get; init; }
    public double CpuFrameMsP99 { get; init; }
    public double InputMsMean { get; init; }
    public double FrameTimingMsMean { get; init; }
    public double SimulationMsMean { get; init; }
    public double PresentationBuildMsMean { get; init; }
    public string PresentationBuildTimingStatus { get; init; } = "";
    public double GpuEncodeMsMean { get; init; }
    public double SwapchainAcquireMsMean { get; init; }
    public double SubmitMsMean { get; init; }
    public double SoftwarePacingMsMean { get; init; }
    public double? PresentMsMean { get; init; }
    public string PresentTimingStatus { get; init; } = "";
    public double? GpuFrameMsMean { get; init; }
    public string GpuTimingStatus { get; init; } = "";
    public double DrawCallsMean { get; init; }
    public double PipelineBindsMean { get; init; }
    public double SamplerBindsMean { get; init; }
    public double UploadBytesMean { get; init; }
    public bool NativeTimingEnabled { get; init; }
    public double VertexUniformPushCallsMean { get; init; }
    public double VertexUniformRequestedBytesMean { get; init; }
    public double VertexUniformAlignedBytesMean { get; init; }
    public double VertexUniformNativeMsMean { get; init; }
    public double FragmentUniformPushCallsMean { get; init; }
    public double FragmentUniformRequestedBytesMean { get; init; }
    public double FragmentUniformAlignedBytesMean { get; init; }
    public double FragmentUniformNativeMsMean { get; init; }
    public double VertexBufferBindCallsMean { get; init; }
    public double VertexBufferBindMsMean { get; init; }
    public double IndexBufferBindCallsMean { get; init; }
    public double IndexBufferBindMsMean { get; init; }
    public double IndexedDrawNativeCallsMean { get; init; }
    public double IndexedDrawNativeMsMean { get; init; }
    public double DescriptorBearingDrawsMean { get; init; }
    public double PipelineBindNativeCallsMean { get; init; }
    public double PipelineBindNativeMsMean { get; init; }
    public double SamplerBindNativeCallsMean { get; init; }
    public double SamplerBindNativeMsMean { get; init; }
    public long TickAttempts { get; init; }
    public long AcquiredFrames { get; init; }
    public long SubmittedFrames { get; init; }
    public long PresentedFrames { get; init; }
    public long UnacquiredFrames { get; init; }
    public long SimulationSteps { get; init; }
    public long DroppedSimulationSteps { get; init; }
    public long Stalls { get; init; }
    public double AllocatedBytesPerSecond { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public int PercentileSampleCapacity { get; init; }
}

internal static class RenderBenchmarkJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
