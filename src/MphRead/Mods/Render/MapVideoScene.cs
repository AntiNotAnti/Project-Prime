#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;
using OpenTK.Graphics.OpenGL;

namespace MphRead;

public partial class Scene
{
    private sealed class VideoChannel
    {
        internal required MapVideoPlaybackKey Key;
        internal required MapVideoTexture Metadata;
        internal required MapVideoTexturePlayer Player;
        internal required long FixedBytes;
        internal readonly Dictionary<int, MaterialAssetKey> Bindings = new();
        internal readonly HashSet<int> Sampled = new();
        internal bool Failed;
        internal bool Retired;
    }
    private readonly Dictionary<MapVideoPlaybackKey, VideoChannel> _mapVideoChannels = new();
    private readonly Dictionary<int, VideoChannel> _mapVideoBindings = new();
    private readonly List<(Task Completion, long Bytes, long CodecBytes)> _retiringVideos = new();
    private readonly Dictionary<int, (MaterialAssetKey Key, MapDefinition Definition, MapVideoTexture Video)> _pendingMapVideos = new();
    private MapVideoSourceStore? _mapVideoSources;
    private readonly Guid _videoDiagnosticScene = Guid.NewGuid();
    private long _videoFrameResidentBytes;
    private int _photographicVideoUniform = -1;
    private double _measuredVideoCpuCores;
    private double _previousVideoCpuSeconds;
    private readonly Stopwatch _videoStatisticsClock = Stopwatch.StartNew();
    private bool _uploadedVideoTextures = Mods.RenderOptions.VideoTextures;
    private int _uploadedVideoMemory = Mods.RenderOptions.VideoTextureMemoryMiB;
    internal int UniqueMapVideoCount => _mapVideoChannels.Count;
    internal int MapVideoBindingCount => _mapVideoBindings.Count;
    internal long MapVideoFrameResidentBytes => _videoFrameResidentBytes;
    internal int ExtractedMapVideoSourceCount => _mapVideoSources?.SourceCount ?? 0;
    private long VideoDecoderResidentEstimate => _mapVideoChannels.Values.Where(c => !c.Retired)
        .Sum(c => Math.Max(MapVideoDecoder.InitialCodecMemoryReserve, c.Player.Decoder.WorkingSetBytes))
        + _retiringVideos.Where(v => !v.Completion.IsCompleted).Sum(v => v.CodecBytes);

    private long VideoFrameBudgetBytes()
    {
        long configured = (long)Mods.RenderOptions.VideoTextureMemoryMiB * 1024 * 1024;
        long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return available > 0 ? Math.Min(configured, Math.Max(32L * 1024 * 1024, available / 4)) : configured;
    }

    private void CollectRetiredVideos()
    {
        for (int i = _retiringVideos.Count - 1; i >= 0; i--)
            if (_retiringVideos[i].Completion.IsCompleted)
            { _videoFrameResidentBytes = Math.Max(0, _videoFrameResidentBytes - _retiringVideos[i].Bytes); _retiringVideos.RemoveAt(i); }
    }

    private void DetachMapVideoTexture(int binding)
    {
        _pendingMapVideos.Remove(binding);
        if (!_mapVideoBindings.Remove(binding, out VideoChannel? channel)) return;
        if (channel.Bindings.Remove(binding, out MaterialAssetKey released))
            MapVideoDiagnostics.Report(_videoDiagnosticScene, binding, released, channel.Metadata,
                "released", "material-disposal", "Material detached; remaining shared references=" + channel.Bindings.Count + ".");
        channel.Sampled.Remove(binding);
        if (channel.Bindings.Count != 0) return;
        _mapVideoChannels.Remove(channel.Key);
        RetireVideoChannel(channel);
        CollectRetiredVideos();
    }

    private void RetireVideoChannel(VideoChannel channel)
    {
        if (channel.Retired) return;
        channel.Retired = true;
        long codecBytes = Math.Max(MapVideoDecoder.InitialCodecMemoryReserve, channel.Player.Decoder.WorkingSetBytes);
        channel.Player.Dispose();
        _retiringVideos.Add((channel.Player.Decoder.Completion, channel.FixedBytes, codecBytes));
        ReleaseTexture(channel.Player.Binding);
    }

    private void StartMapVideoTexture(int binding, MaterialAssetKey? authored,
        MapDefinition? suppliedDefinition = null, MapVideoTexture? suppliedVideo = null)
    {
        if (authored is not { } key) { DetachMapVideoTexture(binding); return; }
        try
        {
            var source = suppliedDefinition != null && suppliedVideo != null
                ? (suppliedDefinition, suppliedVideo) : MapMaterialAssetRegistry.Video(key);
            if (source is not { } resolved) { DetachMapVideoTexture(binding); return; }
            var (definition, video) = resolved;
            MapVideoPlaybackKey playback = MapVideoPlaybackKey.Create(definition, video);
            if (_mapVideoBindings.TryGetValue(binding, out VideoChannel? previous)
                && previous.Key == playback && Mods.RenderOptions.VideoTextures) return;
            DetachMapVideoTexture(binding); CollectRetiredVideos();
            SetVideoFallback(binding);
            void Block(string stage, string detail)
            {
                if (stage is "frame-memory-admission" or "gpu-memory-admission" or "decode-cpu-admission")
                    _pendingMapVideos[binding] = (key, definition, video);
                MapVideoDiagnostics.Report(_videoDiagnosticScene, binding, key, video, "blocked", stage,
                    detail + " Deliberately untextured fallback retained."
                    + (_pendingMapVideos.ContainsKey(binding) ? " Transient resource blocks retry automatically." : ""));
            }
            if (!Mods.RenderOptions.VideoTextures) { Block("activation", "Map video textures are disabled in Graphics settings."); return; }
            if (OperatingSystem.IsAndroid()) { Block("platform", "This packaged MP4 decoder is supported by the desktop OpenGL client."); return; }
            if (!_mapVideoChannels.TryGetValue(playback, out VideoChannel? channel))
            {
                int gpuLimit = GL.GetInteger(GetPName.MaxTextureSize);
                if (video.Width > gpuLimit || video.Height > gpuLimit)
                { Block("gpu-dimensions", $"Source {video.Width}x{video.Height} exceeds GL_MAX_TEXTURE_SIZE={gpuLimit}; no resizing performed."); return; }
                long gpuBytes = 0;
                for (int w = video.Width, h = video.Height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
                { gpuBytes += (long)w * h * 4; if (w == 1 && h == 1) break; }
                long fixedBytes = checked(gpuBytes + (long)video.Width * video.Height * 3 * MapVideoDecoder.BufferCount);
                long codecBytes = VideoDecoderResidentEstimate;
                if (_videoFrameResidentBytes + codecBytes + fixedBytes + MapVideoDecoder.InitialCodecMemoryReserve > VideoFrameBudgetBytes())
                { Block("frame-memory-admission", $"Unique playback requires {fixedBytes} fixed bytes plus a provisional {MapVideoDecoder.InitialCodecMemoryReserve}-byte codec reserve; fixed resident={_videoFrameResidentBytes}, measured/reserved codecs={codecBytes}, budget={VideoFrameBudgetBytes()}. Increase Video playback memory or reduce independent video resolution/workload."); return; }
                double cpuBudget = Math.Max(1, Environment.ProcessorCount - 2) * .75;
                if (_measuredVideoCpuCores >= cpuBudget)
                { Block("decode-cpu-admission", $"Existing decoders used {_measuredVideoCpuCores:0.00} CPU cores in the last sample; admission budget={cpuBudget:0.00} cores, preserving rendering/input headroom."); return; }
                if (_worldMaterialResidentBytes + gpuBytes > TextureAssetManager.WorldMaterialMemoryBudgetBytes())
                { Block("gpu-memory-admission", $"Unique mip chain requires {gpuBytes} bytes; world resident={_worldMaterialResidentBytes}, world budget={TextureAssetManager.WorldMaterialMemoryBudgetBytes()}."); return; }
                _mapVideoSources ??= new();
                MapVideoSourceStore store = _mapVideoSources;
                int canonical = AllocateTexture();
                var player = new MapVideoTexturePlayer(canonical, video, () => store.Get(definition, video), linearColor: () => SceneLinearColorActive);
                channel = new() { Key = playback, Metadata = video, Player = player, FixedBytes = fixedBytes };
                _mapVideoChannels.Add(playback, channel);
                _videoFrameResidentBytes += fixedBytes;
                _worldMaterialTextureBytes[canonical] = gpuBytes; _worldMaterialResidentBytes += gpuBytes;
            }
            channel.Bindings[binding] = key; _mapVideoBindings[binding] = channel;
            MapVideoDiagnostics.Report(_videoDiagnosticScene, binding, key, video, "scheduled", "material-binding",
                $"Shared GPU binding={channel.Player.Binding}; unique playbacks={_mapVideoChannels.Count}; aliases={channel.Bindings.Count}; fixed bytes={channel.FixedBytes}. Static HD toggle/tier do not gate playback.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            MapVideoDiagnostics.Report(_videoDiagnosticScene, binding, key,
                suppliedVideo ?? new MapVideoTexture { Source = "unresolved" }, "failed", "material-lookup/admission", ex.Message);
        }
    }

    private void SetVideoFallback(int binding)
    {
        int active = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        GL.BindTexture(TextureTarget.Texture2D, binding);
        byte[] gray = new byte[8 * 8 * 4];
        for (int i = 0; i < gray.Length; i += 4) { gray[i] = gray[i + 1] = gray[i + 2] = 123; gray[i + 3] = 255; }
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 8, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, gray);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 3);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        InvalidateTextureSampling(binding);
        _flatColors.Remove(binding);
        GL.BindTexture(TextureTarget.Texture2D, previous);
        GL.ActiveTexture((TextureUnit)active);
    }

    private int ResolveMapVideoBinding(int binding, bool sampled = false)
    {
        if (!_mapVideoBindings.TryGetValue(binding, out VideoChannel? channel) || channel.Failed
            || channel.Player.FramesUploaded == 0) return binding;
        if (sampled && channel.Sampled.Add(binding))
            MapVideoDiagnostics.Report(_videoDiagnosticScene, binding, channel.Bindings[binding], channel.Metadata,
                "playing", "production-material-draw", $"Draw sampled shared GPU binding {channel.Player.Binding}; frame={channel.Player.PresentedIndex}.");
        return channel.Player.Binding;
    }

    private void UpdateMapVideoTextures()
    {
        CollectRetiredVideos();
        if (_uploadedVideoTextures != Mods.RenderOptions.VideoTextures || _uploadedVideoMemory != Mods.RenderOptions.VideoTextureMemoryMiB)
        {
            foreach (int binding in _mapVideoBindings.Keys.ToArray()) DetachMapVideoTexture(binding);
            foreach (var pair in _textureSources.ToArray()) StartMapVideoTexture(pair.Key, pair.Value.Authored);
            _uploadedVideoTextures = Mods.RenderOptions.VideoTextures;
            _uploadedVideoMemory = Mods.RenderOptions.VideoTextureMemoryMiB;
        }
        bool paused = this.GameState.MenuPause || this.GameState.DialogPause || Mods.PauseMenu.Open;
        foreach (VideoChannel channel in _mapVideoChannels.Values)
        {
            if (channel.Failed) continue;
            bool first = channel.Player.FramesUploaded == 0;
            bool uploaded = channel.Player.Update(paused);
            if (channel.Player.Failure is { } failed)
            {
                channel.Failed = true; RetireVideoChannel(channel);
                foreach (var pair in channel.Bindings)
                    MapVideoDiagnostics.Report(_videoDiagnosticScene, pair.Key, pair.Value, channel.Metadata,
                        "failed", channel.Player.FailureStage ?? "decode/upload", failed + " Deliberately untextured fallback retained.");
                continue;
            }
            if (uploaded)
            {
                int canonical = channel.Player.Binding;
                _modernTextureSampling[canonical] = (TextureAssetClass.World, TextureAssetChannel.Albedo,
                    true, true, 1 + (int)Math.Floor(Math.Log2(Math.Max(channel.Metadata.Width, channel.Metadata.Height))));
                _textureUploadSampling[canonical] = TextureSamplingPolicy.ResolveModern(TextureAssetClass.World, TextureAssetChannel.Albedo);
                _mipmappedTextures.Add(canonical);
                _flatColors.Remove(canonical);
                if (first)
                    foreach (var pair in channel.Bindings)
                        MapVideoDiagnostics.Report(_videoDiagnosticScene, pair.Key, pair.Value, channel.Metadata,
                            "uploaded", "gpu-upload", $"Decoder verified {channel.Metadata.Width}x{channel.Metadata.Height}; first full-color frame and mip chain uploaded to shared GPU binding {canonical}.");
            }
        }
        if (_videoStatisticsClock.Elapsed.TotalSeconds >= 5 && (_mapVideoChannels.Count > 0 || _pendingMapVideos.Count > 0))
        {
            double seconds = _videoStatisticsClock.Elapsed.TotalSeconds;
            double totalCpu = _mapVideoChannels.Values.Sum(c => c.Player.Decoder.ProcessorSeconds);
            _measuredVideoCpuCores = Math.Max(0, totalCpu - _previousVideoCpuSeconds) / seconds;
            _previousVideoCpuSeconds = totalCpu;
            _videoStatisticsClock.Restart();
            MapVideoDiagnostics.Statistics(new { uniquePlaybacks = _mapVideoChannels.Count,
                materialBindings = _mapVideoBindings.Count, extractedSources = ExtractedMapVideoSourceCount,
                fixedFrameResidentBytes = _videoFrameResidentBytes, fixedFrameBudgetBytes = VideoFrameBudgetBytes(),
                measuredOrReservedCodecBytes = VideoDecoderResidentEstimate,
                measuredDecoderCpuCores = _measuredVideoCpuCores,
                worldGpuResidentBytes = _worldMaterialResidentBytes, showTextures = _showTextures,
                channels = _mapVideoChannels.Values.Select(c => new { c.Key.Source, c.Key.Group, c.Player.Binding,
                    materialBindings = c.Bindings.Count, sampledBindings = c.Sampled.Count, c.Player.FramesUploaded,
                    c.Player.SkippedPresentationFrames, c.Player.LoopsCompleted, c.Player.Decoder.FramesDecoded,
                    c.Player.Decoder.MaximumQueued, c.Player.Decoder.WorkingSetBytes, c.Player.Decoder.MetricsFailure,
                    fixedBytes = c.FixedBytes, c.Failed }).ToArray(),
                note = "Fixed memory is exact RGB queues plus GPU mips. Codec memory is measured working set with a provisional 32 MiB minimum per independent decoder; driver/other game allocations are additional. Presentation slots are not distinct source motion frames." });
            foreach (var request in _pendingMapVideos.ToArray())
                if (_ownedTextures.Contains(request.Key))
                    StartMapVideoTexture(request.Key, request.Value.Key, request.Value.Definition, request.Value.Video);
                else _pendingMapVideos.Remove(request.Key);
        }
    }

    private void DisposeMapVideoTextures()
    {
        _pendingMapVideos.Clear();
        foreach (int binding in _mapVideoBindings.Keys.ToArray()) DetachMapVideoTexture(binding);
        Task[] workers = _retiringVideos.Select(v => v.Completion).ToArray();
        _mapVideoSources?.DisposeAfter(workers); _mapVideoSources = null;
        MapVideoDiagnostics.EndScene(_videoDiagnosticScene);
    }
}
#endif
