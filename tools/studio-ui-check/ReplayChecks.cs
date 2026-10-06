using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MphRead.Mods;
using MphRead.Mods.StudioReplay;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static async Task CheckReplayEditorAsync(string output,string data,string fixture)
    {
        Directory.CreateDirectory(data); string source=Path.Combine(data,"acceptance.ppdemo"); File.Copy(fixture,source);
        byte[] immutable=File.ReadAllBytes(source);
        var paths=new StudioPaths(AppContext.BaseDirectory,data);
        var window=new StudioWindow(paths,new StudioSettings(),new(Guid.NewGuid(),StudioOpenKind.Home));
        await window.InitializeAsync(promptForRecovery:false);window.Show();
        try
        {
            window.NewMapProject("Replay cancellation survivor",example:false);
            var survivor=(ProjectPrime.Studio.Map.MapStudioDocument)window.Documents.ActiveDocument!;
            string survivorDefinition=survivor.Host.Document!.Project.Definition.Serialize();
            var survivorState=survivor.Host.Document.CurrentStateId;
            using(var cancellation=new CancellationTokenSource())
            {
                void CancelAtPrepared()
                {
                    if(window.Jobs.Jobs.Any(job=>job.Title.StartsWith("Prepare replay ",StringComparison.Ordinal)
                        &&job.State==ProjectPrime.Studio.Jobs.StudioJobState.Running&&job.Progress.Fraction==1))cancellation.Cancel();
                }
                window.Jobs.Changed+=CancelAtPrepared;
                try
                {
                    var rejected=await window.HandleLaunchRequestAsync(new(Guid.NewGuid(),StudioOpenKind.Replay,source),cancellation.Token);
                    Check(cancellation.IsCancellationRequested&&!rejected.Accepted&&ReferenceEquals(window.Documents.ActiveDocument,survivor)
                        &&window.Documents.Documents.Count==1&&survivor.Host.Document.CurrentStateId==survivorState
                        &&survivor.Host.Document.Project.Definition.Serialize()==survivorDefinition,
                        "late replay preparation cancellation at progress one preserves existing canonical Map and native tab");
                }
                finally{window.Jobs.Changed-=CancelAtPrepared;}
            }
            await window.Documents.RequestCloseAsync(survivor,_=>Task.FromResult(StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null));
            Check((await window.HandleLaunchRequestAsync(new(Guid.NewGuid(),StudioOpenKind.Replay,source))).Accepted,
                "native Replay factory opens actual standalone replay document");
            var document=window.Documents.ActiveDocument as ReplayStudioDocument ?? throw new InvalidOperationException("Replay factory returned source fallback.");
            var session=document.Session!;
            Headless.Enter();session.Player.OnGraphicsInitialize(256,192);
            var deadline=System.Diagnostics.Stopwatch.StartNew();
            while(!session.Player.Status.Ready&&deadline.Elapsed<TimeSpan.FromSeconds(30))
            {
                session.Player.Advance(TimeSpan.Zero);
                if(session.Player.Status.State=="Error")throw new InvalidOperationException(session.Player.Status.Error);
                await Task.Delay(1);
            }
            Check(session.Player.Status.Ready&&document.CanSave&&!document.Dirty,"actual headless replay inspection adopts passive canonical scene with native editing controls");
            await CaptureReplayVariantsAsync(window,document,output,"replay-studio-camera",1);
            Button four=document.Host.GetVisualDescendants().OfType<Button>().Single(button=>button.Content?.ToString()=="Four Views");
            Click(window,four);await CaptureReplayVariantsAsync(window,document,output,"replay-studio-four-view",4);Click(window,four);
            TabControl inspector=document.Host.GetVisualDescendants().OfType<TabControl>().Single(control=>control.Items.OfType<TabItem>().Any(tab=>tab.Header?.ToString()=="Camera"));
            foreach(string panel in new[]{"Timeline","Combat","Export"})
            {
                inspector.SelectedItem=inspector.Items.OfType<TabItem>().Single(tab=>tab.Header?.ToString()==panel);
                await CaptureReplayVariantsAsync(window,document,output,"replay-studio-"+panel.ToLowerInvariant(),1);
            }
            session.Player.AddBookmark(30,"Search acceptance marker");
            session.Player.PutCameraKey(new(60,new(0,5,10),System.Numerics.Quaternion.Identity));
            var marker=StudioGlobalSearchWindow.Search(window.Documents,window.Commands,new StudioSettings(),"Search acceptance marker").Single(result=>result.Category=="Replay marker");
            marker.Activate();session.Player.Advance(TimeSpan.Zero);
            deadline.Restart();while(!session.Player.Status.Ready&&deadline.Elapsed<TimeSpan.FromSeconds(10)){session.Player.Advance(TimeSpan.Zero);await Task.Delay(1);}
            Check(session.Player.Status.Frame==30,"global replay marker search queues canonical player seek");
            var key=StudioGlobalSearchWindow.Search(window.Documents,window.Commands,new StudioSettings(),"Camera key 60").Single(result=>result.Category=="Camera");
            key.Activate();session.Player.Advance(TimeSpan.Zero);
            deadline.Restart();while(!session.Player.Status.Ready&&deadline.Elapsed<TimeSpan.FromSeconds(10)){session.Player.Advance(TimeSpan.Zero);await Task.Delay(1);}
            Check(session.Player.Status.Frame==60,"global camera-key search queues canonical player seek");
            await window.Commands.ExecuteAsync(StudioCommand.ReplayMarkIn);session.Player.Advance(TimeSpan.Zero);
            Check(session.Player.Status.ClipIn==60,"native Replay Mark In command routes into canonical transport");
            session.Camera=StudioReplayCameraMode.Free;session.Position=new(3,6,9);session.Fov=90;
            await document.SaveAsync(null,CancellationToken.None);
            using(var restored=new ReplayStudioSession(source,paths))
                Check(restored.Camera==session.Camera&&restored.Position==session.Position&&restored.Fov==90,
                    "actual Replay document saves private presentation settings without rewriting recording");
            var oldHost=document.Host;session.Player.SetRange(30,60);session.Player.Advance(TimeSpan.Zero);
            string clip=Path.Combine(data,"acceptance.ppclip");await document.SaveAsync(clip,CancellationToken.None);
            Check(document.Kind==StudioDocumentKind.ReplayClip&&document.Path==clip&&!ReferenceEquals(oldHost,document.Host),
                "native Replay Save As creates canonical clip project and replaces document presentation host");
            session=document.Session!;session.Player.OnGraphicsInitialize(256,192);deadline.Restart();
            while(!session.Player.Status.Ready&&deadline.Elapsed<TimeSpan.FromSeconds(30)){session.Player.Advance(TimeSpan.Zero);await Task.Delay(1);}
            PumpLayout(window);
            bool currentAttached=window.GetVisualDescendants().Any(control=>ReferenceEquals(control,document.Host));
            bool oldAttached=window.GetVisualDescendants().Any(control=>ReferenceEquals(control,oldHost));
            Check(session.Player.Status.Ready&&session.Player.Status.DurationFrames==30&&currentAttached&&!oldAttached
                &&document.Host.IsEffectivelyVisible&&session.Camera==StudioReplayCameraMode.Free,
                "saved clip continues in the actual native tab with selected range and presentation camera preserved: "
                +System.Text.Json.JsonSerializer.Serialize(new{session.Player.Status,currentAttached,oldAttached,session.Camera}));
            await CaptureReplayVariantsAsync(window,document,output,"replay-studio-saved-clip",1);
            Check(immutable.SequenceEqual(File.ReadAllBytes(source))&&!MphRead.Mods.Network.NetSession.Active,
                "native Replay controls retain immutable recording and construct no network session");
            Check(await window.Documents.RequestCloseAsync(document,_=>Task.FromResult(StudioCloseDecision.Cancel),_=>Task.FromResult<string?>(null))
                &&document.State==StudioDocumentState.Closed,"clean replay document closes and releases passive decoder ownership");
        }
        finally{await window.TryCloseAsync();window.Close();await window.DisposeResourcesAsync();}
    }

    private static async Task CaptureReplayVariantsAsync(StudioWindow window,ReplayStudioDocument document,string output,string route,int views)
    {
        foreach((int width,int height,double scale) in new[]{(1280,800,1d),(1920,1080,1d),(1280,800,2d)})
        {
            window.Width=width;window.Height=height;window.SetRenderScaling(scale);PumpLayout(window);
            var refresh=System.Diagnostics.Stopwatch.StartNew();TextBlock? transport;
            do
            {
                transport=document.Host.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block=>block.Text?.Contains(" checkpoints (",StringComparison.Ordinal)==true);
                if(transport is not null)break;
                await Task.Delay(5);PumpLayout(window);
            }while(refresh.Elapsed<TimeSpan.FromSeconds(2));
            Check(transport is not null,route+" has refreshed its actual transport status before capture");
            CheckControlBounds(window,transport!,200,10,route+" keeps readable transport status in allocated viewport");
            var viewports=document.Host.GetVisualDescendants().OfType<ReplayViewportHost>().Where(viewport=>viewport.IsEffectivelyVisible).ToArray();
            Check(viewports.Length==views,route+" uses expected canonical passive replay view count");
            foreach(var viewport in viewports)CheckControlBounds(window,viewport,views==1?300:140,views==1?180:100,route+" reserves unclipped replay viewport allocation");
            foreach(string action in new[]{"Play / Pause","Step","Restart","Mark In","Mark Out","Four Views"})
            {
                var button=document.Host.GetVisualDescendants().OfType<Button>().Single(button=>button.Content?.ToString()==action);
                CheckControlBounds(window,button,30,10,route+" keeps "+action+" toolbar action visible");
            }
            using var bitmap=window.CaptureRenderedFrame()??throw new InvalidOperationException("Replay editor controls did not render.");
            CheckImageContent(bitmap,route);string file=$"{route}-{width}x{height}"+(scale==1?"":"-2x")+".png";
            bitmap.Save(Path.Combine(output,file),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Captures.Add(new{route,logicalWidth=width,logicalHeight=height,scale,viewportBackend="Native control allocation only; headless replay GPU pixels unavailable",file,pixelWidth=bitmap.PixelSize.Width,pixelHeight=bitmap.PixelSize.Height});
            await Task.Yield();
        }
    }
    private static void Click(Window window,Control control)
    {
        PumpLayout(window);Point point=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),window)
            ??throw new InvalidOperationException("Native control has no window origin.");
        window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);PumpLayout(window);
    }
}
