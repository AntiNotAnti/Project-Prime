#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

// Read-only source viewer: no equip/apply, game selection, player or wire state.
internal sealed class NativeCharacterSourcePreviewPage : IDisposable
{
    private sealed record LoadResult(CharacterSourcePreviewSelection? Selection, string Error);
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly bool _asModal;
    private readonly string _id;
    private readonly object _owner = new();
    private readonly LauncherHunterPreviewController _preview = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task<LoadResult>? _load;
    private CharacterSourcePreviewSelection? _selection;
    private int _clip;
    private long _stamp, _revision;
    private double _seconds;
    private bool _paused, _disposed;
    private string _status = "Reading the selected immutable source generation…";
    internal RmlUiDocumentToken Document { get; private set; }
    internal bool Closed { get; private set; }
    internal bool OwnsForeground => !_disposed && !Closed && _host.IsAlive(Document) && _pages.Top == Document
        && _host.CurrentInputDocument == Document && _host.IsVisible(Document);
    internal LauncherHunterPreviewSnapshot Preview => _preview.Current();
    internal CharacterSourceStageRequest? Request => OwnsForeground && _selection != null
        ? new(_owner, _selection.Pack, CharacterSourcePreviewSelection.Appearance, _selection.Clips[_clip].Ordinal, _seconds) : null;

    internal NativeCharacterSourcePreviewPage(RmlUiHost host, RmlUiPageManager pages, string id, bool asModal)
        => (_host, _pages, _id, _asModal) = (host, pages, id, asModal);
    internal void Open()
    {
        _host.VerifyOwnerThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        var spec = new RmlUiPageSpec("native-character-source", "pages/settings/native-character-source.rml", "source_back");
        Document = _asModal ? _pages.OpenModal(spec) : _pages.OpenPage(spec);
        var token = _cancellation.Token;
        _load = Task.Run(() =>
        {
            try { return new LoadResult(CharacterSourcePreviewSelection.Load(_id, token), ""); }
            catch (Exception error) when (error is System.IO.InvalidDataException or System.IO.IOException or InvalidOperationException or ArgumentException or NotSupportedException or OperationCanceledException or System.Text.Json.JsonException or UnauthorizedAccessException or System.Collections.Generic.KeyNotFoundException or OverflowException)
            { return new LoadResult(null, error is OperationCanceledException ? "Preview closed." : error.Message); }
        }, token);
        Refresh();
    }
    internal void Tick()
    {
        _host.VerifyOwnerThread(); if (_disposed || Closed) return;
        if (!_host.IsAlive(Document)) { Closed = true; _cancellation.Cancel(); LauncherHunter.ReleaseSourcePreview(_owner); return; }
        if (!OwnsForeground) { _stamp = 0; LauncherHunter.ReleaseSourcePreview(_owner); return; }
        if (_load is { IsCompleted: true } task)
        {
            _load = null;
            if (!task.IsCanceled)
            {
                var result = task.GetAwaiter().GetResult(); _selection = result.Selection;
                _status = _selection == null ? result.Error : "Captured generation " + _selection.Generation[..Math.Min(12, _selection.Generation.Length)] + ". Reopen to load an updated import.";
            }
        }
        long now = Stopwatch.GetTimestamp();
        if (_selection != null && !_paused && _stamp != 0)
        {
            double duration = _selection.Clips[_clip].Duration;
            double delta = Stopwatch.GetElapsedTime(_stamp, now).TotalSeconds;
            _seconds = duration > 0 ? (_seconds + Math.Min(delta, .25)) % duration : 0;
        }
        _stamp = now; Refresh();
    }
    internal bool Handle(in RmlUiIntent intent)
    {
        _host.VerifyOwnerThread();
        if (!OwnsForeground || intent.Document != Document || !_pages.Accept(intent)) return false;
        if (intent.Kind == RmlUiIntentKind.HunterCancel) { Back(); return true; }
        if (intent.Kind == RmlUiIntentKind.HunterRotate) _preview.Rotate(intent.Argument);
        else if (intent.Kind == RmlUiIntentKind.HunterZoom) _preview.Zoom(intent.Argument);
        else if (intent.Kind == RmlUiIntentKind.HunterPreviewReset) { _preview.Reset(); _seconds = 0; }
        else if (intent.Kind == RmlUiIntentKind.SettingsAction && intent.Argument is >= 246 and <= 249)
        {
            if (_selection == null) return true;
            if (intent.Argument is 246 or 247)
            { _clip = (_clip + (intent.Argument == 246 ? -1 : 1) + _selection.Clips.Count) % _selection.Clips.Count; _seconds = 0; }
            else if (intent.Argument == 248) _paused = !_paused;
            else _seconds = 0;
        }
        else return false;
        _stamp = Stopwatch.GetTimestamp(); Refresh(); return true;
    }
    internal void ReportDraw(bool drawn)
    {
        if (_selection != null) _host.SetText(Document, "source_render_status", drawn
            ? "ANIMATED SOURCE GEOMETRY // DIAGNOSTIC MATERIAL COLORS"
            : GameFiles.Ready ? "Source render unavailable. Reopen this viewer to retry; Back remains usable." : "Configure original game files to use the shared 3D preview.");
    }
    internal void Back() { if (!OwnsForeground) return; Closed = true; LauncherHunter.ReleaseSourcePreview(_owner); }
    private void Refresh()
    {
        if (!_host.IsAlive(Document)) return;
        string clip = _selection == null ? "No source clip loaded" : "CLIP " + (_clip + 1) + "/" + _selection.Clips.Count + " // " + _selection.Clips[_clip].Name;
        _pages.Present(Document, ++_revision, new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["source_status"] = RmlUiBindingValue.FromText(_status), ["source_clip"] = RmlUiBindingValue.FromText(clip),
            ["source_pause"] = RmlUiBindingValue.FromText(_paused ? "PLAY" : "PAUSE"),
            ["source_phase"] = RmlUiBindingValue.FromText(_seconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " seconds"),
            ["disabled:source_prev"] = RmlUiBindingValue.FromBoolean(_selection == null),
            ["disabled:source_next"] = RmlUiBindingValue.FromBoolean(_selection == null),
            ["disabled:source_pause"] = RmlUiBindingValue.FromBoolean(_selection == null)
        });
    }
    public void Dispose()
    {
        if (_disposed) return; _host.VerifyOwnerThread(); _disposed = true; Closed = true;
        _cancellation.Cancel(); _cancellation.Dispose(); LauncherHunter.ReleaseSourcePreview(_owner);
        if (_pages.Top == Document) { if (_asModal) _pages.CloseModal(); else _pages.ClosePage(); }
        _selection = null; _preview.Dispose();
    }
}
#endif
