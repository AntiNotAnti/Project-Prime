using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Replay;

public sealed class ReplayStudioDocument : IStudioDocument, IStudioDocumentNotifications, IStudioSaveAsDocument, IStudioPackageDocument
{
    public StudioDocumentId Id { get; }
    public StudioDocumentKind Kind { get; private set; }
    public string Title => Path == null ? "Replay Studio" : System.IO.Path.GetFileName(Path);
    public string? Path { get; private set; }
    public bool Dirty => false; // Authoritative camera/annotation/reel sidecars save transactionally on edit.
    public bool CanSave => Session?.Player.Status.Ready == true;
    public bool RequiresSaveAs => Kind != StudioDocumentKind.ReplayClip;
    public StudioDocumentState State { get; private set; } = StudioDocumentState.Open;
    public ReplayStudioSession? Session { get; private set; }
    public ReplayStudioWorkspace Host { get; private set; }
    public event Action? Changed;
    private bool _disposed;
    private readonly StudioPaths _paths;
    private readonly string[] _packageDirectories;
    private readonly Func<string, Task>? _exportWorkerLauncher;
    public IReadOnlyList<string> PackageDirectories => _packageDirectories;
    public ReplayStudioDocument(StudioDocumentKind kind, string? path, StudioPaths paths, StudioDocumentId? id = null, IEnumerable<string>? packageDirectories = null, Func<string, Task>? exportWorkerLauncher = null)
    {
        _paths = paths; _packageDirectories = packageDirectories?.ToArray() ?? []; _exportWorkerLauncher = exportWorkerLauncher;
        Id = id ?? StudioDocumentId.New(); Kind = kind; Path = path == null ? null : System.IO.Path.GetFullPath(path);
        if (Path != null) { Session = new(Path, paths, _packageDirectories, exportWorkerLauncher); Session.Changed += OnChanged; }
        Host = new(this);
    }
    public static async Task<ReplayStudioDocument> OpenAsync(StudioDocumentKind kind, string path, StudioPaths paths,
        CancellationToken cancellation = default, StudioDocumentId? id = null, IEnumerable<string>? packageDirectories = null, Func<string, Task>? exportWorkerLauncher = null)
    {
        // Inspect identity off-thread before a native viewport adopts a decoder.
        await StudioSourceDocument.InspectAsync(kind, path, null, cancellation, id);
        await MphRead.Mods.StudioReplay.StudioReplayPlayer.ValidateSourceAsync(path, cancellation);
        return new(kind, path, paths, id, packageDirectories, exportWorkerLauncher);
    }
    private void OnChanged() => Changed?.Invoke();
    public async Task SaveAsync(string? targetPath, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var previous = Session ?? throw new InvalidOperationException("Open a replay before saving a clip.");
        if (targetPath == null || Kind == StudioDocumentKind.ReplayClip && string.Equals(System.IO.Path.GetFullPath(targetPath), Path,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { previous.SavePresentation(); return; }
        var status = previous.Player.Status;
        string saved = await previous.Player.SaveClipProjectAsync(status.ClipIn ?? 0, status.ClipOut ?? status.DurationFrames, targetPath, cancellation);
        var replacement = new ReplayStudioSession(saved, _paths, _packageDirectories, _exportWorkerLauncher)
        { Camera = previous.Camera, PlayerSlot = previous.PlayerSlot, Position = previous.Position, Rotation = previous.Rotation,
            Fov = previous.Fov, GameHud = previous.GameHud, ReplayOverlay = previous.ReplayOverlay, ConstantSpeed = previous.ConstantSpeed,
            CollisionAvoidance = previous.CollisionAvoidance, WhatShooterSaw = previous.WhatShooterSaw };
        replacement.SavePresentation();
        Host.Dispose(); previous.Changed -= OnChanged; previous.Dispose();
        Path = saved; Kind = StudioDocumentKind.ReplayClip; Session = replacement; replacement.Changed += OnChanged;
        Host = new(this); Changed?.Invoke();
    }
    public Task RecoverAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); State = StudioDocumentState.Closed; return Task.CompletedTask; }
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask; _disposed = true;
        Host.Dispose(); if (Session != null) { Session.Changed -= OnChanged; Session.Dispose(); }
        State = StudioDocumentState.Closed; return ValueTask.CompletedTask;
    }
}
