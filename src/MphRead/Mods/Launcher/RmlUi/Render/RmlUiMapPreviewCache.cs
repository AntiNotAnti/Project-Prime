#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ReFuel.Stb;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Converts the existing local PNG thumbnails to the native renderer's TGA
/// format. File IO and decoding stay off the compositor thread. A result is
/// published only for the requested, unchanged source file; no map is rendered
/// or downloaded here.
/// </summary>
internal sealed class RmlUiMapPreviewCache : IDisposable
{
    private const int MaxSourceBytes = 16 * 1024 * 1024;
    private const int MaxTextureBytes = 64 * 1024 * 1024;
    private readonly string _directory;
    private readonly CancellationTokenSource _lifetime = new();
    private Task<Result>? _work;
    private string _requested = "", _ready = "";
    private Stamp _observed;
    private long _nextPoll;
    private bool _disposed;

    private readonly record struct Stamp(bool Exists, long Length, long Modified);
    private readonly record struct Result(string Source, Stamp Stamp, string Image);

    internal RmlUiMapPreviewCache(string directory) => _directory = Path.GetFullPath(directory);

    // Called by the owner thread. Passing an empty path retires the visible map.
    internal string Read(string source)
    {
        if (_disposed) return "";
        if (!String.Equals(source, _requested, StringComparison.Ordinal))
        {
            _requested = source;
            _observed = default;
            _ready = "";
            _nextPoll = 0;
        }
        long now = Environment.TickCount64;
        if (_work is { IsCompleted: true } work)
        {
            _work = null;
            // Convert catches failures, so observing completion cannot leak an
            // unobserved IO exception into a later UI frame.
            Result result = work.GetAwaiter().GetResult();
            if (result.Source == _requested && result.Stamp == Inspect(_requested))
                _ready = result.Image;
        }
        if (now < _nextPoll) return _ready;
        _nextPoll = now + 1000;
        Stamp stamp = Inspect(_requested);
        if (stamp != _observed)
        {
            _observed = stamp;
            _ready = "";
        }
        // One decoder at a time, including rapid map changes. A stale worker
        // cannot publish and the newest request runs after it has retired.
        if (_work == null && stamp.Exists && _ready.Length == 0)
        {
            string requested = _requested;
            _work = Task.Run(() => Convert(requested, stamp, _lifetime.Token));
        }
        return _ready;
    }

    private static Stamp Inspect(string path)
    {
        try
        {
            if (String.IsNullOrEmpty(path)) return default;
            var info = new FileInfo(path);
            return info.Exists && info.LinkTarget == null && info.Length is >= 33 and <= MaxSourceBytes
                ? new(true, info.Length, info.LastWriteTimeUtc.Ticks) : default;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return default; }
    }

    private unsafe Result Convert(string source, Stamp stamp, CancellationToken token)
    {
        string? temporary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (Inspect(source) != stamp) return new(source, stamp, "");
            byte[] bytes;
            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != stamp.Length) return new(source, stamp, "");
                bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
            }
            ReadDimensions(bytes, out int width, out int height);
            token.ThrowIfCancellationRequested();
            using var input = new MemoryStream(bytes, writable: false);
            using StbImage image = StbImage.Load(input, StbiImageFormat.Rgba);
            if (image.Width != width || image.Height != height || image.ImagePointer == IntPtr.Zero)
                throw new InvalidDataException("Thumbnail dimensions changed during decoding.");
            // STB expands the requested surface to RGBA, but this provider's
            // Format/AsSpan length still describe the PNG's original channels.
            // Actual renderer thumbnails are RGB; use the requested surface's
            // bounded dimensions rather than truncating it to three channels.
            byte[] tga = EncodeTga(new ReadOnlySpan<byte>((void*)image.ImagePointer,
                checked(width * height * 4)), width, height);
            token.ThrowIfCancellationRequested();
            if (Inspect(source) != stamp) return new(source, stamp, "");
            string name = System.Convert.ToHexString(SHA256.HashData(tga)).ToLowerInvariant() + ".tga";
            Directory.CreateDirectory(_directory);
            string destination = Path.Combine(_directory, name);
            if (!File.Exists(destination))
            {
                temporary = Path.Combine(_directory, ".preview-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(tga);
                    output.Flush(flushToDisk: true);
                }
                token.ThrowIfCancellationRequested();
                try { File.Move(temporary, destination, overwrite: false); temporary = null; }
                catch (IOException) when (File.Exists(destination)) { }
            }
            var cached = new FileInfo(destination);
            if (cached.LinkTarget != null || cached.Length != tga.Length
                || !File.ReadAllBytes(destination).AsSpan().SequenceEqual(tga))
                throw new InvalidDataException("Thumbnail cache content does not match its digest.");
            token.ThrowIfCancellationRequested();
            return Inspect(source) == stamp ? new(source, stamp, destination) : new(source, stamp, "");
        }
        // Decoder/provider failures are cosmetic. Fatal process failures still
        // propagate, just as they do for the native presentation boundary.
        catch (Exception error) when (error is not OutOfMemoryException
            and not StackOverflowException and not AccessViolationException)
        { return new(source, stamp, ""); }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static void ReadDimensions(ReadOnlySpan<byte> png, out int width, out int height)
    {
        if (png.Length < 33 || png.Length > MaxSourceBytes
            || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(png[8..]) != 13
            || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Thumbnail must begin with a PNG IHDR.");
        width = BinaryPrimitives.ReadInt32BigEndian(png[16..]);
        height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        if (width is < 1 or > 4096 || height is < 1 or > 4096
            || (long)width * height * 4 + 18 > MaxTextureBytes)
            throw new InvalidDataException("Thumbnail exceeds the native texture limit.");
    }

    internal static byte[] EncodeTga(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096
            || (long)width * height * 4 + 18 > MaxTextureBytes
            || rgba.Length < (long)width * height * 4)
            throw new InvalidDataException("Invalid thumbnail pixels.");
        byte[] tga = new byte[checked(18 + width * height * 4)];
        tga[2] = 2; // Uncompressed true colour, top row first, eight alpha bits.
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
        tga[16] = 32;
        tga[17] = 0x28;
        for (int i = 0; i < width * height * 4; i += 4)
        {
            tga[18 + i] = rgba[i + 2];
            tga[19 + i] = rgba[i + 1];
            tga[20 + i] = rgba[i];
            tga[21 + i] = rgba[i + 3]; // Native texture loader premultiplies once.
        }
        return tga;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requested = _ready = "";
        _lifetime.Cancel();
        // A decoder can finish after UI retirement, but has no owner callback.
        if (_work is { } work)
            _ = work.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
        else _lifetime.Dispose();
    }
}
#endif
