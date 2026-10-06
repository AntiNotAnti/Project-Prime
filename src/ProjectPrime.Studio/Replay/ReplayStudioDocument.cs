using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Replay;

public sealed class ReplayStudioDocument : IStudioDocument, IStudioDocumentNotifications, IStudioSaveAsDocument, IStudioPackageDocument, IStudioDiscardableDocument
{
    public StudioDocumentId Id { get; }
    public StudioDocumentKind Kind { get; private set; }
    public string Title => Path == null ? "Replay Studio" : System.IO.Path.GetFileName(Path);
    public string? Path { get; private set; }
    public bool Dirty => Session?.Player.CameraEditsPending == true;
    public bool CanSave => !_saving && Session?.Player.Status.Ready == true;
    public bool RequiresSaveAs => false; // Save flushes source-bound sidecars; Save As creates a selected .ppclip.
    public StudioDocumentState State { get; private set; } = StudioDocumentState.Open;
    public ReplayStudioSession? Session { get; private set; }
    public ReplayStudioWorkspace Host { get; private set; }
    public event Action? Changed;
    private bool _disposed, _saving;
    private readonly StudioPaths _paths;
    private readonly string[] _packageDirectories;
    private readonly Func<string, Task>? _exportWorkerLauncher;
    private readonly Func<string,Func<CancellationToken,Task>,CancellationToken,Task>? _jobRunner;
    public IReadOnlyList<string> PackageDirectories => _packageDirectories;
    public ReplayStudioDocument(StudioDocumentKind kind, string? path, StudioPaths paths, StudioDocumentId? id = null, IEnumerable<string>? packageDirectories = null, Func<string, Task>? exportWorkerLauncher = null,
        Func<string,Func<CancellationToken,Task>,CancellationToken,Task>? jobRunner=null)
    {
        _paths = paths; _packageDirectories = packageDirectories?.ToArray() ?? []; _exportWorkerLauncher = exportWorkerLauncher;_jobRunner=jobRunner;
        Id = id ?? StudioDocumentId.New(); Kind = kind; Path = path == null ? null : System.IO.Path.GetFullPath(path);
        if (Path != null) { Session = new(Path, paths, _packageDirectories, exportWorkerLauncher,jobRunner); Session.Changed += OnChanged; }
        Host = new(this);
    }
    public static async Task<ReplayStudioDocument> OpenAsync(StudioDocumentKind kind, string path, StudioPaths paths,
        CancellationToken cancellation = default, StudioDocumentId? id = null, IEnumerable<string>? packageDirectories = null, Func<string, Task>? exportWorkerLauncher = null,
        Func<string,Func<CancellationToken,Task>,CancellationToken,Task>? jobRunner=null)
    {
        // Inspect identity off-thread before a native viewport adopts a decoder.
        await StudioSourceDocument.InspectAsync(kind, path, null, cancellation, id);
        await MphRead.Mods.StudioReplay.StudioReplayPlayer.ValidateSourceAsync(path, cancellation);
        var document = new ReplayStudioDocument(kind, path, paths, id, packageDirectories, exportWorkerLauncher,jobRunner);
        try
        {
            await document.Session!.Player.WaitForPreparationAsync(cancellation);
            return document;
        }
        catch
        {
            await document.DisposeAsync();
            throw;
        }
    }
    private void OnChanged() => Changed?.Invoke();
    public async Task SaveAsync(string? targetPath, CancellationToken cancellation)
    {
        EnsureOpen(cancellation);
        if (_saving) throw new InvalidOperationException("This replay is already saving.");
        var previous = Session ?? throw new InvalidOperationException("Open a replay before saving a clip.");
        _saving = true; Changed?.Invoke();
        try
        {
            if (targetPath == null || string.Equals(System.IO.Path.GetFullPath(targetPath), Path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            { await previous.Player.FlushCameraEditsAsync(cancellation); EnsureOpen(cancellation); previous.SavePresentation(); return; }
            // Accepted edits flush before swapping the document. While source identity
            // validation runs off-thread, no late edit can be lost by replacing its host.
            await Host.PauseJobsAsync(cancellation);
            await previous.Player.CloseCameraAuthoringAsync(cancellation);
            ReplayStudioSession? unadopted = null;
            try
            {
                EnsureOpen(cancellation);
                var status = previous.Player.Status;
                string saved = await previous.Player.SaveClipProjectAsync(status.ClipIn ?? 0, status.ClipOut ?? status.DurationFrames, targetPath, cancellation);
                EnsureOpen(cancellation);
                var replacement = unadopted = new ReplayStudioSession(saved, _paths, _packageDirectories, _exportWorkerLauncher,_jobRunner)
                { Camera = previous.Camera, PlayerSlot = previous.PlayerSlot, Position = previous.Position, Rotation = previous.Rotation,
                    Fov = previous.Fov, GameHud = previous.GameHud, ReplayOverlay = previous.ReplayOverlay, ConstantSpeed = previous.ConstantSpeed,
                    CollisionAvoidance = previous.CollisionAvoidance, WhatShooterSaw = previous.WhatShooterSaw };
                replacement.SavePresentation();
                Host.Dispose(); previous.Changed -= OnChanged; previous.Dispose();
                Path = saved; Kind = StudioDocumentKind.ReplayClip; Session = replacement; replacement.Changed += OnChanged;
                Host = new(this); unadopted = null;
            }
            catch
            {
                unadopted?.Dispose();
                if (!_disposed && State != StudioDocumentState.Closed && ReferenceEquals(Session, previous))
                    previous.Player.ResumeCameraAuthoring();
                throw;
            }
        }
        finally { if(!_disposed&&State!=StudioDocumentState.Closed)Host.ResumeJobs();_saving = false; Changed?.Invoke(); }
    }
    private void EnsureOpen(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State == StudioDocumentState.Closed) throw new InvalidOperationException("This replay document is closed.");
    }
    public Task RecoverAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public async Task DiscardChangesAsync(CancellationToken cancellation)
    {
        try {await Host.PauseJobsAsync(cancellation);if (Session is { } session) await session.Player.DiscardCameraEditsAsync(cancellation);Changed?.Invoke();}
        finally {if(!_disposed&&State!=StudioDocumentState.Closed)Host.ResumeJobs();}
    }
    public async Task CloseAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        try {await Host.PauseJobsAsync(cancellation);if (Session is { } session) await session.Player.CloseCameraAuthoringAsync(cancellation);State = StudioDocumentState.Closed;}
        finally {if(State!=StudioDocumentState.Closed)Host.ResumeJobs();}
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await Host.PauseJobsAsync();
        if (Session is { } session) await session.Player.CloseCameraAuthoringAsync();
        _disposed = true;
        Host.Dispose(); if (Session != null) { Session.Changed -= OnChanged; Session.Dispose(); }
        State = StudioDocumentState.Closed;
    }
}
