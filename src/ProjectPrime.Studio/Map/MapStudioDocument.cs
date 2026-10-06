using Avalonia.Controls;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Map;

public sealed class MapStudioDocument : IStudioDocument, IStudioDocumentNotifications, IStudioRecoverableDocument, IStudioSaveAsDocument, IStudioDiscardableDocument
{
    public StudioDocumentId Id { get; }
    public StudioDocumentKind Kind => StudioDocumentKind.Map;
    public string Title => Host.Document?.Project.Definition.Name ?? "Map Studio";
    public string? Path => Host.Document?.FilePath;
    public bool Dirty => Host.Document?.IsDirty == true;
    public bool CanSave => Host.Document is not null;
    public bool RequiresSaveAs => Path is null || System.IO.Path.GetExtension(Path).Equals(".ppmap",StringComparison.OrdinalIgnoreCase);
    public StudioDocumentState State { get; private set; } = StudioDocumentState.Open;
    public string? RecoveryPath => Host.RecoveryPath;
    public AvaloniaMapStudioHost Host { get; }
    public event Action? Changed;
    public event Action? CloseRequested;
    private bool _disposed, _discarded;
    private readonly NativeMapStudioHostServices _services;
    public StudioDockHost? DockHost => _services.DockHost;
    public Window? AssetBrowserWindow => _services.AssetBrowserWindow;
    public MapBuildScheduler BuildScheduler => (MapBuildScheduler)_services.BuildScheduler;
    public event Action? LayoutChanged;

    public MapStudioDocument(StudioPaths paths, Window window, StudioJobManager jobs, StudioDocumentId? id = null, IStudioMapIntegration? integration = null, StudioDockLayout? layout = null)
    {
        Id = id ?? StudioDocumentId.New();
        _services=new NativeMapStudioHostServices(paths,window,jobs,integration,layout);
        _services.LayoutChanged+=()=>LayoutChanged?.Invoke();
        Host = new(_services);
        bool restoredAssets=false;
        Host.AttachedToVisualTree+=(_,_)=> { if(!restoredAssets){restoredAssets=true;if(layout?.AssetsWindow.Visible==true)Host.OpenAssetBrowser();} };
        Host.Changed += OnChanged;
        Host.CloseRequested += OnCloseRequested;
    }
    private void OnChanged() => Changed?.Invoke();
    private void OnCloseRequested(object? sender, EventArgs e) => CloseRequested?.Invoke();
    public void NewProject(string name = "Untitled Map", bool example = false) => Host.NewProject(name,example);
    public Task OpenAsync(string path, CancellationToken cancellation = default) => Host.OpenAsync(path,cancellation);
    public async Task OpenRecoveryAsync(string recovery, CancellationToken cancellation = default)
    {
        await Host.OpenRecoveryAsync(recovery,cancellation);
        // A restored unsaved document owns a fresh recovery key. Publish its draft
        // before the session can record that key, including a second immediate crash.
        await Host.FlushAutosaveAsync(cancellation);
    }
    public Task SaveAsync(string? targetPath, CancellationToken cancellation)
        => Host.SaveAsync(targetPath ?? Path ?? throw new IOException("Choose a map project save path."),cancellation);
    public Task RecoverAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Host.RestoreRecovery(); return Task.CompletedTask; }
    public Task FlushAutosaveAsync(CancellationToken cancellation = default) => Host.FlushAutosaveAsync(cancellation);
    public async Task CloseAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        await Host.CancelPendingJobsAsync(cancellation);
        if (Dirty && !_discarded) await Host.PreserveRecoveryAsync(cancellation);
        State = StudioDocumentState.Closed;
    }
    public async Task DiscardChangesAsync(CancellationToken cancellation)
    { await Host.CancelPendingJobsAsync(cancellation);await Host.DiscardRecoveryAsync(cancellation);_discarded=true; }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Host.Changed -= OnChanged;
        Host.CloseRequested -= OnCloseRequested;
        try { await Host.DisposeAsync(!_discarded && State!=StudioDocumentState.Closed); }
        finally { State = StudioDocumentState.Closed; }
    }
}
