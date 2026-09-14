using System;
#if !ANDROID
using System.Collections.Concurrent;
#endif
using System.Collections.Generic;
#if !ANDROID
using System.Diagnostics;
#endif
using System.IO;
using System.Buffers.Binary;
#if !ANDROID
using System.Reflection;
#endif
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Cosmetics;
using MphRead.Mods.Launcher;
#if !ANDROID
using MphRead.Entities;
using OpenTK.Mathematics;
#endif

namespace MphRead.Mods;

internal enum ModelPreviewKind
{
    Hunter,
    Weapon
}

internal sealed record ModelPreviewSpec(ModelPreviewKind Kind, string Key,
    string ModelName, float Scale, float FrameMargin, byte BaseRecolor = 0,
    string? SkinKey = null, ushort ArmorEffectId = 0,
    ushort DeathEffectId = 0, uint DeathSampleTick = 0)
{
    public string WorkerKey => $"{Kind switch
    {
        ModelPreviewKind.Hunter => "hunter",
        ModelPreviewKind.Weapon => "weapon",
        _ => throw new InvalidOperationException($"Unsupported preview kind {(int)Kind}.")
    }}:{Key}" + (Kind == ModelPreviewKind.Hunter && DeathEffectId != 0
        ? $":{BaseRecolor}:death-{DeathEffectId}-t{DeathSampleTick}"
            + (ArmorEffectId == 0 ? "" : $"-a{ArmorEffectId}")
        : Kind == ModelPreviewKind.Hunter && ArmorEffectId != 0
            ? $":{BaseRecolor}:armor-{ArmorEffectId}"
        : Kind == ModelPreviewKind.Hunter && BaseRecolor != 0
            ? $":{BaseRecolor}" : "");
}

/// <summary>
/// Finite, source-backed preview catalog. UI input never becomes a model path.
/// Cache identity includes both the extracted content and this framing contract.
/// </summary>
internal static class ModelPreviewCatalog
{
    internal const int RendererVersion = 3;
    internal const int Width = 640;
    internal const int Height = 640;
    internal const uint DeathStageSampleTick = 36;

    public static bool TryHunter(Hunter hunter, out ModelPreviewSpec? spec)
        => TryHunter(hunter, skin: null, out spec);

    public static bool TryHunter(Hunter hunter, SkinDefinition? skin,
        out ModelPreviewSpec? spec)
        => TryHunter(hunter, skin, armorEffect: null, out spec);

    public static bool TryHunter(Hunter hunter, SkinDefinition? skin,
        ArmorEffectDefinition? armorEffect, out ModelPreviewSpec? spec)
    {
        spec = null;
        if (hunter is < Hunter.Samus or > Hunter.Weavel
            || !Metadata.HunterModels.TryGetValue(hunter, out var models)
            || models.Count == 0)
        {
            return false;
        }
        float scale = Metadata.HunterScales.TryGetValue(hunter, out float authored)
            ? authored : 1;
        if (skin != null && skin.Hunter != hunter)
            return false;
        if (armorEffect != null
            && (!CosmeticCatalog.BuiltIn.TryGetArmorEffect(armorEffect.Id,
                    out ArmorEffectDefinition officialArmor)
                || !String.Equals(officialArmor.Key, armorEffect.Key,
                    StringComparison.Ordinal)))
            return false;
        spec = new ModelPreviewSpec(ModelPreviewKind.Hunter,
            hunter.ToString().ToLowerInvariant(), models[0], scale, 1.3f,
            skin?.BaseRecolor ?? 0, skin?.Key, armorEffect?.Id ?? 0);
        return true;
    }

    public static bool TryWeapon(BeamType beam, out ModelPreviewSpec? spec)
    {
        string? model = beam switch
        {
            BeamType.PowerBeam => Metadata.HunterModels[Hunter.Samus][3],
            BeamType.VoltDriver => Metadata.Items[(int)ItemType.VoltDriver],
            BeamType.Missile => Metadata.Items[(int)ItemType.PickWpnMissile],
            BeamType.Battlehammer => Metadata.Items[(int)ItemType.Battlehammer],
            BeamType.Imperialist => Metadata.Items[(int)ItemType.Imperialist],
            BeamType.Judicator => Metadata.Items[(int)ItemType.Judicator],
            BeamType.Magmaul => Metadata.Items[(int)ItemType.Magmaul],
            BeamType.ShockCoil => Metadata.Items[(int)ItemType.ShockCoil],
            BeamType.OmegaCannon => Metadata.Items[(int)ItemType.OmegaCannon],
            _ => null
        };
        if (model == null)
        {
            spec = null;
            return false;
        }
        spec = new ModelPreviewSpec(ModelPreviewKind.Weapon,
            beam.ToString().ToLowerInvariant(), model, 1,
            beam == BeamType.PowerBeam ? 0.75f : 1.45f);
        return true;
    }

    public static bool TryHunterDeath(Hunter hunter, SkinDefinition? skin,
        DeathEffectDefinition effect, out ModelPreviewSpec? spec,
        ArmorEffectDefinition? armorEffect = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (effect.Id == 0 || !CosmeticCatalog.BuiltIn.TryGetDeathEffect(effect.Id,
                out DeathEffectDefinition official)
            || !String.Equals(official.Key, effect.Key, StringComparison.Ordinal)
            || official.Hunter is Hunter restricted && restricted != hunter
            || armorEffect != null
                && (!CosmeticCatalog.BuiltIn.TryGetArmorEffect(armorEffect.Id,
                        out ArmorEffectDefinition officialArmor)
                    || !String.Equals(officialArmor.Key, armorEffect.Key,
                        StringComparison.Ordinal))
            || !TryHunter(hunter, skin, out spec) || spec == null)
        {
            spec = null;
            return false;
        }
        spec = spec with
        {
            ArmorEffectId = armorEffect?.Id ?? 0,
            DeathEffectId = official.Id,
            DeathSampleTick = DeathStageSampleTick
        };
        return true;
    }

    public static bool TryWorkerKey(string value, out ModelPreviewSpec? spec)
    {
        spec = null;
        string[] parts = value.Split(':', 4, StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 4) return false;
        byte recolor = 0;
        if (parts.Length >= 3 && !Byte.TryParse(parts[2], out recolor)) return false;
        if (parts[0].Equals("hunter", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(parts[1], true, out Hunter hunter)
            && TryHunter(hunter, skin: null, out spec))
        {
            if (parts.Length == 4)
            {
                string marker = parts[3];
                if (marker.StartsWith("armor-", StringComparison.Ordinal))
                {
                    if (!UInt16.TryParse(marker.AsSpan("armor-".Length),
                            out ushort armorId)
                        || !CosmeticCatalog.BuiltIn.TryGetArmorEffect(armorId,
                            out ArmorEffectDefinition armor)
                        || !TryHunter(hunter, skin: null, armor, out spec))
                        return false;
                    spec = spec! with { BaseRecolor = recolor };
                    return true;
                }
                int tickMarker = marker.IndexOf("-t", StringComparison.Ordinal);
                int armorMarker = marker.IndexOf("-a", StringComparison.Ordinal);
                if (!marker.StartsWith("death-", StringComparison.Ordinal)
                    || tickMarker <= "death-".Length
                    || armorMarker >= 0 && armorMarker <= tickMarker + 2)
                    return false;
                ReadOnlySpan<char> tickValue = armorMarker < 0
                    ? marker.AsSpan(tickMarker + 2)
                    : marker.AsSpan(tickMarker + 2, armorMarker - tickMarker - 2);
                if (!UInt16.TryParse(marker.AsSpan("death-".Length,
                        tickMarker - "death-".Length), out ushort deathId)
                    || !UInt32.TryParse(tickValue, out uint sampleTick)
                    || sampleTick != DeathStageSampleTick
                    || !CosmeticCatalog.BuiltIn.TryGetDeathEffect(deathId,
                        out DeathEffectDefinition effect)
                    || !TryHunterDeath(hunter, skin: null, effect, out spec))
                    return false;
                if (armorMarker >= 0)
                {
                    if (armorMarker <= tickMarker + 2
                        || !UInt16.TryParse(marker.AsSpan(armorMarker + 2),
                            out ushort armorId)
                        || !CosmeticCatalog.BuiltIn.TryGetArmorEffect(armorId,
                            out ArmorEffectDefinition armor))
                        return false;
                    spec = spec! with { ArmorEffectId = armor.Id };
                }
            }
            spec = spec! with { BaseRecolor = recolor };
            return true;
        }
        return parts[0].Equals("weapon", StringComparison.OrdinalIgnoreCase)
            && parts.Length == 2
            && Enum.TryParse(parts[1], true, out BeamType beam) && TryWeapon(beam, out spec);
    }

    public static string PathFor(ModelPreviewSpec spec, string contentVersion,
        string contentHash)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{contentVersion}|{contentHash}|model-preview-v{RendererVersion}")))
            .ToLowerInvariant()[..16];
        string kind = spec.Kind == ModelPreviewKind.Hunter ? "hunters" : "weapons";
        string variant = spec.Kind == ModelPreviewKind.Hunter
            ? $"-r{spec.BaseRecolor}" + (spec.DeathEffectId == 0
                ? spec.ArmorEffectId == 0 ? "" : $"-a{spec.ArmorEffectId}"
                : $"-d{spec.DeathEffectId}-t{spec.DeathSampleTick}"
                    + (spec.ArmorEffectId == 0 ? "" : $"-a{spec.ArmorEffectId}")) : "";
        return Path.Combine(GameFiles.Root, "cache", "previews", kind,
            $"{spec.Key}{variant}-v{RendererVersion}-{identity}.png");
    }

    public static string CurrentPath(ModelPreviewSpec spec)
    {
        (string version, string hash) = ContentEnvironment.GetContentIdentity();
        return PathFor(spec, version, hash);
    }
}

/// <summary>Structural validation for generated PNG cache entries.</summary>
internal static class ModelPreviewFile
{
    private static ReadOnlySpan<byte> Signature =>
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool IsUsable(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 57 || !data.AsSpan(0, 8).SequenceEqual(Signature)) return false;
            int offset = 8;
            bool hasHeader = false;
            bool hasPixels = false;
            bool hasEnd = false;
            while (offset <= data.Length - 12)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
                if (length > Int32.MaxValue || offset + 12L + length > data.Length) return false;
                ReadOnlySpan<byte> type = data.AsSpan(offset + 4, 4);
                ReadOnlySpan<byte> payload = data.AsSpan(offset + 8, (int)length);
                if (type.SequenceEqual("IHDR"u8))
                {
                    if (hasHeader || length != 13) return false;
                    int width = BinaryPrimitives.ReadInt32BigEndian(payload[..4]);
                    int height = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(4, 4));
                    if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
                        return false;
                    hasHeader = true;
                }
                else if (type.SequenceEqual("IDAT"u8)) hasPixels |= length > 0;
                else if (type.SequenceEqual("IEND"u8))
                {
                    if (length != 0) return false;
                    hasEnd = true;
                    offset += 12;
                    break;
                }
                offset += checked((int)length + 12);
            }
            return hasHeader && hasPixels && hasEnd && offset == data.Length;
        }
        catch { return false; }
    }
}

/// <summary>
/// Starts one isolated renderer worker per missing preview. The launcher never
/// creates a native render host on its UI thread, and concurrent requests for
/// the same cache entry share one bounded operation.
/// </summary>
#if ANDROID
internal static class ModelPreviewGenerator
{
    internal const string InternalWorkerFlag = "internal-preview-worker";

    // Android cannot start a copy of its APK. A device renderer must install
    // this hook once it can provide an isolated GL context and lifecycle.
    internal static Func<ModelPreviewSpec, CancellationToken, Task<string?>>? PlatformEnsureAsync;

    public static Task<string?> EnsureAsync(ModelPreviewSpec spec,
        CancellationToken cancellationToken = default)
    {
        string path;
        try { path = ModelPreviewCatalog.CurrentPath(spec); }
        catch { return Task.FromResult<string?>(null); }
        if (ModelPreviewFile.IsUsable(path)) return Task.FromResult<string?>(path);
        return PlatformEnsureAsync?.Invoke(spec, cancellationToken)
            ?? Task.FromResult<string?>(null);
    }

    internal static bool IsUsable(string path) => ModelPreviewFile.IsUsable(path);
}
#else
internal static class ModelPreviewGenerator
{
    internal const string InternalWorkerFlag = "internal-preview-worker";

    private static readonly TimeSpan _workerTimeout = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim _workers = new(2, 2);
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _pending
        = new(StringComparer.Ordinal);

    public static async Task<string?> EnsureAsync(ModelPreviewSpec spec,
        CancellationToken cancellationToken = default)
    {
        string path;
        try { path = ModelPreviewCatalog.CurrentPath(spec); }
        catch { return null; }
        if (IsUsable(path)) return path;
        if (Environment.ProcessPath == null) return null;

        Lazy<Task<bool>> operation = _pending.GetOrAdd(path, _ => new Lazy<Task<bool>>(
            () => GenerateAsync(spec, path), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            bool generated = await operation.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return generated && IsUsable(path) ? path : null;
        }
        finally
        {
            if (operation.IsValueCreated && operation.Value.IsCompleted)
                _pending.TryRemove(new KeyValuePair<string, Lazy<Task<bool>>>(path, operation));
        }
    }

    internal static bool IsUsable(string path)
    {
        try
        {
            return ModelPreviewFile.IsUsable(path);
        }
        catch { return false; }
    }

    private static async Task<bool> GenerateAsync(ModelPreviewSpec spec, string path)
    {
        await _workers.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsUsable(path)) return true;
            string? executable = Environment.ProcessPath;
            if (executable == null) return false;
            string? entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            string? entryAssembly = entryAssemblyName == null
                ? null
                : Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.dll");
            ProcessStartInfo start = CreateWorkerStartInfo(spec, executable,
                entryAssembly);
            using Process? process = Process.Start(start);
            if (process == null) return false;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(_workerTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                Console.Error.WriteLine($"[previews] {spec.WorkerKey}: renderer exceeded "
                    + $"{_workerTimeout.TotalSeconds:0}s; terminating it");
                try { process.Kill(entireProcessTree: true); }
                catch { }
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false); }
                catch { }
            }
            string output = "";
            string error = "";
            try
            {
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
                output = stdout.Result;
                error = stderr.Result;
            }
            catch { }
            if (output.Length > 0) Console.Write(output);
            if (error.Length > 0) Console.Error.Write(error);
            return process.HasExited && process.ExitCode == 0 && IsUsable(path);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] {spec.WorkerKey}: {error.Message}");
            return false;
        }
        finally { _workers.Release(); }
    }

    internal static ProcessStartInfo CreateWorkerStartInfo(ModelPreviewSpec spec,
        string executable, string? entryAssembly)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = GameFiles.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (String.IsNullOrWhiteSpace(entryAssembly))
                throw new InvalidOperationException("Cannot locate the managed entry assembly.");
            start.ArgumentList.Add(entryAssembly);
        }
        start.ArgumentList.Add("-modelpreview");
        start.ArgumentList.Add(spec.WorkerKey);
        start.ArgumentList.Add($"--{InternalWorkerFlag}");
        start.ArgumentList.Add($"--renderer={RenderBackendSelection.ToCliValue(RenderBackendSelection.Current)}");
        start.ArgumentList.Add("-size");
        start.ArgumentList.Add($"{ModelPreviewCatalog.Width}x{ModelPreviewCatalog.Height}");
        return start;
    }
}

/// <summary>Renderer-worker lifecycle for one actual local game model.</summary>
#if CLIENT_DESKTOP_HOST
internal sealed class ModelPreviewCapture : IRenderToolClient
{
    private readonly IRenderToolHost _host;
    private readonly ModelPreviewSpec _spec;
    private readonly string _outputPath;
    private int _frames = 2;
    private int _attempts;
    private bool _saved;
    private readonly Vector3 _cameraPosition;
    private readonly Vector3 _cameraTarget;

    private ModelPreviewCapture(ModelPreviewSpec spec, string outputPath, IRenderToolHost host)
    {
        _spec = spec;
        _outputPath = outputPath;
        _host = host;
        Scene = new Scene(features: ClientMatchFeatures.Capture());
        Presentation = host.CreatePresentation(Scene);
        ModelInstance source = Read.GetModelInstance(spec.ModelName);
        EntityBase model = Presentation.AddModel(spec.ModelName);
        model.Recolor = spec.BaseRecolor;
        Vector3 scale = source.Model.Scale * spec.Scale;
        model.Scale = new Vector3(spec.Scale);
        model.Rotation = new Vector3(0, MathF.PI, 0);
        Vector3 min = new(Single.MaxValue);
        Vector3 max = new(Single.MinValue);
        foreach (Node node in source.Model.Nodes)
        {
            if (node.Bounds.Length < 6) continue;
            min = Vector3.ComponentMin(min,
                new Vector3(node.Bounds[0], node.Bounds[1], node.Bounds[2]) * scale);
            max = Vector3.ComponentMax(max,
                new Vector3(node.Bounds[3], node.Bounds[4], node.Bounds[5]) * scale);
        }
        Vector3 center = (min + max) / 2;
        _cameraTarget = new Vector3(-center.X, center.Y, -center.Z);
        float halfExtent = Math.Max((max.X - min.X) / 2, (max.Y - min.Y) / 2);
        halfExtent = Math.Max(halfExtent, (max.Z - min.Z) / 2);
        if (!Single.IsFinite(halfExtent) || halfExtent < 0.01f) halfExtent = 1;
        float distance = halfExtent * spec.FrameMargin
            / MathF.Tan(MathHelper.DegreesToRadians(27.5f));
        _cameraPosition = _cameraTarget + new Vector3(0, 0, Math.Max(0.1f, distance));
        Console.WriteLine($"[previews] {spec.WorkerKey} bounds {min}..{max}, "
            + $"camera {_cameraPosition} -> {_cameraTarget}");
    }

    public Scene Scene { get; }
    public ScenePresentation Presentation { get; }
    public bool Succeeded => _saved;

    public void OnLoad()
    {
        Presentation.Size = _host.Size;
        Presentation.OnLoad();
        Presentation.OnResize();
    }

    public void OnFrame()
    {
        Presentation.OnUpdateFrame();
        Presentation.SetPreviewCamera(_cameraPosition, _cameraTarget);
        if (_frames-- > 0) return;
        var request = new RenderToolCapture(CaptureTargetKind.SceneTarget, _outputPath);
        RenderToolFrameResult frame = _host.Render(Presentation, request);
        for (int i = 0; i < frame.Captures.Count; i++) OnCapture(frame.Captures[i]);
        if (frame.Submitted) _attempts++;
        if (!frame.Submitted || _saved || _attempts >= 3) _host.Close();
        else _frames = 1;
    }

    public void OnCapture(RenderCaptureResult capture)
    {
        if (_saved || capture.Target != CaptureTargetKind.SceneTarget) return;
        _saved = SaveCandidate(capture);
        if (_saved) _host.Close();
    }

    private bool SaveCandidate(RenderCaptureResult capture)
    {
        double lit = RenderToolCaptureSupport.NonBlackFraction(capture);
        Console.WriteLine($"[previews] {_spec.WorkerKey} candidate {lit * 100:0.00}% lit");
        if (lit < 0.0025) return false;
        Action<byte[], int, int, string>? writer = ScreenCapture.PngWriter;
        if (writer == null) return false;
        string? directory = Path.GetDirectoryName(_outputPath);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        writer(capture.CopyBytes(), capture.Width, capture.Height, _outputPath);
        return true;
    }

    public void OnClosing() => Presentation.DoCleanup();

    public static bool Capture(ModelPreviewSpec spec, int width, int height)
    {
        string path;
        try { path = ModelPreviewCatalog.CurrentPath(spec); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] identity failed: {error.Message}");
            return false;
        }
        string temporary = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            ThumbnailMode.Enter();
            // Standalone models have no room light set. Render their authored
            // textures directly instead of producing a black silhouette with
            // only emissive material fragments visible.
            RenderOptions.Lighting = false;
            RenderOptions.Fog = false;
            using IRenderToolHost host = RenderToolHostFactory.Create(
                new Vector2i(width, height), $"{Branding.Name} model preview",
                visible: false, presentable: false);
            var capture = new ModelPreviewCapture(spec, temporary, host);
            host.Run(capture);
            if (!capture.Succeeded) return false;
            string? directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] {spec.WorkerKey}: {error}");
            return false;
        }
        finally
        {
            ThumbnailMode.Exit();
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }
}
#endif
#endif
