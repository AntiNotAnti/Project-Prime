#if !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Range commands capture values from one replay owner; this controller schedules no playback.</summary>
public sealed class TheatreRangeController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ITheatreRangeBackend _backend;
    private readonly TheatreRangeContext _original;
    private TheatreRangeContext _context;
    private bool _disposed, _stale;
    private readonly ConcurrentQueue<(int Generation, TheatreRangeContext Context, string Name, TheatreMaterializedClipResult Result)> _clipResults = new();
    private CancellationTokenSource? _clipCancellation;
    private int _clipGeneration;
    private bool _savingClip;
    private string? _savedClipName;
    private string _status = "Mark a start and a later end, then name the selected range.", _error = "";

    public TheatreRangeController(ITheatreRangeBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _context = _original = _backend.Capture();
    }
    public TheatreRangeSnapshot Snapshot()
    {
        Verify();
        Pump();
        bool ownerCurrent = !_disposed && _backend.IsCurrent(_original);
        TheatreRangeSelection? live = null;
        if (ownerCurrent)
        {
            try
            {
                live = _backend.ReadSelection(_original);
                if (live.Value.MarkIn != _context.MarkIn || live.Value.MarkOut != _context.MarkOut
                    || live.Value.Duration != _context.Replay.Duration)
                {
                    _stale = true;
                    if (_error.Length == 0) _error = "The marks changed elsewhere. Refresh to review the current selection.";
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { _stale = true; _error = error.Message; }
        }
        else if (!_disposed)
        {
            _stale = true;
            if (_error.Length == 0) _error = "The replay owner changed. Close this panel.";
            if (_savingClip && _clipCancellation?.IsCancellationRequested == false)
                try { _clipCancellation.Cancel(); } catch (AggregateException) { }
        }
        bool current = ownerCurrent && !_stale;
        return new(_context.MarkIn, _context.MarkOut, _context.Replay.Duration,
            Path.GetFileName(_original.Replay.LogicalPath), current, Valid(_context), _status, _error)
        { LiveSelection = live, SavingClip = _savingClip, MaterializedClipsAvailable = _backend.MaterializedClipsAvailable };
    }
    public bool Refresh() => Run(() =>
    {
        RequireIdle();
        _context = CaptureOwner(); _stale = false;
        _status = "Selection refreshed from the current replay.";
    });
    public bool MarkIn() => Mark(start: true);
    public bool MarkOut() => Mark(start: false);
    private bool Mark(bool start) => Run(() =>
    {
        RequireIdle();
        var current = CaptureUnchanged();
        if (start) _backend.MarkIn(current); else _backend.MarkOut(current);
        _context = CaptureOwner();
        _status = start ? "Range start marked at the playhead." : "Range end marked at the playhead.";
    });
    public bool SaveHighlight(string name) => Create(name, virtualClip: false);
    public bool CreateVirtualClip(string name) => Create(name, virtualClip: true);
    private bool Create(string name, bool virtualClip) => Run(() =>
    {
        RequireIdle(); name = ValidateName(name);
        var current = CaptureUnchanged();
        if (!Valid(current)) throw new InvalidOperationException("Mark a start and a later end within this replay first.");
        string saved = virtualClip ? _backend.CreateVirtualClip(current, name) : _backend.SaveHighlight(current, name);
        _context = CaptureOwner();
        _status = (virtualClip ? "Virtual clip created: " : "Named highlight saved: ") + saved;
    });
    public bool SaveMaterializedClip(string name) => Run(() =>
    {
        RequireIdle(); name = ValidateName(name);
        var current = CaptureUnchanged();
        if (!Valid(current)) throw new InvalidOperationException("Mark a start and a later end within this replay first.");
        if (!_backend.MaterializedClipsAvailable) throw new InvalidOperationException("Materialized clip saving is unavailable.");
        var cancellation = new CancellationTokenSource();
        Task<TheatreMaterializedClipResult> operation;
        try { operation = _backend.SaveMaterializedClip(current, name, cancellation.Token); }
        catch { cancellation.Dispose(); throw; }
        _clipCancellation = cancellation; _savingClip = true; _savedClipName = null;
        _status = "Saving materialized replay clip...";
        int generation = ++_clipGeneration;
        _ = ObserveClip(operation, generation, current, name);
    });
    private async Task ObserveClip(Task<TheatreMaterializedClipResult> operation, int generation,
        TheatreRangeContext context, string name)
    {
        TheatreMaterializedClipResult result;
        try { result = await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) { result = new(false, "", "Clip save cancelled."); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { result = new(false, "", "Could not save clip: " + error.Message); }
        _clipResults.Enqueue((generation, context, name, result));
    }
    /// <summary>Only the presentation owner publishes async results; no worker touches a document.</summary>
    public void Pump()
    {
        Verify();
        while (_clipResults.TryDequeue(out var completion))
        {
            if (_disposed || completion.Generation != _clipGeneration || !_savingClip) continue;
            _clipCancellation?.Dispose(); _clipCancellation = null; _savingClip = false;
            bool current = false;
            try
            {
                var actual = CaptureOwner();
                if (actual.MarkIn != completion.Context.MarkIn || actual.MarkOut != completion.Context.MarkOut
                    || actual.Replay.AnnotationRevision != completion.Context.Replay.AnnotationRevision) _stale = true;
                current = !_stale && _backend.IsCurrent(completion.Context);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { _stale = true; }
            var result = completion.Result;
            if (result.Saved)
            {
                _status = (current ? "Materialized clip saved: " : "Materialized clip saved for the captured range: ")
                    + Path.GetFileName(result.OutputPath);
                _error = result.Error;
                if (current && result.Error.Length == 0) _savedClipName = completion.Name;
            }
            else
            {
                _status = "Materialized clip was not saved. Your name and reviewed range are retained.";
                _error = result.Error.Length > 0 ? result.Error : "Could not save the materialized clip.";
            }
        }
    }
    public bool TryTakeMaterializedClipSaved(out string name)
    {
        Verify(); Pump(); name = _savedClipName ?? "";
        if (_savedClipName == null) return false;
        _savedClipName = null; return true;
    }
    public bool CancelMaterializedClip()
    {
        Verify(); Pump(); if (_disposed || !_savingClip) return false;
        ++_clipGeneration;
        try { _clipCancellation?.Cancel(); } catch (AggregateException) { }
        finally { _clipCancellation?.Dispose(); _clipCancellation = null; _savingClip = false; _savedClipName = null; }
        _status = "Materialized clip save cancellation requested."; _error = ""; return true;
    }
    private void RequireIdle()
    {
        if (_savingClip) throw new InvalidOperationException("Wait for the clip save to finish, or cancel it first.");
    }
    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Use one line of text, up to 80 characters.");
        return name;
    }
    private TheatreRangeContext CaptureUnchanged()
    {
        if (_stale) throw new InvalidOperationException("Refresh the selection before editing.");
        var current = CaptureOwner();
        if (current.Replay.AnnotationRevision != _context.Replay.AnnotationRevision
            || current.MarkIn != _context.MarkIn || current.MarkOut != _context.MarkOut)
        {
            _stale = true;
            throw new InvalidOperationException("The marks or annotations changed elsewhere. Refresh to review them before creating anything.");
        }
        return current;
    }
    private TheatreRangeContext CaptureOwner()
    {
        if (!_backend.IsCurrent(_original))
        {
            _stale = true;
            throw new InvalidOperationException("The replay owner changed. Close this panel.");
        }
        var current = _backend.Capture();
        var expected = _original.Replay; var actual = current.Replay;
        if (actual.SceneGeneration != expected.SceneGeneration || actual.LogicalPath != expected.LogicalPath
            || actual.PlaybackPath != expected.PlaybackPath || actual.RecordingRevision != expected.RecordingRevision
            || actual.Duration != expected.Duration)
        {
            _stale = true;
            throw new InvalidOperationException("The replay changed. Close this panel and reopen it.");
        }
        return current;
    }
    private static bool Valid(TheatreRangeContext context)
        => context.MarkIn is uint start && context.MarkOut is uint end && start < end && end <= context.Replay.Duration;
    private bool Run(Action action)
    {
        Verify(); if (_disposed) return false;
        try { action(); _error = ""; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Preserve the reviewed range on failure, but stop authorizing writes if its witness changed.
            try
            {
                var current = CaptureOwner();
                if (current.Replay.AnnotationRevision != _context.Replay.AnnotationRevision
                    || current.MarkIn != _context.MarkIn || current.MarkOut != _context.MarkOut) _stale = true;
            }
            catch (Exception check) when (check is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { _stale = true; }
            _error = error.Message; return false;
        }
    }
    private void Verify()
    {
        if (_owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Replay ranges belong to their presentation owner thread.");
    }
    public void Dispose() { Verify(); if (_disposed) return; CancelMaterializedClip(); _disposed = true; _savedClipName = null; }
}
#endif
