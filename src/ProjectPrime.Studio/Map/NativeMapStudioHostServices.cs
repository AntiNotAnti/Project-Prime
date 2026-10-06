using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Map;

public sealed class NativeMapStudioHostServices : IMapStudioHostServices, IDisposable, IAsyncDisposable
{
    private readonly Window _window;
    private readonly StudioJobManager _jobs;
    private readonly IStudioMapIntegration _integration;
    private readonly StudioPaths _paths;
    private readonly StudioPrivateMapRuntime _runtime;
    private readonly StudioDockLayout _layout;
    private Window? _assetWindow;
    public Window? AssetBrowserWindow => _assetWindow;
    private Window? _modalWindow;
    private readonly List<Task> _modalObservers=[];
    public Window? ModalWindow => _modalWindow;
    internal Action<string>? ReportError { get; set; }
    private bool _disposing;
    public StudioDockHost? DockHost { get; private set; }
    public event Action? LayoutChanged;
    private readonly Dictionary<Guid,MapContentIdentity> _installed = [];
    public bool IsStandalone => true;
    public bool NativeFileDialogs => true;
    public bool GameFilesReady => StudioGameAssets.Ready;
    public void ApplyGamePaths() => StudioGameAssets.Apply();
    public string MapLibraryDirectory => Path.Combine(_paths.UserDataDirectory,"map-projects");
    public string UserMapDirectory => MapLibraryDirectory;
    public string CommunitySettingsDirectory => _paths.UserDataDirectory;
    public string StagingDirectory => _paths.StagingDirectory;
    public IMapBuildScheduler BuildScheduler { get; }
    public NativeMapStudioHostServices(StudioPaths paths, Window window, StudioJobManager jobs, IStudioMapIntegration? integration = null, StudioDockLayout? layout = null)
    {
        (_paths,_window,_jobs,_integration) = (paths,window,jobs,integration ?? new UnavailableStudioMapIntegration());
        BuildScheduler = new MapBuildScheduler(paths.BuildCacheDirectory);
        _runtime = new(paths.UserDataDirectory);
        _layout=layout ?? new() { LeftWidth=260,RightWidth=340,BottomHeight=100,BottomVisible=false };
        Directory.CreateDirectory(MapLibraryDirectory);
        Directory.CreateDirectory(StagingDirectory);
        _window.Closed+=OwnerClosed;
    }
    public MapContentIdentity? GetInstalledIdentity(Guid mapId) => _installed.TryGetValue(mapId,out var identity) ? identity : null;
    public async Task<string?> PickFileAsync(string title, bool save, IReadOnlyList<string> extensions, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        FilePickerFileType type = new(title) { Patterns = extensions.Select(extension => "*" + extension).ToArray() };
        if (save)
        {
            if (!_window.StorageProvider.CanSave) throw new IOException("Native save dialogs are unavailable.");
            using IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new() { Title=title, FileTypeChoices=[type], SuggestedFileName="map" + extensions.FirstOrDefault(), ShowOverwritePrompt=true });
            cancellation.ThrowIfCancellationRequested();
            return file?.TryGetLocalPath();
        }
        if (!_window.StorageProvider.CanOpen) throw new IOException("Native file dialogs are unavailable.");
        var files = await _window.StorageProvider.OpenFilePickerAsync(new() { Title=title, AllowMultiple=false, FileTypeFilter=[type] });
        using IStorageFile? opened = files.FirstOrDefault();
        cancellation.ThrowIfCancellationRequested();
        return opened?.TryGetLocalPath();
    }
    public bool ShowModal(Control content, bool fitContent, Action dismiss)
    {
        ObjectDisposedException.ThrowIf(_disposing,this);
        DismissModal();
        var scroll=new ScrollViewer
        {
            Content=content,MaxWidth=1068,MaxHeight=788,
            HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var body=new Border {Padding=new Thickness(16),Child=scroll};
        var window=new Window
        {
            Title="Project Prime Studio · Map dialog",Width=820,Height=680,MinWidth=360,MinHeight=220,
            MaxWidth=1100,MaxHeight=820,Background=_window.Background,Content=body,ShowInTaskbar=false,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            SizeToContent=fitContent?SizeToContent.WidthAndHeight:SizeToContent.Manual
        };
        _modalWindow=window;
        window.AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            if(args.Key==Key.Escape){args.Handled=true;dismiss();}
        },RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            if(args.Handled || args.Key!=Key.Enter || args.KeyModifiers!=KeyModifiers.None
                ||args.Source is TextBox {AcceptsReturn:true})return;
            var action=content.GetLogicalDescendants().Prepend(content).OfType<Control>()
                .Where(control=>control.IsEffectivelyVisible&&control.IsEffectivelyEnabled)
                .Select(control=>control.Tag as MapStudioDialogAction).FirstOrDefault(action=>action?.IsDefault==true);
            if(action is not null){args.Handled=true;action.Invoke();}
        },RoutingStrategies.Bubble);
        window.Opened+=(_,_)=>Dispatcher.UIThread.Post(()=>
        {
            if(!ReferenceEquals(_modalWindow,window))return;
            if(window.FocusManager?.GetFocusedElement() is Control focused && focused!=window && focused!=content && TopLevel.GetTopLevel(focused)==window)return;
            var controls=content.GetVisualDescendants().Prepend(content).OfType<Control>()
                .Where(control=>control.Focusable&&control.IsEffectivelyVisible&&control.IsEffectivelyEnabled).ToArray();
            (controls.FirstOrDefault(control=>control is TextBox)??controls.FirstOrDefault())?.Focus();
        },DispatcherPriority.Background);
        window.Closing+=(_,_)=>{scroll.Content=null;body.Child=null;};
        window.Closed+=(_,_)=>
        {
            bool current=ReferenceEquals(_modalWindow,window);
            if(current)_modalWindow=null;
            scroll.Content=null;window.Content=null;body.Child=null;
            if(current&&!_disposing)dismiss();
        };
        try
        {
            _modalObservers.RemoveAll(task=>task.IsCompleted);
            _modalObservers.Add(ObserveModalAsync(window.ShowDialog(_window),window,dismiss));
            return true;
        }
        catch
        {
            if(ReferenceEquals(_modalWindow,window))_modalWindow=null;
            scroll.Content=null;window.Content=null;body.Child=null;throw;
        }
    }
    private async Task ObserveModalAsync(Task completion,Window window,Action dismiss)
    {
        try { await completion; }
        catch(Exception error)
        {
            if(ReferenceEquals(_modalWindow,window)){DismissModal();dismiss();}
            if(!_disposing)
            {
                try{ReportError?.Invoke("Map dialog could not open: "+error.Message);}
                catch{ /* An observer must not prevent native dialog teardown. */ }
            }
        }
    }
    public Control? CreateDockLayout(Control hierarchy, Control viewport, Control inspector, Control problems)
    {
        DockHost=new(_window,_layout,hierarchy,inspector,problems,
            defaults:new() {LeftWidth=260,RightWidth=340,BottomHeight=100,BottomVisible=false},leftTitle:"Scene",bottomTitle:"Problems");
        DockHost.Center.Content=viewport;
        DockHost.LayoutChanged+=()=>LayoutChanged?.Invoke();
        DockHost.AttachedToVisualTree+=(_,_)=>DockHost.RestoreFloating();
        return DockHost;
    }
    public void ShowProblems(bool visible)
    {
        if (DockHost is null || DockHost.IsRegionVisible(StudioDockRegion.Bottom)==visible) return;
        if (visible) DockHost.Show(StudioDockRegion.Bottom); else DockHost.Hide(StudioDockRegion.Bottom);
    }
    public void ToggleSidePanels()
    {
        if (DockHost is null) return;
        bool visible=DockHost.IsRegionVisible(StudioDockRegion.Left) || DockHost.IsRegionVisible(StudioDockRegion.Right);
        foreach (var region in new[] {StudioDockRegion.Left,StudioDockRegion.Right}) if (visible) DockHost.Hide(region); else DockHost.Show(region);
    }
    public bool OpenAssetBrowser(Control content)
    {
        if (_assetWindow is { } existing) { (content as IDisposable)?.Dispose(); existing.Show();existing.Activate();return true; }
        _layout.AssetsWindow.Normalize(); var state=_layout.AssetsWindow;
        bool opening=true;int savedX=state.X,savedY=state.Y;bool hadPosition=state.HasPosition;
        Window window=new() { Title="Project Prime Studio · Assets",Width=state.Width,Height=state.Height,MinWidth=300,MinHeight=240,Content=content,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        _assetWindow=window;
        window.Opened+=(_,_)=> { if(hadPosition && window.Screens.All.Any(screen=>screen.WorkingArea.Contains(new PixelPoint(savedX+40,savedY+40)))) window.Position=new(savedX,savedY); opening=false;Capture(); };
        void Capture() { if(_disposing||opening)return; state.X=window.Position.X;state.Y=window.Position.Y;state.HasPosition=true;state.Width=window.Width;state.Height=window.Height;LayoutChanged?.Invoke(); }
        window.PositionChanged+=(_,_)=>Capture(); window.SizeChanged+=(_,_)=>Capture();
        window.Closed+=(_,_)=> { Capture();window.Content=null;(content as IDisposable)?.Dispose();_assetWindow=null;if(!_disposing){state.Visible=false;LayoutChanged?.Invoke();} };
        state.Visible=true;window.Show(_window);LayoutChanged?.Invoke();return true;
    }
    public void DismissModal()
    {
        var window=_modalWindow;_modalWindow=null;
        if(window is null)return;
        // Detach preview viewports before destroying their native window.
        if(window.Content is Border body)
        {
            if(body.Child is ScrollViewer scroll)scroll.Content=null;
            body.Child=null;
        }
        window.Content=null;window.Close();
    }
    public IDisposable SubscribeFilesDropped(Action<IReadOnlyList<string>> handler)
    {
        DragDrop.SetAllowDrop(_window,true);
        void DragOver(object? sender, DragEventArgs args)
        { args.DragEffects=args.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; }
        void Drop(object? sender, DragEventArgs args)
        {
            string[] files=args.DataTransfer.TryGetFiles()?.Select(file=>file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            if (files.Length==0) return;
            handler(files); args.Handled=true;
        }
        _window.AddHandler(DragDrop.DragOverEvent,DragOver,RoutingStrategies.Bubble);
        _window.AddHandler(DragDrop.DropEvent,Drop,RoutingStrategies.Bubble);
        return new DropSubscription(()=> { _window.RemoveHandler(DragDrop.DragOverEvent,DragOver); _window.RemoveHandler(DragDrop.DropEvent,Drop); });
    }
    private sealed class DropSubscription(Action release) : IDisposable
    { private Action? _release=release; public void Dispose() => Interlocked.Exchange(ref _release,null)?.Invoke(); }
    public void SetAuthoringBackdrop() { }
    public Task<string> GetCommunityTicketAsync(bool refresh, CancellationToken cancellation) => _integration.CommunityTicketAsync(refresh,cancellation);
    public Task PublishBuildAsync(MapBuildResult result, MapDefinition definition, CancellationToken cancellation)
    {
        _runtime.PublishPrivate(result,definition,cancellation);
        return Task.CompletedTask;
    }
    public async Task<MapDefinition> CommitPackageAsync(string path, MapContentIdentity identity, CancellationToken cancellation)
    {
        var definition = await _integration.InstallAsync(path,identity,cancellation);
        _installed[identity.MapId] = identity;
        return definition;
    }
    public async Task RequestPlaytestAsync(MapProject project, CancellationToken cancellation)
    {
        Directory.CreateDirectory(_paths.PlaytestDirectory);
        string package = Path.Combine(_paths.PlaytestDirectory,Guid.NewGuid().ToString("N") + ".ppmap");
        await BuildScheduler.PackageAsync(MapBuildSnapshot.Capture(project),package,cancellation);
        try { await _integration.PlaytestAsync(package,MapContentIdentity.FromPackage(package),cancellation); }
        finally { if (File.Exists(package)) File.Delete(package); }
    }
    public Task RequestHostAsync(string path, MapContentIdentity identity, string address, CancellationToken cancellation) => _integration.HostAsync(path,identity,address,cancellation);
    public Task<MapAuditResult> AuditAsync(MapProject project, CancellationToken cancellation) => _integration.AuditAsync(project,cancellation);
    public Task RunJobAsync(string label, Func<CancellationToken, Task> work, CancellationToken cancellation)
        => _jobs.RunAsync(label,async (progress,token) => { progress.Report(new(0)); await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => work(token)); progress.Report(new(1)); return true; },cancellation);
    public Task OpenDetachedEditorAsync(string? project, CancellationToken cancellation) => Task.FromException(new InvalidOperationException("This map is already in desktop Studio. Detach an editor panel using its window button."));
    private void OwnerClosed(object? sender,EventArgs args)=>Dispose();
    public void Dispose()
    {
        if(_disposing)return;_disposing=true;_window.Closed-=OwnerClosed;
        DismissModal();_assetWindow?.Close();_assetWindow=null;DockHost?.Dispose();ReportError=null;
    }
    public async ValueTask DisposeAsync()
    {Dispose();await Task.WhenAll(_modalObservers.ToArray());_modalObservers.Clear();}
}
