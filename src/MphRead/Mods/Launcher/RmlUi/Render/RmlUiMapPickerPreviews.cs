#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>Six visible PNG decoders, one cancellable thumbnail-render request.
/// Paging only publishes images belonging to the current room/root.</summary>
internal sealed class RmlUiMapPickerPreviews : IDisposable
{
    private readonly Func<string> _root;
    private readonly Func<string, string> _source;
    private readonly Func<string, string, CancellationToken, Task<int>> _render;
    private RmlUiMapPreviewCache[] _caches = Array.Empty<RmlUiMapPreviewCache>();
    private string[] _rooms = Array.Empty<string>();
    private string _observedRoot = "";
    private readonly bool[] _attempted = new bool[6];
    private CancellationTokenSource? _cancel;
    private Task? _work;
    private bool _disposed;

    internal RmlUiMapPickerPreviews(Func<string>? root = null, Func<string, string>? source = null,
        Func<string, string, CancellationToken, Task<int>>? render = null)
    {
        _root = root ?? (() => GameFiles.Root);
        _source = source ?? ThumbnailGenerator.PathFor;
        _render = render ?? ((room, directory, token) => ThumbnailHost.RenderRoomAsync(room, directory, _ => { }, token));
    }

    internal RmlUiArenaPreview[] Read(IReadOnlyList<string> rooms)
    {
        if (_disposed) return Array.Empty<RmlUiArenaPreview>();
        string root = _root();
        string[] selected = rooms.Take(6).ToArray();
        if (_observedRoot != root || !_rooms.SequenceEqual(selected, StringComparer.Ordinal))
        {
            Retire(); _rooms = selected; Array.Clear(_attempted);
            if (_observedRoot != root)
            {
                foreach (var cache in _caches) cache.Dispose();
                _observedRoot = root;
                _caches = Enumerable.Range(0, 6).Select(_ => new RmlUiMapPreviewCache(
                    Path.Combine(root, "rmlui-media", "map-previews"))).ToArray();
            }
            _cancel = new();
        }
        if (_work is { IsCompleted: true } finished) { _ = finished.Exception; _work = null; }
        var result = new RmlUiArenaPreview[selected.Length];
        for (int i = 0; i < 6; i++)
        {
            string room = i < selected.Length ? selected[i] : "";
            string source = room.Length == 0 ? "" : _source(room);
            string image = _caches[i].Read(source);
            if (i >= selected.Length) continue;
            bool exists;
            try { exists = source.Length > 0 && new FileInfo(source) is { Exists: true, Length: > 0 }; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { exists = false; }
            if (room.Length != 0 && image.Length == 0 && !exists && !_attempted[i] && _work == null)
            {
                _attempted[i] = true;
                CancellationToken token = _cancel!.Token;
                _work = Task.Run(async () =>
                {
                    try { await _render(room, root, token).ConfigureAwait(false); }
                    catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException) { }
                });
            }
            result[i] = new(image, image.Length > 0 ? "" : exists || _work is { IsCompleted: false }
                || !_attempted[i] ? "LOADING PREVIEW" : "PREVIEW UNAVAILABLE");
        }
        return result;
    }

    private void Retire()
    {
        var cancel = _cancel; _cancel = null;
        cancel?.Cancel();
        if (_work is { IsCompleted: false } work)
            _ = work.ContinueWith(_ => cancel?.Dispose(), TaskScheduler.Default);
        else cancel?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Retire();
        foreach (var cache in _caches) cache.Dispose(); _rooms = Array.Empty<string>();
    }
}
#endif
