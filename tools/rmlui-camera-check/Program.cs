using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool value, string message)
{ if (!value) throw new InvalidOperationException("Camera contract: " + message); checks++; }
var memory = new MemoryCamera();
using var camera = new TheatreCameraController(memory);
Check(camera.Snapshot() is { Current: true, Selected: null }, "review is immutable, paused and initially empty");
Check(camera.Add() && memory.Edits.Single() is { Change: TheatreCameraChange.Add, Key.Frame: 60 }
    && memory.Edits[0].Key.Pose == MemoryCamera.Pose, "creation captures exact reviewed frame and pose");
Check(camera.Add() && camera.Snapshot().Pending is { Change: TheatreCameraChange.Replace } && memory.Edits.Count == 1,
    "occupied-frame Add requires exact replacement confirmation");
Check(!camera.Lens(5, 0) && !camera.Move(1) && !camera.Refresh() && memory.Edits.Count == 1,
    "confirmation freezes selection, lens and refresh");
camera.Cancel();
Check(camera.Lens(5, -5) && !camera.Snapshot().Current && camera.Snapshot().Context.Pose.Fov == 80,
    "lens controls invalidate review while preserving the previously displayed pose");
Check(!camera.Add() && !camera.Refresh(), "same-picture settings change cannot claim a new reviewed lens pose");
memory.Present();
Check(camera.Refresh() && camera.Snapshot().Context.Pose is { Fov: 85, Roll: -5 }, "new successful picture plus explicit refresh reviews actual changed lens");
Check(camera.RequestUpdate() && camera.Snapshot().Pending is { Key.Frame: 60, Key.Pose.Fov: 85 },
    "update captures immutable selected frame and replacement pose");
memory.Context = memory.Context with { Pose = memory.Context.Pose with { X = 10 } };
Check(!camera.Confirm() && memory.Edits.Count == 1 && !camera.Snapshot().Current,
    "camera moved after review cannot persist the staged pose");
camera.Cancel(); Check(camera.Refresh(), "explicit refresh reviews changed pose");
Check(camera.RequestUpdate(), "new review can stage replacement");
memory.FailWrite = true;
var pending = camera.Snapshot().Pending;
Check(!camera.Confirm() && camera.Snapshot() is { Current: true } && camera.Snapshot().Pending == pending
    && memory.Context.Keys[0].Pose == MemoryCamera.Pose, "write failure preserves exact pending edit, persisted key and retry authority");
memory.FailWrite = false;
Check(camera.Confirm() && memory.Context.Keys[0].Pose.X == 10 && camera.Snapshot().Pending == null,
    "same immutable replacement retries after write failure");
Check(camera.RequestRemove() && camera.Snapshot().Pending is { Change: TheatreCameraChange.Remove, Key.Frame: 60 },
    "remove stages exact selected persisted key");
memory.Sidecar = "external";
Check(!camera.Confirm() && !camera.Snapshot().Current && memory.Context.Keys.Length == 1,
    "external sidecar revision clears mutation authority and does not delete");
camera.Cancel(); Check(camera.Refresh() && camera.RequestRemove() && camera.Confirm() && memory.Context.Keys.IsEmpty,
    "explicit review followed by exact confirmation removes only selected key");
Check(camera.Add() && camera.Preview() && !camera.Snapshot().Current && memory.Previewed?.Frame == 60,
    "jump uses existing backend seek, invalidates old capture and starts no second transport");
memory.Context = memory.Context with { Transport = new object(), PresentationScene = new object(), SeekGeneration = 2 };
Check(!camera.Add() && camera.Refresh(), "transport/replica replacement invalidates old commands but explicit review accepts same replay");
Check(camera.RequestUpdate(), "fresh context stages a replacement");
memory.Context = memory.Context with { SeekGeneration = 3 };
Check(!camera.Confirm() && memory.Edits.Count == 4, "seek generation changing before confirmation cannot retarget edit");
camera.Cancel(); Check(camera.Refresh(), "review after settled seek");
memory.Context = memory.Context with { SettledPaused = false };
Check(!camera.Add() && camera.Refresh() && !camera.Snapshot().Current && !camera.RequestRemove(),
    "playing or warming context is readonly and cannot persist");
memory.Context = memory.Context with { SettledPaused = true };
Check(camera.Refresh() && camera.FreeCamera() && memory.FreeCalls == 1 && !camera.Snapshot().Current, "free camera uses existing owner and requires a new picture");
memory.Present(); Check(camera.Refresh(), "explicit refresh after free camera picture");
Check(camera.ToggleTrack() && !camera.Snapshot().Current, "track preview invalidates the previously reviewed picture");
memory.Present(); Check(camera.Refresh() && camera.Snapshot().Context.TrackPlaying, "new track picture can be explicitly reviewed");
Check(!camera.Lens(float.NaN, 0) && !camera.Lens(6, 0), "invalid lens deltas do not reach backend");
int captures = memory.Captures; camera.Snapshot(); camera.Snapshot();
Check(memory.Captures == captures, "readonly snapshots never reread files or reauthorize the review");
memory.Context = memory.Context with { Pose = memory.Context.Pose with { Fov = float.NaN } };
Check(!camera.Refresh() && !camera.Snapshot().Current, "invalid camera data clears review authority");
memory.Context = memory.Context with { Pose = MemoryCamera.Pose, SceneGeneration = 2 };
Check(!camera.Refresh(), "different foreground scene cannot rebind old panel");
memory.Context = memory.Context with { SceneGeneration = 1, LogicalPath = "other.ppclip" };
Check(!camera.Refresh(), "different logical replay cannot rebind old panel");
memory.Context = memory.Context with { LogicalPath = "synthetic.ppdemo", RecordingRevision = "replacement" };
Check(!camera.Refresh(), "replacement recording requires fresh panel");
Check(Task.Run(() => { try { camera.Add(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(),
    "another thread cannot dispatch camera edits");
camera.Dispose(); Check(!camera.Add() && !camera.Refresh() && !camera.Snapshot().Current, "disposed camera owner cannot edit");
Console.WriteLine($"PASS {checks} native camera ownership/pose/seek/confirmation/retry checks; synthetic memory only");
PresentationChecks.Run();

sealed class MemoryCamera : ITheatreCameraBackend
{
    public static readonly TheatreCameraPose Pose = new(1, 2, 3, 0, 0, 0, 1, 80, 0, -1, 2, 3);
    public TheatreCameraContext Context = new(1, new object(), new object(), 1, "synthetic.ppdemo", "synthetic.ppdemo",
        "recording", "0", 60, 600, true, Pose, ImmutableArray<TheatreCameraKey>.Empty, false) { PresentationRevision = 1 };
    public string Sidecar = "0";
    public int Captures, FreeCalls;
    public bool FailWrite;
    public TheatreCameraKey? Previewed;
    public List<TheatreCameraEdit> Edits = new();
    public TheatreCameraContext Capture() { Captures++; Context = Context with { CameraRevision = Sidecar }; return Context; }
    public void Present() => Context = Context with { PresentationRevision = Context.PresentationRevision + 1 };
    public bool IsCurrent(TheatreCameraContext c) => Context.SettledPaused && Context.SceneGeneration == c.SceneGeneration
        && ReferenceEquals(Context.Transport, c.Transport) && ReferenceEquals(Context.PresentationScene, c.PresentationScene)
        && Context.SeekGeneration == c.SeekGeneration && Context.Frame == c.Frame && Context.Pose == c.Pose
        && Context.TrackPlaying == c.TrackPlaying && Context.LogicalPath == c.LogicalPath && Context.PlaybackPath == c.PlaybackPath
        && Context.Keys.SequenceEqual(c.Keys);
    private void Validate(TheatreCameraContext c)
    { if (!IsCurrent(c) || c.CameraRevision != Sidecar) throw new TheatreCameraStaleException("Stale synthetic review."); }
    public void SetFreeCamera(TheatreCameraContext c) { Validate(c); FreeCalls++; }
    public void AdjustLens(TheatreCameraContext c, float fov, float roll)
    { Validate(c); Context = Context with { Pose = Context.Pose with { Fov = Math.Clamp(Context.Pose.Fov + fov, 20, 140), Roll = Math.Clamp(Context.Pose.Roll + roll, -180, 180) } }; }
    public void Preview(TheatreCameraContext c, TheatreCameraKey key)
    { Validate(c); Previewed = key; Context = Context with { SeekGeneration = Context.SeekGeneration + 1 }; }
    public void ToggleTrack(TheatreCameraContext c) { Validate(c); Context = Context with { TrackPlaying = !Context.TrackPlaying }; }
    public void Commit(TheatreCameraEdit exact)
    {
        Validate(exact.Context);
        if (FailWrite) throw new IOException("Synthetic locked camera sidecar.");
        Edits.Add(exact);
        var keys = Context.Keys.Where(key => key.Frame != exact.Key.Frame).ToList();
        if (exact.Change != TheatreCameraChange.Remove) keys.Add(exact.Key);
        Sidecar = Guid.NewGuid().ToString(); Context = Context with { Keys = keys.OrderBy(key => key.Frame).ToImmutableArray(), CameraRevision = Sidecar };
    }
}
