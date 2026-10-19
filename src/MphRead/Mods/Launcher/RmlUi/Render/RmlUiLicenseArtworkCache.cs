#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>Decodes original owned GamersCard portraits or rank-star UV crops without engine/GL caches.</summary>
internal sealed class RmlUiLicenseArtworkCache : IDisposable
{
    private readonly bool _rankArtwork;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Func<string> _root;
    private readonly Func<string, string, CancellationToken, byte[]> _decode;
    private CancellationTokenSource? _cancel;
    private Task<Result>? _work;
    private string _requestedRoot = "", _requestedModel = "", _ready = "";
    private long _epoch, _nextPoll;
    private bool _disposed;
    private readonly record struct Result(long Epoch, string Root, string Model, string Image);

    internal RmlUiLicenseArtworkCache(Func<string>? root = null,
        Func<string, string, CancellationToken, byte[]>? decode = null, bool rankArtwork = false)
    {
        _rankArtwork = rankArtwork;
        _root = root ?? (() => GameFiles.Root);
        _decode = decode ?? (rankArtwork ? DecodeOwnedRank : DecodeOwned);
    }

    // Owner-thread requests and publication only; filesystem and decoding belong to the one worker.
    internal string Read(string hunter)
    {
        Owner();
        if (_disposed) return "";
        string root;
        try { root = _root() ?? ""; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { root = ""; }
        string model = _rankArtwork ? RankModel(hunter) : PortraitModel(hunter);
        if (!String.Equals(root, _requestedRoot, StringComparison.Ordinal) || model != _requestedModel)
        {
            _cancel?.Cancel(); _epoch++;
            _requestedRoot = root; _requestedModel = model; _ready = ""; _nextPoll = 0;
        }
        if (_work is { IsCompleted: true } work)
        {
            Result result = work.GetAwaiter().GetResult(); _work = null;
            _cancel?.Dispose(); _cancel = null;
            if (result.Epoch == _epoch && result.Root == root && result.Model == model)
                _ready = result.Image;
        }
        if (_work == null && _ready.Length == 0 && root.Length != 0 && model.Length != 0 && Environment.TickCount64 >= _nextPoll)
        {
            _nextPoll = Environment.TickCount64 + 1000;
            var cancel = _cancel = new CancellationTokenSource(); long epoch = _epoch;
            _work = Task.Run(() => Convert(root, model, epoch, cancel.Token));
        }
        return _ready;
    }

    private Result Convert(string root, string model, long epoch, CancellationToken token)
    {
        string? temporary = null;
        void ValidateRequest()
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) || epoch != Interlocked.Read(ref _epoch)
                || !String.Equals(GameFiles.Root, root, StringComparison.Ordinal))
                throw new OperationCanceledException();
        }
        try
        {
            token.ThrowIfCancellationRequested();
            using var files = GameFiles.TryUseConfiguredFiles(root);
            if (files == null || !GameFiles.Ready || !String.Equals(GameFiles.Root, root, StringComparison.Ordinal))
                return new(epoch, root, model, "");
            byte[] tga = _decode(root, model, token);
            int size = _rankArtwork ? 18 : 32;
            if (tga.Length != 18 + size * size * 4 || tga[2] != 2 || tga[12] != size || tga[13] != 0
                || tga[14] != size || tga[15] != 0 || tga[16] != 32 || tga[17] != 0x28)
                throw new InvalidDataException("Invalid original license portrait");
            token.ThrowIfCancellationRequested();
            string digest = System.Convert.ToHexString(SHA256.HashData(tga)).ToLowerInvariant();
            string directory = Path.Combine(root, "rmlui-media", _rankArtwork ? "license-stars" : "license-portraits");
            string target = Path.Combine(directory, digest + ".tga");
            // No worker IO holds a lock needed by owner Read/Dispose. A stale
            // content-addressed cosmetic file may survive a racing cancellation,
            // but only the fenced owner can adopt its path.
            ValidateRequest(); Directory.CreateDirectory(directory);
            ValidateRequest();
            if (!File.Exists(target))
            {
                temporary = Path.Combine(directory, ".portrait-" + Guid.NewGuid().ToString("N") + ".tmp");
                ValidateRequest();
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(tga); token.ThrowIfCancellationRequested(); output.Flush(flushToDisk: true); }
                ValidateRequest();
                try { File.Move(temporary, target, overwrite: false); temporary = null; }
                catch (IOException) when (File.Exists(target)) { }
            }
            ValidateRequest(); var found = new FileInfo(target);
            if (found.LinkTarget != null || found.Length != tga.Length)
                throw new InvalidDataException("License portrait cache differs from owned pixels");
            ValidateRequest();
            if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(tga))
                throw new InvalidDataException("License portrait cache differs from owned pixels");
            ValidateRequest();
            return new(epoch, root, model, target);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(epoch, root, model, ""); } // Artwork is cosmetic; never expose paths or asset errors in the DOM.
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static byte[] DecodeOwned(string root, string model, CancellationToken token)
        => DecodeTexture(model, token, size: 32);
    private static byte[] DecodeOwnedRank(string root, string model, CancellationToken token)
        => DecodeTexture(model, token, size: 18);

    private static byte[] DecodeTexture(string model, CancellationToken token, int size)
    {
        token.ThrowIfCancellationRequested();
        // noCache avoids both unsynchronised engine model dictionaries and any GL-owned model instance.
        Model owned = global::MphRead.Read.GetModelInstance(model, dir: MetaDir.GamersCard, noCache: true).Model;
        if (owned.Meshes.Count != 1 || owned.Materials.Count != 1 || owned.Recolors.Count != 1)
            throw new InvalidDataException("Unexpected original portrait layout");
        var material = owned.Materials[0]; var recolor = owned.Recolors[0];
        var texture = recolor.Textures[material.TextureId];
        var pixels = recolor.GetPixels(material.TextureId, material.PaletteId);
        if (texture.Width != 32 || texture.Height != 32 || pixels.Count != 32 * 32)
            throw new InvalidDataException("Original portrait pixels unavailable");
        // Original rank quads repeat UV (0,0)..(18,18) of a 32-square bitmap.
        // Preserve alpha verbatim, especially blackstar's black ink/white outline.
        byte[] rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            var pixel = pixels[y * 32 + x]; int i = (y * size + x) * 4;
            rgba[i] = pixel.Red; rgba[i + 1] = pixel.Green;
            rgba[i + 2] = pixel.Blue; rgba[i + 3] = pixel.Alpha;
        }
        token.ThrowIfCancellationRequested();
        return RmlUiMapPreviewCache.EncodeTga(rgba, size, size);
    }

    private static string RankModel(string? tier) => tier switch
    {
        "1" => "redstar", "2" => "bronzestar", "3" => "silverstar", "4" => "goldstar", "5" => "blackstar", _ => ""
    };
    private static string PortraitModel(string? hunter) => hunter?.Trim().ToUpperInvariant() switch
    {
        "SAMUS" => "samusmost", "KANDEN" => "kandenmost", "TRACE" => "tracemost",
        "SYLUX" => "syluxmost", "NOXUS" => "noxmost", "SPIRE" => "spiremost", "WEAVEL" => "weavelmost", _ => ""
    };
    private void Owner()
    { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Portrait requests require the launcher owner"); }
    public void Dispose()
    {
        Owner();
        if (_disposed) return;
        _disposed = true; _epoch++; _requestedRoot = _requestedModel = _ready = ""; _cancel?.Cancel();
        if (_work is { } work)
        { var cancel = _cancel; _ = work.ContinueWith(_ => cancel?.Dispose(), TaskScheduler.Default); }
        else _cancel?.Dispose();
    }
}
#endif
