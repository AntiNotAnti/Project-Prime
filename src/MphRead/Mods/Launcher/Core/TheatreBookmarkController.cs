#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Bookmark selection and exact removal confirmation, bound to one foreground replay.</summary>
public sealed class TheatreBookmarkController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ITheatreBookmarkBackend _backend;
    private readonly TheatreBookmarkContext _original;
    private TheatreBookmarkContext _context;
    private ImmutableArray<TheatreBookmarkEntry> _entries = ImmutableArray<TheatreBookmarkEntry>.Empty;
    private TheatreBookmarkEntry? _pending;
    private int _selected;
    private bool _disposed;
    private string _status = "Name a moment at the playhead, or browse saved bookmarks.", _error = "";

    public TheatreBookmarkController(ITheatreBookmarkBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _context = _original = _backend.Capture();
        Refresh();
    }
    public TheatreBookmarkSnapshot Snapshot()
    {
        Verify();
        return new(_entries, _selected, Selected, _pending, _status, _error,
            !_disposed && _backend.IsCurrent(_original), Path.GetFileName(_original.LogicalPath));
    }
    private TheatreBookmarkEntry? Selected => _entries.Length == 0 ? null : _entries[_selected];
    public bool Refresh() => Run(() =>
    {
        RequireNoConfirmation();
        var next = CaptureOwner();
        var rows = _backend.Read(next);
        if (rows.IsDefault || rows.Any(row => row.Id == Guid.Empty || row.Frame > next.Duration)
            || rows.Select(row => row.Id).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Bookmark data is invalid for this replay.");
        Guid? previous = Selected?.Id;
        _entries = rows.OrderBy(row => row.Frame).ThenBy(row => row.CreatedUtc).ToImmutableArray();
        _selected = 0;
        if (previous is Guid id)
            for (int index = 0; index < _entries.Length; index++)
                if (_entries[index].Id == id) { _selected = index; break; }
        _context = next;
        _status = _entries.Length == 0 ? "No bookmarks yet. Name a moment at the playhead." : "Bookmarks refreshed.";
    });
    public bool Move(int direction) => Run(() =>
    {
        RequireNoConfirmation(); CaptureUnchanged();
        if (direction is not (-1 or 1) || _entries.Length == 0) return;
        _selected = (_selected + direction + _entries.Length) % _entries.Length;
    });
    public bool Add(string name) => Run(() =>
    {
        RequireNoConfirmation();
        name = name.Trim();
        if (name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Use one line of text, up to 80 characters.");
        var current = CaptureUnchanged();
        _backend.Add(current, name);
        if (!Refresh()) throw new IOException(_error);
        _status = "Bookmark saved at the playhead.";
    });
    public bool Jump() => Run(() =>
    {
        RequireNoConfirmation();
        var current = CaptureUnchanged();
        if (Selected is not { } selected) throw new InvalidOperationException("Choose a saved bookmark.");
        _backend.Seek(current, selected);
        _status = "Jumped to " + selected.Name + ".";
    });
    public bool RequestRemove() => Run(() =>
    {
        RequireNoConfirmation(); CaptureUnchanged();
        _pending = Selected ?? throw new InvalidOperationException("Choose a saved bookmark.");
    });
    public bool ConfirmRemove() => Run(() =>
    {
        var exact = _pending ?? throw new InvalidOperationException("No bookmark removal is awaiting confirmation.");
        var current = CaptureUnchanged();
        if (Selected != exact) throw new InvalidOperationException("The bookmark selection changed. Cancel and select it again.");
        _backend.Remove(current, exact);
        _pending = null;
        if (!Refresh()) throw new IOException(_error);
        _status = "Bookmark removed.";
    });
    public void CancelRemove() { Verify(); _pending = null; _error = ""; }
    private TheatreBookmarkContext CaptureOwner()
    {
        if (!_backend.IsCurrent(_original)) throw new InvalidOperationException("The replay owner changed. Close this panel.");
        var current = _backend.Capture();
        if (current.SceneGeneration != _original.SceneGeneration || current.LogicalPath != _original.LogicalPath
            || current.PlaybackPath != _original.PlaybackPath || current.RecordingRevision != _original.RecordingRevision)
            throw new InvalidOperationException("The replay changed. Close this panel and reopen it.");
        return current;
    }
    private TheatreBookmarkContext CaptureUnchanged()
    {
        var current = CaptureOwner();
        if (current.AnnotationRevision != _context.AnnotationRevision)
            throw new InvalidOperationException("Bookmarks changed elsewhere. Cancel removal, then refresh before editing.");
        return current;
    }
    private void RequireNoConfirmation()
    {
        if (_pending != null) throw new InvalidOperationException("Confirm or cancel the selected bookmark removal first.");
    }
    private bool Run(Action action)
    {
        Verify(); if (_disposed) return false;
        try { action(); _error = ""; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _error = error.Message; return false; }
    }
    private void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Replay bookmarks belong to their presentation owner thread.");
    }
    public void Dispose() { Verify(); _disposed = true; _pending = null; _entries = ImmutableArray<TheatreBookmarkEntry>.Empty; }
}
#endif
