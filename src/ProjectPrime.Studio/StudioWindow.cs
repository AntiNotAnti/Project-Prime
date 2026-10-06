using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Interactivity;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Diagnostics;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using MphRead.AvaloniaShared;

namespace ProjectPrime.Studio;

public sealed class StudioWindow : Window
{
    public StudioDocumentHost Documents { get; } = new();
    public StudioJobManager Jobs { get; } = new();
    public ReplayExportWorkerCoordinator ExportWorkers { get; }
    public StudioCommandRouter Commands { get; }
    public StudioDockHost DockHost { get; }
    private readonly StudioSettings _settings;
    private readonly StudioPaths _paths;
    public IStudioMapIntegration MapIntegration { get; set; }
    private readonly StudioSettingsStore _store;
    private readonly StudioLog _log;
    private readonly StudioRecentDocuments _recent;
    private readonly StudioSession? _previous;
    private readonly StudioOpenRequest _initialRequest;
    private readonly StudioShell _shell;
    private readonly Border _homeToolbar;
    private readonly Panel _hudLayer = new();
    private StudioPerformanceHud? _performanceHud;
    public StudioPerformanceHud? PerformanceHud => _performanceHud;
    private Window? _performanceWindow;
    public Window? PerformanceHudWindow => _performanceWindow;
    private readonly StackPanel _inspector = new() { Spacing = 12, Margin = new Thickness(12) };
    private readonly StackPanel _jobList = new() { Spacing = 6, Margin = new Thickness(10) };
    private StackPanel? _floatingJobList;
    private Window? _jobsWindow;
    public Window? JobsWindow => _jobsWindow;
    private readonly TextBlock _status = new() { Name = "StudioStatus", Text = "Ready · No game assets loaded", VerticalAlignment = VerticalAlignment.Center };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _openGate = new(1);
    private readonly DispatcherTimer _pulse;
    private bool _allowClose, _closing, _initialized, _disposed, _restored, _restoring;
    private Task? _initialization;
    private Task? _disposal;
    private readonly List<Task> _queuedRequests = [];

    public StudioWindow(StudioPaths paths, StudioSettings settings, StudioOpenRequest initialRequest, StudioSession? previousSession = null)
    {
        _settings = settings;
        _paths = paths;
        ExportWorkers = new(Jobs, ReplayExportWorkerHost.LaunchAsync);
        MapIntegration = new GameBrokerMapIntegration(paths);
        _store = new(paths);
        _log = new(paths.LogFile);
        try { if (settings.GamePathsFile is { Length:>0 } configured) StudioGameAssets.Configure(configured); else StudioGameAssets.TryConfigureDefault(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { _log.Write("Asset configuration: " + ex.Message); }
        _recent = new(settings.RecentDocuments);
        _initialRequest = initialRequest;
        _previous = previousSession;
        if (initialRequest.SafeMode) { settings.Layout = new(); settings.MapLayout=new() { LeftWidth=260,RightWidth=340,BottomHeight=100,BottomVisible=false }; }
        Title = "Project Prime Studio";
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        MinWidth = 900;
        MinHeight = 600;
        Background = new SolidColorBrush(Color.Parse("#101827"));
        Commands = new(() => new(Documents.ActiveDocument));
        RegisterCommands();
        Commands.Failed += ex => ShowError(ex.Message);
        _shell = new(Documents, _recent, Commands, OpenRecentAsync, CloseDocumentAsync, RestoreSessionSafelyAsync, previousSession?.Documents.Count > 0 && !initialRequest.SafeMode);
        TabControl left = new();
        left.Items.Add(new TabItem
        {
            Header = "Documents", Content = new TextBlock { Text = "Use the center tabs to switch between open map and replay documents.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), LineHeight = 22 }
        });
        left.Items.Add(new TabItem
        {
            Header = "Workflow", Content = new TextBlock { Text = "Save source projects in Studio. Project Prime installs runtime maps and runs external playtests while Studio stays open.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), LineHeight = 22 }
        });
        DockHost = new(this, settings.Layout, left, new ScrollViewer { Content = _inspector }, new ScrollViewer { Content = _jobList });
        DockHost.Center.Content = _shell;
        DockHost.LayoutChanged += PersistSettings;
        DockPanel root = new();
        Menu menu = CreateMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
        Border toolbar = _homeToolbar = new() { Padding = new Thickness(12, 8), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.Parse("#2C3A50")) };
        DockPanel tools = new();
        Button home = new() { Content = "Studio Home", HorizontalAlignment = HorizontalAlignment.Left };
        home.Click += (_, _) => Documents.Select(null);
        DockPanel.SetDock(home, Dock.Left);
        tools.Children.Add(home);
        tools.Children.Add(new TextBlock { Text = "CREATOR WORKSPACES", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#79C9EF")), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        toolbar.Child = tools;
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        Border status = new() { Padding = new Thickness(12, 6), Child = _status, Background = new SolidColorBrush(Color.Parse("#0D1420")) };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(DockHost);
        Grid overlay = new(); overlay.Children.Add(root); overlay.Children.Add(_hudLayer);
        Content = overlay;
        AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DragOverEvent,(_,args)=> { if(args.DataTransfer.Contains(DataFormat.File))args.DragEffects=DragDropEffects.Copy; },RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent,HandleDroppedSources,RoutingStrategies.Tunnel);
        Documents.Changed += DocumentsChanged;
        Jobs.Changed += JobsChanged;
        _pulse = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pulse.Tick += (_, _) => { RefreshJobs(); RefreshPerformanceHudPlacement(); };
        _pulse.Start();
        Closing += (_, e) => { if (_allowClose) return; e.Cancel = true; if (!_closing) _ = CloseWindowAsync(); };
        Opened += (_, _) => { DockHost.RestoreFloating(); _ = InitializeSafelyAsync(); };
        Closed += (_, _) => _ = DisposeSafelyAsync();
        RefreshInspector();
        RefreshJobs();
    }

    public Task InitializeAsync(bool promptForRecovery = true) => _initialization ??= InitializeCoreAsync(promptForRecovery);
    private async Task InitializeSafelyAsync()
    {
        try { await InitializeAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError("Studio initialization failed: " + ex.Message); }
    }
    private async Task DisposeSafelyAsync()
    {
        try { await DisposeResourcesAsync(); }
        catch (Exception ex) { _log.Write("Studio resource disposal failed: " + ex.Message); }
    }
    private async Task InitializeCoreAsync(bool promptForRecovery)
    {
        if (_initialized) return;
        _initialized = true;
        _log.Write("Studio desktop started with canonical map and replay document hosts.");
        _log.Write(StudioStartupMetrics.RecordShellOpened());
        if (!_initialRequest.SafeMode)
        {
            try { ExportWorkers.RestorePersisted(Path.Combine(_paths.BuildCacheDirectory, "replay", "exports")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { ShowError("Could not resume export jobs: " + ex.Message); }
        }
        bool restoredClean=false;
        if (!_initialRequest.SafeMode && _previous is { CleanExit: false, Documents.Count: > 0 } && !_initialRequest.Recover && promptForRecovery)
        {
            bool restore = await ConfirmAsync("Restore previous Studio session?", "Studio did not close normally. Reopen previous documents and restore available autosave drafts?", "Restore");
            if (restore) { await RestoreSessionAsync();restoredClean=_initialRequest.Kind==StudioOpenKind.Home; }
        }
        if(!_initialRequest.SafeMode && !_initialRequest.Recover && _initialRequest.Kind==StudioOpenKind.Home && _settings.ReopenLastSession && _previous is {CleanExit:true,Documents.Count:>0})
        { await RestoreSessionAsync(); restoredClean=true; }
        if(!restoredClean)await HandleLaunchRequestAsync(_initialRequest, _lifetime.Token);
        if (!_closing && !_disposed) PersistSession(false);
    }

    public async Task<StudioRequestResult> HandleLaunchRequestAsync(StudioOpenRequest request, CancellationToken cancellationToken = default)
    {
        if (_closing || _disposed) return StudioRequestResult.Rejected("Studio is closing.");
        if (request.Validate() is { } error) return StudioRequestResult.Rejected(error);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        bool entered = false;
        try
        {
            await _openGate.WaitAsync(linked.Token);
            entered = true;
            linked.Token.ThrowIfCancellationRequested();
            if (request.Recover && !request.SafeMode) await RestoreSessionCoreAsync(linked.Token);
            if (request.Kind == StudioOpenKind.Home) { if (!request.Recover) Documents.Select(null); FocusWindow(); return StudioRequestResult.Success; }
            StudioDocumentKind kind = request.Kind switch { StudioOpenKind.Map => StudioDocumentKind.Map, StudioOpenKind.Replay => StudioDocumentKind.Replay, _ => StudioDocumentKind.ReplayClip };
            await OpenSourceCoreAsync(kind, request.Path!, linked.Token);
            FocusWindow();
            return StudioRequestResult.Success;
        }
        catch (OperationCanceledException) { return StudioRequestResult.Rejected("Open request cancelled."); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { ShowError(ex.Message); return StudioRequestResult.Rejected(ex.Message); }
        finally { if (entered) _openGate.Release(); }
    }

    /// <summary>IPC acceptance is bounded to validation and enqueue. Inspection remains an observable desktop job.</summary>
    public StudioRequestResult EnqueueLaunchRequest(StudioOpenRequest request, CancellationToken cancellationToken = default)
    {
        if (_closing || _disposed) return StudioRequestResult.Rejected("Studio is closing.");
        if (cancellationToken.IsCancellationRequested) return StudioRequestResult.Rejected("Open request cancelled.");
        if (request.SafeMode && !_initialRequest.SafeMode) return StudioRequestResult.Rejected("Close the existing Studio before starting safe mode.");
        if (request.Validate() is { } error) return StudioRequestResult.Rejected(error);
        if (request.Kind != StudioOpenKind.Home)
        {
            StudioDocumentKind kind = request.Kind switch { StudioOpenKind.Map => StudioDocumentKind.Map, StudioOpenKind.Replay => StudioDocumentKind.Replay, _ => StudioDocumentKind.ReplayClip };
            if (!SupportsOpen(kind, request.Path!)) return StudioRequestResult.Rejected("This file extension does not match the requested workspace.");
            if (!File.Exists(request.Path)) return StudioRequestResult.Rejected("The requested source file does not exist or cannot be accessed.");
        }
        _queuedRequests.RemoveAll(task => task.IsCompleted);
        _queuedRequests.Add(ProcessQueuedRequestAsync(request));
        FocusWindow();
        return StudioRequestResult.Success;
    }
    private async Task ProcessQueuedRequestAsync(StudioOpenRequest request)
    {
        try { await HandleLaunchRequestAsync(request, _lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void FocusWindow()
    {
        if (!IsVisible) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void OpenEmptyWorkspace(StudioDocumentKind kind)
    {
        if (_closing) return;
        IStudioDocument? existing = Documents.Documents.FirstOrDefault(doc => doc.Kind == kind && doc.Path is null);
        if (existing is not null) Documents.Select(existing);
        else Documents.Add(StudioSourceDocument.Empty(kind));
    }

    private static bool IsPortableReplay(string path) => path.EndsWith(".ppreplay.zip",StringComparison.OrdinalIgnoreCase);
    private static bool SupportsOpen(StudioDocumentKind kind,string path) => StudioSourceDocument.Supports(kind,path) || kind==StudioDocumentKind.Replay && IsPortableReplay(path);
    private async Task OpenSourceCoreAsync(StudioDocumentKind kind, string path, CancellationToken cancellationToken, StudioDocumentId? id = null, IEnumerable<string>? packageDirectories = null)
    {
        path = Path.GetFullPath(path);
        if (!SupportsOpen(kind,path)) throw new InvalidDataException("This file extension does not match the requested workspace.");
        if (Documents.Find(kind, path) is { } existing) { Documents.Select(existing); return; }
        if(kind==StudioDocumentKind.Replay && IsPortableReplay(path))
        {
            var imported=await Jobs.RunAsync("Import portable replay",async(progress,token)=>
            {progress.Report(new(0));var result=await MphRead.Mods.StudioReplay.StudioReplayPlayer.ImportPortableAsync(path,Path.Combine(_paths.BuildCacheDirectory,"imports"),token);progress.Report(new(1));return result;},cancellationToken);
            path=imported.ReplayPath;packageDirectories=imported.PackageDirectories;
        }
        IStudioDocument document;
        if (kind == StudioDocumentKind.Map)
        {
            var map = CreateMapDocument(id);
            try { await Jobs.RunAsync("Open map " + Path.GetFileName(path),async(progress,token)=> { progress.Report(new(0)); await Dispatcher.UIThread.InvokeAsync(()=>map.OpenAsync(path,token)); progress.Report(new(1)); return true; },cancellationToken); document=map; }
            catch { await map.DisposeAsync(); throw; }
        }
        else
        {
            ReplayStudioDocument? preparedReplay=null;
            try
            {
                document=await Jobs.RunAsync("Prepare replay " + Path.GetFileName(path),async(progress,token)=>
                {
                    progress.Report(new(0));
                    preparedReplay=await Dispatcher.UIThread.InvokeAsync(()=>ReplayStudioDocument.OpenAsync(kind,path,_paths,token,id,packageDirectories,ExportWorkers.LaunchAsync,RunReplayJobAsync));
                    progress.Report(new(1));return preparedReplay;
                },cancellationToken);
            }
            catch
            {
                // The job manager can observe cancellation after preparation returns.
                // Keep the UI-affine owner reachable until successful adoption.
                if(preparedReplay is not null)await preparedReplay.DisposeAsync();
                throw;
            }
        }
        // Publish only after successful inspection. Failed opens leave the current tab intact.
        if (_closing || cancellationToken.IsCancellationRequested) { await document.DisposeAsync(); cancellationToken.ThrowIfCancellationRequested(); return; }
        Documents.Add(document);
        _recent.Add(document);
        PersistSettings();
        _status.Text = "Opened " + document.Title;
        _shell.Refresh();
    }
    private Task RunReplayJobAsync(string label,Func<CancellationToken,Task> work,CancellationToken cancellation)
        =>Jobs.RunAsync(label,async(progress,token)=>
        {
            progress.Report(new(0));
            await Dispatcher.UIThread.InvokeAsync(()=>work(token));
            progress.Report(new(1));return true;
        },cancellation);
    private MapStudioDocument CreateMapDocument(StudioDocumentId? id = null)
    {
        MapStudioDocument map = new(_paths,this,Jobs,id,MapIntegration,_settings.MapLayout);
        map.CloseRequested += () => _ = CloseDocumentAsync(map);
        map.LayoutChanged += PersistSettings;
        return map;
    }
    public void NewMapProject(string name = "Untitled Map", bool example = false)
    {
        if (_closing) return;
        var map=CreateMapDocument(); map.NewProject(name,example); Documents.Add(map);
    }
    public void NewReplayWorkspace()
    {
        if (_closing) return;
        Documents.Add(new ProjectPrime.Studio.Replay.ReplayStudioDocument(StudioDocumentKind.Replay,null,_paths,exportWorkerLauncher:ExportWorkers.LaunchAsync,jobRunner:RunReplayJobAsync));
    }
    private async Task OpenRecentAsync(StudioRecentDocument recent)
    {
        StudioOpenKind kind = recent.Kind == StudioDocumentKind.Map ? StudioOpenKind.Map : recent.Kind == StudioDocumentKind.Replay ? StudioOpenKind.Replay : StudioOpenKind.Clip;
        await HandleLaunchRequestAsync(new(Guid.NewGuid(), kind, recent.Path));
    }
    private async Task OpenDialogAsync(StudioDocumentKind kind)
    {
        string title = kind == StudioDocumentKind.Map ? "Open map source" : kind == StudioDocumentKind.Replay ? "Open replay" : "Open replay clip";
        string[] patterns = kind == StudioDocumentKind.Map ? ["*.json", "*.ppmap"] : kind == StudioDocumentKind.Replay ? ["*.ppdemo","*.ppreplay.zip"] : ["*.ppclip"];
        if (!StorageProvider.CanOpen) { ShowError("Native file dialogs are unavailable on this platform."); return; }
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = true, FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }] });
        foreach (IStorageFile file in files)
        {
            using (file)
            {
                string? path = file.TryGetLocalPath();
                if (path is null) { ShowError("This workspace requires a local source file."); continue; }
                StudioOpenKind openKind = kind == StudioDocumentKind.Map ? StudioOpenKind.Map : kind == StudioDocumentKind.Replay ? StudioOpenKind.Replay : StudioOpenKind.Clip;
                await HandleLaunchRequestAsync(new(Guid.NewGuid(), openKind, path));
            }
        }
    }
    private async Task<string?> PickSavePathAsync(IStudioDocument document)
    {
        if (!StorageProvider.CanSave) throw new IOException("Native save dialogs are unavailable.");
        string extension=document.Kind == StudioDocumentKind.Map ? ".json" : ".ppclip";
        using IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Save " + document.Title, SuggestedFileName = Path.GetFileNameWithoutExtension(document.Title)+extension,
            FileTypeChoices=[new FilePickerFileType("Editable project") { Patterns=["*"+extension] }], DefaultExtension=extension, ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
    private async Task SaveAsync(IStudioDocument document, bool saveAs)
    {
        string? path = saveAs || document is IStudioSaveAsDocument { RequiresSaveAs:true } || document.Path is null ? await PickSavePathAsync(document) : document.Path;
        if (path is null) return;
        await document.SaveAsync(path, _lifetime.Token);
        DocumentsChanged();
    }
    private async Task CloseDocumentAsync(IStudioDocument document)
    {
        try { await Documents.RequestCloseAsync(document, DecideCloseAsync, PickSavePathAsync, _lifetime.Token); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private async Task<StudioCloseDecision> DecideCloseAsync(IStudioDocument document)
    {
        Window dialog = CreateDialog("Unsaved changes", $"Save changes to {document.Title} before closing?");
        TaskCompletionSource<StudioCloseDecision> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StackPanel buttons = (StackPanel)((StackPanel)dialog.Content!).Children[^1];
        AddDialogButton(buttons, "Cancel", () => { result.TrySetResult(StudioCloseDecision.Cancel); dialog.Close(); });
        AddDialogButton(buttons, "Discard", () => { result.TrySetResult(StudioCloseDecision.Discard); dialog.Close(); });
        if (document.CanSave) AddDialogButton(buttons, "Save", () => { result.TrySetResult(StudioCloseDecision.Save); dialog.Close(); });
        dialog.Closed += (_, _) => result.TrySetResult(StudioCloseDecision.Cancel);
        await dialog.ShowDialog(this);
        return await result.Task;
    }
    public async Task<bool> TryCloseAsync()
    {
        if (_allowClose) return true;
        if (_closing) return false;
        _closing = true;
        StudioSession session = SnapshotSession(true);
        IStudioDocument[] previousDocuments=Documents.Documents.ToArray();
        try
        {
            if (!await Documents.RequestCloseAllAsync(DecideCloseAsync, PickSavePathAsync, _lifetime.Token)) { _closing = false; return false; }
            session.Documents=previousDocuments.Where(doc=>doc is not MapStudioDocument || doc.Path is not null || (doc as IStudioRecoverableDocument)?.RecoveryPath is {} recovery && File.Exists(recovery)).Select(doc=>new StudioDocumentSnapshot(doc.Id.Value,doc.Kind,doc.Path,(doc as IStudioRecoverableDocument)?.RecoveryPath,(doc as IStudioPackageDocument)?.PackageDirectories.ToArray())).ToList();
            _lifetime.Cancel();
            ExportWorkers.DetachForShutdown();
            await Jobs.DisposeAsync();
            PersistSettings();
            if (!_initialRequest.SafeMode && !_store.SaveSession(session)) ShowError("Session could not be saved: " + _store.LastError);
            // Keep the dispatcher alive while pending opens and native resources drain.
            await DisposeResourcesAsync();
            _allowClose = true;
            return true;
        }
        catch (Exception ex) { _closing = false; ShowError(ex.Message); return false; }
    }
    private async Task CloseWindowAsync() { if (await TryCloseAsync()) Close(); }
    public Task DisposeResourcesAsync() => _disposal ??= DisposeResourcesCoreAsync();
    private async Task DisposeResourcesCoreAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _pulse.Stop();
        Documents.Changed -= DocumentsChanged;
        Jobs.Changed -= JobsChanged;
        DockHost.Dispose();
        ClosePerformanceHud();
        _jobsWindow?.Close();_jobsWindow=null;_floatingJobList=null;
        ExportWorkers.DetachForShutdown();
        await Jobs.DisposeAsync();
        await Task.WhenAll(_queuedRequests.ToArray());
        try { await Documents.DisposeAsync(); }
        finally
        {
            try { StudioGraphicsHost.Shutdown(); }
            finally
            {
                try { if (MapIntegration is IDisposable integration) integration.Dispose(); }
                finally { _lifetime.Dispose();_log.Write("Studio desktop resources disposed."); }
            }
        }
    }
    public async Task RestoreSessionAsync()
    {
        if (_closing || _initialRequest.SafeMode) return;
        await _openGate.WaitAsync(_lifetime.Token);
        try { await RestoreSessionCoreAsync(_lifetime.Token); }
        finally { _openGate.Release(); }
    }
    private async Task RestoreSessionSafelyAsync()
    {
        try { await RestoreSessionAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError("Session restore failed: " + ex.Message); }
    }
    private async Task RestoreSessionCoreAsync(CancellationToken cancellationToken)
    {
        if (_restored || _previous is null) return;
        _restoring = true;
        try
        {
        foreach (StudioDocumentSnapshot snapshot in _previous.Documents.Take(50))
        {
            if (snapshot is null || Documents.Documents.Any(doc => doc.Id.Value == snapshot.Id)) continue;
            try
            {
                if (!Enum.IsDefined(snapshot.Kind)) continue;
                if (snapshot.Kind == StudioDocumentKind.Map && snapshot.RecoveryPath is { } recovery && File.Exists(recovery))
                { var map=CreateMapDocument(new(snapshot.Id)); try { await map.OpenRecoveryAsync(recovery,cancellationToken); Documents.Add(map); } catch { await map.DisposeAsync(); throw; } }
                else if (snapshot.Path is null) Documents.Add(snapshot.Kind is StudioDocumentKind.Replay or StudioDocumentKind.ReplayClip
                    ? new ProjectPrime.Studio.Replay.ReplayStudioDocument(snapshot.Kind,null,_paths,new(snapshot.Id),exportWorkerLauncher:ExportWorkers.LaunchAsync,jobRunner:RunReplayJobAsync) : StudioSourceDocument.Empty(snapshot.Kind,new(snapshot.Id)));
                else await OpenSourceCoreAsync(snapshot.Kind, snapshot.Path, cancellationToken, new(snapshot.Id), snapshot.PackageDirectories?.Where(path=>path is not null && Path.IsPathFullyQualified(path)).Take(16));
            }
            catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { ShowError("Could not restore source: " + ex.Message); }
        }
        if (_previous.SelectedDocument is { } selected && Documents.Documents.FirstOrDefault(doc => doc.Id.Value == selected) is { } active) Documents.Select(active);
        _restored = true;
        }
        finally
        {
            _restoring = false;
            if(_restored && !_closing && !_disposed)PersistSession(false);
        }
    }
    private void RegisterCommands()
    {
        Commands.Register(StudioCommand.OpenMap, _ => OpenDialogAsync(StudioDocumentKind.Map));
        Commands.Register(StudioCommand.OpenReplay, _ => OpenDialogAsync(StudioDocumentKind.Replay));
        Commands.Register(StudioCommand.OpenClip, _ => OpenDialogAsync(StudioDocumentKind.ReplayClip));
        Commands.Register(StudioCommand.NewMapWorkspace, _ => { NewMapProject(); return Task.CompletedTask; });
        Commands.Register(StudioCommand.NewReplayWorkspace, _ => { NewReplayWorkspace(); return Task.CompletedTask; });
        Commands.Register(StudioCommand.Save, ctx => SaveAsync(ctx.Document!, false), ctx => ctx.Document is { CanSave: true, Dirty: true });
        Commands.Register(StudioCommand.SaveAs, ctx => SaveAsync(ctx.Document!, true), ctx => ctx.Document is { CanSave: true });
        Commands.Register(StudioCommand.Close, ctx => CloseDocumentAsync(ctx.Document!), ctx => ctx.Document is not null);
        Commands.Register(StudioCommand.RestoreLayout, ctx => { (ctx.Document is MapStudioDocument map ? map.DockHost : DockHost)?.RestoreDefaults(); return Task.CompletedTask; });
        Commands.Register(StudioCommand.ClearRecent, _ => { _recent.Clear(); PersistSettings(); _shell.Refresh(); return Task.CompletedTask; });
        Commands.Register(StudioCommand.Undo, ctx => { ((MapStudioDocument)ctx.Document!).Host.Undo(); return Task.CompletedTask; },ctx => ctx.Document is MapStudioDocument map && map.Host.Document?.History.CanUndo == true);
        Commands.Register(StudioCommand.Redo, ctx => { ((MapStudioDocument)ctx.Document!).Host.Redo(); return Task.CompletedTask; },ctx => ctx.Document is MapStudioDocument map && map.Host.Document?.History.CanRedo == true);
        Commands.Register(StudioCommand.MapValidate, ctx => ((MapStudioDocument)ctx.Document!).Host.ValidateAsync(),ctx => ctx.Document is MapStudioDocument {CanSave:true});
        Commands.Register(StudioCommand.MapBuild, ctx => ((MapStudioDocument)ctx.Document!).Host.BuildAsync(),ctx => ctx.Document is MapStudioDocument {CanSave:true});
        Commands.Register(StudioCommand.MapPlaytest, ctx => ((MapStudioDocument)ctx.Document!).Host.PlaytestAsync(),ctx => ctx.Document is MapStudioDocument {CanSave:true});
        Commands.Register(StudioCommand.FrameAll, ctx => { ((MapStudioDocument)ctx.Document!).Host.FrameAll(); return Task.CompletedTask; },ctx => ctx.Document is MapStudioDocument {CanSave:true});
        Commands.Register(StudioCommand.ToggleWireframe, ctx => { ((MapStudioDocument)ctx.Document!).Host.ToggleWireframe(); return Task.CompletedTask; },ctx => ctx.Document is MapStudioDocument {CanSave:true});
        Commands.Register(StudioCommand.ReplayPlayPause, ctx => { ((ReplayStudioDocument)ctx.Document!).Session!.Player.TogglePause(); return Task.CompletedTask; },ctx => ctx.Document is ReplayStudioDocument {Session:not null});
        Commands.Register(StudioCommand.ReplayMarkIn, ctx => { ((ReplayStudioDocument)ctx.Document!).Session!.Player.MarkIn(); return Task.CompletedTask; },ctx => ctx.Document is ReplayStudioDocument {Session:not null});
        Commands.Register(StudioCommand.GlobalSearch, async _ => await new StudioGlobalSearchWindow(Documents,Commands,_settings).ShowDialog(this));
        Commands.Register(StudioCommand.ConfigureHotkeys, async _ => await new StudioHotkeyWindow(_settings,PersistSettings).ShowDialog(this));
    }
    private Menu CreateMenu()
    {
        MenuItem file = new() { Header = "_File" };
        MenuItem home = new() { Header = "Studio Home" };
        home.Click += (_, _) => Documents.Select(null);
        file.Items.Add(home);
        AddMenu(file, "New _map", StudioCommand.NewMapWorkspace);
        AddMenu(file, "New _replay workspace", StudioCommand.NewReplayWorkspace);
        MenuItem assets=new() { Header="Choose game asset paths…" };
        assets.Click += async (_,_) => await ChooseAssetPathsAsync();
        file.Items.Add(assets);
        MenuItem reopen=new() {Header="Reopen last session on startup",ToggleType=MenuItemToggleType.CheckBox,IsChecked=_settings.ReopenLastSession};
        reopen.Click+=(_,_)=> { _settings.ReopenLastSession=reopen.IsChecked;PersistSettings(); };file.Items.Add(reopen);
        AddMenu(file, "Open _map…", StudioCommand.OpenMap);
        AddMenu(file, "Open _replay…", StudioCommand.OpenReplay);
        AddMenu(file, "Open _clip…", StudioCommand.OpenClip);
        file.Items.Add(new Separator());
        AddMenu(file, "_Save", StudioCommand.Save);
        AddMenu(file, "Save _as…", StudioCommand.SaveAs);
        AddMenu(file, "_Close document", StudioCommand.Close);
        MenuItem exit = new() { Header = "E_xit" };
        exit.Click += async (_, _) => await CloseWindowAsync();
        file.Items.Add(exit);
        MenuItem edit = new() { Header = "_Edit" };
        AddMenu(edit, "_Undo", StudioCommand.Undo);
        AddMenu(edit, "_Redo", StudioCommand.Redo);
        AddMenu(edit, "Search documents and commands…", StudioCommand.GlobalSearch);
        AddMenu(edit, "Configure shortcuts…", StudioCommand.ConfigureHotkeys);
        MenuItem view = new() { Header = "_View" };
        foreach (StudioDockRegion region in new[] { StudioDockRegion.Left, StudioDockRegion.Right, StudioDockRegion.Bottom })
        {
            MenuItem panel = new() { Header = "Show " + region + " panel" };
            panel.Click += (_, _) => (Documents.ActiveDocument is MapStudioDocument map ? map.DockHost : DockHost)?.Show(region);
            view.Items.Add(panel);
        }
        AddMenu(view, "Restore default layout", StudioCommand.RestoreLayout);
        MenuItem performance = new() { Header = "Performance HUD" };
        performance.Click += (_,_) => TogglePerformanceHud(); view.Items.Add(performance);
        MenuItem jobs = new() { Header = "Background jobs…" };
        jobs.Click += (_,_) => OpenJobsWindow();view.Items.Add(jobs);
        MenuItem tools = new() { Header = "_Tools" };
        AddMenu(tools,"Validate map",StudioCommand.MapValidate);
        AddMenu(tools,"Build map package",StudioCommand.MapBuild);
        AddMenu(tools,"External playtest",StudioCommand.MapPlaytest);
        MenuItem recent = new() { Header = "_Recent" };
        AddMenu(recent, "Clear recent documents", StudioCommand.ClearRecent);
        MenuItem help = new() { Header = "_Help" };
        MenuItem about = new() { Header = "About Project Prime Studio…" };
        about.Click += async (_, _) => await new StudioAboutWindow(_paths).ShowDialog(this);
        help.Items.Add(about);
        Menu menu = new();
        menu.Items.Add(file); menu.Items.Add(edit); menu.Items.Add(view); menu.Items.Add(tools); menu.Items.Add(recent); menu.Items.Add(help);
        return menu;
    }
    private void AddMenu(MenuItem menu, string label, StudioCommand command) => menu.Items.Add(new MenuItem { Header = label, Command = Commands[command] });
    private async Task ChooseAssetPathsAsync()
    {
        try
        {
            if (!StorageProvider.CanOpen) throw new IOException("Native file dialogs are unavailable.");
            var files=await StorageProvider.OpenFilePickerAsync(new() { Title="Choose Project Prime extraction paths.txt",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Extraction paths") {Patterns=["*.txt"]}] });
            using IStorageFile? file=files.FirstOrDefault();
            if (file?.TryGetLocalPath() is not { } path) return;
            StudioGameAssets.Configure(path); _settings.GamePathsFile=path; PersistSettings();
            _status.Text=StudioGameAssets.Ready ? "Game asset paths configured" : "Asset paths configured · " + StudioGameAssets.Problem;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    public void TogglePerformanceHud()
    {
        if (_performanceHud is not null)
        { ClosePerformanceHud(); return; }
        if(_disposed || _closing)return;
        _performanceHud=new(Jobs,()=>Documents.ActiveDocument)
        { HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(12,84,12,12),IsHitTestVisible=true };
        _hudLayer.Children.Add(_performanceHud);
        RefreshPerformanceHudPlacement();
    }
    public void RefreshPerformanceHudPlacement()
    {
        if(_disposed || _closing || _performanceHud is not {} hud || _performanceWindow is not null || !StudioGraphicsHost.HasDevice)return;
        // Native viewport children occupy their own airspace. An owned native window
        // keeps diagnostics visible without covering or resizing the editor surface.
        _hudLayer.Children.Remove(hud);hud.Margin=new Thickness(8);hud.Width=double.NaN;
        hud.HorizontalAlignment=HorizontalAlignment.Stretch;hud.VerticalAlignment=VerticalAlignment.Stretch;
        var window=new Window
        {
            Title="Project Prime Studio · Performance",Width=380,Height=580,MinWidth=300,MinHeight=240,
            Background=Background,Content=hud,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        _performanceWindow=window;
        window.Closed+=(_,_)=>
        {
            window.Content=null;hud.Dispose();
            if(ReferenceEquals(_performanceHud,hud))_performanceHud=null;
            if(ReferenceEquals(_performanceWindow,window))_performanceWindow=null;
        };
        window.Show(this);
    }
    private void ClosePerformanceHud()
    {
        var hud=_performanceHud;var window=_performanceWindow;
        _performanceHud=null;_performanceWindow=null;
        if(hud is not null){_hudLayer.Children.Remove(hud);hud.Dispose();}
        if(window is not null){window.Content=null;window.Close();}
    }
    private void HandleDroppedSources(object? sender, DragEventArgs args)
    {
        var paths=args.DataTransfer.TryGetFiles()?.Select(file=>file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        var assetOwner=Documents.ActiveDocument as MapStudioDocument;
        bool accepted=false;
        foreach(string path in paths)
        {
            StudioOpenKind? kind=IsPortableReplay(path) ? StudioOpenKind.Replay : Path.GetExtension(path).ToLowerInvariant() switch {".json" or ".ppmap"=>StudioOpenKind.Map,".ppdemo"=>StudioOpenKind.Replay,".ppclip"=>StudioOpenKind.Clip,_=>null};
            if(kind is not {} target)continue;
            var result=EnqueueLaunchRequest(new(Guid.NewGuid(),target,Path.GetFullPath(path)),_lifetime.Token);
            if(!result.Accepted)ShowError(result.Error??"Could not open the dropped document.");accepted=true;
        }
        var assets=paths.Where(AvaloniaMapStudioHost.SupportsAssetDrop).ToArray();
        if(assetOwner is not null && assets.Length>0){_=ImportDroppedAssetsSafelyAsync(assetOwner,assets);accepted=true;}
        if(accepted)args.Handled=true;
    }
    private async Task ImportDroppedAssetsSafelyAsync(MapStudioDocument owner,string[] paths)
    {
        try{await owner.Host.ImportDroppedFilesAsync(paths,_lifetime.Token);}
        catch(OperationCanceledException){}
        catch(Exception error){ShowError(error.Message);}
    }
    private void HandleKey(object? sender, KeyEventArgs e)
    {
        if (StudioHotkeys.Match(_settings,e,Commands.AvailableCommands) is { } command && Commands[command].CanExecute(null))
        { Commands[command].Execute(null); e.Handled=true; }
    }
    private void DocumentsChanged()
    {
        if (_disposed) return;
        _shell.Refresh(); RefreshInspector(); Commands.Refresh();
        DockHost.SetWorkspaceFocus(Documents.ActiveDocument is MapStudioDocument or ProjectPrime.Studio.Replay.ReplayStudioDocument);
        _homeToolbar.IsVisible = Documents.ActiveDocument is not (MapStudioDocument or ReplayStudioDocument);
        Title = Documents.ActiveDocument is { } doc ? doc.Title + " · Project Prime Studio" : "Project Prime Studio";
        if (!_closing && _initialized && !_restoring) PersistSession(false);
    }
    private void RefreshInspector()
    {
        _inspector.Children.Clear();
        IStudioDocument? document = Documents.ActiveDocument;
        Label("Workspace", document?.Kind.ToString() ?? "Studio Home");
        Label("Capabilities", document is null ? "Open files and arrange panels" : document is MapStudioDocument ? "Map authoring" : document is ReplayStudioDocument ? "Replay playback and clip authoring" : "Read-only source inspection");
        if (document is StudioSourceDocument source && source.Path is not null)
        {
            Label("Source", source.Path);
            Label("Bytes", source.Length?.ToString("N0") ?? "—");
            Label("Modified (UTC)", source.LastModifiedUtc?.ToString("u") ?? "—");
            Label("SHA-256", source.Sha256 ?? "—");
        }
        void Label(string caption, string value)
        {
            _inspector.Children.Add(new TextBlock { Text = caption.ToUpperInvariant(), FontSize = 10, Foreground = Brushes.LightGray });
            _inspector.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
    }
    private void JobsChanged() => Dispatcher.UIThread.Post(() => { if (!_disposed) RefreshJobs(); });
    public void OpenJobsWindow()
    {
        if(_disposed || _closing)return;
        if(_jobsWindow is {} existing){existing.Show();existing.Activate();return;}
        var list=new StackPanel {Spacing=10,Margin=new Thickness(12)};
        Window window=new() {Title="Project Prime Studio · Background jobs",Width=700,Height=420,MinWidth=400,MinHeight=240,Background=Background,Content=new ScrollViewer {Content=list},WindowStartupLocation=WindowStartupLocation.CenterOwner};
        _jobsWindow=window;_floatingJobList=list;
        window.Closed+=(_,_)=> {if(ReferenceEquals(_jobsWindow,window)){_jobsWindow=null;_floatingJobList=null;}};
        RefreshJobs();window.Show(this);
    }
    private void RefreshJobs()
    {
        var current = Jobs.Jobs;
        StudioJob[] jobs = current.Where(job=>job.State==StudioJobState.Running).Reverse()
            .Concat(current.Where(job=>job.State!=StudioJobState.Running).TakeLast(8).Reverse()).ToArray();
        Populate(_jobList);
        if(_floatingJobList is {} floating)Populate(floating);
        StudioJob? running = jobs.FirstOrDefault(job => job.State == StudioJobState.Running);
        if (running is not null) _status.Text = $"{running.Title} · {running.Progress.Fraction:P0} · {running.Elapsed.TotalSeconds:F1}s";
        void Populate(StackPanel list)
        {
        list.Children.Clear();
        if (jobs.Length == 0) list.Children.Add(new TextBlock { Text = "No jobs. Document preparation, map builds and exports appear here with progress and cancellation.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
        foreach (StudioJob job in jobs)
        {
            DockPanel row = new();
            if (job.State == StudioJobState.Running)
            {
                Button cancel = new() { Content = "Cancel", Padding = new Thickness(8, 2) };
                cancel.Click += (_, _) => job.Cancel();
                DockPanel.SetDock(cancel, Dock.Right); row.Children.Add(cancel);
            }
            row.Children.Add(new TextBlock { Text = $"{job.Title} · {job.State} · {job.Progress.Fraction:P0} · {job.Elapsed.TotalSeconds:F1}s" + (job.Progress.Detail is {} detail ? " · "+detail : "") + (job.Error is null ? "" : " · " + job.Error), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            list.Children.Add(row);
        }
        }
    }
    private void PersistSettings()
    {
        if (_initialRequest.SafeMode) return;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Layout = DockHost.CaptureLayout();
        _settings.RecentDocuments = _recent.Items.ToList();
        if (!_store.SaveSettings(_settings)) ShowError("Settings could not be saved: " + _store.LastError);
    }
    private StudioSession SnapshotSession(bool clean) => new()
    {
        CleanExit = clean, SelectedDocument = Documents.ActiveDocument?.Id.Value,
        Documents = Documents.Documents.Select(doc => new StudioDocumentSnapshot(doc.Id.Value, doc.Kind, doc.Path,(doc as IStudioRecoverableDocument)?.RecoveryPath,(doc as IStudioPackageDocument)?.PackageDirectories.ToArray())).ToList()
    };
    private void PersistSession(bool clean) { if (!_initialRequest.SafeMode && !_store.SaveSession(SnapshotSession(clean))) ShowError("Session could not be saved: " + _store.LastError); }
    private void ShowError(string error) { _status.Text = error; _log.Write(error); }
    private async Task<bool> ConfirmAsync(string title, string message, string accept)
    {
        Window dialog = CreateDialog(title, message);
        bool result = false;
        StackPanel buttons = (StackPanel)((StackPanel)dialog.Content!).Children[^1];
        AddDialogButton(buttons, "Cancel", dialog.Close);
        AddDialogButton(buttons, accept, () => { result = true; dialog.Close(); });
        await dialog.ShowDialog(this);
        return result;
    }
    private static Window CreateDialog(string title, string message)
    {
        StackPanel content = new() { Margin = new Thickness(24), Spacing = 24 };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 });
        return new Window { Title = title, Content = content, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
    }
    private static void AddDialogButton(StackPanel buttons, string title, Action action)
    {
        Button button = new() { Content = title };
        button.Click += (_, _) => action();
        buttons.Children.Add(button);
    }
    private sealed class FractionProgress(IProgress<StudioJobProgress> target) : IProgress<double>
    { public void Report(double value) => target.Report(new(value)); }
}
