#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Render;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>One bounded preview request/decoder; late selections cannot publish into the current card.</summary>
internal sealed class NativeCommunityPreview : IDisposable
{
    internal readonly record struct View(long Revision, string Image, string Status);
    private readonly INativeCommunityPreviewSource? _source;
    private readonly string _directory;
    private readonly RmlUiMapPreviewCache _images;
    private Task<Result>? _download;
    private CancellationTokenSource? _cancel;
    private string _selected = "", _requested = "", _png = "", _status = "", _image = "";
    private long _revision, _decodeStarted;
    private bool _disposed;
    private readonly record struct Result(string Hash, string Png, string Status);

    internal NativeCommunityPreview(INativeCommunityPreviewSource? source, string directory)
    {
        _source = source; _directory = Path.GetFullPath(directory);
        _images = new(Path.Combine(_directory, "decoded"));
    }

    internal View Read(NativeCommunityEntry? selected)
    {
        if (_disposed) return new(_revision, "", "");
        string hash = selected is { Installed: false } entry
            && NativeCommunityInstallController.IsVerifiedSelection(entry) ? entry.LatestHash : "";
        if (hash != _selected)
        {
            _selected = hash; _png = ""; _requested = "";
            try { _cancel?.Cancel(); } catch (ObjectDisposedException) { }
            _cancel = null;
            Set("", hash.Length == 0 ? "" : "LOADING EXACT REVISION PREVIEW");
            _images.Read("");
        }
        if (_download is { IsCompleted: true } work)
        {
            _download = null;
            Result result = work.GetAwaiter().GetResult();
            if (result.Hash == _selected)
            {
                _png = result.Png;
                _decodeStarted = Environment.TickCount64;
                Set("", result.Status);
            }
        }
        if (hash.Length > 0 && _download == null && _requested != hash)
        {
            _requested = hash;
            if (_source == null) Set("", "CREATOR PREVIEW NOT AVAILABLE");
            else
            {
                var cancel = _cancel = new CancellationTokenSource();
                _download = Task.Run(() => Download(hash, cancel));
            }
        }
        string image = _images.Read(_png);
        if (image.Length > 0) Set(image, "PREVIEW OF THE EXACT PUBLISHED REVISION");
        else if (_png.Length > 0 && Environment.TickCount64 - _decodeStarted > 5000)
            Set("", "CREATOR PREVIEW COULD NOT BE DECODED // RETRY WITH REFRESH");
        return new(_revision, _image, _status);
    }

    private async Task<Result> Download(string hash, CancellationTokenSource cancel)
    {
        try
        {
            byte[]? bytes = await _source!.ReadPreviewAsync(hash, cancel.Token).ConfigureAwait(false);
            cancel.Token.ThrowIfCancellationRequested();
            if (bytes == null) return new(hash, "", "CREATOR HAS NOT ADDED A COMPATIBLE PNG PREVIEW");
            if (bytes.Length is < 33 or > 1048576 || !bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                return new(hash, "", "CREATOR PREVIEW IS NOT A SUPPORTED PNG");
            uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13
                || !bytes.AsSpan(12, 4).SequenceEqual(new byte[] { 73, 72, 68, 82 })
                || width == 0 || height == 0 || width > 16384 || height > 16384
                || (ulong)width * height * 4 > 64 * 1024 * 1024)
                return new(hash, "", "CREATOR PREVIEW IS NOT A SUPPORTED PNG");
            Directory.CreateDirectory(_directory);
            string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string path = Path.Combine(_directory, digest + ".png");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return new(hash, path, "DECODING CREATOR PREVIEW");
        }
        catch (OperationCanceledException) { return new(hash, "", "PREVIEW REQUEST RETIRED"); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { return new(hash, "", "PREVIEW UNAVAILABLE // RETRY WITH REFRESH"); }
        finally { cancel.Dispose(); }
    }

    private void Set(string image, string status)
    {
        if (_image == image && _status == status) return;
        _image = image; _status = status; _revision++;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { _cancel?.Cancel(); } catch (ObjectDisposedException) { }
        _images.Dispose(); _selected = _png = _image = "";
    }
}
#endif
