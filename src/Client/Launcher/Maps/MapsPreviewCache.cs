using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Bounded preview bytes with asynchronous, off-UI-thread bitmap decoding.
/// The cache owns bytes only; every decoded bitmap is returned in a lease and
/// must be disposed by its presentation owner.
/// </summary>
internal sealed class MapsPreviewCache : IDisposable
{
    private const int MaximumPreviewBytes = 16 * 1024 * 1024;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private int _disposed;

    internal MapsPreviewCache(int capacity = 24)
    {
        if (capacity is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    internal async Task<MapsPreviewLease?> LoadAsync(string key,
        Func<CancellationToken, Task<byte[]?>> load,
        CancellationToken cancellationToken = default)
    {
        if (key is not { Length: > 0 and <= 512 })
            throw new ArgumentException("Invalid map preview key.", nameof(key));
        ArgumentNullException.ThrowIfNull(load);
        if (Volatile.Read(ref _disposed) != 0) return null;

        byte[]? cachedBytes;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out cachedBytes))
            {
                Touch(key);
            }
        }

        byte[] previewBytes;
        if (cachedBytes == null)
        {
            byte[]? loaded = await load(cancellationToken).ConfigureAwait(false);
            if (loaded is not { Length: > 0 and <= MaximumPreviewBytes }) return null;
            previewBytes = loaded;
            cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            previewBytes = cachedBytes!;
        }
        MapsPreviewLease lease;
        try
        {
            // Avalonia's Bitmap constructor performs decode work. Keep both
            // disk reads and this decode away from the UI dispatcher.
            lease = await Task.Run(() => Decode(previewBytes), CancellationToken.None)
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                lease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            lease.Dispose();
            return null;
        }

        if (CountedMissing(key))
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    _entries[key] = previewBytes!;
                    Touch(key);
                    while (_order.Count > _capacity)
                    {
                        string evicted = _order.Last!.Value;
                        _order.RemoveLast();
                        _entries.Remove(evicted);
                    }
                }
            }
        }
        return lease;
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Clear();
    }

    private bool CountedMissing(string key)
    {
        lock (_gate) return !_entries.ContainsKey(key);
    }

    private static bool HasUsableSize(byte[]? bytes)
        => bytes is { Length: > 0 and <= MaximumPreviewBytes };

    private static MapsPreviewLease Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return new MapsPreviewLease(new Bitmap(stream));
    }

    private void Touch(string key)
    {
        LinkedListNode<string>? node = _order.Find(key);
        if (node != null) _order.Remove(node);
        _order.AddFirst(key);
    }
}

/// <summary>One decoded preview owned by a single realized card.</summary>
internal sealed class MapsPreviewLease : IDisposable
{
    private Bitmap? _bitmap;

    internal MapsPreviewLease(Bitmap bitmap)
        => _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));

    internal Bitmap Bitmap
        => Volatile.Read(ref _bitmap) ?? throw new ObjectDisposedException(nameof(MapsPreviewLease));

    public void Dispose() => Interlocked.Exchange(ref _bitmap, null)?.Dispose();
}
