#if !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Immutable camera review and exact replacement/removal confirmation, owned by one engine thread.</summary>
public sealed class TheatreCameraController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ITheatreCameraBackend _backend;
    private readonly TheatreCameraContext _original;
    private TheatreCameraContext _context;
    private TheatreCameraEdit? _pending;
    private int _selected;
    private bool _disposed, _authority;
    private long? _awaitingPresentation;
    private string _status = "Pause playback, then refresh to review the camera at this frame.", _error = "";
    public TheatreCameraController(ITheatreCameraBackend backend)
    { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _original = _context = backend.Capture(); Refresh(); }
    private TheatreCameraKey? Selected => _context.Keys.Length == 0 ? null : _context.Keys[_selected];
    public TheatreCameraSnapshot Snapshot()
    {
        Verify();
        if (!_disposed && _authority && !_backend.IsCurrent(_context)) _authority = false;
        return new(_context, _selected, Selected, _pending, !_disposed && _authority, _status, _error);
    }
    public bool Refresh() => Run(() =>
    {
        RequireNoConfirmation();
        _authority = false;
        var next = _backend.Capture();
        if (_awaitingPresentation is long previous && next.PresentationRevision <= previous)
            throw new InvalidOperationException("Wait for a new successful replay picture, then refresh to review the changed camera.");
        if (next.SceneGeneration != _original.SceneGeneration || next.LogicalPath != _original.LogicalPath
            || next.PlaybackPath != _original.PlaybackPath || next.RecordingRevision != _original.RecordingRevision)
            throw new InvalidOperationException("The replay owner or recording changed. Close this panel.");
        if (!ValidPose(next.Pose) || next.Frame > next.Duration || next.Keys.IsDefault || next.Keys.Length > 64
            || next.Keys.Any(key => key.Frame > next.Duration || !ValidPose(key.Pose))
            || next.Keys.Select(key => key.Frame).Distinct().Count() != next.Keys.Length)
            throw new InvalidDataException("The camera track is invalid for this replay.");
        uint? frame = Selected?.Frame;
        _context = next; _selected = 0;
        _awaitingPresentation = null;
        for (int index = 0; index < next.Keys.Length; index++)
            if (next.Keys[index].Frame == frame) { _selected = index; break; }
        _authority = next.SettledPaused && _backend.IsCurrent(next);
        _status = _authority ? "Camera reviewed. Close this panel to move the free camera, then reopen it."
            : "Pause playback and wait for seeking to finish, then refresh.";
    });
    public bool Move(int direction) => Run(() =>
    {
        RequireNoConfirmation(); RequireReviewed();
        if (direction is not (-1 or 1)) throw new ArgumentException("Choose the previous or next key.");
        if (_context.Keys.Length > 0) _selected = (_selected + direction + _context.Keys.Length) % _context.Keys.Length;
    });
    public bool FreeCamera() => Live(() => _backend.SetFreeCamera(_context));
    public bool Lens(float fov, float roll) => Live(() =>
    {
        if (!float.IsFinite(fov) || !float.IsFinite(roll) || Math.Abs(fov) > 5 || Math.Abs(roll) > 5)
            throw new ArgumentException("Lens adjustments must be finite steps up to five degrees.");
        _backend.AdjustLens(_context, fov, roll);
    });
    public bool ToggleTrack() => Live(() => _backend.ToggleTrack(_context));
    private bool Live(Action edit) => Run(() =>
    {
        RequireNoConfirmation(); RequireReviewed(); edit();
        _authority = false; _awaitingPresentation = _context.PresentationRevision;
        _status = "Camera changed. Wait for a new picture, then refresh to review it before saving.";
    });
    public bool Preview() => Run(() =>
    {
        RequireNoConfirmation(); RequireReviewed();
        var key = Selected ?? throw new InvalidOperationException("Choose a camera keyframe.");
        _backend.Preview(_context, key); _authority = false;
        _status = "Keyframe selected. Wait for the seek to finish, then refresh before editing.";
    });
    public bool Add() => Run(() =>
    {
        RequireNoConfirmation(); RequireReviewed();
        var key = new TheatreCameraKey(_context.Frame, _context.Pose);
        if (_context.Keys.Any(row => row.Frame == key.Frame))
            _pending = new(TheatreCameraChange.Replace, _context, key);
        else { _backend.Commit(new(TheatreCameraChange.Add, _context, key)); RefreshOrThrow(); _status = "Camera keyframe saved."; }
    });
    public bool RequestUpdate() => Run(() =>
    {
        RequireNoConfirmation(); RequireReviewed();
        var key = Selected ?? throw new InvalidOperationException("Choose a camera keyframe.");
        _pending = new(TheatreCameraChange.Replace, _context, key with { Pose = _context.Pose });
    });
    public bool RequestRemove() => Run(() =>
    {
        RequireNoConfirmation(); RequireReviewed();
        _pending = new(TheatreCameraChange.Remove, _context,
            Selected ?? throw new InvalidOperationException("Choose a camera keyframe."));
    });
    public bool Confirm() => Run(() =>
    {
        var exact = _pending ?? throw new InvalidOperationException("No camera edit is awaiting confirmation.");
        RequireReviewed();
        if (exact.Context != _context) throw new InvalidOperationException("The reviewed camera changed. Cancel and refresh.");
        _backend.Commit(exact); _pending = null; RefreshOrThrow();
        _status = exact.Change == TheatreCameraChange.Remove ? "Camera keyframe removed." : "Camera keyframe replaced.";
    });
    public void Cancel() { Verify(); _pending = null; _error = ""; }
    private void RefreshOrThrow() { if (!Refresh()) throw new IOException(_error); }
    private void RequireNoConfirmation()
    { if (_pending != null) throw new InvalidOperationException("Confirm or cancel the exact camera edit first."); }
    private void RequireReviewed()
    {
        if (!_authority || !_context.SettledPaused || !_backend.IsCurrent(_context))
        { _authority = false; throw new InvalidOperationException("The camera or replay changed. Cancel any confirmation, pause and refresh."); }
    }
    private bool Run(Action action)
    {
        Verify(); if (_disposed) return false;
        try { action(); _error = ""; return true; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if (error is TheatreCameraStaleException || !_backend.IsCurrent(_context)) _authority = false; _error = error.Message; return false; }
    }
    private void Verify()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Replay camera edits belong to the engine thread."); }
    public void Dispose() { Verify(); _disposed = true; _authority = false; _pending = null; }
    internal static bool ValidHandles(TheatreCameraHandles handles)
    {
        bool Vector(TheatreCameraVector? v)=>v==null||float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
        return Vector(handles.Incoming)&&Vector(handles.Outgoing)&&new[]{handles.FovIn,handles.FovOut,handles.RollIn,handles.RollOut}.All(float.IsFinite);
    }
    internal static bool ValidPose(TheatreCameraPose pose)
    {
        float length = pose.Qx * pose.Qx + pose.Qy * pose.Qy + pose.Qz * pose.Qz + pose.Qw * pose.Qw;
        return float.IsFinite(pose.X) && float.IsFinite(pose.Y) && float.IsFinite(pose.Z)
            && Math.Abs(pose.X) <= 1000000 && Math.Abs(pose.Y) <= 1000000 && Math.Abs(pose.Z) <= 1000000
            && float.IsFinite(length) && Math.Abs(length - 1) < .001f
            && float.IsFinite(pose.Fov) && pose.Fov is >= 1 and <= 175
            && float.IsFinite(pose.Roll) && Math.Abs(pose.Roll) <= 360
            && (pose.Handles==null||ValidHandles(pose.Handles))
            && pose.LookAt is >= -1 and < 8 && pose.Interpolation is >= 0 and < 5 && pose.Ease is >= 0 and < 4;
    }
}
#endif
