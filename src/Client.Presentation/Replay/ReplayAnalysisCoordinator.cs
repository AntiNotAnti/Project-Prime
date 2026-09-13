using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

public sealed record ReplayAnalysisResult(ReplayLibraryMetadata Library,
    ReplayHighlightMetadata Highlights);

public static class ReplayApplicationPaths
{
    public static string DataRoot
    {
        get
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
            return Path.Combine(Path.GetFullPath(root), "Project Prime");
        }
    }

    public static string CacheRoot => Path.Combine(DataRoot, "cache");
    public static string UserDataRoot => Path.Combine(DataRoot, "user");
}

/// <summary>
/// Bounds replay analysis, deduplicates the selected replay and cancels stale
/// selection work. Results are immutable and retained for the controller's
/// lifetime.
/// </summary>
public sealed class ReplayAnalysisCoordinator : IDisposable
{
    public const int DefaultMaximumConcurrency = 1;
    private readonly ReplayHighlightMetadataService _highlights;
    private readonly ReplayLibraryMetadataService _library;
    private readonly SemaphoreSlim _gate;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(
        StringComparer.Ordinal);
    private CancellationTokenSource? _selection;
    private string? _selectionPath;
    private Task<ReplayAnalysisResult>? _selectionTask;
    private int _disposed;

    public ReplayAnalysisCoordinator(ReplayHighlightMetadataService? highlights = null,
        ReplayLibraryMetadataService? library = null,
        int maximumConcurrency = DefaultMaximumConcurrency)
    {
        if (maximumConcurrency is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _highlights = highlights ?? new ReplayHighlightMetadataService(
            ReplayApplicationPaths.DataRoot);
        _library = library ?? new ReplayLibraryMetadataService();
        _gate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public Task<ReplayAnalysisResult> SelectAsync(string replayPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayPath);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        string path = Path.GetFullPath(replayPath);
        lock (_sync)
        {
            if (_cache.TryGetValue(path, out CacheEntry? cached)
                && cached.Matches(path))
                return Task.FromResult(cached.Result);
            _cache.Remove(path);
            if (String.Equals(_selectionPath, path, StringComparison.Ordinal)
                && _selectionTask is { IsCanceled: false, IsFaulted: false } existing)
                return existing.WaitAsync(cancellationToken);

            _selection?.Cancel();
            _selection?.Dispose();
            _selection = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token, cancellationToken);
            _selectionPath = path;
            _selectionTask = AnalyzeAsync(path, _selection.Token);
            return _selectionTask;
        }
    }

    public bool TryGetCached(string replayPath, out ReplayAnalysisResult? result)
    {
        string path = Path.GetFullPath(replayPath);
        lock (_sync)
        {
            if (_cache.TryGetValue(path, out CacheEntry? cached)
                && cached.Matches(path))
            {
                result = cached.Result;
                return true;
            }
            _cache.Remove(path);
            result = null;
            return false;
        }
    }

    public void CancelSelection()
    {
        lock (_sync)
        {
            _selection?.Cancel();
            _selection?.Dispose();
            _selection = null;
            _selectionPath = null;
            _selectionTask = null;
        }
    }

    private async Task<ReplayAnalysisResult> AnalyzeAsync(string path,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CacheEntry.SourceStamp before = CacheEntry.ReadStamp(path);
            ReplayHighlightMetadata highlights = await Task.Run(
                () => _highlights.Get(path, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ReplayLibraryMetadata library = await Task.Run(
                () => _library.GetOrCreate(path, highlights, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (CacheEntry.ReadStamp(path) != before)
                throw new IOException("Replay changed while it was being analyzed.");
            var result = new ReplayAnalysisResult(library, highlights);
            lock (_sync) _cache[path] = CacheEntry.Create(path, result);
            return result;
        }
        finally
        {
            _gate.Release();
            lock (_sync)
            {
                if (String.Equals(_selectionPath, path, StringComparison.Ordinal))
                {
                    _selectionPath = null;
                    _selectionTask = null;
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelSelection();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed record CacheEntry(long Length, long WriteUtcTicks,
        ReplayAnalysisResult Result)
    {
        internal readonly record struct SourceStamp(long Length, long WriteUtcTicks);

        internal static SourceStamp ReadStamp(string path)
        {
            var file = new FileInfo(path);
            return new(file.Exists ? file.Length : -1,
                file.Exists ? file.LastWriteTimeUtc.Ticks : 0);
        }

        internal static CacheEntry Create(string path, ReplayAnalysisResult result)
        {
            SourceStamp stamp = ReadStamp(path);
            return new(stamp.Length, stamp.WriteUtcTicks, result);
        }

        internal bool Matches(string path)
        {
            SourceStamp stamp = ReadStamp(path);
            return stamp.Length >= 0 && stamp.Length == Length
                && stamp.WriteUtcTicks == WriteUtcTicks;
        }
    }
}
