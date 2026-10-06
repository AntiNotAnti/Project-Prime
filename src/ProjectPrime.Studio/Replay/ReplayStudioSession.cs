using System.Numerics;
using System.Text.Json;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Replay;

/// <summary>One document's transport, authoring, diagnostics, export and views.</summary>
public sealed class ReplayStudioSession : IStudioReplayGraphicsSession, IDisposable
{
    public StudioReplayPlayer Player { get; }
    public StudioReplayPlayer? ComparisonPlayer { get; private set; }
    public StudioReplayCameraMode Camera { get; set; } = StudioReplayCameraMode.Player;
    public int PlayerSlot { get; set; }
    public Vector3 Position { get; set; } = new(0, 5, 10);
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public float Fov { get; set; } = 78;
    public bool GameHud { get; set; } = true;
    public bool ReplayOverlay { get; set; }
    public bool ConstantSpeed { get; set; }
    public bool CollisionAvoidance { get; set; } = true;
    public bool CombatRays { get; set; }
    public StudioReplayCombat? CombatSelection { get; set; }
    public bool WhatShooterSaw { get; set; } = true;
    public string ExportDirectory { get; }
    private int _views;
    private bool _disposed;
    private readonly string _presentationPath;
    private readonly StudioPaths _paths;
    private bool _comparisonInitialized;
    public event Action? Changed;
    public ReplayStudioSession(string path, StudioPaths paths, IEnumerable<string>? packageDirectories = null, Func<string, Task>? exportWorkerLauncher = null)
    {
        _paths = paths;
        Player = new(path, Path.Combine(paths.BuildCacheDirectory, "replay"),
            new[] { Path.Combine(paths.InstallationDirectory, "maps") }.Concat(packageDirectories ?? []));
        ExportDirectory = Path.Combine(paths.UserDataDirectory, "replay-exports");
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path)))).ToLowerInvariant();
        _presentationPath = Path.Combine(paths.UserDataDirectory, "replay-presentation", key + ".json");
        RestorePresentation(); Player.Changed += OnChanged;
        Player.ExportWorkerLauncher = exportWorkerLauncher ?? ReplayExportWorkerHost.LaunchAsync;
    }
    private void OnChanged() => Changed?.Invoke();
    public StudioReplayView View(int width, int height) => new(width, height, Camera, PlayerSlot,
        Position, Rotation, Fov, GameHud, ReplayOverlay, ConstantSpeed: ConstantSpeed,
        CollisionAvoidance: CollisionAvoidance, Combat: CombatSelection, CombatRays: CombatRays, ShooterView: WhatShooterSaw);
    public IStudioReplayGraphicsSession SecondaryView(Func<int, int, StudioReplayView> view) => new ViewSession(this, view);
    public IStudioReplayGraphicsSession ComparisonView() => new ComparisonViewSession(this);
    public StudioReplayCapture? Capture(StudioReplayView view) => Player.Capture(view);
    public void OnGraphicsInitialize(int width, int height)
    { if (_views++ == 0) Player.OnGraphicsInitialize(width, height); }
    public void OnGraphicsFrame(TimeSpan elapsed, StudioReplayView view)
    {
        Player.OnGraphicsFrame(elapsed, view);
        if (ComparisonPlayer is { } comparison)
        {
            if (!_comparisonInitialized) { comparison.OnGraphicsInitialize(view.Width, view.Height); _comparisonInitialized = true; }
            if (comparison.Status.Ready && comparison.Status.Frame != Player.Status.Frame) comparison.Seek(Player.Status.Frame);
            comparison.Advance(TimeSpan.Zero);
        }
    }
    public void OnGraphicsDeinitialize(bool nativeReleaseEligible)
    {
        if (_views > 0 && --_views == 0)
        {
            Player.OnGraphicsDeinitialize(nativeReleaseEligible);
            if (_comparisonInitialized) ComparisonPlayer?.OnGraphicsDeinitialize(nativeReleaseEligible);
            _comparisonInitialized = false;
        }
    }
    public void LoadComparison(string path)
    {
        ComparisonPlayer?.Dispose(); ComparisonPlayer = new(path, Path.Combine(_paths.BuildCacheDirectory, "replay"),
            [Path.Combine(_paths.InstallationDirectory, "maps")]); _comparisonInitialized = false;
    }
    public StudioReplayComparison CompareCurrentFrame()
    {
        if (ComparisonPlayer is not { } comparison) throw new InvalidOperationException("Open a second replay to compare.");
        if (!comparison.Status.Ready || comparison.Status.Frame != Player.Status.Frame) throw new InvalidOperationException("The comparison replay is still seeking to the current frame.");
        return StudioReplayPlayer.Compare(Player.Snapshot(), comparison.Snapshot());
    }
    private sealed class ViewSession(ReplayStudioSession session, Func<int, int, StudioReplayView> viewFactory) : IStudioReplayGraphicsSession
    {
        public void OnGraphicsInitialize(int width, int height) => session.OnGraphicsInitialize(width, height);
        public void OnGraphicsFrame(TimeSpan elapsed, StudioReplayView view) => session.Player.Render(viewFactory(view.Width, view.Height));
        public void OnGraphicsDeinitialize(bool nativeReleaseEligible) => session.OnGraphicsDeinitialize(nativeReleaseEligible);
        public StudioReplayCapture? Capture(StudioReplayView view) => session.Player.Capture(viewFactory(view.Width, view.Height));
    }
    private sealed class ComparisonViewSession(ReplayStudioSession session) : IStudioReplayGraphicsSession
    {
        public void OnGraphicsInitialize(int width,int height) => session.OnGraphicsInitialize(width,height);
        public void OnGraphicsFrame(TimeSpan elapsed,StudioReplayView view) { if(session._comparisonInitialized)session.ComparisonPlayer?.Render(view); }
        public void OnGraphicsDeinitialize(bool nativeReleaseEligible) => session.OnGraphicsDeinitialize(nativeReleaseEligible);
        public StudioReplayCapture? Capture(StudioReplayView view) => session._comparisonInitialized ? session.ComparisonPlayer?.Capture(view) : null;
    }
    public void PutCurrentKey() => Player.PutCameraKey(new(Player.Status.Frame, Position, Rotation, Fov));
    public void SavePresentation()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_presentationPath)!);
        string staging = _presentationPath + ".staging";
        File.WriteAllText(staging, JsonSerializer.Serialize(new Presentation(Camera, PlayerSlot, Position, Rotation, Fov, GameHud, ReplayOverlay, ConstantSpeed, CollisionAvoidance, WhatShooterSaw),
            new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        File.Move(staging, _presentationPath, true);
    }
    private void RestorePresentation()
    {
        if (!File.Exists(_presentationPath)) return;
        try
        {
            var value = JsonSerializer.Deserialize<Presentation>(File.ReadAllText(_presentationPath), new JsonSerializerOptions { IncludeFields = true });
            if (value == null) return;
            if (!Enum.IsDefined(value.Camera) || value.PlayerSlot is < 0 or > 7 || !float.IsFinite(value.Position.LengthSquared())
                || !float.IsFinite(value.Rotation.LengthSquared()) || value.Rotation.LengthSquared() < .00001f || !float.IsFinite(value.Fov) || value.Fov is < 10 or > 150) return;
            Camera = value.Camera; PlayerSlot = value.PlayerSlot; Position = value.Position; Rotation = Quaternion.Normalize(value.Rotation);
            Fov = value.Fov; GameHud = value.GameHud; ReplayOverlay = value.ReplayOverlay; ConstantSpeed = value.ConstantSpeed;
            CollisionAvoidance = value.CollisionAvoidance; WhatShooterSaw = value.WhatShooterSaw;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }
    private sealed record Presentation(StudioReplayCameraMode Camera, int PlayerSlot, Vector3 Position,
        Quaternion Rotation, float Fov, bool GameHud, bool ReplayOverlay, bool ConstantSpeed, bool CollisionAvoidance = true, bool WhatShooterSaw = true);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Player.Changed -= OnChanged; Player.Dispose(); ComparisonPlayer?.Dispose();
    }
}
