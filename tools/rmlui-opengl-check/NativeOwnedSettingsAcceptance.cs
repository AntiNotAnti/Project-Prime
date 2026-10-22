using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Diagnostics;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using ReFuel.Stb;

// Opt-in qualification against owned extracted assets. Shell owns the actual
// sole window and offline scene; edits use native controls, not engine setters.
internal sealed class NativeOwnedSettingsAcceptance
{
    private record Step(string Name,Func<bool> Poll,int Timeout=25000);
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    private const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    private readonly Queue<Step> _steps=new();
    private readonly List<object> _checks=new();
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private readonly string _output;
    private RenderWindow? _window;
    private Exception? _error;
    private bool _complete,_closing;
    private long _started,_swaps,_previewReturnedAt;
    private RmlUiDocumentToken _previewDocument;
    private string _previewJson="",_previewRaw="",_previewField="",_previewFocus="";
    private Dictionary<string,byte[]?> _previewFiles=new();
    private int _pictureAt;
    private RmlUiHost Host=>ReadStatic<RmlUiHost>("_host")??throw new InvalidOperationException("Native host is not ready.");
    private NativeHudEditorPage Hud=>ReadStatic<NativeHudEditorPage>("_hudEditor")??throw new InvalidOperationException("HUD editor is not open.");
    private HudEditorController HudController=>Read<HudEditorController>(Hud,"_controller");
    private static T Read<T>(object owner,string field)=>(T)owner.GetType().GetField(field,Private)!.GetValue(owner)!;
    private static T? ReadStatic<T>(string field)where T:class=>(T?)typeof(RmlUiOpenGlSmoke).GetField(field,Static)!.GetValue(null);
    private NativeOwnedSettingsAcceptance(string output){_output=output;Plan();}

    internal static int Run(string[] args)
    {
        if(args.Length!=3)throw new ArgumentException("--settings-live-window SETUP_paths.txt NEW_OUTPUT_DIRECTORY");
        string setup=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
        if(!File.Exists(setup)||Directory.Exists(output)||File.Exists(output))throw new ArgumentException("Use existing owned extraction paths and a new output directory.");
        Directory.CreateDirectory(output);string user=Path.Combine(output,"isolated-user-data");Directory.CreateDirectory(user);
        string setupRoot=Path.GetDirectoryName(setup)!;
        var lines=File.ReadAllLines(setup).Select(line=>
        {
            int split=line.IndexOf('=');if(split<0)return line;
            string key=line[..split].Trim(),value=line[(split+1)..].Trim();
            return key+"="+(value.Length==0?"":Path.GetFullPath(value,setupRoot));
        }).Where(line=>!line.StartsWith("Export=",StringComparison.Ordinal)).ToList();
        lines.Add("Export="+Path.Combine(user,"exports"));File.WriteAllLines(Path.Combine(user,"paths.txt"),lines);
        Directory.CreateDirectory(Path.Combine(user,"exports"));
        string cwd=Directory.GetCurrentDirectory();string? previousUser=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string? previousNative=Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE");
        IThumbnailHost? previousThumbnail=null;bool thumbnailBoundary=false;var run=new NativeOwnedSettingsAcceptance(output);
        try
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",user);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE","1");
            Directory.SetCurrentDirectory(user);ConsoleSetup.Run();Directory.SetCurrentDirectory(user);
            Require(Path.GetFullPath("Savedata/settings.json").StartsWith(user+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"settings writes stay in private qualification folder");
            GameFiles.Root=user;LauncherPrefs.Directory=user;Paths.SetPath("Export",Path.Combine(user,"exports"));
            if(!GameFiles.Ready)throw new InvalidOperationException("Owned extraction unavailable: "+GameFiles.Problem());
            GraphicsBackendPolicy.Configure("opengl");WindowMode.ForceStartup(WindowStartMode.Windowed);
            LauncherPrefs.AutoUpdate=false;previousThumbnail=ThumbnailHost.Current;ThumbnailHost.Current=new NoThumbnails();thumbnailBoundary=true;
            LauncherWindowCheck.NativeReplayWindowObserved+=run.Observe;
            if(!GuiLauncher.TryRun()||!run._complete)throw new InvalidOperationException("Production Shell closed before owned settings qualification completed.",run._error);
        }
        catch(Exception error){run._error??=error;Console.Error.WriteLine(error);}
        finally
        {
            SettingsPreview.End();LauncherWindowCheck.NativeReplayWindowObserved-=run.Observe;
            if(run._window!=null)run._window.RenderFrame-=run.Frame;
            if(thumbnailBoundary)ThumbnailHost.Current=previousThumbnail;
            Directory.SetCurrentDirectory(cwd);Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",previousUser);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE",previousNative);
        }
        bool passed=run._complete&&run._error==null;
        File.WriteAllText(Path.Combine(output,"acceptance.json"),JsonSerializer.Serialize(new{Passed=passed,OwnedSetup=setup,UserData=user,
            Claim="Actual production Shell, offline owned scene, native HUD controls, both stacked projections and preview Escape/accelerated timeout. No physical pen or network acceptance claim.",
            run._swaps,Checks=run._checks,Pending=run._steps.Select(step=>step.Name),Error=run._error?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
    private void Observe(RenderWindow window)
    {if(_window!=null)return;_window=window;window.RenderFrame+=Frame;LauncherWindowCheck.NativeReplayWindowObserved-=Observe;}
    private void Frame(FrameEventArgs args)
    {
        if(_closing)return;
        try
        {
            _swaps++;if(_clock.Elapsed.TotalSeconds>180)throw new TimeoutException("Owned settings qualification exceeded three minutes.");
            Require(ReferenceEquals(_window,Shell.Window)&&!NetSession.Active&&!DemoPlayback.IsActive,"single local Shell owner without a network/replay session");
            if(_steps.Count==0){_complete=true;_closing=true;Shell.RequestQuit();return;}
            var step=_steps.Peek();if(_started==0)_started=_clock.ElapsedMilliseconds;
            if(_clock.ElapsedMilliseconds-_started>step.Timeout)throw new TimeoutException("Timed out: "+step.Name);
            if(step.Poll())
            {_checks.Add(new{step.Name,Swap=_swaps,ElapsedMs=_clock.ElapsedMilliseconds});Console.WriteLine("OWNED SETTINGS PASS "+step.Name);_steps.Dequeue();_started=0;}
        }
        catch(Exception error)
        {_error=error;try{Capture("failure");}catch{}Console.Error.WriteLine(error);_closing=true;Shell.RequestQuit();}
    }
    private void Do(string name,Action action)=>_steps.Enqueue(new(name,()=>{action();return true;}));
    private void Wait(string name,Func<bool> poll,int timeout=25000)=>_steps.Enqueue(new(name,poll,timeout));
    private static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);}
    private void Activate(string id)
    {Require(Host.FocusDocument(Host.CurrentInputDocument,id),"native focus: "+id);Host.Input.Key(2,true);Host.Input.Key(2,false);}
    private void OpenHud()
    {
        Do("open production gameplay pause",()=>Require(PauseMenu.HandleEscape(_window!),"production pause opens"));
        Wait("native pause ready for settings",()=>RmlUiOpenGlSmoke.NativePauseOpen);
        Do("open production in-game Settings",()=>Activate("ingame_settings"));
        Wait("native in-game settings overview owns focus",()=>ReadStatic<NativeFullSettingsPage>("_fullSettings") is{OwnsForeground:true});
        Do("open HUD through actual Settings button",()=>Activate("native_settings_hud"));
        Wait("actual HUD editor owns gameplay settings",()=>ReadStatic<NativeHudEditorPage>("_hudEditor") is{OwnsForeground:true});
        Do("choose HUD Position/Layout",()=>Activate("hud_section_layout"));
    }
    private void Search(string query)
    {Host.SetField(Hud.Document,"hud_property_search",query);Activate("hud_search");}
    private int Row(string label)=>Array.FindIndex(HudController.Snapshot().Properties.ToArray(),property=>property.Label.Contains(label,StringComparison.OrdinalIgnoreCase));
    private void SetOption(string label,string value,Func<bool> runtime)
    {
        Do("find inline "+label,()=>Search(label));Wait("inline property exposed: "+label,()=>Row(label)>=0);
        Do("edit native field: "+label,()=>{string id="hud_property"+Row(label)+"_input";Require(Host.FocusDocument(Hud.Document,id),"focus inline property");Host.SetField(Hud.Document,id,value);});
        Wait("automatic commit publishes "+label,runtime);
    }
    private void ResumeGameplay(string aspect)
    {
        Do("return from HUD after "+aspect,()=>Activate("hud_cancel"));
        Wait("retained full Settings resumes after "+aspect,()=>ReadStatic<NativeHudEditorPage>("_hudEditor")==null&&ReadStatic<NativeFullSettingsPage>("_fullSettings") is{OwnsForeground:true});
        Do("back from Settings after "+aspect,()=>Activate("native_settings_back"));
        Wait("native pause owns returned Settings",()=>RmlUiOpenGlSmoke.NativePauseOpen);
        Do("resume actual gameplay after "+aspect,()=>Activate("ingame_resume"));
        Wait("actual stacked gameplay projection "+aspect,()=>
        {
            if(PauseMenu.Open||RmlUiOpenGlSmoke.CapturesInput)return false;
            var scene=_window!.Scene;if(!scene.UseDsLayout)return false;
            var projection=Read<Matrix4>(scene,"_perspectiveMatrix");float expected=aspect=="4:3"?4f/3:5f/3;
            Require(MathF.Abs(projection.M22/projection.M11-expected)<.001f,"actual rendered perspective follows stacked screen aspect");
            Capture("gameplay-stacked-"+aspect.Replace(':','x'));return true;
        });
    }
    private void Plan()
    {
        Do("configure sole production window",()=>{_window!.ClientSize=new(1280,900);_window.Focus();});
        Wait("native startup ready",()=>RmlUiOpenGlSmoke.NativeStartupOpen&&ReadStatic<RmlUiHost>("_host") is{Active:true});
        Do("continue production native startup",()=>Activate("native_startup_continue"));
        Wait("native startup finished",()=>!RmlUiOpenGlSmoke.NativeStartupOpen);
        Wait("start owned offline Samus arena without bots",()=>Shell.RmlUiLaunchOffline(new("MP3 PROVING GROUND",GameMode.Battle,Hunter.Samus,0,0,0)));
        Wait("owned offline scene is actually rendered",()=>_window is{HasScene:true}&&_window.Scene.FrameCount>2,90000);
        Do("capture original offline gameplay",()=>Capture("gameplay-original"));
        OpenHud();SetOption("Stylus input mode","true",()=>PointerInput.StylusMode);
        SetOption("Stacked dual-screen layout","true",()=>DsLayout.Enabled&&DsLayout.Active);
        SetOption("Screen proportions","4:3",()=>DsLayout.ScreenAspect=="4:3");ResumeGameplay("4:3");
        OpenHud();SetOption("Screen proportions","5:3",()=>DsLayout.ScreenAspect=="5:3");ResumeGameplay("5:3");
        OpenHud();Do("find real crosshair element scale",()=>Search("/elements/core.crosshair/scale"));
        Wait("crosshair scale appears in Layout",()=>HudController.Snapshot().Properties.Any(property=>property.Path=="/elements/core.crosshair/scale"));
        foreach(bool timeout in new[]{false,true})PlanPreview(timeout);
    }
    private void PlanPreview(bool timeout)
    {
        Do("request actual HUD live preview "+(timeout?"timeout":"Escape"),()=>
        {
            int row=Array.FindIndex(HudController.Snapshot().Properties.ToArray(),property=>property.Path=="/elements/core.crosshair/scale");
            _previewDocument=Hud.Document;_previewJson=HudProfileStore.Serialize(HudProfiles.CopyCurrent());_previewFiles=SettingsFiles();
            _previewRaw=HudProfiles.CopyCurrent().Elements["core.crosshair"].Scale==1.275f?"1.375":"1.275";
            _previewField="hud_property"+row+"_input";Host.SetField(Hud.Document,_previewField,_previewRaw);
            Activate("hud_live_preview");_previewFocus=Host.FocusedElement();
        });
        Wait("production preview has live gameplay ownership",()=>
        {
            if(!SettingsPreview.Active)return false;
            Require(!Host.IsVisible(_previewDocument)&&Host.IsAlive(_previewDocument)&&!PauseMenu.Open,"preview retains and hides exact HUD document");
            Require(HudProfiles.CopyCurrent().Elements["core.crosshair"].Scale==float.Parse(_previewRaw,CultureInfo.InvariantCulture),"proposal reaches actual runtime profile");
            _pictureAt=(int)_swaps;return true;
        });
        Wait("preview draws real gameplay frames",()=>_swaps-_pictureAt>=5);
        Do("capture live preview and return "+(timeout?"by timeout":"by Escape"),()=>
        {
            Capture(timeout?"gameplay-preview-timeout":"gameplay-preview-escape");
            Require(SameFiles(_previewFiles),"live preview writes no preference bytes");
            if(timeout)typeof(SettingsPreview).GetField("_started",Static)!.SetValue(null,Stopwatch.GetTimestamp()-Stopwatch.Frequency*(SettingsPreview.DurationSeconds+1L));
            else Require(PauseMenu.HandleEscape(_window!),"production Escape gate returns from live preview");
        });
        Wait("preview returns to same HUD document "+(timeout?"timeout":"Escape"),()=>
        {
            if(SettingsPreview.Active||!Hud.OwnsForeground)return false;
            Require(Hud.Document==_previewDocument&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==_previewJson,"exact runtime profile and document restored");
            Require(Host.ReadField(Hud.Document,_previewField)==_previewRaw&&Host.FocusedElement()==_previewFocus&&SameFiles(_previewFiles),"raw pending field, focus and saved bytes preserved");
            Capture(timeout?"hud-return-timeout":"hud-return-escape");_previewReturnedAt=Stopwatch.GetTimestamp();return true;
        });
        Wait("returned preview proposal remains detached while idle",()=>
        {
            if(Stopwatch.GetElapsedTime(_previewReturnedAt).TotalMilliseconds<800)return false;
            Require(Hud.Document==_previewDocument&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==_previewJson
                &&SameFiles(_previewFiles)&&Host.ReadField(Hud.Document,_previewField)==_previewRaw,
                "idle return must not silently persist or reapply the preview proposal");
            return true;
        });
    }
    private static Dictionary<string,byte[]?> SettingsFiles()
    {
        string hud=HudProfiles.StorageDirectory??Paths.Combine("Savedata","hud-profiles");
        return new[]{Path.Combine(LauncherPrefs.Directory,"launcher.txt"),Path.Combine(LauncherPrefs.Directory,"controls.txt"),Paths.Combine("Savedata","settings.json"),Path.Combine(hud,"active.json"),Path.Combine(hud,"active.json.bak")}
            .ToDictionary(path=>path,path=>File.Exists(path)?File.ReadAllBytes(path):null);
    }
    private static bool SameFiles(Dictionary<string,byte[]?> before)=>before.All(item=>item.Value==null?!File.Exists(item.Key):File.Exists(item.Key)&&File.ReadAllBytes(item.Key).AsSpan().SequenceEqual(item.Value));
    private void Capture(string name)
    {
        var size=_window!.FramebufferSize;int fbo=GL.GetInteger(GetPName.ReadFramebufferBinding),buffer=GL.GetInteger(GetPName.ReadBuffer),alignment=GL.GetInteger(GetPName.PackAlignment);
        byte[] pixels=new byte[size.X*size.Y*3];
        try{GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,0);GL.ReadBuffer(ReadBufferMode.Front);GL.PixelStore(PixelStoreParameter.PackAlignment,1);GL.ReadPixels(0,0,size.X,size.Y,PixelFormat.Rgb,PixelType.UnsignedByte,pixels);}
        finally{GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,fbo);GL.ReadBuffer((ReadBufferMode)buffer);GL.PixelStore(PixelStoreParameter.PackAlignment,alignment);}
        using var stream=File.Create(Path.Combine(_output,name+".png"));StbImage.FlipVerticallyOnSave=true;PngEncoder.Write<byte>(pixels,size.X,size.Y,StbiImageFormat.Rgb,stream);
    }
    private sealed class NoThumbnails:IThumbnailHost
    {public Task<int> RenderAsync(IReadOnlyList<string> rooms,Action<string> report)=>Task.FromResult(0);}
}
