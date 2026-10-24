#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Launcher.Core;

public sealed class TheatreExportController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ITheatreExportBackend _backend;
    private readonly TheatreExportContext _original;
    private TheatreExportContext _context;
    private TheatreExportOptions _options;
    private TheatreExportConfirmation? _confirmation;
    private bool _disposed, _authority;
    private int _selected;
    private string _status = "Review the source, camera and output options before queueing a video.", _error = "";
    public TheatreExportController(ITheatreExportBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _original = _context = backend.Capture();
        var first = backend.Presets[0];
        _options = new(0, first.Resolution, first.Fps, first.GameHud, first.ReplayOverlay, "libx264",
            _context.MarkIn is { } start && _context.MarkOut is { } end && start < end ? TheatreExportSource.Selection : TheatreExportSource.FullReplay,
            TheatreExportCamera.FirstPerson);
        Refresh();
    }
    public TheatreExportSnapshot Snapshot()
    {
        Verify();
        if (!_disposed && _authority && !_backend.IsCurrent(_context)) _authority = false;
        var queue = _backend.Queue();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, queue.Pending.Length - 1));
        return new(_context, _options, _backend.Presets, _backend.Encoders(), queue, _confirmation,
            !_disposed && _authority, _status, _error) { SelectedIndex = _selected };
    }
    public bool Refresh() => Run(() =>
    {
        RequireNoConfirmation(); _authority = false;
        var next = _backend.Capture();
        if (next.SceneGeneration != _original.SceneGeneration || !ReferenceEquals(next.PlaybackOwner, _original.PlaybackOwner)
            || next.LogicalPath != _original.LogicalPath || next.PlaybackPath != _original.PlaybackPath
            || next.SourceRevision != _original.SourceRevision)
            throw new InvalidOperationException("The original replay owner or source changed. Close this panel.");
        _context = next; _authority = _backend.IsCurrent(next);
        _status = "Export inputs refreshed. Accepted jobs keep their own reviewed inputs.";
    });
    public bool SetEncoderExecutable(string executable)=>Run(()=>
    {
        RequireNoConfirmation();executable=(executable??"").Trim();
        if(executable.Length>32768||executable.Any(char.IsControl))throw new ArgumentException("Enter an encoder executable path, or leave it blank for PNG frames.");
        _options=_options with{EncoderExecutable=executable};
    });
    public bool Change(string field) => Run(() =>
    {
        RequireNoConfirmation();
        switch (field)
        {
            case "preset":
                int index = (_options.Preset + 1) % _backend.Presets.Length;
                var preset = _backend.Presets[index];
                _options = _options with { Preset = index, Resolution = preset.Resolution, Fps = preset.Fps,
                    GameHud = preset.GameHud, ReplayOverlay = preset.ReplayOverlay }; break;
            case "resolution": _options = _options with { Resolution = (_options.Resolution + 1) % 4 }; break;
            case "fps":
                int[] rates = { 24, 30, 48, 60, 90, 120, 144, 240 };
                _options = _options with { Fps = rates[(Array.IndexOf(rates, _options.Fps) + 1) % rates.Length] }; break;
            case "game-hud": _options = _options with { GameHud = !_options.GameHud }; break;
            case "replay-overlay": _options = _options with { ReplayOverlay = !_options.ReplayOverlay }; break;
            case "encoder":
                var encoders = _backend.Encoders();
                if (encoders.IsEmpty) throw new InvalidOperationException("No encoder choice is available.");
                _options = _options with { Encoder = encoders[(encoders.IndexOf(_options.Encoder) + 1) % encoders.Length] }; break;
            case "source": _options = _options with { Source = (TheatreExportSource)(((int)_options.Source + 1) % 3) }; break;
            case "camera": _options = _options with { Camera = (TheatreExportCamera)(((int)_options.Camera + 1) % 5) }; break;
            default: throw new ArgumentException("Unknown export option.");
        }
    });
    public bool Enqueue() => Run(() =>
    {
        RequireNoConfirmation();
        if (!_authority || !_backend.IsCurrent(_context))
        { _authority = false; throw new InvalidOperationException("The export inputs changed. Refresh before queueing."); }
        if (_options.Preset < 0 || _options.Preset >= _backend.Presets.Length || _options.Resolution is < 0 or > 3
            || !new[] { 24, 30, 48, 60, 90, 120, 144, 240 }.Contains(_options.Fps)
            || !Enum.IsDefined(_options.Source) || !Enum.IsDefined(_options.Camera)
            || !_backend.Encoders().Contains(_options.Encoder)) throw new ArgumentException("Review supported export options.");
        if (_options.Source == TheatreExportSource.Selection && (_context.MarkIn is not { } start
            || _context.MarkOut is not { } end || start >= end || end > _context.Duration))
            throw new InvalidOperationException("Set a valid Mark In and Mark Out range, then refresh.");
        if (_options.Source == TheatreExportSource.ExistingReel && _context.ReelSegments == 0)
            throw new InvalidOperationException("This replay has no saved reel segments.");
        _backend.Enqueue(_context, _options);
        _status = "Video accepted by the existing export queue. Closing this panel keeps accepted work running.";
    });
    public bool Move(int direction) => Run(() =>
    {
        RequireNoConfirmation(); var queue = _backend.Queue();
        if (direction is not (-1 or 1)) throw new ArgumentException("Choose previous or next pending export.");
        if (queue.Pending.Length > 0) _selected = (_selected + direction + queue.Pending.Length) % queue.Pending.Length;
    });
    public bool Request(TheatreExportConfirmationKind kind) => Run(() =>
    {
        RequireNoConfirmation(); var queue = _backend.Queue();
        if (!Enum.IsDefined(kind) || kind == TheatreExportConfirmationKind.CancelActive && queue.Active == null
            || kind == TheatreExportConfirmationKind.Retry && queue.Retry == null
            || kind == TheatreExportConfirmationKind.ClearPending && queue.Pending.IsEmpty)
            throw new InvalidOperationException("No matching export operation is available.");
        _confirmation = new(kind, queue);
    });
    public bool Confirm() => Run(() =>
    {
        var exact = _confirmation ?? throw new InvalidOperationException("No export operation is awaiting confirmation.");
        bool accepted = exact.Kind switch
        {
            TheatreExportConfirmationKind.CancelActive => _backend.CancelExact(exact.Queue),
            TheatreExportConfirmationKind.Retry => _backend.RetryExact(exact.Queue),
            TheatreExportConfirmationKind.ClearPending => _backend.ClearExact(exact.Queue), _ => false
        };
        if (!accepted) throw new InvalidOperationException("The reviewed job or queue changed. Cancel this confirmation and review it again.");
        _confirmation = null;
        _status = exact.Kind == TheatreExportConfirmationKind.CancelActive ? "Cancellation requested for the reviewed export."
            : exact.Kind == TheatreExportConfirmationKind.Retry ? "The original export was queued for retry." : "The reviewed pending exports were cleared.";
    });
    public void CancelConfirmation() { Verify(); _confirmation = null; _error = ""; }
    public bool OpenOutput() => Run(() =>
    {
        RequireNoConfirmation(); string output = _backend.Queue().OutputPath;
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Complete an export before opening its output folder.");
        _backend.OpenOutput(output);
    });
    private void RequireNoConfirmation()
    { if (_confirmation != null) throw new InvalidOperationException("Confirm or cancel the reviewed export operation first."); }
    private bool Run(Action action)
    {
        Verify(); if (_disposed) return false;
        try { action(); _error = ""; return true; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (error is TheatreExportStaleException || !_backend.IsCurrent(_context)) _authority = false; _error = error.Message; return false; }
    }
    private void Verify()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Replay export presentation belongs to the engine thread."); }
    public void Dispose() { Verify(); _disposed = true; _authority = false; _confirmation = null; }
}
#endif
