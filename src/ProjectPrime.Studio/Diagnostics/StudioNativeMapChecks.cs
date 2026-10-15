using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using MphRead.AvaloniaShared;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Diagnostics;

/// <summary>Opt-in physical desktop gate; never run by the device-free self-test.</summary>
internal static class StudioNativeMapChecks
{
    private static string _source="",_root="";
    private static bool _replay;
    public static int Run(string source,string? pathsFile=null)
    {
        _source=Path.GetFullPath(source);
        _root=Path.Combine(Path.GetTempPath(),"prime-native-map-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        StudioGameAssets.InitializeAuthoringProcess();
        _replay=pathsFile!=null;
        if(pathsFile!=null)StudioGameAssets.Configure(pathsFile);
        StudioGraphicsHost.Initialize(false);
        try{return Program.ConfigureDesktop(AppBuilder.Configure<NativeCheckApp>()).StartWithClassicDesktopLifetime([]);}
        finally{StudioGraphicsHost.Shutdown();Directory.Delete(_root,true);}
    }
    public sealed class NativeCheckApp:Application
    {
        public override void Initialize()=>Styles.Add(new FluentTheme());
        public override void OnFrameworkInitializationCompleted()
        {
            if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Window window=new(){Title="Studio native map lifecycle check",Width=1280,Height=800};
                desktop.MainWindow=window;
                window.Opened+=async(_,_)=>
                {
                    int code=0;
                    try{await ExerciseAsync(window);}
                    catch(Exception ex){Console.Error.WriteLine(ex);code=1;}
                    desktop.Shutdown(code);
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
    private static async Task WaitForAsync(Func<bool> condition,string label)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(!condition())
        {
            try{await Task.Delay(30,deadline.Token);}
            catch(OperationCanceledException){throw new InvalidOperationException("Native map check timed out: "+label);}
        }
        // A stable observation catches delayed context teardown/recreation.
        await Task.Delay(150);
        if(!condition())throw new InvalidOperationException("Native map state did not settle: "+label);
    }
    private static async Task ExerciseAsync(Window window)
    {
        if(_replay){await ExerciseReplayAsync(window);return;}
        StudioPaths paths=new(AppContext.BaseDirectory,_root);
        await using var jobs=new StudioJobManager();
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks++;}
        for(int cycle=0;cycle<10;cycle++)
        {
            var map=new MapStudioDocument(paths,window,jobs);
            try
            {
                await map.OpenAsync(_source);
                window.Content=map.Host;
                await WaitForAsync(()=>StudioGraphicsHost.NativeSurfaceCount==1,"one native surface");
                Check(StudioGraphicsHost.HasDevice,"native renderer initialized");
                window.Width=cycle%2==0?1100:1280;
                window.Height=cycle%2==0?740:800;
                await Task.Delay(100);
                map.Host.ToggleFourViews();
                await WaitForAsync(()=>StudioGraphicsHost.NativeSurfaceCount==4,"four native surfaces");
                Check(StudioGraphicsHost.ResidentWorldCount==4,"four canonical render scenes observed");
                map.Host.ToggleFourViews();
                await WaitForAsync(()=>StudioGraphicsHost.NativeSurfaceCount==1,"one surface after reparent");
                var doc=map.Host.Document!;
                doc.Edit("Native acceptance edit",definition=>definition.Description="native cycle "+cycle);
                Check(map.Dirty,"native tab editing sets dirty state");
                map.Host.Undo();Check(!map.Dirty,"native undo restores source state");
                map.Host.Redo();
                await map.SaveAsync(Path.Combine(_root,"native-"+cycle+".json"),default);
                Check(!map.Dirty,"native edit saves through canonical document");
            }
            finally
            {
                window.Content=null;
                await map.DisposeAsync();
            }
            await WaitForAsync(()=>StudioGraphicsHost.NativeSurfaceCount==0,"all native surfaces released");
            Check(!StudioGraphicsHost.HasDevice&&StudioGraphicsHost.ResidentWorldCount==0,"native lifetime drains completely");
            long privateBytes=System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
            Console.WriteLine($"[native-map-check] cycle={cycle+1} surfaces={StudioGraphicsHost.NativeSurfaceCount} managedBytes={GC.GetTotalMemory(false)} privateBytes={(privateBytes>0?privateBytes.ToString():"unavailable")}");
        }
        Console.WriteLine($"Studio native map: {checks} checks passed; ten real single/four-view/resize/edit/save/close lifecycles. No preview readback.");
    }
    private static async Task ExerciseReplayAsync(Window window)
    {
        StudioPaths paths=new(AppContext.BaseDirectory,_root);
        string before=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_source)));
        int checks=0;
        for(int cycle=0;cycle<10;cycle++)
        {
            var replay=await ReplayStudioDocument.OpenAsync(StudioDocumentKind.Replay,_source,paths,default);
            try
            {
                window.Content=replay.Host;
                await WaitForAsync(()=>StudioGraphicsHost.NativeSurfaceCount==1&&replay.Session!=null,"native replay initialized");
                replay.Seek(180);
                await WaitForAsync(()=>replay.CurrentFrame>=180,"native replay seek");checks++;
                replay.Multiview=true;replay.ReplayOverlay=true;
                window.Width=cycle%2==0?1100:1280;window.Height=cycle%2==0?740:800;
                await Task.Delay(250);
                if(!StudioGraphicsHost.HasDevice||replay.Session==null)throw new InvalidOperationException("Replay comparison lost its native device.");
                checks++;
            }
            finally{window.Content=null;await replay.DisposeAsync();}
            await WaitForAsync(()=>StudioGraphicsHost.NativeSurfaceCount==0&&replay.Session==null,"native replay and audio released");checks++;
            Console.WriteLine($"[native-replay-check] cycle={cycle+1} surfaces={StudioGraphicsHost.NativeSurfaceCount} managedBytes={GC.GetTotalMemory(false)}");
        }
        string after=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_source)));
        if(before!=after)throw new InvalidOperationException("The native preview changed its source replay.");
        checks++;
        Console.WriteLine($"Studio native replay: {checks} checks passed; ten real preview/seek/comparison/resize/close lifecycles; SHA256 before={before} after={after}.");
    }
}
