using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Settings;
using Avalonia.VisualTree;
using MphRead.Mods.StudioRendering;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio.Replay;

internal static partial class Program
{
    private sealed record NativeDocument(Guid Id, string Kind, string? Path, bool Dirty);
    private sealed record NativeReplay(bool Ready,uint Frame,uint Duration,string State,string? Error,int Views,
        string Camera,float Fov,int CameraKeys,float Rate,uint? ClipIn,uint? ClipOut,NativeReplayClock Clock);
    private sealed record NativeReplayClock(double WorkspaceSeconds,double LastViewSeconds,double AccumulatorSeconds,
        bool SharedTimerEnabled,bool[] TimerEnabled,bool[] AutomaticRendering);
    private sealed record NativeMap(Guid DocumentId,Guid MapId,ulong State,ulong? SavedState,Guid[] Selection,Guid? ActiveObject,int CommandCount,
        bool CanUndo,bool CanRedo,string DefinitionHash,string Layout,bool Dirty,string? Path);
    private sealed record NativeViewport(double Width,double Height,double RenderScale,int PixelWidth,int PixelHeight);
    private sealed record NativeMapPresentation(int Index,bool Active,bool Failed,string? Error,StudioRenderMetrics? Metrics);
    private sealed record NativeSnapshot(int ProcessId, NativeDocument[] Documents, int MapViewports, StudioRenderMetrics? MapMetrics,
        int Worlds, int NativeSurfaces, int ViewportTargets,NativeReplay? Replay,NativeMap? Map,long MetricsRevision,NativeViewport[] MapBounds,long Timestamp,int? Generation,
        string? Backend=null,string? Adapter=null,NativeMapPresentation[]? MapPresentations=null);
    private sealed record NativeStartup(int ProcessId,double UsableHomeMilliseconds,long WorkingSetBytes,double Width,double Height,double RenderScale,int PixelWidth,int PixelHeight,
        string Kind="Home",string? SourceHash=null,long SourceBytes=0,int AuthoredObjects=0,uint? Frame=null,string? GameplayHash=null,
        string? PresentationHash=null,string? FullGraphHash=null,NativeViewport? Viewport=null);
    private static readonly Dictionary<int,NativeStartup> NativeStartups=[];
    private static long _nativeMetricsRevision;

    private static int RunNativeProbe(string[] args)
    {
        if ((args.Length != 2&&args.Length!=4) || !Path.IsPathFullyQualified(args[1])) return 2;
        var paths = new StudioPaths(AppContext.BaseDirectory, args[1]);
        var startup=Stopwatch.StartNew();
        StudioGraphicsHost.DiagnosticsChanged+=_=>Interlocked.Increment(ref _nativeMetricsRevision);
        var request = new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, Recover: true);
        if(args.Length==4&&!StudioLaunchRequest.TryParse(args[2..],out request,out _))return 2;
        var ready = new TaskCompletionSource<StudioWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifetime = new CancellationTokenSource();
        StudioInstanceGuard? guard = null;
        try
        {
            guard = StudioInstanceGuard.TryAcquireAsync(paths.InstallationDirectory, paths.UserDataDirectory, request,
                async (forwarded, token) =>
                {
                    StudioWindow window = await ready.Task.WaitAsync(token).ConfigureAwait(false);
                    return await Dispatcher.UIThread.InvokeAsync(() => window.EnqueueLaunchRequest(forwarded, token));
                }, lifetime.Token).GetAwaiter().GetResult();
            if (!guard.IsPrimary) return 2;
            App.Configure(paths, request, window =>
            {
                ready.TrySetResult(window);
                _ = RunNativeCommandsAsync(window,startup,paths);
            });
            return ProjectPrime.Studio.Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            lifetime.Cancel();
            ready.TrySetCanceled(lifetime.Token);
            if (guard is not null) guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task RunNativeCommandsAsync(StudioWindow window,Stopwatch startup,StudioPaths paths)
    {
        try
        {
            await window.InitializeAsync();
            await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Render);
            if(window.Documents.ActiveDocument is MapStudioDocument or ReplayStudioDocument)
            {
                var viewportDeadline=Stopwatch.StartNew();
                while(viewportDeadline.Elapsed<TimeSpan.FromSeconds(20))
                {
                    bool usable=await Dispatcher.UIThread.InvokeAsync(()=>window.Documents.ActiveDocument switch
                    {
                        MapStudioDocument=>StudioGraphicsHost.LastMetrics is {DrawCalls:>0}&&StudioGraphicsHost.NativeSurfaceCount>0,
                        ReplayStudioDocument {Session:{ } replay}=>replay.Player.Status.Ready&&replay.Player.Performance.RenderMilliseconds is >0&&StudioGraphicsHost.NativeSurfaceCount>0,
                        _=>false
                    });
                    if(usable)break;
                    if(viewportDeadline.Elapsed>TimeSpan.FromSeconds(19))throw new TimeoutException("Initial source did not reach a usable native viewport.");
                    await Task.Delay(10);
                }
            }
            var home=window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block=>block.Name=="StudioHomeHeading");
            if(window.Documents.Documents.Count==0&&(home is null||home.Bounds.Width<=0||home.Bounds.Height<=0))
                throw new InvalidOperationException("Native Home did not allocate visible heading before ready.");
            double scale=window.RenderScaling;
            double usableMilliseconds=startup.Elapsed.TotalMilliseconds;long workingSet=Process.GetCurrentProcess().WorkingSet64;
            var active=window.Documents.ActiveDocument;
            string? sourceHash=active?.Path is { } startupSourcePath?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(startupSourcePath))):null;
            var world=active is ReplayStudioDocument {Session:{ } activeReplay}?activeReplay.Player.Snapshot():null;
            var startupViewport=window.GetVisualDescendants().OfType<Control>().FirstOrDefault(control=>control.IsEffectivelyVisible&&(control.GetType().Name=="MapViewport"||control is ReplayViewportHost));
            Console.WriteLine("READY "+JsonSerializer.Serialize(new NativeStartup(Environment.ProcessId,usableMilliseconds,
                workingSet,window.ClientSize.Width,window.ClientSize.Height,scale,
                (int)Math.Round(window.ClientSize.Width*scale),(int)Math.Round(window.ClientSize.Height*scale),active?.Kind.ToString()??"Home",sourceHash,
                active?.Path is {} sourcePath?new FileInfo(sourcePath).Length:0,active is MapStudioDocument {Host.Document:{ } initialMap}?initialMap.Project.Definition.Geometry.Count:0,
                world?.Frame,world?.GameplayHash,world?.PresentationHash,world?.FullGraphHash,
                startupViewport is null?null:new(startupViewport.Bounds.Width,startupViewport.Bounds.Height,scale,(int)Math.Round(startupViewport.Bounds.Width*scale),(int)Math.Round(startupViewport.Bounds.Height*scale)))));
            Console.Out.Flush();
            while (await Task.Run(Console.ReadLine) is { } command)
            {
                if (command == "status")
                {
                    NativeSnapshot snapshot = await Dispatcher.UIThread.InvokeAsync(() => new NativeSnapshot(Environment.ProcessId,
                        window.Documents.Documents.Select(document => new NativeDocument(document.Id.Value, document.Kind.ToString(), document.Path, document.Dirty)).ToArray(),
                        window.GetVisualDescendants().OfType<Control>().Count(control => control.GetType().Name == "MapViewport" && control.IsEffectivelyVisible),
                        StudioGraphicsHost.LastMetrics, StudioGraphicsHost.ResidentWorldCount,
                        StudioGraphicsHost.NativeSurfaceCount, StudioGraphicsHost.ViewportSurfaceCount,
                        window.Documents.ActiveDocument is ReplayStudioDocument {Session:{ } replay} replayDocument
                            ?new NativeReplay(replay.Player.Status.Ready,replay.Player.Status.Frame,replay.Player.Status.DurationFrames,
                                replay.Player.Status.State,replay.Player.Status.Error,replayDocument.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Count(view=>view.IsEffectivelyVisible),
                                replay.Camera.ToString(),replay.Fov,replay.Player.CameraKeys.Count,replay.Player.Status.Rate,replay.Player.Status.ClipIn,replay.Player.Status.ClipOut,
                                ReadNativeReplayClock(replayDocument)):null,
                        window.Documents.ActiveDocument is MapStudioDocument {Host.Document:{ } map} mapDocument
                            ?new NativeMap(mapDocument.Id.Value,map.Project.Definition.MapId,map.CurrentStateId.Value,map.SavedStateId?.Value,map.Selection.Order().ToArray(),map.ActiveObjectId,
                                map.History.CommandCount,map.History.CanUndo,map.History.CanRedo,
                                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(map.Project.Definition.Serialize()))),
                                JsonSerializer.Serialize(mapDocument.DockHost!.CaptureLayout()),mapDocument.Dirty,mapDocument.Path):null,
                        Interlocked.Read(ref _nativeMetricsRevision),window.GetVisualDescendants().OfType<Control>()
                            .Where(control=>control.GetType().Name=="MapViewport"&&control.IsEffectivelyVisible)
                            .Select(control=>new NativeViewport(control.Bounds.Width,control.Bounds.Height,window.RenderScaling,
                                (int)Math.Round(control.Bounds.Width*window.RenderScaling),(int)Math.Round(control.Bounds.Height*window.RenderScaling))).ToArray(),Stopwatch.GetTimestamp(),StudioGraphicsHost.DeviceGeneration,StudioGraphicsHost.Backend,StudioGraphicsHost.Adapter,ReadNativeMapPresentations(window)));
                    Console.WriteLine("STATUS " + JsonSerializer.Serialize(snapshot));
                    Console.Out.Flush();
                }
                else if (command == "map-four")
                {
                    await Dispatcher.UIThread.InvokeAsync(() => ((MapStudioDocument)window.Documents.ActiveDocument!).Host.ToggleFourViews());
                    Console.WriteLine("MAP-FOUR"); Console.Out.Flush();
                }
                else if(command.StartsWith("resize ",StringComparison.Ordinal))
                {
                    string[] dimensions=command.Split(' ');window.Width=int.Parse(dimensions[1]);window.Height=int.Parse(dimensions[2]);
                    // Native Configure/Resize is asynchronous; report the measured allocation
                    // after the platform has delivered it, instead of the old client size.
                    await Task.Delay(100);
                    await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Render);
                    Console.WriteLine("RESIZED "+JsonSerializer.Serialize(new{Width=window.ClientSize.Width,Height=window.ClientSize.Height,Scale=window.RenderScaling}));Console.Out.Flush();
                }
                else if(command=="frame-all")
                {
                    ((MapStudioDocument)window.Documents.ActiveDocument!).Host.FrameAll();
                    Console.WriteLine("FRAMED");Console.Out.Flush();
                }
                else if(command=="focus-map")
                {
                    var dock=((MapStudioDocument)window.Documents.ActiveDocument!).DockHost!;
                    foreach(var region in new[]{ProjectPrime.Studio.Shell.StudioDockRegion.Left,ProjectPrime.Studio.Shell.StudioDockRegion.Right,ProjectPrime.Studio.Shell.StudioDockRegion.Bottom})dock.Hide(region);
                    window.UpdateLayout();Console.WriteLine("MAP-FOCUSED");Console.Out.Flush();
                }
                else if(command=="redraw-map")
                {
                    ((MapStudioDocument)window.Documents.ActiveDocument!).Host.FrameAll();
                    foreach(Control viewport in window.GetVisualDescendants().OfType<Control>().Where(control=>control.GetType().Name=="MapViewport"&&control.IsEffectivelyVisible))viewport.InvalidateVisual();
                    Console.WriteLine("MAP-REDRAW");Console.Out.Flush();
                }
                else if(command.StartsWith("map-mode ",StringComparison.Ordinal))
                {
                    ((MapStudioDocument)window.Documents.ActiveDocument!).Host.SetViewportMode(command[9..]);
                    Console.WriteLine("MAP-MODE");Console.Out.Flush();
                }
                else if(command.StartsWith("capture-hud ",StringComparison.Ordinal))
                {
                    if(window.PerformanceHud is null)window.TogglePerformanceHud();
                    window.RefreshPerformanceHudPlacement();await Task.Delay(100);
                    var hud=window.PerformanceHud??throw new InvalidOperationException("HUD did not allocate.");hud.RefreshSnapshot();
                    var floating=window.PerformanceHudWindow??throw new InvalidOperationException("Native graphics HUD did not use its owned window.");
                    floating.UpdateLayout();double hudScale=floating.RenderScaling;
                    using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Round(floating.ClientSize.Width*hudScale),(int)Math.Round(floating.ClientSize.Height*hudScale)),new Vector(96*hudScale,96*hudScale));
                    bitmap.Render(floating);bitmap.Save(command[12..],new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    Console.WriteLine("HUD-CAPTURED "+JsonSerializer.Serialize(new{Visible=floating.IsVisible,OwnsWindow=ReferenceEquals(floating.Owner,window),
                        NativeHandle=floating.TryGetPlatformHandle()?.Handle.ToInt64()??0,Sampling=hud.IsSampling,Width=bitmap.PixelSize.Width,Height=bitmap.PixelSize.Height,
                        hud.Snapshot,hud.DisplayText}));Console.Out.Flush();
                }
                else if(command=="hide-hud")
                {
                    var hud=window.PerformanceHud;window.TogglePerformanceHud();
                    Console.WriteLine("HUD-HIDDEN "+JsonSerializer.Serialize(new{Sampling=hud?.IsSampling,Released=hud?.Snapshot is null,WindowReleased=window.PerformanceHudWindow is null}));Console.Out.Flush();
                }
                else if(command=="new-map")
                {
                    window.NewMapProject("STUDIO_NATIVE_PLAYTEST",example:true);
                    var map=((MapStudioDocument)window.Documents.ActiveDocument!).Host.Document!;
                    Guid id=map.Project.Definition.Geometry[0].Id;map.Selection.Add(id);map.ActiveObjectId=id;
                    map.EditObjects("Native unsaved map edit",[id],definition=>definition.Geometry[0].Transform.Scale[0]+=2);
                    Console.WriteLine("MAP-CREATED");Console.Out.Flush();
                }
                else if(command=="mutate-map")
                {
                    var map=((MapStudioDocument)window.Documents.ActiveDocument!).Host.Document!;
                    Guid id=map.Project.Definition.Geometry[0].Id;
                    map.EditObjects("Native playtest version edit",[id],definition=>definition.Geometry[0].Transform.Scale[0]+=2);
                    Console.WriteLine("MAP-MUTATED");Console.Out.Flush();
                }
                else if(command=="autosave-map")
                {
                    await ((MapStudioDocument)window.Documents.ActiveDocument!).FlushAutosaveAsync();
                    Console.WriteLine("MAP-AUTOSAVED");Console.Out.Flush();
                }
                else if(command.StartsWith("package-map ",StringComparison.Ordinal))
                {
                    var map=((MapStudioDocument)window.Documents.ActiveDocument!).Host.Document!.CaptureBuildSnapshot();
                    string path=command[12..];await Task.Run(()=>MapPackageBuilder.Build(map.CreateDefinition(),path));
                    Console.WriteLine("MAP-PACKAGED");Console.Out.Flush();
                }
                else if(command.StartsWith("capture-map ",StringComparison.Ordinal))
                {
                    var host=((MapStudioDocument)window.Documents.ActiveDocument!).Host;
                    var capture=(StudioViewportImage?)host.GetType().GetMethod("CaptureViewport")?.Invoke(host,null)
                        ??throw new InvalidOperationException("Native Map did not capture its GPU target.");
                    StudioReplayPlayer.SavePng(command[12..],new(capture.Width,capture.Height,capture.Rgba));
                    Console.WriteLine("MAP-CAPTURED "+JsonSerializer.Serialize(new{capture.Width,capture.Height}));Console.Out.Flush();
                }
                else if (command == "close-document")
                {
                    bool accepted = await window.Documents.RequestCloseAsync(window.Documents.ActiveDocument!,
                        _ => Task.FromResult(ProjectPrime.Studio.Shell.StudioCloseDecision.Cancel), _ => Task.FromResult<string?>(null));
                    Console.WriteLine(accepted ? "DOCUMENT-CLOSED" : "DOCUMENT-CANCELLED"); Console.Out.Flush();
                }
                else if(command=="replay-four")
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    document.Host.GetVisualDescendants().OfType<Button>().Single(button=>button.Content?.ToString()=="Four Views")
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Console.WriteLine("REPLAY-FOUR");Console.Out.Flush();
                }
                else if(command=="replay-hud-off")
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    document.Session!.GameHud=false;document.Session.SavePresentation();
                    foreach(var viewport in document.Host.GetVisualDescendants().OfType<ReplayViewportHost>())viewport.Retry();
                    Console.WriteLine("REPLAY-HUD-OFF");Console.Out.Flush();
                }
                else if(command.StartsWith("replay-hud ",StringComparison.Ordinal))
                {
                    var replay=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!;
                    replay.GameHud=bool.Parse(command[11..]);
                    Console.WriteLine("REPLAY-HUD");Console.Out.Flush();
                }
                else if(command.StartsWith("replay-slot ",StringComparison.Ordinal))
                {
                    var replay=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!;
                    replay.PlayerSlot=int.Parse(command[12..]);
                    Console.WriteLine("REPLAY-SLOT");Console.Out.Flush();
                }
                else if(command=="replay-state")
                {
                    var replay=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!;
                    Console.WriteLine("REPLAY-STATE "+JsonSerializer.Serialize(new{World=replay.Player.Snapshot(),replay.GameHud,replay.PlayerSlot},
                        new JsonSerializerOptions{IncludeFields=true}));Console.Out.Flush();
                }
                else if(command=="replay-transport")
                {
                    var player=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player;
                    player.SetRate(2);player.SetRange(5,45);player.Seek(17);player.Pause();
                    Console.WriteLine("REPLAY-TRANSPORT");Console.Out.Flush();
                }
                else if(command.StartsWith("replay-play ",StringComparison.Ordinal))
                {
                    var player=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player;
                    player.SetRate(float.Parse(command[12..],System.Globalization.CultureInfo.InvariantCulture));player.Seek(0,resume:true);player.Advance(TimeSpan.Zero);
                    Console.WriteLine("REPLAY-PLAYING");Console.Out.Flush();
                }
                else if(command=="replay-pause")
                {
                    var player=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player;
                    player.Pause();player.Advance(TimeSpan.Zero);
                    Console.WriteLine("REPLAY-PAUSED");Console.Out.Flush();
                }
                else if(command=="replay-retry")
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    foreach(var viewport in document.Host.GetVisualDescendants().OfType<ReplayViewportHost>())viewport.Retry();
                    Console.WriteLine("REPLAY-RETRY");Console.Out.Flush();
                }
                else if(command=="replay-fail-last-and-loss")
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    var last=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Where(view=>view.IsEffectivelyVisible).Last();
                    last.GetType().GetMethod("Fail",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(last,[new InvalidOperationException("Owned stale fourth-view test failure")]);
                    int? generation=StudioGraphicsHost.DeviceGeneration;StudioGraphicsHost.Device.SimulateDeviceLossForDiagnostics();
                    Console.WriteLine("REPLAY-LOST "+generation);Console.Out.Flush();
                }
                else if(command=="replay-resources")
                {
                    Console.WriteLine("REPLAY-RESOURCES "+JsonSerializer.Serialize(new{Counts=MphRead.Mods.Render.ModernGraphicsCompat.LiveResources,
                        Generation=StudioGraphicsHost.DeviceGeneration,Surfaces=StudioGraphicsHost.NativeSurfaceCount}));Console.Out.Flush();
                }
                else if(command=="replay-dispose-views")
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    var views=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Where(view=>view.IsEffectivelyVisible).ToArray();
                    foreach(var view in views.Take(views.Length-1))view.Dispose();views[^1].Dispose();
                    Console.WriteLine("REPLAY-VIEWS-DISPOSED");Console.Out.Flush();
                }
                else if(command.StartsWith("capture-replay ",StringComparison.Ordinal))
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    var viewport=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().First(view=>view.IsEffectivelyVisible);
                    var capture=viewport.Capture(640,360)??throw new InvalidOperationException("Native Replay did not capture its real GPU target.");
                    StudioReplayPlayer.SavePng(command[15..],capture);Console.WriteLine("CAPTURED");Console.Out.Flush();
                }
                else if(command.StartsWith("capture-replay-state ",StringComparison.Ordinal))
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    var viewport=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().First(view=>view.IsEffectivelyVisible);
                    var capture=viewport.Capture(640,360)??throw new InvalidOperationException("Native Replay did not capture its real GPU target.");
                    StudioReplayPlayer.SavePng(command[21..],capture);
                    Console.WriteLine("CAPTURE-STATE "+JsonSerializer.Serialize(document.Session!.Player.Snapshot(),new JsonSerializerOptions{IncludeFields=true}));Console.Out.Flush();
                }
                else if(command.StartsWith("capture-replay-view ",StringComparison.Ordinal))
                {
                    int separator=command.IndexOf(' ',20);int index=int.Parse(command[20..separator]);
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    var viewport=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Where(view=>view.IsEffectivelyVisible).ElementAt(index);
                    var capture=viewport.Capture(640,360)??throw new InvalidOperationException("Native Replay view did not capture its real GPU target.");
                    StudioReplayPlayer.SavePng(command[(separator+1)..],capture);
                    Console.WriteLine("VIEW-CAPTURED "+JsonSerializer.Serialize(new{Index=index,Frame=document.Session!.Player.Status.Frame,
                        Camera=viewport.ViewFactory(640,360).Camera.ToString()}));Console.Out.Flush();
                }
                else if(command=="prepare-replay-clip")
                {
                    var replay=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!;
                    replay.Camera=StudioReplayCameraMode.Free;replay.Position=new(4,5,8);replay.Fov=65;replay.SavePresentation();
                    replay.Player.PutCameraKey(new(40,new(4,5,8),System.Numerics.Quaternion.Identity,Fov:65));
                    replay.Player.SetRange(30,60);replay.Player.Advance(TimeSpan.Zero);
                    Console.WriteLine("REPLAY-CLIP-PREPARED");Console.Out.Flush();
                }
                else if(command.StartsWith("save-replay-as ",StringComparison.Ordinal))
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    await document.SaveAsync(command[15..],CancellationToken.None);
                    Console.WriteLine("REPLAY-SAVED");Console.Out.Flush();
                }
                else if(command.StartsWith("export ",StringComparison.Ordinal))
                {
                    var request=JsonSerializer.Deserialize<StudioReplayExportRequest>(command[7..],new JsonSerializerOptions{IncludeFields=true})!;
                    var player=((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player;
                    Guid id=player.QueueExport(request);
                    Console.WriteLine("EXPORT "+id.ToString("N"));Console.Out.Flush();
                }
                else if(command.StartsWith("cancel-export ",StringComparison.Ordinal))
                {
                    ((ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player.CancelExport(Guid.Parse(command[14..]));
                    Console.WriteLine("EXPORT-CANCELLED");Console.Out.Flush();
                }
                else if(command is "close-cancel" or "close-discard")
                {
                    Task<bool> closing=window.TryCloseAsync();
                    Window dialog=window.OwnedWindows.Single(owned=>owned.Title=="Unsaved changes");
                    string label=command=="close-cancel"?"Cancel":"Discard";
                    dialog.GetVisualDescendants().OfType<Button>().Single(button=>button.Content?.ToString()==label)
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    bool accepted=await closing;
                    Console.WriteLine(accepted?"CLOSED":"CLOSE-CANCELLED");Console.Out.Flush();
                    if(accepted){window.Close();return;}
                }
                else if (command == "close")
                {
                    bool accepted = await window.TryCloseAsync();
                    Console.WriteLine(accepted ? "CLOSED" : "CLOSE-CANCELLED");
                    Console.Out.Flush();
                    if (accepted) { window.Close(); return; }
                }
                else if(!await TryHandleNativeExtraCommandAsync(window,paths,command))throw new InvalidOperationException("Unexpected native test command.");
            }
        }
        catch (Exception ex) { Console.WriteLine("ERROR " + JsonSerializer.Serialize(ex.ToString())); Console.Out.Flush(); }
    }

    private static NativeMapPresentation[] ReadNativeMapPresentations(StudioWindow window)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        return window.GetVisualDescendants().OfType<Control>()
            .Where(control=>control.GetType().Name=="MapViewport"&&control.IsEffectivelyVisible)
            .Select((control,index)=>
            {
                var presentation=control.GetType().GetField("_studioPresentation",flags)?.GetValue(control) as MapViewportHost;
                bool failed=control.GetType().GetField("_studioPresentationFailed",flags)?.GetValue(control) is true;
                return new NativeMapPresentation(index,presentation?.Active==true,failed,
                    (presentation?.NativeControl as StudioNativeViewport)?.GraphicsError,presentation?.Metrics);
            }).ToArray();
    }

    private static NativeReplayClock ReadNativeReplayClock(ReplayStudioDocument document)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object workspace=document.Host;Type type=workspace.GetType();
        var clock=(Stopwatch)type.GetField("_viewClock",flags)!.GetValue(workspace)!;
        var last=(TimeSpan)type.GetField("_lastViewTime",flags)!.GetValue(workspace)!;
        var timer=(DispatcherTimer)type.GetField("_viewTimer",flags)!.GetValue(workspace)!;
        double accumulator=(double)document.Session!.Player.GetType().GetField("_fixedAccumulator",flags)!.GetValue(document.Session.Player)!;
        var hosts=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Where(view=>view.IsEffectivelyVisible).ToArray();
        return new(clock.Elapsed.TotalSeconds,last.TotalSeconds,accumulator,timer.IsEnabled,
            hosts.Select(view=>((DispatcherTimer)view.GetType().GetField("_timer",flags)!.GetValue(view)!).IsEnabled).ToArray(),
            hosts.Select(view=>view.AutomaticRenderingEnabled).ToArray());
    }

    private static async Task CheckNativeProcessLifecycleAsync(string directory)
    {
        string primaryData = Path.Combine(directory, "native-primary");
        string peerData = Path.Combine(directory, "native-peer");
        string source = Path.Combine(directory, "native-source.json");
        MapProjectSerializer.Save(MapTemplates.Create("NATIVE LIFECYCLE MAP", false), source);
        string sourceBefore = File.ReadAllText(source);
        Process? primary = null, peer = null;
        try
        {
            primary = await StartNativeProbeAsync(primaryData);
            peer = await StartNativeProbeAsync(peerData);
            Check((await NativeStatusAsync(primary)).Documents.Length == 0 && (await NativeStatusAsync(peer)).Documents.Length == 0,
                "native classic desktop windows start independently with isolated profiles");
            string descriptorPath = StudioEndpointStore.GetDescriptorPath(AppContext.BaseDirectory, primaryData);
            Check(StudioEndpointStore.Read(AppContext.BaseDirectory, primaryData).ProcessId == primary.Id,
                "native desktop endpoint is owned by actual window process");
            using (Process secondary = StartStudioExecutable(primaryData, ["--map", source]))
            {
                await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(secondary.ExitCode == 0 && !primary.HasExited, "actual second Studio executable forwards and exits while native owner remains alive");
            }
            NativeSnapshot opened = await WaitForNativeDocumentAsync(primary, source);
            Check(opened.Documents.Length == 1 && !opened.Documents[0].Dirty && File.ReadAllText(source) == sourceBefore,
                "native production forwarding opens one clean source without changing its bytes");
            NativeSnapshot rendered = await WaitForNativeMapMetricsAsync(primary, 1);
            Check(rendered.MapMetrics is { MeshUploads: > 0, ResidentMeshes: > 0, DrawCalls: > 0, ReadbackBytes: 0 },
                "actual native Map viewport retains GPU geometry and presents without full-frame readback");
            Console.WriteLine("Native Map single-view presented snapshot: "+JsonSerializer.Serialize(rendered));
            long initialUploads = rendered.MapMetrics!.MeshUploads;
            await primary.StandardInput.WriteLineAsync("map-four"); await primary.StandardInput.FlushAsync();
            await ReadNativeLineAsync(primary, "MAP-FOUR");
            NativeSnapshot four = await WaitForNativeMapMetricsAsync(primary, 4,rendered.MetricsRevision);
            Console.WriteLine("Native Map four-view presented snapshot: "+JsonSerializer.Serialize(four));
            Check(four.MapMetrics is { DrawCalls: > 0, ReadbackBytes: 0 } && four.MapMetrics.MeshUploads == initialUploads
                && four.Worlds == 1 && four.NativeSurfaces == 4 && four.ViewportTargets == 4,
                "actual four native Map viewports share retained meshes and avoid full-frame readback; initial="+JsonSerializer.Serialize(rendered)+"; four="+JsonSerializer.Serialize(four));
            await primary.StandardInput.WriteLineAsync("close-document"); await primary.StandardInput.FlushAsync();
            await ReadNativeLineAsync(primary, "DOCUMENT-CLOSED");
            NativeSnapshot released = await NativeStatusAsync(primary);
            Check(released.Documents.Length == 0 && released.Worlds == 0 && released.NativeSurfaces == 0 && released.ViewportTargets == 0,
                "actual final Map document close releases shared GPU world and all native viewport targets");
            await CheckNativeAdmissionFailureAsync(primary,Path.Combine(directory,"native-admission-cpu-fallback.png"));
            using (Process reopened = StartStudioExecutable(primaryData, ["--map", source]))
            {
                await reopened.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Check(reopened.ExitCode == 0, "native Map document reopens through the production second executable");
            }
            await WaitForNativeDocumentAsync(primary, source);
            Check((await NativeStatusAsync(peer)).Documents.Length == 0, "native independent profile is unaffected by forwarded document");

            peer.Kill(entireProcessTree: true);
            await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(!primary.HasExited && (await NativeStatusAsync(primary)).Documents.Length == 1,
                "native peer process crash leaves Studio document alive");
            peer.Dispose();
            peer = await StartNativeProbeAsync(peerData);
            await CloseNativeProbeAsync(primary);
            Check(!peer.HasExited && !File.Exists(descriptorPath), "normal native Studio close releases endpoint and leaves independent desktop peer alive");
            primary.Dispose();
            primary = await StartNativeProbeAsync(primaryData);
            Check((await WaitForNativeDocumentAsync(primary,source)).Documents.Any(document => document.Path == source),
                "native restart explicitly restores persisted source document");
            string crashedPipe = StudioEndpointStore.Read(AppContext.BaseDirectory, primaryData).PipeName;
            primary.Kill(entireProcessTree: true);
            await primary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(!peer.HasExited, "native Studio process crash leaves independent desktop peer alive");
            primary.Dispose();
            primary = await StartNativeProbeAsync(primaryData);
            Check(StudioEndpointStore.Read(AppContext.BaseDirectory, primaryData).PipeName != crashedPipe,
                "native restart replaces stale descriptor after abrupt owner crash");
            Check((await WaitForNativeDocumentAsync(primary,source)).Documents.Any(document => document.Path == source),
                "native explicit recovery survives abrupt process termination");
            await CloseNativeProbeAsync(primary);
            await CloseNativeProbeAsync(peer);
            Check(!File.Exists(descriptorPath), "normal native application shutdown removes active descriptor");
            Console.WriteLine("Native desktop lifecycle completed. Peer is an independent Studio profile; game/playtest broker lifecycle is a separate gate.");
        }
        finally
        {
            foreach (Process? process in new[] { primary, peer })
            {
                if (process is null) continue;
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
                finally { process.Dispose(); }
            }
        }
    }

    private static Process StartChild(string[] args, string? userData = null, bool studioExecutable = false)
    {
        string executable = studioExecutable ? Path.Combine(AppContext.BaseDirectory, "ProjectPrimeStudio" + (OperatingSystem.IsWindows() ? ".exe" : ""))
            : Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate runtime.");
        ProcessStartInfo start = new(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (!studioExecutable && Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in args) start.ArgumentList.Add(argument);
        if (userData is not null) start.Environment["PROJECT_PRIME_STUDIO_USER_DATA"] = userData;
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start native desktop child.");
    }

    private static Process StartStudioExecutable(string userData, string[] args) => StartChild(args, userData, studioExecutable: true);

    private static async Task<Process> StartNativeProbeAsync(string data,string[]? initialRequest=null)
    {
        Process process = StartChild(["--native-probe", data,..initialRequest??[]], data);
        try
        {
            string ready=await ReadNativeLineAsync(process,"READY ");
            NativeStartups[process.Id]=JsonSerializer.Deserialize<NativeStartup>(ready[6..])??throw new InvalidDataException("Native ready measurements missing.");
            return process;
        }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); process.Dispose(); throw; }
    }

    private static async Task<string> ReadNativeLineAsync(Process process, string prefix)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync(deadline.Token);
            if (line is null) throw new InvalidOperationException("Native probe exited before " + prefix + ": " + await process.StandardError.ReadToEndAsync());
            if (line.StartsWith("ERROR ", StringComparison.Ordinal)) throw new InvalidOperationException(line);
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
        }
        }
        catch(OperationCanceledException)when(deadline.IsCancellationRequested)
        {
            // Preserve an owned macOS child's blocked native/UI stack before its
            // test cleanup kills it. Other platforms retain the ordinary timeout.
            if(OperatingSystem.IsMacOS()&&!process.HasExited)
            {
                string path=Path.Combine(Path.GetTempPath(),"project-prime-native-probe-"+process.Id+".sample.txt");
                var start=new ProcessStartInfo("/usr/bin/sample"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(string argument in new[]{process.Id.ToString(),"1","1","-file",path})start.ArgumentList.Add(argument);
                using var sampler=Process.Start(start);
                if(sampler is not null)
                {
                    try{await sampler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));Console.Error.WriteLine("Owned native timeout sample: "+path);}
                    catch(TimeoutException){if(!sampler.HasExited)sampler.Kill();}
                }
            }
            throw;
        }
    }

    private static async Task<NativeSnapshot> NativeStatusAsync(Process process)
    {
        await process.StandardInput.WriteLineAsync("status");
        await process.StandardInput.FlushAsync();
        string line = await ReadNativeLineAsync(process, "STATUS ");
        return JsonSerializer.Deserialize<NativeSnapshot>(line[7..]) ?? throw new InvalidOperationException("Empty native probe status.");
    }

    private static async Task<NativeSnapshot> WaitForNativeDocumentAsync(Process process, string path)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            NativeSnapshot snapshot = await NativeStatusAsync(process);
            if (snapshot.Documents.Any(document => document.Path == path)) return snapshot;
            await Task.Delay(20);
        }
        throw new TimeoutException("Forwarded source did not publish to native desktop document host.");
    }

    private static bool IsNativeMapPresentationReady(NativeSnapshot snapshot,int viewports,long afterRevision)
        => snapshot.MapViewports==viewports && snapshot.MapMetrics is {DrawCalls:>0}
            && snapshot.MetricsRevision>afterRevision && snapshot.Worlds==1
            && snapshot.NativeSurfaces==viewports && snapshot.ViewportTargets==viewports
            && snapshot.MapPresentations is { } presentations && presentations.Length==viewports
            && presentations.All(view=>view.Active&&!view.Failed&&view.Metrics is {DrawCalls:>0});

    private static async Task<NativeSnapshot> WaitForNativeMapMetricsAsync(Process process, int viewports,long afterRevision=-1)
    {
        var timer = Stopwatch.StartNew();
        NativeSnapshot? last=null;
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            NativeSnapshot snapshot = last=await NativeStatusAsync(process);
            // Visual grid allocation precedes native child admission and each
            // viewport's first Present. Retained global metrics alone can still
            // describe the old single view, so require own metrics after a
            // returned normal Present. This no-capture/no-pick transition does
            // not independently certify the native outcome or GPU completion.
            if (IsNativeMapPresentationReady(snapshot,viewports,afterRevision)) return snapshot;
            await Task.Delay(50);
        }
        throw new TimeoutException("Native Map viewports did not present GPU frames: " + viewports+"; afterRevision="+afterRevision+"; last="+JsonSerializer.Serialize(last));
    }

    private static async Task CloseNativeProbeAsync(Process process)
    {
        await process.StandardInput.WriteLineAsync("close");
        await process.StandardInput.FlushAsync();
        await ReadNativeLineAsync(process, "CLOSED");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(process.ExitCode == 0, "native window closes through normal lifecycle and releases desktop resources");
    }
}
