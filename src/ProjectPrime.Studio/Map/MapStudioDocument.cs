using Avalonia.Controls;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;
namespace ProjectPrime.Studio.Map;

public sealed class MapStudioDocument : IStudioDocument,IStudioDocumentNotifications,IStudioRecoverableDocument,IStudioSaveAsDocument,IStudioDiscardableDocument
{
    public StudioDocumentId Id {get;}
    public StudioDocumentKind Kind=>StudioDocumentKind.Map;
    public string Title=>Host.Document?.Project.Definition.Name??"Map Studio";
    public string? Path=>Host.Document?.FilePath;
    public bool Dirty=>Host.Document?.IsDirty==true;
    public bool CanSave=>Host.Document!=null;
    public bool RequiresSaveAs=>Path==null||System.IO.Path.GetExtension(Path).Equals(".ppmap",StringComparison.OrdinalIgnoreCase);
    public StudioDocumentState State {get;private set;}=StudioDocumentState.Open;
    public string? RecoveryPath=>Host.RecoveryPath;
    public AvaloniaMapStudioHost Host {get;}
    public StudioDockHost? DockHost=>null;
    public MapBuildScheduler BuildScheduler=>MapBuildScheduler.Shared;
    public event Action? Changed;
    public event Action? CloseRequested;
    // The current canonical screen owns its retained panel layout.
    public event Action? LayoutChanged {add{} remove{}}
    private readonly StudioPaths _paths;
    private readonly IStudioMapIntegration _integration;
    private bool _disposed,_discarded;
    public MapStudioDocument(StudioPaths paths,Window window,StudioJobManager jobs,StudioDocumentId? id=null,IStudioMapIntegration? integration=null,StudioDockLayout? layout=null)
    {
        Id=id??StudioDocumentId.New();_paths=paths;_integration=integration??new UnavailableStudioMapIntegration();
        Directory.CreateDirectory(paths.UserDataDirectory);
        CustomRooms.UserMapDirectory=System.IO.Path.Combine(paths.UserDataDirectory,"user-maps");
        Directory.CreateDirectory(CustomRooms.UserMapDirectory);
        Host=new(PlaytestAsync);
        Host.ConfigureGameServices(new DelegateCommunityCredentialSource(token=>_integration.CommunityTicketAsync(false,token),token=>_integration.CommunityTicketAsync(true,token)),
            _integration.InstallAsync,_integration.HostAsync,_integration.AuditAsync);
        Host.Changed+=OnChanged;Host.CloseRequested+=OnClose;
    }
    private void OnChanged()=>Changed?.Invoke();
    private void OnClose(object? sender,EventArgs args)=>CloseRequested?.Invoke();
    private async Task PlaytestAsync(MapProject project,CancellationToken cancellation)
    {
        string root=System.IO.Path.Combine(_paths.PlaytestDirectory,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string package=await BuildScheduler.PackageAsync(MapBuildSnapshot.Capture(project),System.IO.Path.Combine(root,"playtest.ppmap"),cancellation);
            var identity=MapContentIdentity.FromPackage(package);
            await _integration.PlaytestAsync(package,identity,cancellation);
        }
        finally{Directory.Delete(root,true);}
    }
    public void NewProject(string name="Untitled Map",bool example=false)=>Host.NewProject(name,example);
    public Task OpenAsync(string path,CancellationToken cancellation=default)=>Host.OpenAsync(path,cancellation);
    public async Task OpenRecoveryAsync(string recovery,CancellationToken cancellation=default)
    {await Host.OpenRecoveryAsync(recovery,cancellation);await Host.FlushAutosaveAsync(cancellation);}
    public Task SaveAsync(string? path,CancellationToken cancellation)=>Host.SaveAsync(path??Path??throw new IOException("Choose a map save path."),cancellation);
    public Task RecoverAsync(CancellationToken cancellation){cancellation.ThrowIfCancellationRequested();Host.RestoreRecovery();return Task.CompletedTask;}
    public Task FlushAutosaveAsync(CancellationToken cancellation=default)=>Host.FlushAutosaveAsync(cancellation);
    public async Task CloseAsync(CancellationToken cancellation)
    {await Host.CancelPendingJobsAsync(cancellation);if(Dirty&&!_discarded)await Host.PreserveRecoveryAsync(cancellation);State=StudioDocumentState.Closed;}
    public async Task DiscardChangesAsync(CancellationToken cancellation)
    {await Host.CancelPendingJobsAsync(cancellation);await Host.DiscardRecoveryAsync(cancellation);_discarded=true;}
    public async ValueTask DisposeAsync()
    {
        if(_disposed)return;_disposed=true;Host.Changed-=OnChanged;Host.CloseRequested-=OnClose;
        await Host.DisposeAsync(!_discarded&&State!=StudioDocumentState.Closed);State=StudioDocumentState.Closed;
    }
}
