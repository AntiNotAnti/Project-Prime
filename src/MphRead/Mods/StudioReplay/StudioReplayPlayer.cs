#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.Render;
using SkiaSharp;
using TkVector = OpenTK.Mathematics.Vector3;
using TkQuaternion = OpenTK.Mathematics.Quaternion;
using Vector2i = OpenTK.Mathematics.Vector2i;

namespace MphRead.Mods.StudioReplay;

/// <summary>Native Studio boundary around the existing passive replay player.
/// All simulation and graphics commands execute on one explicit owner thread.</summary>
public sealed partial class StudioReplayPlayer : IStudioReplayGraphicsSession, IDisposable
{
    private readonly StudioReplayResources _resources;
    private readonly string _cacheRoot;
    private readonly ReplayCameraTrack _camera = new();
    private readonly Queue<Action<PassiveReplayPlayer>> _commands = new();
    private ReplayPreparationJob? _preparation;
    private PassiveReplayPlayer? _player;
    private int? _sceneGraphicsGeneration;
    private int? _owner;
    private Vector2i _size = new(640, 480);
    private double _fixedAccumulator;
    private bool _disposed;
    private string? _error;
    private uint? _resumeFrame;
    private bool _resumePlaying;
    private float _resumeRate = 1;
    private uint? _resumeIn, _resumeOut;
    private string? _playbackPath;
    private double _advanceMilliseconds, _renderMilliseconds;
    private bool _hasAdvance, _hasRender, _hasSeek;
    private long _seekRequestId, _appliedSeekRequestId, _settledSeekRequestId;
    public long SeekRequestId => _seekRequestId;
    public long AppliedSeekRequestId => _appliedSeekRequestId;
    public long SettledSeekRequestId => _settledSeekRequestId;
    private StudioReplayHeatSample[] _heatmapOverlay = Array.Empty<StudioReplayHeatSample>();
    private bool _cameraPathOverlay;
    public string LogicalPath { get; }
    public event Action? Changed;
    public IReadOnlyList<StudioReplayCameraKey> CameraKeys => _camera.Keys.Select(PublicKey).ToArray();
    public StudioReplayPerformance Performance => new(_player?.SeekRestoreFrame ?? 0, _player?.SeekSimulationSteps ?? 0,
        _hasSeek ? _player?.SeekMilliseconds : null, _player?.RejectedCheckpoints ?? 0, _player?.CheckpointSource ?? "detached preparation",
        _hasAdvance ? _advanceMilliseconds : null, _hasRender ? _renderMilliseconds : null);
    public IReadOnlyList<StudioReplayExportStatus> Exports => _exports.Select(j => j.Status).ToArray();
    public StudioReplayStatus Status => _player is { } player
        ? new(player.Ready, player.Preparing, player.Transport.CurrentFrame, player.Transport.DurationFrames,
            player.Transport.State.ToString(), player.Transport.PlaybackRate, player.Transport.ClipIn, player.Transport.ClipOut,
            player.CheckpointCount, player.CheckpointBytes, _error ?? player.LastCheckpointError,
            player.Current.Session.Metadata?.RoomKey, _resources.CustomMapRoot)
        : new(false, _preparation != null, 0, 0, _error == null ? "Preparing" : "Error", 1, null, null, 0, 0, _error, null, _resources.CustomMapRoot);
    public StudioReplayPlayer(string path, string privateCacheRoot, IEnumerable<string>? packageDirectories = null)
    {
        LogicalPath = Path.GetFullPath(path);
        _cacheRoot = Path.GetFullPath(privateCacheRoot);
        _resources = new(privateCacheRoot, packageDirectories);
        _cameraWrites = new(PersistCameraState);
        BeginPreparation();
    }
    private void BeginPreparation()
    {
        using var scope = _resources.Enter();
        _preparation = ReplayPreparationJob.Start(PrepareImmutablePlaybackSource);
    }
    /// <summary>Await detached source work without taking or adopting its result.</summary>
    public async Task WaitForPreparationAsync(CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_preparation is { } preparation) await preparation.Completion.WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
    }
    public void OnGraphicsInitialize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _owner ??= Environment.CurrentManagedThreadId; RequireOwner();
        _size = new(Math.Max(1, width), Math.Max(1, height));
        AdoptIfReady();
    }
    private void AdoptIfReady()
    {
        if (_preparation is not { Completed: true } prepared) return;
        _preparation = null;
        using var scope = _resources.Enter();
        try
        {
            using var source = prepared.TakeCompleted();
            var candidate = new PassiveReplayPlayer(source, _size, new(EnableAsyncPreparation: true));
            _sceneGraphicsGeneration = ModernGraphicsCompat.Active ? ModernGraphicsCompat.DeviceGeneration : null;
            _player = candidate; _playbackPath = source.Path;
            if (_preparedCameraState is { } cameraState)
            {
                if (!_camera.ImportState(cameraState) || _camera.WindowDuration is { } duration && duration != candidate.Transport.DurationFrames)
                { _cameraLoadError = _camera.LastError ?? "The camera sample window differs from the recording duration."; _camera.Clear(); }
                _preparedCameraState = null;
            }
            candidate.Transport.Pause();
            candidate.Transport.SetPlaybackRate(_resumeRate);
            if (_resumeIn is { } start) candidate.Transport.SetMarkIn(start);
            if (_resumeOut is { } end) candidate.Transport.SetMarkOut(end);
            if (_resumeFrame is { } frame) { candidate.Seek(frame, _resumePlaying); _resumeFrame = null; }
            else candidate.Seek(0, resume: false);
            _error = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { _error = ex.Message; }
        finally { prepared.Dispose(); Changed?.Invoke(); }
    }
    public void OnGraphicsFrame(TimeSpan elapsed, StudioReplayView view)
    {
        Advance(elapsed);
        Render(view);
    }
    /// <summary>Call once per host frame, regardless of the number of views.</summary>
    public void Advance(TimeSpan elapsed)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        RequireOwner(); AdoptIfReady();
        using var scope = _resources.Enter();
        if (_player is { } player)
        {
            while (_commands.TryDequeue(out var command)) command(player);
            if (player.Transport.IsSeeking || !player.Ready) player.Update(120, 3);
            else
            {
                _fixedAccumulator += Math.Clamp(elapsed.TotalSeconds, 0, .25);
                int ticks = 0;
                while (_fixedAccumulator >= 1d / 60 && ticks++ < 15)
                { player.Update(120); _fixedAccumulator -= 1d / 60; }
            }
        }
        if (_player is { Ready: true }) _settledSeekRequestId = _appliedSeekRequestId;
        AdvanceExports();
        _advanceMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _hasAdvance = true;
        Changed?.Invoke();
    }
    public void Render(StudioReplayView view)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        RequireOwner();
        if (_player is not { CanPresent: true } player) return;
        using var scope = _resources.Enter();
        RenderPlayer(player, view, _fixedAccumulator * 60);
        _renderMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _hasRender = true;
    }
    private void RenderPlayer(PassiveReplayPlayer player, StudioReplayView view, double hostAlpha, ReplayCameraTrack? camera = null, bool editorOverlays = true)
    {
        ReplayCameraTrack track = camera ?? _camera;
        Scene scene = player.Current.Scene;
        Vector2i output = new(Math.Max(1, view.Width), Math.Max(1, view.Height));
        if (scene.Size != output) { scene.Size = output; scene.OnResize(); }
        scene.ReplayPresentationFrame = view.PresentationFrame ?? player.Transport.PresentationFrame(hostAlpha);
        scene.ReplayRenderAlpha = view.PresentationAlpha ?? player.Transport.PresentationAlpha(hostAlpha);
        scene.StudioReplayGameHud = view.GameHud;
        scene.StudioReplayCombatRays = view.CombatRays;
        scene.StudioReplayShooterView = view.ShooterView && view.Camera == StudioReplayCameraMode.Player;
        scene.StudioReplayOverlayText = view.ReplayOverlay
            ? $"{player.Transport.State}  {ReplayHud.Time(player.Transport.CurrentFrame)}/{ReplayHud.Time(player.Transport.DurationFrames)}  {player.Transport.PlaybackRate:0.##}x  {view.Camera} P{view.PlayerSlot + 1}" : null;
        scene.StudioReplayOverlayProgress = player.Transport.DurationFrames == 0 ? 0 : (float)player.Transport.CurrentFrame / player.Transport.DurationFrames;
        scene.StudioReplayCombat = null;
        var overlay = new List<(TkVector, TkVector, float)>();
        foreach (var point in (editorOverlays ? _heatmapOverlay : Array.Empty<StudioReplayHeatSample>()).Take(64)) overlay.Add((Tk(point.Position), new(1, .4f, .15f), Math.Clamp(point.Weight / 10, .1f, .7f)));
        if (editorOverlays && _cameraPathOverlay && track.Keys.Count > 1)
        {
            uint start = track.Keys[0].Frame, end = track.Keys[^1].Frame;
            for (int i = 0; i < 48; i++)
                if (track.Sample(start + (end - start) * i / 47d, out var pathKey, view.ConstantSpeed))
                {
                    TkVector adjusted = pathKey.Position;
                    if (view.CollisionAvoidance)
                    { var anchor = track.Keys.LastOrDefault(k => k.Frame <= pathKey.Frame); adjusted = scene.AdjustStudioCameraPosition(anchor.Position, adjusted); }
                    overlay.Add((pathKey.Position, new(.7f, .3f, 1), .08f));
                    if (adjusted != pathKey.Position) overlay.Add((adjusted, new(.2f, 1, .4f), .09f));
                }
        }
        scene.StudioReplayOverlayPoints = overlay;
        if (view.Combat is { } selected && scene.ReplayPoses is { } poses)
        {
            uint offset = player.Current.Session.RecordingFrame - player.Transport.CurrentFrame;
            scene.StudioReplayCombat = poses.CombatDiagnosticsAt(selected.RecordingFrame + offset)
                .Where(d => d.Fact.ShotId == selected.ShotId && d.Fact.ShooterSlot == selected.Shooter).Cast<ReplayCombatDiagnostic?>().FirstOrDefault();
        }
        if (view.Camera == StudioReplayCameraMode.Player)
            scene.SetStudioReplayPlayerCamera(view.PlayerSlot);
        else
        {
            TkVector position = Tk(view.Position);
            TkQuaternion rotation = Tk(view.Rotation == default ? Quaternion.Identity : view.Rotation);
            float fov = view.Fov;
            TkVector up = TkVector.Transform(TkVector.UnitY, rotation);
            TkVector facing = TkVector.Transform(-TkVector.UnitZ, rotation);
            if (view.Camera == StudioReplayCameraMode.Authored && track.Sample(scene.ReplayPresentationFrame, out var key, view.ConstantSpeed))
            {
                position = key.Position; rotation = key.Rotation; fov = key.Fov * 180 / MathF.PI;
                if (view.CollisionAvoidance)
                {
                    var anchor = track.Keys.LastOrDefault(k => k.Frame <= scene.ReplayPresentationFrame);
                    position = scene.AdjustStudioCameraPosition(anchor.Position, position);
                }
                up = TkVector.Transform(TkVector.UnitY, rotation);
                facing = TkVector.Transform(-TkVector.UnitZ, rotation);
                if (key.LookAtSlot >= 0 && key.LookAtSlot < scene.Players.Items.Count)
                    facing = (scene.Players.Items[key.LookAtSlot].Position + TkVector.UnitY - position).Normalized();
                up = TkVector.Transform(up, TkQuaternion.FromAxisAngle(facing, key.Roll));
            }
            else if (view.Camera is StudioReplayCameraMode.Chase or StudioReplayCameraMode.Orbit or StudioReplayCameraMode.Overview)
            {
                var actor = scene.Players.Items[Math.Clamp(view.PlayerSlot, 0, scene.Players.Items.Count - 1)];
                var target = actor.Position + TkVector.UnitY;
                if (view.Camera == StudioReplayCameraMode.Chase) position = target - actor.FacingVector * 5 + TkVector.UnitY * 2;
                else if (view.Camera == StudioReplayCameraMode.Overview) position = target + new TkVector(0, 30, 15);
                else { float angle = (float)(scene.ReplayPresentationFrame / 240); position = target + new TkVector(MathF.Cos(angle) * 8, 4, MathF.Sin(angle) * 8); }
                facing = (target - position).Normalized(); up = TkVector.UnitY;
            }
            scene.SetStudioReplayCamera(position, position + facing, up, fov);
        }
        scene.OnDrawFrame(); scene.OnRenderFrame(); scene.AfterRenderFrame();
    }
    public StudioReplayCapture? Capture(StudioReplayView view)
    {
        RequireOwner();
        if (_player is not { CanPresent: true } player) return null;
        using var scope = _resources.Enter(); RenderPlayer(player, view, _fixedAccumulator * 60);
        return CaptureScene(player.Current.Scene);
    }
    private static StudioReplayCapture CaptureScene(Scene scene)
    {
        // The scene target deliberately excludes the 2D HUD. Explicit replay
        // captures and exports read the canonical completed native composite.
        byte[] rgb = scene.ReadWindowBuffer(out int width, out int height) ?? throw new IOException("The replay graphics target is unavailable.");
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int source = ((height - 1 - y) * width + x) * 3, target = (y * width + x) * 4;
            rgba[target] = rgb[source]; rgba[target + 1] = rgb[source + 1]; rgba[target + 2] = rgb[source + 2]; rgba[target + 3] = 255;
        }
        return new(width, height, rgba);
    }
    public void OnGraphicsDeinitialize(bool nativeReleaseEligible)
    {
        RequireOwner();
        if (_player != null)
        {
            var transport = _player.Transport;
            _resumeFrame = transport.RequestedSeekTarget ?? transport.CurrentFrame;
            _resumePlaying = transport.IsSeeking ? transport.ResumeAfterSeek : transport.State == ReplayState.Playing;
            _resumeRate = transport.PlaybackRate; _resumeIn = transport.ClipIn; _resumeOut = transport.ClipOut;
        }
        using var scope = _resources.Enter();
        bool release = nativeReleaseEligible && (_sceneGraphicsGeneration == null
            || ModernGraphicsCompat.CanReleaseNativeResources && _sceneGraphicsGeneration == ModernGraphicsCompat.DeviceGeneration);
        RenderResourceLifetime.WithNativeReleaseEligibility(release, () =>
        {
            _player?.Dispose(); _player = null;
            foreach (var job in _exports) job.ReleasePlayer();
        });
        _sceneGraphicsGeneration = null;
        if (!_disposed && _preparation == null) BeginPreparation();
    }
    private void Queue(Action<PassiveReplayPlayer> command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_commands.Count >= 256) throw new InvalidOperationException("Replay command queue is full.");
        _commands.Enqueue(command);
    }
    public void TogglePause() => Queue(p => p.Transport.TogglePause());
    public void Pause() => Queue(p => p.Transport.Pause());
    public void StepForward() => Queue(p => p.Transport.StepForward());
    public void Seek(uint frame, bool resume = false) => RequestSeek(frame, resume);
    public long RequestSeek(uint frame, bool resume = false)
    {
        long request = checked(_seekRequestId + 1);
        Queue(p => { _appliedSeekRequestId = request; _hasSeek = true; p.Seek(frame, resume); });
        _seekRequestId = request;
        return request;
    }
    // Cancellation is processed by the same owner-frame queue as seek commands.
    // An old observer cannot cancel a newer scrub, including one still queued.
    public void CancelSeek(long request) => Queue(p =>
    {
        if (request != _seekRequestId || request != _appliedSeekRequestId || request <= _settledSeekRequestId) return;
        if (p.CancelPendingSeek()) _fixedAccumulator = 0;
    });
    public StudioReplayWorldSnapshot Snapshot()
    {
        RequireOwner(); using var scope = _resources.Enter();
        if (_player is not { } player) throw new InvalidOperationException("Replay preparation is not complete.");
        uint frame = player.Transport.CurrentFrame;
        using var graph = ReplayWorldCheckpoint.Capture(player.Current);
        return new(frame, ReplayStateHash.Compute(player.Current.Scene, frame), player.Current.Scene.ReplayPresentationHash(frame),
            graph.GraphFingerprint(),
            player.Current.Scene.Players.Items.Select(p => new StudioReplayPlayerPose(p.SlotIndex, Public(p.Position),
                Public(p.FacingVector), p.LoadFlags.TestFlag(MphRead.Entities.LoadFlags.Active))).ToArray());
    }
    public void SetRate(float rate) => Queue(p => p.Transport.SetPlaybackRate(rate));
    public void MarkIn() => Queue(p => p.Transport.MarkIn());
    public void MarkOut() => Queue(p => p.Transport.MarkOut());
    public void SetRange(uint start, uint end) => Queue(p => { p.Transport.SetMarkIn(start); p.Transport.SetMarkOut(end); });
    public void SetHeatmapOverlay(IEnumerable<StudioReplayHeatSample> samples) => _heatmapOverlay = samples.Take(64).ToArray();
    public void ShowCameraPath(bool show) => _cameraPathOverlay = show;
    public IReadOnlyList<StudioReplayEvent> Events => _player?.Current.Session.Events
        .Select(e => new StudioReplayEvent(e.Frame, e.Type.ToString(), e.ActorSlot, e.TargetSlot, e.Value)).ToArray() ?? Array.Empty<StudioReplayEvent>();
    public IReadOnlyList<StudioReplayPlayerInfo> Analytics()
    {
        if (_player is not { } player) return Array.Empty<StudioReplayPlayerInfo>();
        var analysis = ReplayStudio.Analytics(player.Current.Session.Events, player.Transport.DurationFrames);
        return analysis.Players.Select(p => new StudioReplayPlayerInfo(p.Slot,
            player.Current.Session.Metadata?.Players.FirstOrDefault(n => n.Slot == p.Slot).Name ?? $"P{p.Slot + 1}", p.Kills, p.Deaths, p.Damage)).ToArray();
    }
    public IReadOnlyList<StudioReplayMarker> Markers => ReplayAnnotations.Bookmarks(LogicalPath)
        .Select(b => new StudioReplayMarker(b.Id, b.Frame, b.Frame, b.Name, "Bookmarks"))
        .Concat(ReplayAnnotations.Highlights(LogicalPath).Select(h => new StudioReplayMarker(h.Id, h.StartFrame, h.EndFrame, h.Name, "Annotations")))
        .Concat(ReplayReels.Segments(LogicalPath).Select(s => new StudioReplayMarker(s.Id, s.StartFrame, s.EndFrame, s.Name, "Reel"))).ToArray();
    public void AddBookmark(uint frame, string name) { ReplayAnnotations.AddBookmark(LogicalPath, frame, name, Status.DurationFrames); Changed?.Invoke(); }
    public void AddHighlight(uint start, uint end, string name) { ReplayAnnotations.AddHighlight(LogicalPath, start, end, name); Changed?.Invoke(); }
    public void AddReel(uint start, uint end, string name) { ReplayReels.Add(LogicalPath, start, end, name); Changed?.Invoke(); }
    public void MoveReel(Guid id, int delta) { ReplayReels.Move(LogicalPath, id, delta); Changed?.Invoke(); }
    public void TrimReel(Guid id, uint start, uint end) { ReplayReels.Trim(LogicalPath, id, start, end); Changed?.Invoke(); }
    public void RemoveMarker(StudioReplayMarker marker)
    {
        if (marker.Track == "Bookmarks") ReplayAnnotations.RemoveBookmark(LogicalPath, marker.Id);
        else if (marker.Track == "Reel") ReplayReels.Remove(LogicalPath, marker.Id);
        else ReplayAnnotations.RemoveHighlight(LogicalPath, marker.Id);
        Changed?.Invoke();
    }
    public void SetOrganization(IEnumerable<string> tags, IEnumerable<string> collections) { ReplayAnnotations.SetOrganization(LogicalPath, tags, collections); Changed?.Invoke(); }
    public bool PutCameraKey(StudioReplayCameraKey key)
    {
        RequireCameraAuthoring();
        bool changed = _camera.Edit(track => track.Put(new(key.Frame, Tk(key.Position), Tk(key.Rotation),
            key.Fov * MathF.PI / 180, (sbyte)key.LookAtSlot, key.Roll * MathF.PI / 180,
            (ReplayCameraInterpolation)key.Interpolation, (ReplayCameraEase)key.Ease,
            key.IncomingTangent is { } incoming ? Tk(incoming) : null, key.OutgoingTangent is { } outgoing ? Tk(outgoing) : null,
            key.FovIncomingTangent * MathF.PI / 180, key.FovOutgoingTangent * MathF.PI / 180,
            key.RollIncomingTangent * MathF.PI / 180, key.RollOutgoingTangent * MathF.PI / 180)));
        if (!changed && _camera.LastError != null) throw new IOException(_camera.LastError);
        if (changed) QueueCameraSave();
        Changed?.Invoke(); return changed;
    }
    public void RemoveCameraKeys(IEnumerable<uint> frames)
    {
        RequireCameraAuthoring();
        bool changed = _camera.Edit(track => track.RemoveMany(frames));
        if (!changed && _camera.LastError != null) throw new ArgumentException(_camera.LastError);
        if (changed) QueueCameraSave();
        Changed?.Invoke();
    }
    public StudioReplayCameraKey? SampleCamera(double frame, bool constantSpeed = false)
        => _camera.Sample(frame, out var sample, constantSpeed) ? PublicKey(sample) : null;
    public void TransformCameraKeys(IEnumerable<uint> frames, Vector3 translation, Quaternion rotation, int frameOffset = 0)
    {
        RequireCameraAuthoring();
        var selection = frames.ToHashSet(); var selected = _camera.Keys.Where(k => selection.Contains(k.Frame)).ToArray();
        var occupied = _camera.Keys.Where(k => !selection.Contains(k.Frame)).Select(k => k.Frame).ToHashSet();
        if (selected.Any(k => (long)k.Frame + frameOffset < 0 || (long)k.Frame + frameOffset > uint.MaxValue
            || occupied.Contains((uint)((long)k.Frame + frameOffset)))) throw new ArgumentException("The transform would overlap another camera key.");
        TkQuaternion turn = Tk(Quaternion.Normalize(rotation));
        bool changed = _camera.Edit(track =>
        {
            if (!track.RemoveMany(selection)) return false;
            foreach (var key in selected)
                if (!track.Put(key with { Frame = (uint)((long)key.Frame + frameOffset), Position = TkVector.Transform(key.Position, turn) + Tk(translation),
                    Rotation = (turn * key.Rotation).Normalized(), IncomingTangent = key.IncomingTangent is { } input ? TkVector.Transform(input, turn) : null,
                    OutgoingTangent = key.OutgoingTangent is { } output ? TkVector.Transform(output, turn) : null })) throw new ArgumentException(track.LastError);
            return true;
        });
        if (!changed && _camera.LastError != null) throw new ArgumentException(_camera.LastError);
        if (changed) QueueCameraSave();
        Changed?.Invoke();
    }
    public Task<string> ExtractClipAsync(uint start, uint end, string destination, CancellationToken cancellation = default)
    {
        if (!Status.Ready || _playbackPath == null) throw new InvalidOperationException("Wait for replay preparation before extracting a clip.");
        string source = _playbackPath;
        var jobResources = RetainJobResources();
        return Task.Run(() =>
        {
            using var ownedResources = jobResources;
            cancellation.ThrowIfCancellationRequested();
            var result = ReplayArchive.Extract(source, start, end, destination, cancellation);
            if (result != ReplayOpenResult.Success) throw new IOException("Clip extraction failed: " + result);
            return destination;
        });
    }
    public IReadOnlyList<StudioReplayCombat> CombatAt(uint frame)
    {
        RequireOwner();
        if (_player?.Current.Scene.ReplayPoses is not { } poses) return Array.Empty<StudioReplayCombat>();
        uint offset = _player.Current.Session.RecordingFrame - _player.Transport.CurrentFrame;
        return poses.CombatDiagnosticsAt(checked(frame + offset)).Select(d => new StudioReplayCombat(d.RecordingFrame >= offset ? d.RecordingFrame - offset : 0,
            d.FireRecordingFrame >= offset ? d.FireRecordingFrame - offset : 0,
            d.Fact.ShotId, d.Fact.DamageEventId, d.Fact.ShooterSlot, d.Fact.VictimSlot, d.Fact.ResolveTick, d.Fact.LaunchFrame,
            Public(d.Fire.Origin), Public(d.Fire.Aim), Public(d.Fire.Direction), Public(d.Fact.ImpactPoint), (int)d.Fact.Damage,
            d.Fact.Headshot, d.Fact.Lethal, double.IsFinite(d.AckServerFrame) ? d.AckServerFrame : null, d.HasAckTarget ? Public(d.AckTargetPosition) : null,
            ReplayCombatDiagnostics.Explain(d.Fact), d.HasPose,
            d.HasFire && d.RecordingFrame >= d.FireRecordingFrame ? (d.RecordingFrame-d.FireRecordingFrame)/60f : null,
            d.DamageDirection is { } incoming ? Public(incoming) : null)).ToArray();
    }
    private static TkVector Tk(Vector3 v) => new(v.X, v.Y, v.Z);
    private static TkQuaternion Tk(Quaternion v) => new(v.X, v.Y, v.Z, v.W);
    private static Vector3 Public(TkVector v) => new(v.X, v.Y, v.Z);
    private static StudioReplayCameraKey PublicKey(ReplayCameraKeyframe key) => new(key.Frame, Public(key.Position),
        new(key.Rotation.X, key.Rotation.Y, key.Rotation.Z, key.Rotation.W), key.Fov * 180 / MathF.PI,
        key.LookAtSlot, key.Roll * 180 / MathF.PI, (StudioReplayCameraInterpolation)key.Interpolation, (StudioReplayCameraEase)key.Ease,
        key.IncomingTangent is { } incoming ? Public(incoming) : null, key.OutgoingTangent is { } outgoing ? Public(outgoing) : null,
        key.FovIncomingTangent * 180 / MathF.PI, key.FovOutgoingTangent * 180 / MathF.PI,
        key.RollIncomingTangent * 180 / MathF.PI, key.RollOutgoingTangent * 180 / MathF.PI);
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Replay presentation requires its graphics owner thread.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        IDisposable? sourcePin = StopSnapshotOwnership();
        _preparation?.Dispose(); _preparation = null;
        using var scope = _resources.Enter();
        RenderResourceLifetime.WithNativeReleaseEligibility(false, () => { _player?.Dispose(); _player = null; foreach (var job in _exports) { job.Cancel(); job.ReleasePlayer(); job.Encoder?.Dispose(); } });
        _commands.Clear(); _disposed = true;
        sourcePin?.Dispose(); _resources.Dispose();
    }
}

#endif
