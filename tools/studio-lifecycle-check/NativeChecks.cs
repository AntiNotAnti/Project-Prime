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
    private sealed record NativeReplay(bool Ready,uint Frame,uint Duration,string State,string? Error,int Views);
    private sealed record NativeMap(Guid DocumentId,Guid MapId,ulong State,ulong? SavedState,Guid[] Selection,Guid? ActiveObject,int CommandCount,
        bool CanUndo,bool CanRedo,string DefinitionHash,string Layout,bool Dirty,string? Path);
    private sealed record NativeSnapshot(int ProcessId, NativeDocument[] Documents, int MapViewports, StudioRenderMetrics? MapMetrics,
        int Worlds, int NativeSurfaces, int ViewportTargets,NativeReplay? Replay,NativeMap? Map);
    private sealed record NativeStartup(int ProcessId,double UsableHomeMilliseconds,long WorkingSetBytes,double Width,double Height,double RenderScale,int PixelWidth,int PixelHeight);
    private static readonly Dictionary<int,NativeStartup> NativeStartups=[];

    private static int RunNativeProbe(string[] args)
    {
        if (args.Length != 2 || !Path.IsPathFullyQualified(args[1])) return 2;
        var paths = new StudioPaths(AppContext.BaseDirectory, args[1]);
        var startup=Stopwatch.StartNew();
        var request = new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, Recover: true);
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
                _ = RunNativeCommandsAsync(window,startup);
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

    private static async Task RunNativeCommandsAsync(StudioWindow window,Stopwatch startup)
    {
        try
        {
            await window.InitializeAsync();
            await Dispatcher.UIThread.InvokeAsync(()=>window.UpdateLayout(),DispatcherPriority.Render);
            var home=window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block=>block.Name=="StudioHomeHeading");
            if(window.Documents.Documents.Count==0&&(home is null||home.Bounds.Width<=0||home.Bounds.Height<=0))
                throw new InvalidOperationException("Native Home did not allocate visible heading before ready.");
            double scale=window.RenderScaling;
            Console.WriteLine("READY "+JsonSerializer.Serialize(new NativeStartup(Environment.ProcessId,startup.Elapsed.TotalMilliseconds,
                Process.GetCurrentProcess().WorkingSet64,window.ClientSize.Width,window.ClientSize.Height,scale,
                (int)Math.Round(window.ClientSize.Width*scale),(int)Math.Round(window.ClientSize.Height*scale))));
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
                                replay.Player.Status.State,replay.Player.Status.Error,replayDocument.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Count(view=>view.IsEffectivelyVisible)):null,
                        window.Documents.ActiveDocument is MapStudioDocument {Host.Document:{ } map} mapDocument
                            ?new NativeMap(mapDocument.Id.Value,map.Project.Definition.MapId,map.CurrentStateId.Value,map.SavedStateId?.Value,map.Selection.Order().ToArray(),map.ActiveObjectId,
                                map.History.CommandCount,map.History.CanUndo,map.History.CanRedo,
                                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(map.Project.Definition.Serialize()))),
                                JsonSerializer.Serialize(mapDocument.DockHost!.CaptureLayout()),mapDocument.Dirty,mapDocument.Path):null));
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
                    string[] dimensions=command.Split(' ');window.Width=int.Parse(dimensions[1]);window.Height=int.Parse(dimensions[2]);window.UpdateLayout();
                    Console.WriteLine("RESIZED "+JsonSerializer.Serialize(new{Width=window.ClientSize.Width,Height=window.ClientSize.Height,Scale=window.RenderScaling}));Console.Out.Flush();
                }
                else if(command=="frame-all")
                {
                    ((MapStudioDocument)window.Documents.ActiveDocument!).Host.FrameAll();
                    Console.WriteLine("FRAMED");Console.Out.Flush();
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
                else if(command.StartsWith("capture-replay ",StringComparison.Ordinal))
                {
                    var document=(ReplayStudioDocument)window.Documents.ActiveDocument!;
                    var viewport=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().First(view=>view.IsEffectivelyVisible);
                    var capture=viewport.Capture(640,360)??throw new InvalidOperationException("Native Replay did not capture its real GPU target.");
                    StudioReplayPlayer.SavePng(command[15..],capture);Console.WriteLine("CAPTURED");Console.Out.Flush();
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
                else throw new InvalidOperationException("Unexpected native test command.");
            }
        }
        catch (Exception ex) { Console.WriteLine("ERROR " + JsonSerializer.Serialize(ex.ToString())); Console.Out.Flush(); }
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
            long initialUploads = rendered.MapMetrics!.MeshUploads;
            await primary.StandardInput.WriteLineAsync("map-four"); await primary.StandardInput.FlushAsync();
            await ReadNativeLineAsync(primary, "MAP-FOUR");
            NativeSnapshot four = await WaitForNativeMapMetricsAsync(primary, 4);
            Check(four.MapMetrics is { DrawCalls: > 0, ReadbackBytes: 0 } && four.MapMetrics.MeshUploads == initialUploads
                && four.Worlds == 1 && four.NativeSurfaces == 4 && four.ViewportTargets == 4,
                "actual four native Map viewports share retained meshes and avoid full-frame readback");
            await primary.StandardInput.WriteLineAsync("close-document"); await primary.StandardInput.FlushAsync();
            await ReadNativeLineAsync(primary, "DOCUMENT-CLOSED");
            NativeSnapshot released = await NativeStatusAsync(primary);
            Check(released.Documents.Length == 0 && released.Worlds == 0 && released.NativeSurfaces == 0 && released.ViewportTargets == 0,
                "actual final Map document close releases shared GPU world and all native viewport targets");
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

    private static async Task<Process> StartNativeProbeAsync(string data)
    {
        Process process = StartChild(["--native-probe", data], data);
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
        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync(deadline.Token);
            if (line is null) throw new InvalidOperationException("Native probe exited before " + prefix + ": " + await process.StandardError.ReadToEndAsync());
            if (line.StartsWith("ERROR ", StringComparison.Ordinal)) throw new InvalidOperationException(line);
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
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

    private static async Task<NativeSnapshot> WaitForNativeMapMetricsAsync(Process process, int viewports)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            NativeSnapshot snapshot = await NativeStatusAsync(process);
            if (snapshot.MapViewports == viewports && snapshot.MapMetrics is { DrawCalls: > 0 }) return snapshot;
            await Task.Delay(50);
        }
        throw new TimeoutException("Native Map viewports did not present GPU frames: " + viewports);
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
