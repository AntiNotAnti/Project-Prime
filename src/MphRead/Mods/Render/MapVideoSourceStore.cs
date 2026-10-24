using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Render;

internal readonly record struct MapVideoPlaybackKey(Guid Map, string Source, string Storage,
    int Width, int Height, int Rate, int Frames, bool Loop, string Group)
{
    internal static MapVideoPlaybackKey Create(MapDefinition definition, MapVideoTexture video)
        => new(definition.MapId, video.Source, definition.BundlePath ?? definition.BaseDirectory ?? "",
            video.Width, video.Height, video.FramesPerSecond, video.FrameCount, video.Loop, video.PlaybackGroup);
}

/// <summary>Copies each encoded source once, on a decoder worker, without allocating the complete MP4.</summary>
internal sealed class MapVideoSourceStore
{
    private readonly ConcurrentDictionary<string, Lazy<string>> _sources = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ProjectPrime-Video-v15", Guid.NewGuid().ToString("N"));
    internal int SourceCount => _sources.Count;

    internal string Get(MapDefinition definition, MapVideoTexture video)
    {
        string identity = definition.MapId.ToString("N") + "|" + (definition.BundlePath ?? definition.BaseDirectory) + "|" + video.Source;
        return _sources.GetOrAdd(identity, _ => new Lazy<string>(() => Extract(definition, video, identity),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private string Extract(MapDefinition definition, MapVideoTexture video, string identity)
    {
        _stop.Token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);
        string target = Path.Combine(DirectoryPath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".mp4");
        using (Stream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            MapAssets.CopyTo(definition, video.Source, output, _stop.Token);
        return target;
    }

    internal void DisposeAfter(Task[] workers)
    {
        _stop.Cancel();
        _ = Task.Run(async () =>
        {
            try { await Task.WhenAll(workers).ConfigureAwait(false); } catch { }
            for (int i = 0; i < 10; i++)
            {
                try { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); break; }
                catch (IOException) { await Task.Delay(200).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) { break; }
            }
            _sources.Clear(); _stop.Dispose();
        });
    }
}
