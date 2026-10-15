using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioRendering;
using MphRead.Mods.StudioReplay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace ProjectPrime.Studio.Replay;

public sealed class ReplayViewportHost : Grid, IDisposable
{
    private readonly ReplayStudioDocument _document;
    private readonly Surface _surface;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _status=new(){Margin=new Thickness(8),TextWrapping=TextWrapping.Wrap};
    private readonly Slider _timeline=new(){Minimum=0,Maximum=1};
    private readonly ConcurrentQueue<Action<PassiveReplayPlayer>> _commands=new();
    private readonly ListBox _keys=new(){SelectionMode=SelectionMode.Multiple,Height=115};
    private readonly ListBox _markers=new(){Height=115};
    private readonly CameraGraph _graph=new(){Height=72};
    private bool _updating, _disposed;
    private Vector2i _size=new(640,360);
    private long _lastTicks;
    private double _clock;
    private IDisposable? _registration;
    private StudioReplayPreviewAudio? _audio;
    private readonly StudioReplayViewTarget _primaryView=new(),_comparisonView=new();
    internal void Enqueue(Action<PassiveReplayPlayer> action)=>_commands.Enqueue(action);
    public ReplayViewportHost(ReplayStudioDocument document)
    {
        _document=document;RowDefinitions=new RowDefinitions("Auto,*,Auto,Auto,Auto");
        var tools=new WrapPanel{Orientation=Orientation.Horizontal};
        void Button(string label,Action action,Panel? panel=null)
        {var button=new Button{Content=label};button.Click+=(_,_)=>{try{action();}catch(Exception ex){_status.Text=ex.Message;}};(panel??tools).Children.Add(button);}
        Button("Play / Pause",document.TogglePause);Button("Step",()=>Enqueue(p=>p.Transport.StepForward()));
        foreach(float rate in new[]{0.25f,0.5f,1,2,4})Button(rate+"×",()=>Enqueue(p=>p.Transport.SetPlaybackRate(rate)));
        Button("Mark In",document.MarkIn);Button("Mark Out",document.MarkOut);
        Button("Previous event",()=>Enqueue(p=>p.Transport.JumpEvent(false)));Button("Next event",()=>Enqueue(p=>p.Transport.JumpEvent(true)));
        var mode=new ComboBox{ItemsSource=Enum.GetValues<ReplayCameraMode>().Where(m=>m!=ReplayCameraMode.Free).ToArray(),SelectedItem=ReplayCameraMode.FirstPerson};
        mode.SelectionChanged+=(_,_)=>{if(mode.SelectedItem is ReplayCameraMode value)document.CameraMode=value;};tools.Children.Add(mode);
        var slot=new ComboBox{ItemsSource=Enumerable.Range(1,8).Select(i=>"Player "+i).ToArray(),SelectedIndex=0};slot.SelectionChanged+=(_,_)=>document.Slot=Math.Max(0,slot.SelectedIndex);tools.Children.Add(slot);
        var track=new CheckBox{Content="Camera track",IsChecked=true};track.IsCheckedChanged+=(_,_)=>document.PlayTrack=track.IsChecked==true;tools.Children.Add(track);
        var hud=new CheckBox{Content="Game HUD",IsChecked=true};hud.IsCheckedChanged+=(_,_)=>document.GameHud=hud.IsChecked==true;tools.Children.Add(hud);
        var overlay=new CheckBox{Content="Replay overlay"};overlay.IsCheckedChanged+=(_,_)=>document.ReplayOverlay=overlay.IsChecked==true;tools.Children.Add(overlay);
        var director=new CheckBox{Content="Director"};director.IsCheckedChanged+=(_,_)=>document.Director=director.IsChecked==true;tools.Children.Add(director);
        var previewSound=new CheckBox{Content="Preview audio",IsChecked=true};previewSound.IsCheckedChanged+=(_,_)=>document.PreviewAudio=previewSound.IsChecked==true;tools.Children.Add(previewSound);
        var compare=new CheckBox{Content="Compare views"};compare.IsCheckedChanged+=(_,_)=>document.Multiview=compare.IsChecked==true;tools.Children.Add(compare);
        var compareMode=new ComboBox{ItemsSource=new[]{ReplayCameraMode.FirstPerson,ReplayCameraMode.Chase,ReplayCameraMode.Orbit},SelectedItem=ReplayCameraMode.Chase};compareMode.SelectionChanged+=(_,_)=>{if(compareMode.SelectedItem is ReplayCameraMode selected)document.ComparisonMode=selected;};tools.Children.Add(compareMode);
        var compareSlot=new ComboBox{ItemsSource=Enumerable.Range(1,8).Select(i=>"Compare player "+i).ToArray(),SelectedIndex=1};compareSlot.SelectionChanged+=(_,_)=>document.ComparisonSlot=Math.Max(0,compareSlot.SelectedIndex);tools.Children.Add(compareSlot);
        Button("Camera Key",document.AddCameraKey);
        var cameraInputs=new List<TextBox>();
        foreach(var label in new[]{"X","Y","Z","Yaw","Pitch","FOV"}){var input=new TextBox{PlaceholderText=label,Text=label=="FOV"?"70":"0",Width=56};cameraInputs.Add(input);tools.Children.Add(input);}
        Button("Set Key",()=>{var v=cameraInputs.Select(i=>float.Parse(i.Text??"0",System.Globalization.CultureInfo.InvariantCulture)).ToArray();document.SetCameraKey(v[0],v[1],v[2],v[3],v[4],v[5]);});
        var authoring=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*"),Margin=new Thickness(6)};
        var cameraPanel=new StackPanel{Spacing=4};cameraPanel.Children.Add(new TextBlock{Text="Camera keys · select multiple to move/delete"});cameraPanel.Children.Add(_keys);cameraPanel.Children.Add(_graph);
        var cameraActions=new WrapPanel();cameraPanel.Children.Add(cameraActions);
        uint[] Selected()=>_keys.SelectedItems?.OfType<CameraKeyItem>().Select(k=>k.Frame).ToArray()??[];
        var shift=new TextBox{Text="60",Width=64};cameraActions.Children.Add(shift);
        Button("Move frames",()=>document.EditCameraKeys(Selected(),int.Parse(shift.Text??"0"),false),cameraActions);
        Button("Delete keys",()=>document.EditCameraKeys(Selected(),0,true),cameraActions);
        var interpolation=new ComboBox{ItemsSource=Enum.GetValues<ReplayCameraInterpolation>(),SelectedItem=ReplayCameraInterpolation.Linear};cameraActions.Children.Add(interpolation);
        var ease=new ComboBox{ItemsSource=Enum.GetValues<ReplayCameraEase>(),SelectedItem=ReplayCameraEase.None};cameraActions.Children.Add(ease);
        Button("Set interpolation",()=>document.EditCameraKeys(Selected(),0,false,(ReplayCameraInterpolation)interpolation.SelectedItem!,(ReplayCameraEase)ease.SelectedItem!),cameraActions);
        _keys.DoubleTapped+=(_,_)=>{if(_keys.SelectedItem is CameraKeyItem key)document.Seek(key.Frame);};
        authoring.Children.Add(cameraPanel);
        var annotationPanel=new StackPanel{Spacing=4};SetColumn(annotationPanel,1);authoring.Children.Add(annotationPanel);
        annotationPanel.Children.Add(new TextBlock{Text="Bookmarks, combat events and named ranges"});annotationPanel.Children.Add(_markers);
        _markers.DoubleTapped+=(_,_)=>{if(_markers.SelectedItem is MarkerItem marker)document.Seek(marker.Frame);};
        var markerActions=new WrapPanel();annotationPanel.Children.Add(markerActions);
        Button("Delete marker",()=>
        {
            if(document.Path is not {} path||_markers.SelectedItem is not MarkerItem {Id:{} id} marker)return;
            if(marker.Kind==MarkerKind.Bookmark)ReplayAnnotations.RemoveBookmark(path,id);
            else if(marker.Kind==MarkerKind.Clip)ReplayAnnotations.RemoveHighlight(path,id);
            else if(marker.Kind==MarkerKind.Reel)ReplayReels.Remove(path,id);
            RefreshAuthoring();
        },markerActions);
        Button("Reel ↑",()=>{if(document.Path is {} path&&_markers.SelectedItem is MarkerItem{Kind:MarkerKind.Reel,Id:{} id}){ReplayReels.Move(path,id,-1);RefreshAuthoring();}},markerActions);
        Button("Reel ↓",()=>{if(document.Path is {} path&&_markers.SelectedItem is MarkerItem{Kind:MarkerKind.Reel,Id:{} id}){ReplayReels.Move(path,id,1);RefreshAuthoring();}},markerActions);
        Button("Reel camera",()=>{if(document.Path is {} path&&_markers.SelectedItem is MarkerItem{Kind:MarkerKind.Reel,Id:{} id}){ReplayReels.CycleCamera(path,id);RefreshAuthoring();}},markerActions);
        Button("Trim reel to marks",()=>{if(document.Path is {} path&&_markers.SelectedItem is MarkerItem{Kind:MarkerKind.Reel,Id:{} id}){ReplayReels.Trim(path,id,document.ClipIn??0,document.ClipOut??document.DurationFrames);RefreshAuthoring();}},markerActions);
        var annotationTools=new WrapPanel();var name=new TextBox{PlaceholderText="Name",Width=170};annotationTools.Children.Add(name);annotationPanel.Children.Add(annotationTools);
        Button("Bookmark",()=>document.Bookmark(name.Text??""),annotationTools);Button("Name clip",()=>document.NamedClip(name.Text??"",false),annotationTools);Button("Add to reel",()=>document.NamedClip(name.Text??"",true),annotationTools);
        annotationPanel.Children.Add(new TextBlock{Text="Video export uses fixed replay frames and offline audio. Historical audiovisual parity requires a matching recording and exact map package.",TextWrapping=TextWrapping.Wrap});
        var exports=new WrapPanel();annotationPanel.Children.Add(exports);
        var resolution=new ComboBox{ItemsSource=new[]{"1280×720","1920×1080","2560×1440","3840×2160"},SelectedIndex=0};exports.Children.Add(resolution);
        var fps=new ComboBox{ItemsSource=ReplayExportRates.Supported,SelectedItem=60};exports.Children.Add(fps);
        var preset=new ComboBox{ItemsSource=new[]{"ultrafast","medium","slow"},SelectedItem="medium"};exports.Children.Add(preset);
        var exportSound=new CheckBox{Content="Export audio",IsChecked=true};exportSound.IsCheckedChanged+=(_,_)=>document.ExportAudio=exportSound.IsChecked==true;exports.Children.Add(exportSound);
        var clipButton=new Button{Content="Export replay clip"};exports.Children.Add(clipButton);
        clipButton.Click+=async (_,_)=>await ExportAsync(false,resolution,fps,preset);
        var videoButton=new Button{Content="Export video"};exports.Children.Add(videoButton);
        videoButton.Click+=async (_,_)=>await ExportAsync(true,resolution,fps,preset);
        var reelButton=new Button{Content="Export reel video"};exports.Children.Add(reelButton);
        reelButton.Click+=async (_,_)=>await ExportAsync(true,resolution,fps,preset,true);
        Children.Add(tools);_surface=new(this){IsVisible=!StudioGraphicsState.SafeMode};Children.Add(_surface);SetRow(_surface,1);
        Children.Add(_timeline);SetRow(_timeline,2);Children.Add(authoring);SetRow(authoring,3);Children.Add(_status);SetRow(_status,4);
        _timeline.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&!_updating)document.Seek((uint)_timeline.Value);};
        _timer=new DispatcherTimer(TimeSpan.FromSeconds(1d/60),DispatcherPriority.Render,(_,_)=>
        {
            double scale=TopLevel.GetTopLevel(this)?.RenderScaling??1;
            _size=new(Math.Max(1,(int)(_surface.Bounds.Width*scale)),Math.Max(1,(int)(_surface.Bounds.Height*scale)));
            _surface.RequestNextFrameRendering();
        });
        AttachedToVisualTree+=(_,_)=>{if(!StudioGraphicsState.SafeMode&&!_disposed)_timer.Start();};DetachedFromVisualTree+=(_,_)=>_timer.Stop();
        document.Changed+=RefreshAuthoring;
        if(StudioGraphicsState.SafeMode)_status.Text="Replay graphics are disabled in Safe Mode. Reopen Studio normally to view and export.";
        else if(document.Path is null)_status.Text="Open a .ppdemo or .ppclip recording using File → Open.";
        RefreshAuthoring();
    }
    private async Task ExportAsync(bool video,ComboBox resolution,ComboBox fps,ComboBox preset,bool reel=false)
    {
        try
        {
            var top=TopLevel.GetTopLevel(this);if(top is null)return;
            var file=await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions{Title=video?"Export replay video":"Export replay clip",SuggestedFileName=video?"replay.mp4":"clip.ppdemo",DefaultExtension=video?"mp4":"ppdemo",FileTypeChoices=[new Avalonia.Platform.Storage.FilePickerFileType(video?"MP4 video":"Project Prime replay"){Patterns=[video?"*.mp4":"*.ppdemo"]}]});
            if(file is null||!file.Path.IsFile)return;
            int index=resolution.SelectedIndex;int width=new[]{1280,1920,2560,3840}[index],height=new[]{720,1080,1440,2160}[index];int rate=(int)fps.SelectedItem!;string quality=(string)preset.SelectedItem!;
            Func<CancellationToken,Task> export=token=>video?_document.ExportVideoAsync(file.Path.LocalPath,width,height,rate,quality,token,reel):_document.ExportSelectionAsync(file.Path.LocalPath,token);
            if(_document.JobRunner is {} runner)await runner(video?"Export replay video":"Export replay clip",export,CancellationToken.None);else await export(CancellationToken.None);
            _status.Text=video?"Video exported.":"Replay clip exported.";
        }
        catch(OperationCanceledException){_status.Text="Replay export cancelled.";}
        catch(Exception ex){_status.Text=ex.Message;}
    }
    private void RefreshAuthoring()
    {
        if(_disposed)return;
        var selected=_keys.SelectedItems?.OfType<CameraKeyItem>().Select(k=>k.Frame).ToHashSet()??[];
        var keys=_document.CameraKeys();_keys.ItemsSource=keys.Select(k=>new CameraKeyItem(k.Frame,k.Interpolation)).ToArray();
        if(_keys.SelectedItems is {} selection)foreach(var item in _keys.Items.OfType<CameraKeyItem>())if(selected.Contains(item.Frame))selection.Add(item);
        _graph.Keys=keys;_graph.InvalidateVisual();
        var markers=_document.Events.Select(e=>new MarkerItem(e.Frame,e.Type.ToString())).ToList();
        if(_document.Path is {} path)
        {
            markers.AddRange(ReplayAnnotations.Bookmarks(path).Select(b=>new MarkerItem(b.Frame,b.Name,b.Id,MarkerKind.Bookmark)));
            markers.AddRange(ReplayAnnotations.Highlights(path).Select(b=>new MarkerItem(b.StartFrame,"Clip: "+b.Name,b.Id,MarkerKind.Clip)));
            markers.AddRange(ReplayReels.Segments(path).Select(b=>new MarkerItem(b.StartFrame,"Reel: "+b.Name+" · "+b.Camera,b.Id,MarkerKind.Reel)));
        }
        _markers.ItemsSource=markers.OrderBy(m=>m.Frame).ToArray();
    }
    private readonly ReplayCameraTrack _emptyTrack=new();
    private void Frame(int framebuffer)
    {
        if(_disposed||StudioGraphicsState.SafeMode)return;
        var size=_size;_document.Initialize(size.X,size.Y);var session=_document.Session;if(session is null)return;
        _registration??=StudioGraphicsState.RegisterSurface("OpenGL",GL.GetString(StringName.Renderer));
        while(_commands.TryDequeue(out var command))command(session.Player);
        _audio??=new StudioReplayPreviewAudio(session.Player);
        _audio.Update(_document.PreviewAudio);
        if(_document.Director&&session.Player.Ready)session.Director.Tick(session.Player.Current.Scene,session.Player.Current.Session);
        var primaryMode=_document.Director?session.Director.CurrentMode:_document.CameraMode;
        var primarySlot=_document.Director&&session.Director.CurrentSlot>=0?session.Director.CurrentSlot:_document.Slot;
        lock(_document.Camera)session.Player.Current.Scene.StudioReplayPrepare(_document.PlayTrack?_document.Camera:_emptyTrack,session.Player.Transport.CurrentFrame,1,primaryMode,primarySlot);
        long now=Stopwatch.GetTimestamp();double elapsed=_lastTicks==0?0:Math.Min(0.25,Stopwatch.GetElapsedTime(_lastTicks,now).TotalSeconds);_lastTicks=now;
        if(!session.Player.Ready){session.Player.Update(120,8);_clock=0;}
        else if(!session.Player.Transport.IsPaused&&!session.Player.Transport.AtEnd)
        {
            _clock+=elapsed;int updates=0;
            while(_clock>=1d/60&&updates++<15){session.Player.Update(120,8);_clock-=1d/60;}
        }
        else{session.Player.Update(120,8);_clock=0;}
        _document.Refresh();
        if(session.Player.Ready)
        {
            var scene=session.Player.Current.Scene;
            if(!ReferenceEquals(session.OverlaySession,session.Player.Current.Session)){session.Overlay=new MphRead.Mods.StudioReplay.StudioReplayOverlay(session.Player.Current.Session);session.OverlaySession=session.Player.Current.Session;}
            scene.StudioReplayOverlay=_document.ReplayOverlay?session.Overlay!.Draw:null;
            if(_document.Multiview)
            {
                var viewSize=new Vector2i(Math.Max(1,size.X/2),size.Y);
                lock(_document.Camera)_primaryView.Render(scene,_document.PlayTrack?_document.Camera:_emptyTrack,session.Player.Transport.CurrentFrame,1,primaryMode,primarySlot,_document.GameHud,viewSize);
                _primaryView.Blit(framebuffer,0,0,size.X/2,size.Y);
                _comparisonView.Render(scene,_emptyTrack,session.Player.Transport.CurrentFrame,1,_document.ComparisonMode,_document.ComparisonSlot,_document.GameHud,viewSize);
                _comparisonView.Blit(framebuffer,size.X/2,0,size.X-size.X/2,size.Y);
            }
            else
            {
                if(scene.Size!=size){scene.Size=size;scene.OnResize();}
                scene.StudioOutputFramebuffer=framebuffer;scene.StudioReplayGameHud=_document.GameHud;
                lock(_document.Camera)scene.StudioReplayPrepare(_document.PlayTrack?_document.Camera:_emptyTrack,session.Player.Transport.CurrentFrame,1,primaryMode,primarySlot);
                lock(_document.Camera){scene.OnDrawFrame();scene.OnRenderFrame();}
            }
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer,framebuffer);
        Dispatcher.UIThread.Post(()=>
        {
            if(_disposed)return;
            _updating=true;_timeline.Maximum=Math.Max(1,_document.DurationFrames);_timeline.Value=_document.CurrentFrame;_updating=false;
            _status.Text=_document.Diagnostic+" · In "+(_document.ClipIn?.ToString()??"—")+" / Out "+(_document.ClipOut?.ToString()??"—");
        });
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_timer.Stop();_document.Changed-=RefreshAuthoring;_surface.IsVisible=false;}
    private sealed class Surface(ReplayViewportHost owner) : OpenGlControlBase
    {
        private bool _ready;
        private MacLegacyGlSurface? _legacy;
        protected override void OnOpenGlInit(GlInterface gl)
        {
            try
            {
                if(StudioGraphicsState.SafeMode)return;
                if(OperatingSystem.IsMacOS())_legacy=new MacLegacyGlSurface();
                else StudioGlBindings.Initialize(gl);
                _ready=true;
            }
            catch(Exception ex){Fail("Replay graphics unavailable: "+ex.Message);}
        }
        protected override void OnOpenGlRender(GlInterface gl,int fb)
        {
            if(!_ready)return;
            try
            {
                if(_legacy is {} legacy)
                    legacy.Render(owner._size.X,owner._size.Y,fb,()=>owner.Frame(GL.GetInteger(GetPName.FramebufferBinding)));
                else owner.Frame(fb);
            }
            catch(Exception ex)
            {
                _ready=false;
                string diagnostic=ex.Message;
                try{if(_legacy is {} legacy)legacy.RunInLegacy(Release);else Release();}
                catch(Exception cleanup){diagnostic+=" · cleanup: "+cleanup.Message;}
                Fail("Replay presentation failed: "+diagnostic);
            }
        }
        private void Release()
        {
            owner._audio?.Dispose();owner._audio=null;owner._primaryView.Dispose();owner._comparisonView.Dispose();
            owner._document.Release();owner._registration?.Dispose();owner._registration=null;
        }
        private void Fail(string message)=>Dispatcher.UIThread.Post(()=>{owner._timer.Stop();owner._status.Text=message;});
        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            try{if(_legacy is {} legacy)legacy.RunInLegacy(Release);else Release();}
            finally{_legacy?.Dispose();_legacy=null;_ready=false;}
        }
    }
    private sealed record CameraKeyItem(uint Frame,ReplayCameraInterpolation Interpolation){public override string ToString()=>$"{Frame} · {Interpolation}";}
    private enum MarkerKind{Event,Bookmark,Clip,Reel}
    private sealed record MarkerItem(uint Frame,string Label,Guid? Id=null,MarkerKind Kind=MarkerKind.Event){public override string ToString()=>$"{Frame} · {Label}";}
    private sealed class CameraGraph : Control
    {
        internal ReplayCameraKeyframe[] Keys=[];
        public override void Render(DrawingContext context)
        {
            base.Render(context);context.FillRectangle(Brushes.Black,new Rect(Bounds.Size));if(Keys.Length<2)return;
            double maxFrame=Math.Max(1,Keys[^1].Frame),min=Keys.Min(k=>(double)k.Position.Y),max=Keys.Max(k=>(double)k.Position.Y);
            var pen=new Pen(Brushes.DeepSkyBlue,2);Point? previous=null;
            foreach(var key in Keys){var point=new Point(key.Frame/maxFrame*Bounds.Width,Bounds.Height-8-(key.Position.Y-min)/Math.Max(1,max-min)*(Bounds.Height-16));if(previous is {} start)context.DrawLine(pen,start,point);context.DrawEllipse(Brushes.White,null,point,3,3);previous=point;}
        }
    }
}
