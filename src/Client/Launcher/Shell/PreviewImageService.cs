using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace MphRead.Mods.Launcher.Gui;

public sealed record PrimePreviewImage(string Key, string Path, byte[] Data);

/// <summary>
/// Bounded local preview ownership. It only reads paths supplied by the local
/// asset pipeline; it never downloads or invents an image.
/// </summary>
public sealed class PreviewImageService : IDisposable
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, PrimePreviewImage> _images = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private int _disposed;

    public PreviewImageService(int capacity = 24)
    {
        if (capacity is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count { get { lock (_gate) return _images.Count; } }

    public async Task<PrimePreviewImage?> LoadLocalAsync(string key, string path,
        CancellationToken cancellationToken = default)
    {
        if (key is not { Length: > 0 and <= 256 }) throw new ArgumentException("Invalid preview key.", nameof(key));
        if (path is not { Length: > 0 }) return null;
        lock (_gate)
        {
            if (_images.TryGetValue(key, out PrimePreviewImage? cached))
            {
                Touch(key);
                return cached;
            }
        }
        if (Volatile.Read(ref _disposed) != 0) return null;
        byte[] data;
        try { data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if (data.Length == 0 || data.Length > 64 * 1024 * 1024) return null;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var decoded = new Bitmap(stream);
            if (decoded.PixelSize.Width <= 0 || decoded.PixelSize.Height <= 0) return null;
        }
        catch { return null; }
        var image = new PrimePreviewImage(key, Path.GetFullPath(path), data);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return null;
            _images[key] = image;
            Touch(key);
            while (_order.Count > _capacity)
            {
                string last = _order.Last!.Value;
                _order.RemoveLast();
                _images.Remove(last);
            }
        }
        return image;
    }

    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (!_images.Remove(key)) return false;
            LinkedListNode<string>? node = _order.Find(key);
            if (node != null) _order.Remove(node);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) { _images.Clear(); _order.Clear(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Clear();
    }

    private void Touch(string key)
    {
        LinkedListNode<string>? node = _order.Find(key);
        if (node != null) _order.Remove(node);
        _order.AddFirst(key);
    }
}

public sealed class MapPreviewService
{
    private readonly PreviewImageService _images;
    public MapPreviewService(PreviewImageService? images = null) => _images = images ?? new PreviewImageService();
    public Task<PrimePreviewImage?> LoadAsync(string roomKey, CancellationToken cancellationToken = default)
        => _images.LoadLocalAsync(roomKey, ThumbnailGenerator.PathFor(roomKey), cancellationToken);
}

public sealed class HunterPreviewService
{
    private readonly PreviewImageService _images;
    public HunterPreviewService(PreviewImageService? images = null) => _images = images ?? new PreviewImageService();
    public async Task<PrimePreviewImage?> LoadAsync(Hunter hunter, CancellationToken cancellationToken = default)
    {
        if (!ModelPreviewCatalog.TryHunter(hunter, out ModelPreviewSpec? spec) || spec == null)
            return null;
        return await GeneratedPreviewLoader.LoadAsync(_images, spec, cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class WeaponPreviewService
{
    private readonly PreviewImageService _images;
    public WeaponPreviewService(PreviewImageService? images = null) => _images = images ?? new PreviewImageService();
    public async Task<PrimePreviewImage?> LoadAsync(BeamType beam,
        CancellationToken cancellationToken = default)
    {
        if (!ModelPreviewCatalog.TryWeapon(beam, out ModelPreviewSpec? spec) || spec == null)
            return null;
        return await GeneratedPreviewLoader.LoadAsync(_images, spec, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal static class GeneratedPreviewLoader
{
    public static async Task<PrimePreviewImage?> LoadAsync(PreviewImageService images,
        ModelPreviewSpec spec, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string? path = await ModelPreviewGenerator.EnsureAsync(spec, cancellationToken)
                .ConfigureAwait(false);
            if (path == null) return null;
            string key = $"model:{spec.WorkerKey}:{path}";
            PrimePreviewImage? image = await images.LoadLocalAsync(key, path, cancellationToken)
                .ConfigureAwait(false);
            if (image != null) return image;

            // The generator owns this exact content-addressed path. If the UI
            // decoder rejects it, remove it once so EnsureAsync can rebuild it.
            images.Remove(key);
            try { File.Delete(path); }
            catch { return null; }
        }
        return null;
    }
}
