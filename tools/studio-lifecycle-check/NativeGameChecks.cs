using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.StudioIntegration;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;

internal static partial class Program
{
    private static int RunNativeGameProbe(string[] args)
    {
        if (args.Length != 4 || !File.Exists(args[3])) return 2;
        string data = Path.GetFullPath(args[1]); Directory.CreateDirectory(data);
        File.Copy(args[3], Path.Combine(data, "paths.txt"), overwrite:true);
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", data);
        Environment.SetEnvironmentVariable("PROJECT_PRIME_STUDIO_IPC_DATA", Path.GetFullPath(args[2]));
        var commands = new Thread(() =>
        {
            var deadline = Stopwatch.StartNew();
            while (!Shell.Active && deadline.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(10);
            if (!Shell.Active)
            {
                var diagnostic=new StringBuilder();MphRead.Mods.DebugLog.AppendRecent(diagnostic);
                Console.WriteLine("ERROR Native game did not reach Shell.Active: "+diagnostic.ToString().Replace('\n',' ').Replace('\r',' '));Console.Out.Flush();return;
            }
            Console.WriteLine("GAME-READY"); Console.Out.Flush();
            while (Console.ReadLine() is { } command)
            {
                if (command != "close") continue;
                // The production front screen's Quit action, scheduled on its owner dispatcher.
                Dispatcher.UIThread.Post(Shell.RequestQuit); return;
            }
        }) { IsBackground = true, Name = "Native lifecycle game commands" };
        commands.Start();
        try
        {
            string[] gameArgs = ["-launcher", "-windowed", "-customruntimeroot", Path.Combine(data,"runtime"), "-customruntimenamespace", Guid.NewGuid().ToString("N"),
                "-usermapdirectory", Path.Combine(data,"installed"), "-mapdirectory", Path.Combine(data,"maps")];
            typeof(MphRead.Scene).Assembly.GetType("MphRead.Program",true)!.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [gameArgs]);
            return Environment.ExitCode;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task CheckNativeGameLifecycleAsync(string directory, string assets)
    {
        string profile = Path.Combine(directory,"game-studio-profile"), gameData = Path.Combine(directory,"game-state");
        string package=Path.Combine(directory,"native-playtest.ppmap");
        Process? studio = null, game = null;
        string? priorGame = Environment.GetEnvironmentVariable("PROJECT_PRIME_GAME_PATH");
        Environment.SetEnvironmentVariable("PROJECT_PRIME_GAME_PATH", Path.Combine(AppContext.BaseDirectory,"ProjectPrime" + (OperatingSystem.IsWindows()?".exe":"")));
        try
        {
            studio = await StartNativeProbeAsync(profile);
            game = await StartNativeGameProbeAsync(gameData,profile,assets);
            using var broker = new StudioGameBrokerClient(AppContext.BaseDirectory,profile);
            StudioGameResult diagnostics = await WaitForGameDiagnosticsAsync(broker);
            Check(diagnostics.Accepted && !studio.HasExited && !game.HasExited, "native Project Prime shell and native Studio coexist through independent application owners");
            Check(LocalIpcEndpointStore.Read(AppContext.BaseDirectory,profile,StudioEndpointRole.Game).ProcessId == game.Id,
                "production game broker endpoint belongs to actual native game shell process");
            using(Process duplicate=StartChild(["--game-probe",gameData,profile,Path.GetFullPath(assets)]))
            {
                try
                {
                    await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Check(duplicate.ExitCode==1&&!game.HasExited&&LocalIpcEndpointStore.Read(AppContext.BaseDirectory,profile,StudioEndpointRole.Game).ProcessId==game.Id,
                        "production interactive game guard rejects duplicate game without replacing active owner or broker descriptor");
                }
                finally{if(!duplicate.HasExited){duplicate.Kill(entireProcessTree:true);await duplicate.WaitForExitAsync();}}
            }
            NativeMap mapBefore=await CreateNativeUnsavedMapAsync(studio);
            await NativePackageMapAsync(studio,package);var identity=MapContentIdentity.FromPackage(package);
            Check(mapBefore.Dirty&&mapBefore.Path is null&&mapBefore.CanUndo&&mapBefore.CommandCount==1,
                "native playtest starts from real unsaved canonical Map with selection and undo history");
            StudioGameResult play = await broker.RequestPlaytestAsync(package,GameStudioBroker.ToWireIdentity(identity),new(Bots:0));
            Check(play.Accepted && play.PlaytestId != Guid.Empty && play.Identity?.PackageHash == identity.PackageHash.ToString(),
                "native game accepts exact immutable Studio playtest package");
            StudioGameResult playing = await WaitForPlaytestAsync(broker,play.PlaytestId,StudioPlaytestState.Started);
            Check(playing.PlaytestState == StudioPlaytestState.Started && !studio.HasExited, "actual native gameplay scene starts while Studio remains independent");
            await CheckNativeMapUnchangedAsync(studio,mapBefore,"native game playtest start preserves dirty editor document identity/state/history/selection/layout");
            string runtime=Path.Combine(gameData,"runtime");var runtimeBefore=HashDirectory(runtime);
            Check(runtimeBefore.Count>0,"actual native gameplay owns privately generated immutable map runtime files");
            await studio.StandardInput.WriteLineAsync("mutate-map");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"MAP-MUTATED");
            NativeMap edited=(await NativeStatusAsync(studio)).Map!;
            string updatedPackage=Path.Combine(directory,"native-playtest-updated.ppmap");await NativePackageMapAsync(studio,updatedPackage);
            var updatedIdentity=MapContentIdentity.FromPackage(updatedPackage);
            Check(updatedIdentity.MapId==identity.MapId&&updatedIdentity.ContentHash!=identity.ContentHash,
                "editing real native Map builds a new immutable content identity without replacing earlier package");
            StudioGameResult fenced=await broker.InstallMapPackageAsync(updatedPackage,GameStudioBroker.ToWireIdentity(updatedIdentity));
            Check(!fenced.Accepted&&fenced.Deferred&&HashDirectory(runtime).OrderBy(pair=>pair.Key).SequenceEqual(runtimeBefore.OrderBy(pair=>pair.Key)),
                "actual live game scene runtime lease rejects installation and preserves every active generated byte");
            await CheckNativeMapUnchangedAsync(studio,edited,"live publication rejection preserves unsaved native Map edits and editor lifecycle");
            Check((await broker.StopPlaytestAsync(play.PlaytestId)).Accepted,"owned native playtest stops before updated package publication");
            await WaitForPlaytestAsync(broker,play.PlaytestId,StudioPlaytestState.Ended);
            play=await RequestNativePlaytestAfterStopAsync(broker,updatedPackage,updatedIdentity);
            await WaitForPlaytestAsync(broker,play.PlaytestId,StudioPlaytestState.Started);
            Check(play.Identity?.PackageHash==updatedIdentity.PackageHash.ToString(),"actual stop/edit/rebuild/restart uses exact newly authored package identity");
            await CheckNativeMapUnchangedAsync(studio,edited,"native updated game scene preserves unsaved editor state and undo history");
            await studio.StandardInput.WriteLineAsync("close-cancel");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CLOSE-CANCELLED");
            await CheckNativeMapUnchangedAsync(studio,edited,"actual native Unsaved changes Cancel retains document and active game playtest");
            await CloseDirtyNativeProbeAsync(studio); studio.Dispose(); studio = null;
            Check(!game.HasExited && (await broker.GetPlaytestStatusAsync(play.PlaytestId)).PlaytestState == StudioPlaytestState.Started,
                "closing Studio leaves actual game playtest scene running");
            studio = await StartNativeProbeAsync(profile);
            NativeMap afterRestart=await CreateNativeUnsavedMapAsync(studio);
            Check(!(await broker.StopPlaytestAsync(Guid.NewGuid())).Accepted, "stale playtest stop cannot terminate current native game scene");
            Check((await broker.StopPlaytestAsync(play.PlaytestId)).Accepted, "native playtest stop reaches normal game owner lifecycle");
            await WaitForPlaytestAsync(broker,play.PlaytestId,StudioPlaytestState.Ended);
            await CheckNativeMapUnchangedAsync(studio,afterRestart,"normal native game scene stop leaves real dirty editor tab/history/layout intact");
            game.Kill(entireProcessTree:true); await game.WaitForExitAsync(); game.Dispose(); game = null;
            Check(!studio.HasExited,"actual game crash leaves native Studio alive and responsive");
            await CheckNativeMapUnchangedAsync(studio,afterRestart,"actual game crash preserves unsaved native Map contents and lifecycle");
            game = await StartNativeGameProbeAsync(gameData,profile,assets);
            Check((await WaitForGameDiagnosticsAsync(broker)).Accepted, "game broker reconnects to restarted native game without stale endpoint trust");
            await studio.StandardInput.WriteLineAsync("autosave-map");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"MAP-AUTOSAVED");
            studio.Kill(entireProcessTree:true); await studio.WaitForExitAsync(); studio.Dispose(); studio = null;
            Check(!game.HasExited && (await broker.GetDiagnosticsAsync()).Accepted, "actual Studio crash leaves native Project Prime alive and responsive");
            studio=await StartNativeProbeAsync(profile);
            NativeSnapshot recovered=await NativeStatusAsync(studio);
            Check(!game.HasExited&&recovered.Map is {Dirty:true} recoveredMap&&recoveredMap.DefinitionHash==afterRestart.DefinitionHash&&(await broker.GetDiagnosticsAsync()).Accepted,
                "native game-first launch then Studio launch preserves both independent application lifetimes");
            await CloseDirtyNativeProbeAsync(studio);studio.Dispose();studio=null;
            await game.StandardInput.WriteLineAsync("close"); await game.StandardInput.FlushAsync();
            await game.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Check(game.ExitCode == 0 && !File.Exists(LocalIpcEndpointStore.GetDescriptorPath(AppContext.BaseDirectory,profile,StudioEndpointRole.Game)),
                "normal native game Quit releases graphics/audio owner and removes broker descriptor");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_GAME_PATH",priorGame);
            foreach (Process? child in new[] {studio,game})
            {
                if(child is null)continue;
                if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync();} child.Dispose();
            }
        }
    }

    private static async Task<NativeMap> CreateNativeUnsavedMapAsync(Process studio)
    {
        await studio.StandardInput.WriteLineAsync("new-map");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"MAP-CREATED");
        return (await WaitForNativeMapMetricsAsync(studio,1)).Map??throw new InvalidOperationException("Actual native Map state missing.");
    }
    private static async Task NativePackageMapAsync(Process studio,string path)
    {
        await studio.StandardInput.WriteLineAsync("package-map "+path);await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"MAP-PACKAGED");
    }
    private static async Task CheckNativeMapUnchangedAsync(Process studio,NativeMap before,string message)
        =>Check(JsonSerializer.Serialize((await NativeStatusAsync(studio)).Map)==JsonSerializer.Serialize(before),message);
    private static async Task CloseDirtyNativeProbeAsync(Process studio)
    {
        await studio.StandardInput.WriteLineAsync("close-discard");await studio.StandardInput.FlushAsync();await ReadNativeLineAsync(studio,"CLOSED");
        await studio.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(studio.ExitCode==0,"actual native dirty close uses visible Unsaved changes Discard through normal application shutdown");
    }
    private static async Task<StudioGameResult> RequestNativePlaytestAfterStopAsync(StudioGameBrokerClient broker,string package,MapContentIdentity identity)
    {
        var timer=Stopwatch.StartNew();StudioGameResult result;
        do
        {
            result=await broker.RequestPlaytestAsync(package,GameStudioBroker.ToWireIdentity(identity),new(Bots:0));
            if(result.Accepted)return result;if(!result.Deferred)throw new InvalidOperationException(result.Error);await Task.Delay(50);
        }while(timer.Elapsed<TimeSpan.FromSeconds(10));
        throw new TimeoutException(result.Error);
    }

    private static async Task<Process> StartNativeGameProbeAsync(string data,string profile,string assets)
    {
        Process process=StartChild(["--game-probe",data,profile,Path.GetFullPath(assets)]);
        try{await ReadNativeLineAsync(process,"GAME-READY");return process;}
        catch(Exception ex)
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}
            string errors=await process.StandardError.ReadToEndAsync();string output=await process.StandardOutput.ReadToEndAsync();process.Dispose();
            foreach(string log in Directory.EnumerateFiles(data,"*.log",SearchOption.AllDirectories).Take(5))
            {string content=File.ReadAllText(log);errors+="\n"+Path.GetFileName(log)+":\n"+content[^Math.Min(8000,content.Length)..];}
            throw new InvalidOperationException("Native game failed before Shell-ready: "+errors[^Math.Min(8000,errors.Length)..]+"\n"+output[^Math.Min(8000,output.Length)..],ex);
        }
    }
    private static async Task<StudioGameResult> WaitForGameDiagnosticsAsync(StudioGameBrokerClient broker)
    {
        var deadline=Stopwatch.StartNew();
        StudioGameResult result=StudioGameResult.Rejected("Game did not become available.");
        while(deadline.Elapsed<TimeSpan.FromSeconds(20))
        {result=await broker.GetDiagnosticsAsync();if(result.Accepted)return result;await Task.Delay(100);}
        throw new TimeoutException(result.Error);
    }
    private static async Task<StudioGameResult> WaitForPlaytestAsync(StudioGameBrokerClient broker,Guid id,StudioPlaytestState state)
    {
        var deadline=Stopwatch.StartNew(); StudioGameResult? result=null;
        while(deadline.Elapsed<TimeSpan.FromSeconds(30))
        {
            result=await broker.GetPlaytestStatusAsync(id);
            if(result.Accepted&&result.PlaytestState==state)return result;
            if(result.PlaytestState==StudioPlaytestState.Rejected)throw new InvalidOperationException(result.Error);
            await Task.Delay(100);
        }
        throw new TimeoutException("Native playtest did not reach "+state+": "+result?.Error);
    }
}
